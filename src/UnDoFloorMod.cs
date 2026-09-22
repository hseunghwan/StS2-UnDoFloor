using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace StS2UnDoFloor;

/// <summary>
/// Go back to an earlier floor of the current run.
/// Every run save the game writes (on room entry, and again when a combat/event finishes) is kept in memory as a
/// checkpoint; clicking a visited node on the map screen restores one of them.
/// </summary>
[ModInitializer(nameof(Init))]
public static class UnDoFloorMod
{
    public const string Id = "StS2-UnDoFloor";

    public static void Init()
    {
        new Harmony("cycle1223." + Id).PatchAll(Assembly.GetExecutingAssembly());
        Log.Info($"[{Id}] initialized");
    }
}
