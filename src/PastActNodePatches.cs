using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace StS2UnDoFloor;

/// <summary>
/// Map nodes keep reading <c>_runState.Act</c> / <c>CurrentActIndex</c> live: the hover-out and press tweens fade
/// the outline to the act's background colour, and the hover tip looks up the node's history in the current act.
/// While <see cref="ActBrowser"/> shows a past act those reads must see that act, or an unhovered node on the act-1
/// map fades to act 2's colour (a yellow rim on purple). The swap uses the backing field, exactly like
/// ActBrowser.SetMapAs, because the CurrentActIndex setter clears the visited coords; every patched method is
/// synchronous, so the real value is back before the frame ends.
/// </summary>
[HarmonyPatch]
internal static class PastActNodePatches
{
    private static readonly AccessTools.FieldRef<RunState, int> CurrentActIndexField =
        AccessTools.FieldRefAccess<RunState, int>("_currentActIndex");

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "AnimUnhover", "AnimPressDown", "OnSelected" })
        {
            yield return AccessTools.Method(typeof(NNormalMapPoint), name);
            yield return AccessTools.Method(typeof(NAncientMapPoint), name);
        }
        yield return AccessTools.Method(typeof(NMapPoint), "OnFocus");
    }

    private static void Prefix(out int? __state)
    {
        __state = null;
        if (ActBrowser.ViewedActIndex is not int viewed)
        {
            return;
        }
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
        {
            return;
        }
        ref int current = ref CurrentActIndexField(runState);
        if (current == viewed)
        {
            return;
        }
        __state = current;
        current = viewed;
    }

    private static void Postfix(int? __state)
    {
        if (__state is not int saved)
        {
            return;
        }
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (runState != null)
        {
            CurrentActIndexField(runState) = saved;
        }
    }
}
