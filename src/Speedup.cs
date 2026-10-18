using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2Cppframeworks;
using Il2CppMoon.Streaming;
using Il2CppMoon.TheLoop;
using UnityEngine;

namespace FasterLoading;

/// <summary>
/// Loosens the game's streaming limits while a teleport loading screen (fast travel, respawn, dungeons) is up, and
/// restores them when it hides. See docs/internal.md for the game code behind each lever.
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

    private static float _idleWait = GameIdleWait;
    private static int _idleFramesNeeded;
    private static float _seenBusyTime = float.NaN; // m_lastStreamingBusyTime as of the previous call
    private static int _idleFrames;                 // calls in a row that found it unchanged = frames streaming stayed idle

    private static bool _earlyUnload;  // force the old area's unload decision from the start of the screen
    private static bool _earlyDone;    // streaming went idle once: from here on the game forces it itself
    private static int _extraUnloads;  // extra unload starts allowed per frame

    // Il2CppInterop cannot read Nullable<struct> fields (its getter crashes), so their HasValue byte is read directly.
    private static int _heroPositionOffset = -1, _preloadOffset = -1;
    private static bool _earlyUnloadBroken;

    /// <summary>Prefix of CheckLoadingComplete: begin on the first call, then shorten the idle wait.</summary>
    public static void OnCheckLoadingComplete(PlayerTeleportationHandlerQ3 handler)
    {
        _lastCheck = Time.realtimeSinceStartup;
        if (!_active) Begin();
        if (_earlyUnload && !_earlyDone) ForceUnloadDecision();
        if (_idleWait >= GameIdleWait) return;

        // While streaming is busy the game sets m_lastStreamingBusyTime to Time.time (after this prefix), so an unchanged
        // value means streaming stayed idle for one more frame.
        float busyTime = handler.m_lastStreamingBusyTime;
        if (busyTime == _seenBusyTime) _idleFrames++;
        else { _seenBusyTime = busyTime; _idleFrames = 0; }

        // The game ends the screen once Time.time - m_lastStreamingBusyTime >= 1 s. The first idle frame also asks for the
        // old area's unloads, which start a frame or two later and make streaming busy again: the frame minimum keeps one
        // long frame from ending the wait before they have started.
        float idleFor = Time.time - busyTime;
        if (idleFor >= _idleWait && _idleFrames >= _idleFramesNeeded && idleFor < GameIdleWait)
            handler.m_lastStreamingBusyTime = Time.time - GameIdleWait - 0.01f;
    }

    /// <summary>
    /// The game unloads a part of the world 10 s after nothing needs it any more, or at once when
    /// StreamingGraphProcessor.ForceProcessUnloadsOnce is set (read and cleared by the next streaming graph update). It sets
    /// that on the first frame with nothing left to load, so the old area only starts unloading after the new one is
    /// loaded. Setting it from the start queues the old area's unloads right away, alongside the loads. Only while the
    /// destination is the streaming point (TeleportPreloadLocation set, no hero position): then everything outside it
    /// is the old area. Stops at the first idle frame, where the game takes over.
    /// </summary>
    private static void ForceUnloadDecision()
    {
        try
        {
            var streaming = ViewFrame.Active?.Streaming.Item1;
            var graph = streaming?.GraphProcessor;
            var world = streaming?.ActiveStreamingState?.WorldStreaming;
            if (graph == null || world == null) return;
            if (!world.Busy) { _earlyDone = true; return; }
            if (_heroPositionOffset < 0)
            {
                _heroPositionOffset = FieldOffset("LocalHeroPosition");
                _preloadOffset = FieldOffset("TeleportPreloadLocation");
            }
            // Nullable<T> starts with its HasValue bool.
            bool preload = Marshal.ReadByte(graph.Pointer + _preloadOffset) != 0;
            bool hero = Marshal.ReadByte(graph.Pointer + _heroPositionOffset) != 0;
            if (preload && !hero) graph.ForceProcessUnloadsOnce = true;
        }
        catch (Exception e)
        {
            // Leave the game's own timing; the other levers keep working.
            _earlyUnload = false;
            if (_earlyUnloadBroken) return;
            _earlyUnloadBroken = true;
            FasterLoadingMod.Log.Warning("Early unload switched off: " + e.Message);
        }
    }

    private static int FieldOffset(string name)
    {
        var field = IL2CPP.GetIl2CppField(Il2CppClassPointerStore<StreamingGraphProcessor>.NativeClassPtr, name);
        if (field == IntPtr.Zero) throw new MissingFieldException("StreamingGraphProcessor." + name);
        return (int)IL2CPP.il2cpp_field_get_offset(field);
    }

    /// <summary>
    /// Postfix of UpdateWorldStreaming.UpdateStreaming: the game starts at most one load or unload per frame, and an
    /// unload only on frames without a load to start. During the screen, start up to N more queued unloads right after
    /// (FinishWorldStreaming handles every payload put on that frame's list). Only with parallel loading on: the game's
    /// one-at-a-time rule is skipped otherwise.
    /// </summary>
    public static void OnUpdateStreaming(GameStreamingState state)
    {
        if (!_active || _extraUnloads <= 0 || !_parallelSet || state?.WorldStreaming == null) return;
        for (int i = 0; i < _extraUnloads; i++)
        {
            if (!UpdateWorldStreaming.StartUnloadingPayloads(state.WorldStreaming)) break;
        }
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

        _idleWait = Math.Clamp(Prefs.IdleWaitSeconds.Value, 0f, GameIdleWait);
        _idleFramesNeeded = Math.Max(0, Prefs.IdleWaitFrames.Value);
        _seenBusyTime = float.NaN;
        _idleFrames = 0;

        _earlyUnload = Prefs.EarlyUnload.Value && !_earlyUnloadBroken;
        _earlyDone = false;
        _extraUnloads = Math.Max(0, Prefs.ExtraUnloadsPerFrame.Value);
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
        _idleWait = GameIdleWait;
        _earlyUnload = false;
        _extraUnloads = 0;
    }

    public static void Shutdown()
    {
        Restore();
        _active = false;
    }
}
