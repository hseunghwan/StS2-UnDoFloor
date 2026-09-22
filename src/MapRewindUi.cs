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
/// Lets the player click visited (Traveled) nodes on the map screen to rewind to them.
/// Traveled map points are disabled NButtons, so their normal press path is dead, but Godot still delivers
/// _GuiInput to the control; we listen to that signal directly and pop a dialog offering the floor's checkpoints.
/// </summary>
public static class MapRewindUi
{
    private const string HookedMeta = "undofloor_hooked";

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
        Log.Info($"[{UnDoFloorMod.Id}] Map nodes: {traveled} traveled, {hooked} newly hooked, {FloorHistory.Checkpoints.Count} checkpoints held.");
    }

    private static void OnMapPointInput(NMapPoint point, InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            return;
        }
        Log.Info($"[{UnDoFloorMod.Id}] Map node clicked: {point.Point.coord} state={point.State} canRewind={FloorRewinder.CanRewind()}.");
        if (point.State != MapPointState.Traveled || !FloorRewinder.CanRewind())
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
        point.AcceptEvent();
        ShowDialog(point, checkpoints);
    }

    private static void ShowDialog(NMapPoint point, List<FloorCheckpoint> checkpoints)
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
            DialogText = $"Go back to floor {floor}?\nEverything after that point will be lost.",
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
