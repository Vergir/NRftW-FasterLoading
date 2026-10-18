using MelonLoader;

namespace FasterLoading;

/// <summary>Tuning, stored in UserData/MelonPreferences.cfg under [FasterLoading]. There is no in-game setting: to play
/// without the mod, remove it.</summary>
internal static class Prefs
{
    public static MelonPreferences_Entry<bool> ParallelLoading = null!;
    public static MelonPreferences_Entry<float> IdleWaitSeconds = null!;
    public static MelonPreferences_Entry<int> IdleWaitFrames = null!;
    public static MelonPreferences_Entry<bool> EarlyUnload = null!;
    public static MelonPreferences_Entry<int> ExtraUnloadsPerFrame = null!;
    public static MelonPreferences_Entry<bool> LogLoadTimes = null!;

    public static void Init()
    {
        var cat = MelonPreferences.CreateCategory("FasterLoading", "Faster Loading");
        ParallelLoading = cat.CreateEntry("ParallelLoading", true,
            description: "Let the game load and unload several parts of the world at once during the loading screen (the game does them one by one).");
        IdleWaitSeconds = cat.CreateEntry("IdleWaitSeconds", 0.1f,
            description: "How long loading must have been idle before the loading screen may end. The game waits 1 second. 1 or more = leave the game's wait.");
        IdleWaitFrames = cat.CreateEntry("IdleWaitFrames", 3,
            description: "And for at least this many frames, so that one slow frame cannot end the wait before the old area starts unloading.");
        EarlyUnload = cat.CreateEntry("EarlyUnload", true,
            description: "Start unloading the area you left at the start of the loading screen, alongside loading the new one. The game waits until the new area is loaded.");
        ExtraUnloadsPerFrame = cat.CreateEntry("ExtraUnloadsPerFrame", 8,
            description: "Parts of the old area that may start unloading per frame on top of the game's one. 0 = the game's pace. Needs ParallelLoading.");
        LogLoadTimes = cat.CreateEntry("LogLoadTimes", true,
            description: "Write the length of every loading screen to the MelonLoader log.");

        // 1.0.0 saved its default idle wait (0.25 s) in the cfg; move users who never changed it to the new default.
        if (IdleWaitSeconds.Value == 0.25f)
        {
            IdleWaitSeconds.Value = IdleWaitSeconds.DefaultValue;
            cat.SaveToFile(false);
        }
        // 1.0.0's WorkBudgetMs had no measurable effect and is gone.
        cat.DeleteEntry("WorkBudgetMs");
    }
}
