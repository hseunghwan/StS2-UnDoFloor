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

    // The map screen reads the act, map and visited coords straight from the RunState while it builds nodes
    // (boss/ancient art, traveled colour, jitter seed, path ticks). The CurrentActIndex setter has side effects
    // (it clears visited coords), so the swap goes through the backing fields and is undone right after SetMap.
    private static readonly AccessTools.FieldRef<RunState, int> CurrentActIndexField =
        AccessTools.FieldRefAccess<RunState, int>("_currentActIndex");

    private static readonly AccessTools.FieldRef<RunState, List<MapCoord>> VisitedMapCoordsField =
        AccessTools.FieldRefAccess<RunState, List<MapCoord>>("_visitedMapCoords");

    // The parchment background picks its textures from RunState.Act whenever its visibility changes; calling that
    // handler while the state is swapped repaints it for the browsed act. Node outlines are tinted with the act's
    // MapBgColor to hide them, so background and nodes must come from the same act or the outlines show through.
    private static readonly AccessTools.FieldRef<NMapScreen, NMapBg> MapBgField =
        AccessTools.FieldRefAccess<NMapScreen, NMapBg>("_mapBgContainer");

    private static readonly System.Reflection.MethodInfo MapBgRefresh =
        AccessTools.Method(typeof(NMapBg), "OnVisibilityChanged");

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
                    MapRewindUi.RefreshMarkers(_screen);
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
        // Full-rect, non-interactive holder so the arrows can sit at the screen edges. The top of the screen is off
        // limits: GlobalUi draws the TopBar (and relic row) above the map screen, so anything placed there is hidden.
        Control nav = new Control { Name = NavName, MouseFilter = Control.MouseFilterEnum.Ignore };
        nav.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // Everything sits on the left edge, vertically centred: nothing of the map screen lives there.
        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        PlaceLeft(_title, top: -80f, bottom: -48f);
        _left = new Button { Text = ModText.PreviousActButton, FocusMode = Control.FocusModeEnum.None };
        PlaceLeft(_left, top: -40f, bottom: 8f);
        _right = new Button { Text = ModText.NextActButton, FocusMode = Control.FocusModeEnum.None };
        PlaceLeft(_right, top: 16f, bottom: 64f);

        _left.Pressed += () => Step(screen, -1);
        _right.Pressed += () => Step(screen, 1);
        nav.AddChild(_left);
        nav.AddChild(_right);
        nav.AddChild(_title);
        screen.AddChild(nav);

        screen.Closed += () => ShowCurrentAct(screen);
        Refresh(screen);
    }

    private static void PlaceLeft(Control control, float top, float bottom)
    {
        control.SetAnchorsPreset(Control.LayoutPreset.CenterLeft);
        control.OffsetLeft = 40f;
        control.OffsetRight = 240f;
        control.OffsetTop = top;
        control.OffsetBottom = bottom;
        control.GrowVertical = Control.GrowDirection.Both;
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
        // Re-applied on every refresh so a language change while the run is loaded reaches the buttons too.
        _left.Text = ModText.PreviousActButton;
        _right.Text = ModText.NextActButton;
        _title.Text = ModText.ActHeader(viewed + 1, isPast: viewed != current);
        bool show = acts.Count > 0 || ViewedActIndex != null;
        if (screen.GetNodeOrNull(NavName) is Control control)
        {
            control.Visible = show;
        }
        Log.Info($"[{UnDoFloorMod.Id}] Act nav: current={current + 1} viewed={viewed + 1} pastActs=[{string.Join(",", acts.Select(a => a + 1))}] visible={show}.");
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
        SavedActMap pastMap = new SavedActMap(savedMap);
        SetMapAs(screen, runState, actIndex, pastMap, save.VisitedMapCoords);
        // Only the visited nodes of that act are meaningful; the "next floor" candidates SetMap computed must not
        // look travelable.
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

    /// <summary>
    /// Runs NMapScreen.SetMap while the RunState temporarily claims to be in <paramref name="actIndex"/> with
    /// <paramref name="map"/> and <paramref name="visited"/>, so every node is built as that act's node. SetMap is
    /// synchronous, so the real values are back before anything else can observe the state.
    /// </summary>
    private static void SetMapAs(NMapScreen screen, RunState runState, int actIndex, ActMap map, IReadOnlyList<MapCoord> visited)
    {
        ref int currentActIndex = ref CurrentActIndexField(runState);
        List<MapCoord> visitedList = VisitedMapCoordsField(runState);
        int savedActIndex = currentActIndex;
        ActMap savedMap = runState.Map;
        List<MapCoord> savedVisited = new List<MapCoord>(visitedList);
        _swapping = true;
        try
        {
            currentActIndex = actIndex;
            runState.Map = map;
            visitedList.Clear();
            visitedList.AddRange(visited);
            screen.SetMap(map, runState.Rng.Seed, clearDrawings: false);
            MapBgRefresh.Invoke(MapBgField(screen), null);
        }
        finally
        {
            currentActIndex = savedActIndex;
            runState.Map = savedMap;
            visitedList.Clear();
            visitedList.AddRange(savedVisited);
            _swapping = false;
        }
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
        MapBgRefresh.Invoke(MapBgField(screen), null);
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
