using System;
using Il2Cppframeworks;
using Il2CppMoon.Streaming;
using Il2CppMoon.TheLoop;
using Il2CppMoon.UnloadSceneTimesliced;
using UnityEngine;

namespace FasterLoading;

/// <summary>
/// Loosens the game's streaming limits while a teleport loading screen (fast travel, respawn) is up, and restores them
/// when it hides. See docs/internal.md for the game code behind each lever.
/// <para>Start: the first PlayerTeleportationHandlerQ3.CheckLoadingComplete call, which the game makes every frame from the
/// moment the screen is fully black until the new area is loaded. End: LoadingScreenAPI.HideLoadingScreen.</para>
/// </summary>
internal static class Speedup
{
    /// <summary>The game's "streaming idle for 1 s" constant in CheckLoadingComplete.</summary>
    private const float GameIdleWait = 1f;
    /// <summary>Give up and restore if CheckLoadingComplete stopped being called and no hide came (should not happen).</summary>
    private const float WatchdogSeconds = 60f;

    private static bool _active; // inside a teleport loading screen, our values applied
    private static float _start, _lastCheck;

    private static bool _parallelSet, _savedSequential;
    private static bool _budgetSet;
    private static double _savedTotalBudget;
    private static float _savedUnloadBudget;
    private static float _idleWait = GameIdleWait;
    private static bool _loggedBudget;

    /// <summary>Prefix of CheckLoadingComplete: begin on the first call, then shorten the idle wait.</summary>
    public static void OnCheckLoadingComplete(PlayerTeleportationHandlerQ3 handler)
    {
        _lastCheck = Time.realtimeSinceStartup;
        if (!_active) Begin();
        if (_idleWait >= GameIdleWait) return;

        // The game ends the screen once Time.time - m_lastStreamingBusyTime >= 1 s; while streaming is busy it sets the
        // field to Time.time itself, so moving it back only ever acts on idle frames.
        float idleFor = Time.time - handler.m_lastStreamingBusyTime;
        if (idleFor >= _idleWait && idleFor < GameIdleWait)
            handler.m_lastStreamingBusyTime = Time.time - GameIdleWait - 0.01f;
    }

    /// <summary>Postfix of HideLoadingScreen. The game also hides at the start of a teleport (before the screen shows);
    /// that call finds us inactive and does nothing.</summary>
    public static void OnHideLoadingScreen()
    {
        if (_active) End("hidden");
    }

    /// <summary>Per frame: the watchdog.</summary>
    public static void Update()
    {
        if (_active && Time.realtimeSinceStartup - _lastCheck > WatchdogSeconds) End("watchdog");
    }

    private static void Begin()
    {
        _active = true;
        _start = Time.realtimeSinceStartup;

        if (Prefs.ParallelLoading.Value)
        {
            try
            {
                _savedSequential = UpdateWorldStreaming.ShouldLoadPayloadsSequentially;
                UpdateWorldStreaming.ShouldLoadPayloadsSequentially = false;
                _parallelSet = true;
            }
            catch (Exception e) { FasterLoadingMod.Log.Warning("Parallel loading: " + e.Message); }
        }

        float budget = Prefs.WorkBudgetMs.Value;
        if (budget > 0)
        {
            try
            {
                var state = ViewFrame.Active?.Timeslicing.Item1;
                if (state != null)
                {
                    _savedTotalBudget = state.TotalFrameBudget;
                    _savedUnloadBudget = UnloadSceneTimeslicedState.TimesliceBudget;
                    // Only ever raise: never lower a budget the game (or a future update) set higher.
                    state.TotalFrameBudget = Math.Max(_savedTotalBudget, budget);
                    UnloadSceneTimeslicedState.TimesliceBudget = Math.Max(_savedUnloadBudget, budget);
                    _budgetSet = true;
                    if (!_loggedBudget)
                    {
                        _loggedBudget = true;
                        FasterLoadingMod.Log.Msg($"Work budget during loading screens: {state.TotalFrameBudget} ms (game: {_savedTotalBudget}), " +
                                                 $"unloading {UnloadSceneTimeslicedState.TimesliceBudget} ms (game: {_savedUnloadBudget})");
                    }
                }
            }
            catch (Exception e) { FasterLoadingMod.Log.Warning("Work budget: " + e.Message); }
        }

        _idleWait = Math.Clamp(Prefs.IdleWaitSeconds.Value, 0f, GameIdleWait);
    }

    private static void End(string why)
    {
        Restore();
        _active = false;
        float seconds = Time.realtimeSinceStartup - _start;
        if (Prefs.LogLoadTimes.Value || why != "hidden")
            FasterLoadingMod.Log.Msg($"Loading screen: {seconds:0.0} s" + (why != "hidden" ? $" (ended by {why})" : ""));
    }

    /// <summary>Put back every value we changed. Safe to call any time (also on unload / hot reload).</summary>
    public static void Restore()
    {
        if (_parallelSet)
        {
            try { UpdateWorldStreaming.ShouldLoadPayloadsSequentially = _savedSequential; }
            catch (Exception e) { FasterLoadingMod.Log.Warning("Restoring parallel loading: " + e.Message); }
            _parallelSet = false;
        }
        if (_budgetSet)
        {
            try
            {
                var state = ViewFrame.Active?.Timeslicing.Item1;
                if (state != null) state.TotalFrameBudget = _savedTotalBudget;
                UnloadSceneTimeslicedState.TimesliceBudget = _savedUnloadBudget;
            }
            catch (Exception e) { FasterLoadingMod.Log.Warning("Restoring work budget: " + e.Message); }
            _budgetSet = false;
        }
        _idleWait = GameIdleWait;
    }

    public static void Shutdown()
    {
        Restore();
        _active = false;
    }
}
