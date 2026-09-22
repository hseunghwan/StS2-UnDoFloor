using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace StS2UnDoFloor;

/// <summary>
/// Left/right arrows on the map screen that page through the acts that have checkpoints. Viewing an earlier act
/// swaps the screen's map for that act's saved map (from the act's latest checkpoint) and marks its visited nodes as
/// Traveled so they can be clicked to rewind. Closing the map, or rewinding, puts the real map back.
/// </summary>
public static class ActBrowser
{
    private const string NavName = "UnDoFloorActNav";

    private static readonly AccessTools.FieldRef<NMapScreen, Dictionary<MapCoord, NMapPoint>> MapPointsField =
        AccessTools.FieldRefAccess<NMapScreen, Dictionary<MapCoord, NMapPoint>>("_mapPointDictionary");

    private static readonly AccessTools.FieldRef<NMapScreen, NMapMarker> MarkerField =
        AccessTools.FieldRefAccess<NMapScreen, NMapMarker>("_marker");

    private static readonly System.Reflection.MethodInfo RecalculateTravelability =
        AccessTools.Method(typeof(NMapScreen), "RecalculateTravelability");

    private static NMapScreen? _screen;
    private static Button? _left;
    private static Button? _right;
    private static Label? _title;
    private static bool _swapping;
    private static bool _subscribed;

    /// <summary>The act whose map is currently shown, or null when the screen shows the real current act.</summary>
    public static int? ViewedActIndex { get; private set; }

    /// <summary>Act whose checkpoints a clicked node refers to.</summary>
    public static int EffectiveActIndex(RunState runState) => ViewedActIndex ?? runState.CurrentActIndex;

    /// <summary>True while we are inside our own SetMap call, so the travelability postfix leaves our states alone.</summary>
    public static bool IsSwapping => _swapping;

    internal static void Reset()
    {
        ViewedActIndex = null;
    }

    /// <summary>Adds the arrow controls to the map screen once, then keeps them in sync.</summary>
    internal static void EnsureInstalled(NMapScreen screen)
    {
        if (!_subscribed)
        {
            // One static subscription; the screen it refreshes is whichever one is installed right now.
            FloorHistory.Changed += () =>
            {
                if (_screen != null && GodotObject.IsInstanceValid(_screen))
                {
                    Refresh(_screen);
                }
            };
            _subscribed = true;
        }
        _screen = screen;
        if (screen.HasNode(NavName))
        {
            Refresh(screen);
            return;
        }
        HBoxContainer nav = new HBoxContainer { Name = NavName, MouseFilter = Control.MouseFilterEnum.Pass };
        nav.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        nav.OffsetLeft = -260f;
        nav.OffsetRight = 260f;
        nav.OffsetTop = 40f;
        nav.GrowHorizontal = Control.GrowDirection.Both;
        nav.Alignment = BoxContainer.AlignmentMode.Center;
        nav.AddThemeConstantOverride("separation", 24);

        _left = new Button { Text = "<  Previous act", FocusMode = Control.FocusModeEnum.None };
        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(180f, 0f) };
        _right = new Button { Text = "Next act  >", FocusMode = Control.FocusModeEnum.None };
        _left.Pressed += () => Step(screen, -1);
        _right.Pressed += () => Step(screen, 1);
        nav.AddChild(_left);
        nav.AddChild(_title);
        nav.AddChild(_right);
        screen.AddChild(nav);

        screen.Closed += () => ShowCurrentAct(screen);
        Refresh(screen);
    }

    private static void Refresh(NMapScreen screen)
    {
        if (_left == null || _right == null || _title == null || !GodotObject.IsInstanceValid(_left))
        {
            return;
        }
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
        {
            return;
        }
        int current = runState.CurrentActIndex;
        int viewed = ViewedActIndex ?? current;
        List<int> acts = FloorHistory.ActsWithCheckpoints().Where(a => a < current).ToList();
        _left.Disabled = !acts.Any(a => a < viewed);
        _right.Disabled = viewed >= current;
        _title.Text = viewed == current ? $"Act {current + 1}" : $"Act {viewed + 1} (past)";
        Node? nav = screen.GetNodeOrNull(NavName);
        if (nav is Control control)
        {
            control.Visible = acts.Count > 0 || ViewedActIndex != null;
        }
    }

    private static void Step(NMapScreen screen, int direction)
    {
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null || FloorRewinder.IsBusy)
        {
            return;
        }
        int current = runState.CurrentActIndex;
        int viewed = ViewedActIndex ?? current;
        IEnumerable<int> candidates = direction < 0
            ? FloorHistory.ActsWithCheckpoints().Where(a => a < viewed).OrderByDescending(a => a)
            : FloorHistory.ActsWithCheckpoints().Append(current).Where(a => a > viewed).OrderBy(a => a);
        int? target = candidates.Cast<int?>().FirstOrDefault();
        if (target == null)
        {
            return;
        }
        if (target == current)
        {
            ShowCurrentAct(screen);
        }
        else
        {
            ShowPastAct(screen, runState, target.Value);
        }
    }

    private static void ShowPastAct(NMapScreen screen, RunState runState, int actIndex)
    {
        FloorCheckpoint? latest = FloorHistory.LatestInAct(actIndex);
        if (latest == null)
        {
            return;
        }
        SerializableRun save = latest.LoadSave();
        SerializableActMap? savedMap = actIndex < save.Acts.Count ? save.Acts[actIndex].SavedMap : null;
        if (savedMap == null)
        {
            Log.Warn($"[{UnDoFloorMod.Id}] Checkpoint {latest} has no saved map for act {actIndex + 1}.");
            return;
        }
        Log.Info($"[{UnDoFloorMod.Id}] Showing act {actIndex + 1} map from {latest}.");
        ViewedActIndex = actIndex;
        _swapping = true;
        try
        {
            screen.SetMap(new SavedActMap(savedMap), runState.Rng.Seed, clearDrawings: false);
        }
        finally
        {
            _swapping = false;
        }
        // SetMap marked the *current* act's visited coords; replace with the browsed act's.
        HashSet<MapCoord> visited = save.VisitedMapCoords.ToHashSet();
        foreach (NMapPoint point in MapPointsField(screen).Values)
        {
            point.State = visited.Contains(point.Point.coord) ? MapPointState.Traveled : MapPointState.Untravelable;
        }
        MarkerField(screen).HideMapPoint();
        screen.Drawings.Visible = false;
        MapRewindUi.HookVisitedNodes(screen);
        Refresh(screen);
    }

    private static void ShowCurrentAct(NMapScreen screen)
    {
        if (ViewedActIndex == null)
        {
            return;
        }
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        ViewedActIndex = null;
        if (runState == null || !GodotObject.IsInstanceValid(screen))
        {
            return;
        }
        Log.Info($"[{UnDoFloorMod.Id}] Showing current act {runState.CurrentActIndex + 1} map again.");
        screen.SetMap(runState.Map, runState.Rng.Seed, clearDrawings: false);
        if (!screen.IsVisible())
        {
            // SetMap only recalculates states while visible; do it now so the next Open finds the right states.
            RecalculateTravelability.Invoke(screen, null);
        }
        screen.Drawings.Visible = true;
        IReadOnlyList<MapCoord> coords = runState.VisitedMapCoords;
        if (coords.Count > 0)
        {
            screen.InitMarker(coords[coords.Count - 1]);
        }
        Refresh(screen);
    }
}
