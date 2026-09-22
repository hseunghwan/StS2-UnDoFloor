using System;
using System.Text.Json;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Saves;

namespace StS2UnDoFloor;

public enum CheckpointKind
{
    /// <summary>The save written when the room was entered: redo the room itself.</summary>
    Entered,

    /// <summary>The save written when the room's combat/event finished: keep its outcome, re-pick the next node.</summary>
    Completed
}

/// <summary>
/// One restorable point of the run. The save is kept as JSON so nothing live can mutate it, and so it is exactly what
/// the game itself would have loaded from disk. The same JSON is what <see cref="CheckpointStore"/> writes to disk.
/// </summary>
public sealed class FloorCheckpoint
{
    public long RunStartTime { get; }

    public int ActIndex { get; }

    public MapCoord Coord { get; }

    public CheckpointKind Kind { get; }

    public string Json { get; }

    private FloorCheckpoint(SerializableRun save, string json)
    {
        if (save.VisitedMapCoords.Count == 0)
        {
            throw new ArgumentException("A checkpoint needs a current map coordinate.", nameof(save));
        }
        RunStartTime = save.StartTime;
        ActIndex = save.CurrentActIndex;
        Coord = save.VisitedMapCoords[save.VisitedMapCoords.Count - 1];
        Kind = save.PreFinishedRoom == null ? CheckpointKind.Entered : CheckpointKind.Completed;
        Json = json;
    }

    public static FloorCheckpoint FromSave(SerializableRun save)
    {
        return new FloorCheckpoint(save, JsonSerializer.Serialize(save, JsonSerializationUtility.GetTypeInfo<SerializableRun>()));
    }

    public static FloorCheckpoint FromJson(string json)
    {
        SerializableRun save = JsonSerializer.Deserialize(json, JsonSerializationUtility.GetTypeInfo<SerializableRun>())
            ?? throw new InvalidOperationException("Checkpoint JSON deserialized to null.");
        return new FloorCheckpoint(save, json);
    }

    /// <summary>Floor number as the game displays it for this act (row 0 is floor 1).</summary>
    public int Floor => Coord.row + 1;

    /// <summary>Stable file name: one file per (act, node, kind) slot.</summary>
    public string FileName => $"act{ActIndex:D2}_row{Coord.row:D2}_col{Coord.col:D2}_{Kind}.json";

    public bool SameSlot(FloorCheckpoint other)
    {
        return ActIndex == other.ActIndex && Coord == other.Coord && Kind == other.Kind;
    }

    /// <summary>True if this checkpoint lies after <paramref name="target"/> on the run's timeline.</summary>
    public bool IsAfter(FloorCheckpoint target)
    {
        if (ActIndex != target.ActIndex)
        {
            return ActIndex > target.ActIndex;
        }
        if (Coord.row != target.Coord.row)
        {
            return Coord.row > target.Coord.row;
        }
        // Same floor: Completed comes after Entered.
        return Kind == CheckpointKind.Completed && target.Kind == CheckpointKind.Entered;
    }

    public SerializableRun LoadSave()
    {
        return JsonSerializer.Deserialize(Json, JsonSerializationUtility.GetTypeInfo<SerializableRun>())
            ?? throw new InvalidOperationException("Checkpoint JSON deserialized to null.");
    }

    public override string ToString()
    {
        return $"act {ActIndex + 1} floor {Floor} ({Coord}) {Kind}";
    }
}
