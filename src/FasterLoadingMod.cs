using FasterLoading;
using MelonLoader;

[assembly: MelonInfo(typeof(FasterLoadingMod), "Faster Loading", "1.1.0", "vergir")]
[assembly: MelonGame("Moon Studios", "NoRestForTheWicked")]
// MelonLoader would otherwise apply every [HarmonyPatch] itself, twice with our PatchAll.
[assembly: HarmonyDontPatchAll]

namespace FasterLoading;

/// <summary>Entry point: shorter fast-travel and respawn loading screens. See docs/internal.md.</summary>
public class FasterLoadingMod : MelonMod
{
    public static FasterLoadingMod Instance { get; private set; } = null!;
    public static MelonLogger.Instance Log => Instance.LoggerInstance;

    public override void OnInitializeMelon()
    {
        Instance = this;
        Prefs.Init();
        HarmonyInstance.PatchAll(typeof(FasterLoadingMod).Assembly);
    }

    /// <summary>Unload / hot reload: put the game's values back.</summary>
    public override void OnDeinitializeMelon() => Speedup.Shutdown();

    public override void OnUpdate() => Speedup.Update();
}
