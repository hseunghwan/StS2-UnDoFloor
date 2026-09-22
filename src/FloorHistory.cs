using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace StS2UnDoFloor;

/// <summary>
/// Every run save the game has written for the current run, in memory and mirrored to disk by
/// <see cref="CheckpointStore"/>. The game saves on room entry (RunManager.EnterMapPointInternal) and again when a
/// combat is won or an event finishes (the "pre-finished" save), so those two moments become the Entered / Completed
/// checkpoints of each floor.
/// </summary>
public static class FloorHistory
{
    private static readonly List<FloorCheckpoint> _checkpoints = new List<FloorCheckpoint>();

    private static long? _loadedRunStartTime;

    public static IReadOnlyList<FloorCheckpoint> Checkpoints => _checkpoints;

    public static event Action? Changed;

    public static IEnumerable<FloorCheckpoint> ForCoord(int actIndex, MapCoord coord)
    {
        return _checkpoints.Where(c => c.ActIndex == actIndex && c.Coord == coord).OrderBy(c => c.Kind);
    }

    /// <summary>Act indexes that have at least one checkpoint, ascending.</summary>
    public static IEnumerable<int> ActsWithCheckpoints()
    {
        return _checkpoints.Select(c => c.ActIndex).Distinct().OrderBy(a => a);
    }

    /// <summary>The most recent checkpoint of an act; its save carries that act's map and full visited-node list.</summary>
    public static FloorCheckpoint? LatestInAct(int actIndex)
    {
        return _checkpoints
            .Where(c => c.ActIndex == actIndex)
            .OrderByDescending(c => c.Coord.row)
            .ThenByDescending(c => c.Kind)
            .FirstOrDefault();
    }

    /// <summary>Makes sure the list reflects the run identified by <paramref name="runStartTime"/>, loading from disk if needed.</summary>
    public static void EnsureLoaded(long runStartTime)
    {
        if (_loadedRunStartTime == runStartTime)
        {
            return;
        }
        if (_loadedRunStartTime != null)
        {
            Log.Info($"[{UnDoFloorMod.Id}] Run changed; dropping {_checkpoints.Count} in-memory checkpoints.");
        }
        _checkpoints.Clear();
        _checkpoints.AddRange(CheckpointStore.Load(runStartTime));
        CheckpointStore.PruneOtherRuns(runStartTime);
        _loadedRunStartTime = runStartTime;
        Changed?.Invoke();
    }

    internal static void Record(SerializableRun save)
    {
        if (save.VisitedMapCoords.Count == 0)
        {
            // Between acts / before the first node there is nothing on the map to click.
            return;
        }
        EnsureLoaded(save.StartTime);
        FloorCheckpoint checkpoint = FloorCheckpoint.FromSave(save);
        _checkpoints.RemoveAll(c => c.SameSlot(checkpoint));
        _checkpoints.Add(checkpoint);
        CheckpointStore.Write(checkpoint);
        Log.Info($"[{UnDoFloorMod.Id}] Checkpoint recorded: {checkpoint} (total {_checkpoints.Count}).");
        Changed?.Invoke();
    }

    /// <summary>After rewinding to <paramref name="target"/>, everything that happened after it is a dead branch.</summary>
    internal static void TruncateAfter(FloorCheckpoint target)
    {
        List<FloorCheckpoint> dead = _checkpoints.Where(c => c.IsAfter(target)).ToList();
        if (dead.Count == 0)
        {
            return;
        }
        foreach (FloorCheckpoint checkpoint in dead)
        {
            _checkpoints.Remove(checkpoint);
            CheckpointStore.Delete(checkpoint);
        }
        Log.Info($"[{UnDoFloorMod.Id}] Dropped {dead.Count} checkpoints after {target}.");
        Changed?.Invoke();
    }
}

/// <summary>
/// Every run save passes through this overload (RunSaveManager.SaveRun(SerializableRun, bool)), including the
/// reload-count resave done when a save is loaded. Multiplayer saves are ignored: this mod is singleplayer only.
/// </summary>
[HarmonyPatch(typeof(RunSaveManager), nameof(RunSaveManager.SaveRun), typeof(SerializableRun), typeof(bool))]
internal static class RunSaveManager_SaveRun_Patch
{
    private static void Prefix(SerializableRun save, bool isMultiplayer)
    {
        if (isMultiplayer)
        {
            return;
        }
        try
        {
            FloorHistory.Record(save);
        }
        catch (Exception e)
        {
            Log.Error($"[{UnDoFloorMod.Id}] Failed to record checkpoint:\n{e}");
        }
    }
}
