using MelonLoader;

namespace FasterLoading;

/// <summary>Tuning, stored in UserData/MelonPreferences.cfg under [FasterLoading]. There is no in-game setting: to play
/// without the mod, remove it.</summary>
internal static class Prefs
{
    public static MelonPreferences_Entry<bool> ParallelLoading = null!;
    public static MelonPreferences_Entry<float> WorkBudgetMs = null!;
    public static MelonPreferences_Entry<float> IdleWaitSeconds = null!;
    public static MelonPreferences_Entry<bool> LogLoadTimes = null!;

    public static void Init()
    {
        var cat = MelonPreferences.CreateCategory("FasterLoading", "Faster Loading");
        ParallelLoading = cat.CreateEntry("ParallelLoading", true,
            description: "Let the game load and unload several parts of the world at once during the loading screen (the game does them one by one).");
        WorkBudgetMs = cat.CreateEntry("WorkBudgetMs", 12f,
            description: "Milliseconds per frame the game may spend on its own loading work, mostly unloading the area you left, during the loading screen. " +
                         "The game uses 1 (0.8 for unloading). 0 = leave the game's values.");
        IdleWaitSeconds = cat.CreateEntry("IdleWaitSeconds", 0.25f,
            description: "How long loading must have been idle before the loading screen may end. The game waits 1 second. 1 or more = leave the game's wait.");
        LogLoadTimes = cat.CreateEntry("LogLoadTimes", true,
            description: "Write the length of every loading screen to the MelonLoader log.");
    }
}
