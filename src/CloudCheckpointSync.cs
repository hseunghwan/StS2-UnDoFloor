using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace StS2UnDoFloor;

/// <summary>
/// Mirrors the current run's checkpoints to Steam Cloud so a run can be rewound on another PC.
/// The local <see cref="CheckpointStore"/> stays the source of truth; the cloud copy is one fixed file per profile
/// (<see cref="CheckpointBundle.FileName"/> next to current_run.save) that is overwritten on every save and reused by
/// every run, so the number of cloud files never grows. Writes go through the game's own <see cref="CloudSaveStore"/>
/// (local save dir + Steam Remote Storage, cloud failures already swallowed there); reads come from the local copy
/// that the startup cloud sync downloaded. Nothing here is on the rewind path: any failure is logged and ignored.
/// </summary>
internal static class CloudCheckpointSync
{
    /// <summary>Largest envelope (UTF-8 bytes) that is uploaded; above this the cloud copy is left stale and a warning logged.</summary>
    public const int MaxBundleBytes = 4 * 1024 * 1024;

    private static readonly AccessTools.FieldRef<SaveManager, ISaveStore> SaveStoreField =
        AccessTools.FieldRefAccess<SaveManager, ISaveStore>("_saveStore");

    private static bool _uploading;
    private static UploadJob? _queued;

    private sealed record UploadJob(int ProfileId, long RunStartTime, List<string> CheckpointJsons);

    /// <summary>Bundle path for a profile, relative to the save store root (e.g. modded/profile1/saves/undofloor_checkpoints.save).</summary>
    public static string BundlePath(int profileId)
    {
        // Same resolution as current_run.save so the bundle always sits next to it (UserDataPathProvider.IsRunningModded
        // is set in OneTimeInitialization.ExecuteVeryEarly, before the startup cloud sync and long before any run save).
        return RunSaveManager.GetRunSavePath(profileId, CheckpointBundle.FileName);
    }

    private static CloudSaveStore? GetCloudStore(SaveManager saveManager)
    {
        return SaveStoreField(saveManager) as CloudSaveStore;
    }

