using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace StS2UnDoFloor;

/// <summary>
/// Lets the player click map nodes that have checkpoints to rewind to them, and marks those nodes with a small dot.
/// Nodes on the current path are disabled NButtons (their normal press path is dead) but Godot still delivers
/// _GuiInput to the control, so a left click works there. Nodes from an abandoned timeline may currently be
/// Travelable, where a left click must keep meaning "travel"; those take a right click instead.
/// </summary>
public static class MapRewindUi
{
    private const string HookedMeta = "undofloor_hooked";
    private const string MarkName = "UnDoFloorMark";
    private static readonly Color MarkColor = new Color(0.35f, 0.9f, 1f, 0.9f);

    private static readonly AccessTools.FieldRef<NMapScreen, Dictionary<MapCoord, NMapPoint>> MapPointsField =
        AccessTools.FieldRefAccess<NMapScreen, Dictionary<MapCoord, NMapPoint>>("_mapPointDictionary");

    private static AcceptDialog? _dialog;

    /// <summary>Called after the map screen reassigns node states; attaches the click hook to every node once.</summary>
    internal static void HookVisitedNodes(NMapScreen screen)
    {
        int hooked = 0;
        int traveled = 0;
        foreach (NMapPoint point in MapPointsField(screen).Values)
        {
            if (point.State == MapPointState.Traveled)
            {
                traveled++;
            }
            if (point.HasMeta(HookedMeta))
            {
                continue;
            }
            point.SetMeta(HookedMeta, true);
            NMapPoint captured = point;
            point.GuiInput += inputEvent => OnMapPointInput(captured, inputEvent);
            hooked++;
        }
        RefreshMarkers(screen);
        Log.Info($"[{UnDoFloorMod.Id}] Map nodes: {traveled} traveled, {hooked} newly hooked, {FloorHistory.Checkpoints.Count} checkpoints held.");
    }

    /// <summary>Shows a dot on every node of the displayed act that has a checkpoint, on the current path or not.</summary>
    internal static void RefreshMarkers(NMapScreen screen)
    {
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null || !GodotObject.IsInstanceValid(screen))
        {
            return;
        }
        int actIndex = ActBrowser.EffectiveActIndex(runState);
        foreach (NMapPoint point in MapPointsField(screen).Values)
        {
            bool has = FloorHistory.HasAny(actIndex, point.Point.coord);
            Node? existing = point.GetNodeOrNull(MarkName);
            if (has && existing == null)
            {
                ColorRect mark = new ColorRect
                {
                    Name = MarkName,
                    Color = MarkColor,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    Size = new Vector2(14f, 14f),
                    Position = new Vector2(point.Size.X - 6f, -8f),
                    ZIndex = 5
                };
                point.AddChild(mark);
            }
            else if (!has && existing != null)
            {
                existing.QueueFree();
            }
        }
    }

    private static void OnMapPointInput(NMapPoint point, InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton { Pressed: false } mouse)
        {
            return;
        }
        if (mouse.ButtonIndex != MouseButton.Left && mouse.ButtonIndex != MouseButton.Right)
        {
            return;
        }
        // A left click on a Travelable node is the game's own "travel here"; rewinding into that node needs a right click.
        if (mouse.ButtonIndex == MouseButton.Left && point.State == MapPointState.Travelable)
        {
            return;
        }
        Log.Info($"[{UnDoFloorMod.Id}] Map node clicked ({mouse.ButtonIndex}): {point.Point.coord} state={point.State} canRewind={FloorRewinder.CanRewind()}.");
        if (!FloorRewinder.CanRewind())
        {
            return;
        }
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
        {
            return;
        }
        int actIndex = ActBrowser.EffectiveActIndex(runState);
        List<FloorCheckpoint> checkpoints = FloorHistory.ForCoord(actIndex, point.Point.coord).ToList();
        if (checkpoints.Count == 0)
        {
            Log.Info($"[{UnDoFloorMod.Id}] No checkpoint for {point.Point.coord}; nothing to rewind to.");
            return;
        }
        bool onCurrentPath = ActBrowser.ViewedActIndex == null && runState.VisitedMapCoords.Contains(point.Point.coord);
        point.AcceptEvent();
        ShowDialog(point, checkpoints, onCurrentPath);
    }

    private static void ShowDialog(NMapPoint point, List<FloorCheckpoint> checkpoints, bool onCurrentPath)
    {
        NGame? game = NGame.Instance;
        if (game == null)
        {
            return;
        }
        CloseDialog();

        int floor = point.Point.coord.row + 1;
        int actNumber = checkpoints[0].ActIndex + 1;
        AcceptDialog dialog = new AcceptDialog
        {
            Title = $"Act {actNumber} - Floor {floor} - {point.Point.PointType}",
            DialogText = onCurrentPath
                ? $"Go back to act {actNumber}, floor {floor}?\nLater floors stay available as another timeline."
                : $"Jump to act {actNumber}, floor {floor} of an earlier timeline?\nYour current path stays available too.",
            Exclusive = true
        };
        dialog.GetOkButton().Visible = false;
        dialog.AddCancelButton("Cancel");
        foreach (FloorCheckpoint checkpoint in checkpoints)
        {
            string label = checkpoint.Kind == CheckpointKind.Entered
                ? "Redo this floor"
                : "Keep result, re-pick path";
            dialog.AddButton(label, right: true, action: checkpoint.Kind.ToString());
        }
        dialog.CustomAction += action =>
        {
            FloorCheckpoint? chosen = checkpoints.FirstOrDefault(c => c.Kind.ToString() == action.ToString());
            CloseDialog();
            if (chosen != null)
            {
                TaskHelper.RunSafely(FloorRewinder.RewindTo(chosen));
            }
        };
        dialog.Canceled += CloseDialog;
        dialog.CloseRequested += CloseDialog;

        _dialog = dialog;
        game.AddChild(dialog);
        dialog.PopupCentered();
    }

    private static void CloseDialog()
    {
        if (_dialog != null && GodotObject.IsInstanceValid(_dialog))
        {
            _dialog.QueueFree();
        }
        _dialog = null;
    }
}

[HarmonyPatch(typeof(NMapScreen), "RecalculateTravelability")]
internal static class NMapScreen_RecalculateTravelability_Patch
{
    private static void Postfix(NMapScreen __instance)
    {
        if (ActBrowser.IsSwapping)
        {
            // ActBrowser assigns its own node states right after this; it also hooks the nodes itself.
            return;
        }
        try
        {
            MapRewindUi.HookVisitedNodes(__instance);
            ActBrowser.EnsureInstalled(__instance);
        }
        catch (System.Exception e)
        {
            Log.Error($"[{UnDoFloorMod.Id}] Failed to hook map nodes:\n{e}");
        }
    }
}
