using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

[ModInitializer("ModInit")]
public static class ModStart
{
    public static void ModInit()
    {
        GD.Print("[FirstMod]Mod Initialized");
    }
}