    /// <summary>
    /// The checkpoints of <paramref name="runStartTime"/> held by the current profile's bundle as last synced from the
    /// cloud, or an empty list when there is no bundle, it belongs to another run, it has been merged before, or it
    /// cannot be read. When checkpoints come back, <paramref name="fingerprint"/> identifies the bundle; the caller
    /// passes it to <see cref="MarkMerged"/> once the merge has been written, so the bundle is never applied twice.
    /// </summary>
    public static List<FloorCheckpoint> LoadForRun(long runStartTime, bool hasLocalCheckpoints, out string? fingerprint)
    {
        List<FloorCheckpoint> result = new List<FloorCheckpoint>();
        fingerprint = null;
        try
        {
            SaveManager saveManager = SaveManager.Instance;
            if (GetCloudStore(saveManager) is not CloudSaveStore store)
            {
                return result;
            }
            string path = BundlePath(saveManager.CurrentProfileId);
            if (!store.FileExists(path))
            {
                Log.Info($"[{UnDoFloorMod.Id}] No cloud checkpoint bundle at {path}.");
                return result;
            }
            string? envelope = store.ReadFile(path);
            if (string.IsNullOrWhiteSpace(envelope))
            {
                Log.Warn($"[{UnDoFloorMod.Id}] Cloud checkpoint bundle {path} is empty; ignoring it.");
                return result;
            }
            if (envelope.Length > MaxBundleBytes)
            {
                // We never write one this big, so it is not a bundle of ours; refuse before inflating anything.
                Log.Warn($"[{UnDoFloorMod.Id}] Cloud checkpoint bundle {path} is {envelope.Length:N0} chars, over the {MaxBundleBytes:N0} limit; ignoring it.");
                return result;
            }
            long bundleRun = CheckpointBundle.PeekRunStartTime(envelope);
            if (bundleRun != runStartTime)
            {
                Log.Info($"[{UnDoFloorMod.Id}] Cloud checkpoint bundle is for run {bundleRun}, current run is {runStartTime}; ignoring it.");
                return result;
            }
            // The local bundle file is whatever was written last: by this machine (its own upload, or one left in
            // place when an upload was skipped or crashed) or by the startup sync (another PC's upload). A bundle is
            // merged at most once: everything this machine wrote or already merged is on the consumed list, and only
            // an unseen bundle can be newer than mod_configs.
            string candidate = Fingerprint(envelope);
            if (!CheckpointStore.HasConsumedBundleList(runStartTime) && hasLocalCheckpoints)
            {
                // First load with this version on a run that already has local checkpoints: the bundle on disk is
                // almost certainly this machine's own (an older mod version had no list), and merging it could roll
                // a slot back. Adopt it as seen instead; another PC's next upload differs and will merge normally.
                CheckpointStore.MarkBundleConsumed(runStartTime, candidate, createDir: true);
                Log.Info($"[{UnDoFloorMod.Id}] No consumed-bundle list yet for run {runStartTime}; adopting the current bundle as this machine's own without merging.");
                return result;
            }
            if (CheckpointStore.IsBundleConsumed(runStartTime, candidate))
            {
                Log.Info($"[{UnDoFloorMod.Id}] Cloud checkpoint bundle was already merged or written by this machine; nothing new to merge.");
                return result;
            }
            // From here the run is tracked: an (empty) list now exists, so a crash during a fresh machine's first
            // merge is replayed on the next load instead of being mistaken for the adopt-without-merge case above.
            CheckpointStore.EnsureConsumedBundleList(runStartTime);
            CheckpointBundle.Decoded bundle = CheckpointBundle.Decode(envelope);
            foreach (string json in bundle.CheckpointJsons)
            {
                try
                {
                    FloorCheckpoint checkpoint = FloorCheckpoint.FromJson(json);
                    if (checkpoint.RunStartTime == runStartTime)
                    {
                        result.Add(checkpoint);
                    }
                }
                catch (Exception e)
                {
                    Log.Warn($"[{UnDoFloorMod.Id}] Skipping unreadable checkpoint in cloud bundle: {e.Message}");
                }
            }
            Log.Info($"[{UnDoFloorMod.Id}] Read {result.Count} checkpoints for run {runStartTime} from cloud bundle {path}.");
            fingerprint = candidate;
        }
        catch (Exception e)
        {
            Log.Warn($"[{UnDoFloorMod.Id}] Could not read the cloud checkpoint bundle; local checkpoints are unaffected: {e.Message}");
            result.Clear();
        }
        return result;
    }

    /// <summary>
    /// Records that the bundle <see cref="LoadForRun"/> returned has been merged and written to disk. Marking after
    /// the merge means a crash in between replays the same merge on the next load (harmless: the merge runs
    /// synchronously inside the first Record, so no newer local save can exist by then), while marking before it
    /// would leave the imported checkpoints missing for good. The guarantee is therefore "a recorded bundle is never
    /// reapplied over later local saves", not that a merge can never execute twice.
    /// </summary>
    public static void MarkMerged(long runStartTime, string fingerprint)
    {
        CheckpointStore.MarkBundleConsumed(runStartTime, fingerprint, createDir: true);
    }

    /// <summary>
    /// Queues an upload of the whole checkpoint list. Only the latest snapshot is kept while an upload is in flight,
    /// so a burst of saves produces one cloud write with the final state.
    /// </summary>
    public static void ScheduleUpload(long runStartTime, IReadOnlyList<FloorCheckpoint> checkpoints)
    {
        try
        {
            SaveManager saveManager = SaveManager.Instance;
            if (GetCloudStore(saveManager) == null || !saveManager.IsProfileInitialized)
            {
                return;
            }
            _queued = new UploadJob(saveManager.CurrentProfileId, runStartTime, checkpoints.Select(c => c.Json).ToList());
            if (!_uploading)
            {
                TaskHelper.RunSafely(UploadLoop());
            }
        }
        catch (Exception e)
        {
            Log.Warn($"[{UnDoFloorMod.Id}] Could not queue the cloud checkpoint upload: {e.Message}");
        }
    }

    private static async Task UploadLoop()
    {
        _uploading = true;
        try
        {
            while (_queued is UploadJob job)
            {
                _queued = null;
                await UploadOnce(job);
            }
        }
        finally
        {
            _uploading = false;
        }
    }

