using HarmonyLib;
using Il2Cppframeworks;
using Il2CppMoon.LoadingScreen;

namespace FasterLoading.Patches;

/// <summary>Called every frame while a teleport loading screen waits for the new area.</summary>
[HarmonyPatch(typeof(PlayerTeleportationHandlerQ3), nameof(PlayerTeleportationHandlerQ3.CheckLoadingComplete))]
internal static class CheckLoadingCompletePatch
{
    private static bool _reported; // runs every frame: log a failure once, not 60 times a second

    private static void Prefix(PlayerTeleportationHandlerQ3 __instance)
    {
        try { Speedup.OnCheckLoadingComplete(__instance); }
        catch (System.Exception e)
        {
            if (_reported) return;
            _reported = true;
            FasterLoadingMod.Log.Error("CheckLoadingComplete: " + e);
        }
    }
}

[HarmonyPatch(typeof(LoadingScreenAPI), nameof(LoadingScreenAPI.HideLoadingScreen))]
internal static class HideLoadingScreenPatch
{
    private static void Postfix()
    {
        try { Speedup.OnHideLoadingScreen(); }
        catch (System.Exception e) { FasterLoadingMod.Log.Error("HideLoadingScreen: " + e); }
    }
}
