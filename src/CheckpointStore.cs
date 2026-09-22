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
