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
/// Lets the player left-click any map node that has a checkpoint to rewind to it, and marks those nodes with a dot.
/// Godot emits the gui_input signal (our handler) before calling the control's _GuiInput (the game's press logic),
/// so on a node that is both Travelable and has a checkpoint we open our dialog and, via a Harmony prefix on
/// NMapPoint.OnRelease, stop the game from also travelling. The dialog then offers "Travel here" for that case.
/// </summary>
public static class MapRewindUi
{
    private const string HookedMeta = "undofloor_hooked";
    private const string MarkName = "UnDoFloorMark";

    /// <summary>The Mangled Tiara relic art: the only crown-shaped sprite the game ships, worn by boss nodes that have a checkpoint.</summary>
    private const string CrownTexturePath = "res://images/atlases/relic_atlas.sprites/mangled_tiara.tres";
    private const float CrownSize = 90f;
    private static readonly Color MarkColor = new Color(0.35f, 0.9f, 1f, 1f);

    private static readonly AccessTools.FieldRef<NMapScreen, Dictionary<MapCoord, NMapPoint>> MapPointsField =
        AccessTools.FieldRefAccess<NMapScreen, Dictionary<MapCoord, NMapPoint>>("_mapPointDictionary");

    private static readonly System.Reflection.MethodInfo OnReleaseMethod =
        AccessTools.Method(typeof(NMapPoint), "OnRelease");

    private static AcceptDialog? _dialog;

    /// <summary>Set while we deliberately forward a click to the game's travel logic from the dialog.</summary>
    private static bool _forwardingTravel;

    /// <summary>
    /// True when the game's own release handling for this node should be skipped because our dialog took the click.
    /// </summary>
    internal static bool ShouldInterceptRelease(NMapPoint point)
    {
        if (_forwardingTravel || !FloorRewinder.CanRewind())
        {
            return false;
        }
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        return runState != null && FloorHistory.HasAny(ActBrowser.EffectiveActIndex(runState), point.Point.coord);
    }

    private static void TravelTo(NMapPoint point)
    {
        if (!GodotObject.IsInstanceValid(point))
        {
            return;
        }
        _forwardingTravel = true;
        try
        {
            // Runs the game's own travel checks (FTUE, drawing mode, on-screen) exactly as a plain click would.
            OnReleaseMethod.Invoke(point, null);
        }
        finally
        {
            _forwardingTravel = false;
        }
    }

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

    /// <summary>
    /// Gives every node of the displayed act that has a checkpoint a sky-blue outline, on the current path or not.
    /// Normal and ancient map points already carry an "Outline" TextureRect (the icon's outline sprite from the
    /// compressed atlas) that the game tints on hover; we clone it, tint the clone and leave the original alone so the
    /// game's hover tweens never fight ours. Boss nodes have no outline sprite and get a small dot instead.
    /// </summary>
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
            Node? existing = point.FindChild(MarkName, recursive: true, owned: false);
            if (has && existing == null)
            {
                AddMark(point);
            }
            else if (!has && existing != null)
            {
                existing.QueueFree();
            }
        }
    }

    private static void AddMark(NMapPoint point)
    {
        if (point.FindChild("Outline", recursive: true, owned: false) is TextureRect { Texture: not null } outline)
        {
            TextureRect mark = (TextureRect)outline.Duplicate();
            mark.Name = MarkName;
            mark.Visible = true;
            mark.Modulate = MarkColor;
            mark.SelfModulate = Colors.White;
            mark.MouseFilter = Control.MouseFilterEnum.Ignore;
            // The outline sprites also carry interior highlight strokes (very visible on the big ancient nodes).
            // Drawing the clone behind its parent, the icon art, hides everything inside the silhouette and leaves
            // only the rim that sticks out past the icon.
            mark.ShowBehindParent = true;
            outline.GetParent().AddChild(mark);
            return;
        }
        // Boss nodes are Spine animations with no outline sprite: crown them instead.
        Texture2D? texture = ResourceLoader.Load<Texture2D>(CrownTexturePath, null, ResourceLoader.CacheMode.Reuse);
        if (texture == null)
        {
            Log.Warn($"[{UnDoFloorMod.Id}] Crown texture {CrownTexturePath} is not available; boss node left unmarked.");
            return;
        }
        TextureRect crown = new TextureRect
        {
            Name = MarkName,
            Texture = texture,
            Modulate = MarkColor,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = new Vector2(CrownSize, CrownSize),
            // Sits on the top scale of the boss art (the art starts a little inside the rect), drawn over it.
            Position = new Vector2((point.Size.X - CrownSize) * 0.5f, -20f)
            // No ZIndex: it is absolute within the canvas layer and would float above overlays such as the pause menu.
        };
        point.AddChild(crown);
    }

    private static void OnMapPointInput(NMapPoint point, InputEvent inputEvent)
    {
        // Left button only: the right button is the map's drawing/erasing tool.
        if (inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mouse)
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
            Title = ModText.DialogTitle(actNumber, floor, point.Point.PointType),
            DialogText = onCurrentPath
                ? ModText.RewindBody(actNumber, floor)
                : ModText.JumpBody(actNumber, floor),
            Exclusive = true
        };
        dialog.GetOkButton().Visible = false;
        dialog.AddCancelButton(ModText.Cancel);
        if (point.IsEnabled)
        {
            // The click also meant "go here" in the game; keep that reachable.
            dialog.AddButton(ModText.TravelHere, right: true, action: "Travel");
        }
        foreach (FloorCheckpoint checkpoint in checkpoints)
        {
            string label = checkpoint.Kind == CheckpointKind.Entered
                ? ModText.RedoFloor
                : ModText.KeepResult;
            dialog.AddButton(label, right: true, action: checkpoint.Kind.ToString());
        }
        dialog.CustomAction += action =>
        {
            string name = action.ToString();
            CloseDialog();
            if (name == "Travel")
            {
                TravelTo(point);
                return;
            }
            FloorCheckpoint? chosen = checkpoints.FirstOrDefault(c => c.Kind.ToString() == name);
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

/// <summary>Skips the game's travel-on-release when our dialog has taken the click (see ShouldInterceptRelease).</summary>
[HarmonyPatch(typeof(NMapPoint), "OnRelease")]
internal static class NMapPoint_OnRelease_Patch
{
    private static bool Prefix(NMapPoint __instance)
    {
        try
        {
            return !MapRewindUi.ShouldInterceptRelease(__instance);
        }
        catch (System.Exception e)
        {
            Log.Error($"[{UnDoFloorMod.Id}] OnRelease intercept failed; letting the game handle the click:\n{e}");
            return true;
        }
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