    private static async Task UploadOnce(UploadJob job)
    {
        try
        {
            // Compression is the expensive part; it only touches immutable strings, so it can leave the main thread.
            (string envelope, string fingerprint) = await Task.Run(() =>
            {
                string encoded = CheckpointBundle.Encode(job.RunStartTime, job.CheckpointJsons);
                return (encoded, Fingerprint(encoded));
            });
            int bytes = Encoding.UTF8.GetByteCount(envelope);
            if (bytes > MaxBundleBytes)
            {
                Log.Warn($"[{UnDoFloorMod.Id}] Cloud checkpoint bundle would be {bytes:N0} bytes ({job.CheckpointJsons.Count} checkpoints), over the {MaxBundleBytes:N0}-byte limit. " +
                         "Local checkpoints are saved as usual; the cloud copy is NOT updated and stays at its last uploaded state.");
                return;
            }
            if (GetCloudStore(SaveManager.Instance) is not CloudSaveStore store)
            {
                return;
            }
            string path = BundlePath(job.ProfileId);
            // Marked before the write, so no window exists in which the file is on disk but not yet on the list.
            // The run folder is never (re)created here: it may have been pruned by a newer run while this was queued.
            CheckpointStore.MarkBundleConsumed(job.RunStartTime, fingerprint, createDir: false);
            // CloudSaveStore writes the local file first and logs (rather than throws) when the Steam write fails.
            await store.WriteFileAsync(path, envelope);
            Log.Info($"[{UnDoFloorMod.Id}] Cloud checkpoint bundle written: {job.CheckpointJsons.Count} checkpoints, {bytes:N0} bytes, run {job.RunStartTime}, {path}.");
        }
        catch (Exception e)
        {
            Log.Warn($"[{UnDoFloorMod.Id}] Cloud checkpoint bundle upload failed; local checkpoints are unaffected: {e.Message}");
        }
    }

    /// <summary>
    /// Runs after the game's own startup cloud sync and applies the same step to the bundle of every profile, so the
    /// task NGame.GameStartup waits on also covers the mod's file.
    /// </summary>
    internal static async Task AfterVanillaSync(SaveManager saveManager, Task vanilla, bool overwriteCloudWithLocal)
    {
        // The game's own outcome (including a fault) is preserved as-is; only our extension is fenced off below.
        await vanilla;
        string mode = overwriteCloudWithLocal ? "local -> cloud" : "cloud -> local";
        try
        {
            if (GetCloudStore(saveManager) is not CloudSaveStore store)
            {
                return;
            }
            for (int profileId = 1; profileId <= 3; profileId++)
            {
                string path = BundlePath(profileId);
                try
                {
                    if (overwriteCloudWithLocal)
                    {
                        await store.OverwriteCloudWithLocal(path);
                    }
                    else
                    {
                        await store.SyncCloudToLocal(path);
                    }
                }
                catch (Exception e)
                {
                    Log.Warn($"[{UnDoFloorMod.Id}] Cloud sync ({mode}) of {path} failed: {e.Message}");
                }
            }
            Log.Info($"[{UnDoFloorMod.Id}] Checkpoint bundle cloud sync ({mode}) finished for profiles 1-3.");
        }
        catch (Exception e)
        {
            Log.Warn($"[{UnDoFloorMod.Id}] Checkpoint bundle cloud sync ({mode}) skipped: {e.Message}");
        }
    }

    /// <summary>SHA-256 of the envelope's UTF-8 bytes, as hex.</summary>
    private static string Fingerprint(string envelope)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope)));
    }
}

/// <summary>Extends the startup cloud -> local sync (NGame.DoCloudSync) to the checkpoint bundle.</summary>
[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SyncCloudToLocal))]
internal static class SaveManager_SyncCloudToLocal_Patch
{
    private static void Postfix(SaveManager __instance, ref Task __result)
    {
        __result = CloudCheckpointSync.AfterVanillaSync(__instance, __result, overwriteCloudWithLocal: false);
    }
}

/// <summary>Extends the first-launch local -> cloud upload (and the cloud-sync-disabled path) to the checkpoint bundle.</summary>
[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.OverwriteCloudWithLocal))]
internal static class SaveManager_OverwriteCloudWithLocal_Patch
{
    private static void Postfix(SaveManager __instance, ref Task __result)
    {
        __result = CloudCheckpointSync.AfterVanillaSync(__instance, __result, overwriteCloudWithLocal: true);
    }
}
