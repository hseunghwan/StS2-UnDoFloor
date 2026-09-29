using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace StS2UnDoFloor;

/// <summary>
/// Disk copy of the checkpoint list so floors survive quitting the game.
/// Layout: &lt;user data&gt;/mod_configs/StS2-UnDoFloor/&lt;run start time&gt;/act00_row03_col02_Entered.json
/// One folder per run (keyed by the run's StartTime, which never changes within a run); the folder is pruned when a
/// different run starts recording.
/// </summary>
internal static class CheckpointStore
{
    private static readonly string Root = Path.Combine(OS.GetUserDataDir(), "mod_configs", UnDoFloorMod.Id);

    private static string RunDir(long runStartTime) => Path.Combine(Root, runStartTime.ToString());

    public static List<FloorCheckpoint> Load(long runStartTime)
    {
        List<FloorCheckpoint> result = new List<FloorCheckpoint>();
        string dir = RunDir(runStartTime);
        if (!Directory.Exists(dir))
        {
            return result;
        }
        foreach (string file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                result.Add(FloorCheckpoint.FromJson(File.ReadAllText(file)));
            }
            catch (Exception e)
            {
                Log.Warn($"[{UnDoFloorMod.Id}] Skipping unreadable checkpoint file {file}: {e.Message}");
            }
        }
        Log.Info($"[{UnDoFloorMod.Id}] Loaded {result.Count} checkpoints from {dir}.");
        return result;
    }

    public static void Write(FloorCheckpoint checkpoint)
    {
        try
        {
            string dir = RunDir(checkpoint.RunStartTime);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, checkpoint.FileName), checkpoint.Json);
        }
        catch (Exception e)
        {
            Log.Error($"[{UnDoFloorMod.Id}] Failed to write checkpoint {checkpoint}:\n{e}");
        }
    }


    private const string ConsumedBundlesFile = "cloud_bundles_seen.txt";

    /// <summary>True when the run folder has a consumed-bundle list at all (absent before this version first ran on the run).</summary>
    public static bool HasConsumedBundleList(long runStartTime)
    {
        return File.Exists(Path.Combine(RunDir(runStartTime), ConsumedBundlesFile));
    }

    /// <summary>Creates an empty consumed-bundle list if there is none, so the run is known to be tracked from here on.</summary>
    public static void EnsureConsumedBundleList(long runStartTime)
    {
        try
        {
            string dir = RunDir(runStartTime);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, ConsumedBundlesFile);
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "");
            }
        }
        catch (Exception e)
        {
            Log.Warn($"[{UnDoFloorMod.Id}] Could not create the consumed-bundle list: {e.Message}");
        }
    }

    /// <summary>
    /// True when a cloud bundle with this fingerprint has already been merged into, or written from, this run's
    /// checkpoints (see CloudCheckpointSync): such a bundle carries nothing newer than the folder itself. The list is
    /// append-only for the run's lifetime (one 64-char line per upload, pruned with the run) so a bundle can never
    /// come back as "unseen"; when the list cannot be read the bundle is reported consumed, since merging on a guess
    /// could overwrite local checkpoints while skipping only delays a merge. Not a *.json file, so <see cref="Load"/>
    /// never sees it.
    /// </summary>
    public static bool IsBundleConsumed(long runStartTime, string fingerprint)
    {
        try
        {
            string path = Path.Combine(RunDir(runStartTime), ConsumedBundlesFile);
            return File.Exists(path) && Array.IndexOf(File.ReadAllLines(path), fingerprint) >= 0;
        }
        catch (Exception e)
        {
            Log.Warn($"[{UnDoFloorMod.Id}] Could not read the consumed-bundle list; treating the bundle as already merged: {e.Message}");
            return true;
        }
    }

    /// <summary>
    /// Appends a bundle fingerprint to the consumed list. With <paramref name="createDir"/> false, nothing is written
    /// when the run folder is gone (an upload finishing after <see cref="PruneOtherRuns"/> must not resurrect the folder).
    /// </summary>
    public static void MarkBundleConsumed(long runStartTime, string fingerprint, bool createDir)
    {
        try
        {
            string dir = RunDir(runStartTime);
            if (!Directory.Exists(dir))
            {
                if (!createDir)
                {
                    return;
                }
                Directory.CreateDirectory(dir);
            }
            string path = Path.Combine(dir, ConsumedBundlesFile);
            if (File.Exists(path) && Array.IndexOf(File.ReadAllLines(path), fingerprint) >= 0)
            {
                return;
            }
            File.AppendAllText(path, fingerprint + "\n");
        }
        catch (Exception e)
        {
            Log.Warn($"[{UnDoFloorMod.Id}] Could not update the consumed-bundle list: {e.Message}");
        }
    }

    /// <summary>Removes every run folder except the one for <paramref name="keepRunStartTime"/>.</summary>
    public static void PruneOtherRuns(long keepRunStartTime)
    {
        if (!Directory.Exists(Root))
        {
            return;
        }
        string keep = RunDir(keepRunStartTime);
        foreach (string dir in Directory.GetDirectories(Root))
        {
            if (string.Equals(Path.GetFullPath(dir), Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            try
            {
                Directory.Delete(dir, recursive: true);
                Log.Info($"[{UnDoFloorMod.Id}] Pruned old run checkpoints at {dir}.");
            }
            catch (Exception e)
            {
                Log.Warn($"[{UnDoFloorMod.Id}] Could not prune {dir}: {e.Message}");
            }
        }
    }
}
