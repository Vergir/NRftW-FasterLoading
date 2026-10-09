# Faster Loading: how it works

Game build 29466. Names are the IL2CPP dump's (interop: prefix `Il2Cpp` on the namespace, `frameworks` ->
`Il2Cppframeworks`). RVAs are for reference only.

## A teleport, phase by phase

| Phase | Typical | Code |
|---|---|---|
| Kneel at the whisper | ~1.4 s | `Quantum.FastTravelActionData` (StartUpLength), then `FastTravelAPI.UseFastTravel` (simulation) |
| Fade to black | ~0.45 s | `frameworks.PlayerTeleportationHandlerQ3.Bind` -> `LoadingScreenAPI.TryAllocate` / `PushAfterFadeOut`, `ScreenFader.ForceSmoothFade` |
| Screen up, load new area | 4-12 s | `<Bind>b__21_0` (fully black) -> `ShowForHandle` -> `ShowLoadingScreen`; each frame `Handler.Update` -> `CheckLoadingComplete` 0x870B1C0; streaming `Moon.Streaming.UpdateWorldStreaming.UpdateStreaming` -> `RuntimeSceneLoader.Load` (Addressables additive scenes) |
| Unload the old area | 1-3 s | `UpdateUnloadSceneTimesliced.Tick` (timesliced destroy) + Addressables unload |
| Idle wait | 1.0 s | `CheckLoadingComplete`: `Time.time - m_lastStreamingBusyTime (@0x18) < 1.0` -> not done |
| Cleanup sequence | 4-6 frames | `CleanupSequenceAPI.InitiateCleanupSequence`; on cleanup frame 3 `UpdateGCManagement.Tick` -> `GCManagementAPI.ForceGC` (full GC + `MoonUnloadUnusedAssetsSync`, ~1 s, at most every 20 s / 90 frames) |
| Done | | `Handler.UpdateProgress(1, done)` -> simulation `PlayerTeleportationSystem.CompleteTeleport` -> `PopAndDispose` -> `HideLoadingScreen` |

`CheckLoadingComplete` (verified in disassembly):

```
if !WorldStreaming.Busy: GraphProcessor.ForceProcessUnloadsOnce = true
else: m_lastStreamingBusyTime = Time.time
if Time.time - m_lastStreamingBusyTime < 1.0: return false
if LoadingScreenAPI.AnyInitializersRemaining(): return false
start the cleanup sequence; state = 2; return true
```

The first idle frame queues the old area's unloads; they make streaming busy again, so the idle timer restarts and the
unloads finish behind the screen.

## Levers (all view side, applied from the first CheckLoadingComplete call until HideLoadingScreen)

| Lever | Game value | Mod | Where |
|---|---|---|---|
| Parallel payloads | `UpdateWorldStreaming.ShouldLoadPayloadsSequentially` (static) = true: nothing new starts while a load or unload is in flight | false | static field |
| Total frame budget | `TimeslicerState.TotalFrameBudget` = 1.0 ms, minus Unity's last-frame integration time | 12 | `ViewFrame.Active.Timeslicing.Item1` |
| Unload budget | `UnloadSceneTimeslicedState.TimesliceBudget` (static) = 0.8 ms, capped by the remaining total | 12 | static field |
| Idle wait | 1.0 s | 0.25 s | prefix on `CheckLoadingComplete`: when idle for >= 0.25 s, move `m_lastStreamingBusyTime` back by 1 s |

Budgets are only ever raised (Math.Max with the game's value) and every value is put back at `HideLoadingScreen`, on a
60 s watchdog (no CheckLoadingComplete call and no hide), and on unload / hot reload. The game also calls
`HideLoadingScreen` right at the start of a teleport, before the screen shows; we are not active yet then.

Hooks: prefix `PlayerTeleportationHandlerQ3.CheckLoadingComplete(Frame)`, postfix `LoadingScreenAPI.HideLoadingScreen()`.
No ref/struct parameters. No in-game setting (removed before release: the mod does nothing else, so off = uninstalled).

## Measurements (2026-10-08/09, author's PC, Mountain Gate <-> Marketplace; timed with a separate diagnostic mod, LoadTimer, not in this repo)

| Config | One way (144 scene events) | Back (141-142) |
|---|---|---|
| Game | 11.2, 11.1 | 8.3, 8.2, 8.3 |
| Parallel only | 8.6, 8.8, 8.7 | 7.3, 6.3, 6.5 |
| Budget 12 + idle 0.25 + skip ForceGC (round 1, other pair) | 9.5 -> 7.2 | 12.2 -> 10.3 |
| **1.0.0** (parallel + budget 12 + idle 0.25) | **8.7, 8.0, 8.4** | **5.7, 5.8, 6.1** |

Steam Deck (2026-10-17, Mountain Gate <-> Marketplace, LoadTimer toggling the (since removed) Enabled pref per trip pair; loading screen
runs at 26-38 fps there, so the one-payload-per-frame serial loading costs more):

| | Game | 1.0.0 |
|---|---|---|
| One way | 20.3, 19.0, 19.0 (cold first: 30.4) | 15.2, 13.8, 13.6, 13.8 |
| Back | 13.8, 12.8, 11.4, 12.9 | 9.5, 8.1, 8.1, 9.5 |

Co-op (2026-10-17, ~3 h, 78 loading screens incl. Crucible floors): no mod warnings, no desync messages.

Which screens: every teleport with a loading screen goes through `PlayerTeleportationHandlerQ3` (fast travel, respawn,
Crucible floors seen in the log, dungeon entrances with a loading screen). Seamless cave entrances have no screen; the
first realm load from the main menu is a different path (`LoadingToGameStageState`) and is not changed.

The one-way trips of 1.0.0 still carry ~1.3 s after the last scene event: that is ForceGC (it runs every other trip at
this pace), kept on purpose.

Tried and dropped:

* **Uncapped frame rate** (`Application.targetFrameRate = -1`) during the screen: no gain. The frames take ~16 ms of
  real work (64 fps uncapped vs 60 capped on this PC), and the game starts at most one payload per frame anyway.
* **World cameras off** during the screen: -0.2 to -0.4 s, and the loading screen art went black (it needs a camera).
* **Skipping ForceGC**: saves ~1 s when it runs, but the full GC it does pushes the next automatic GC (0.4-1.2 s
  freeze, every 1-4 min) out of gameplay. Kept.
* **Kneel length** (`FastTravelActionData.StartUpLength`): simulation side, would desync co-op.

Not tried: `AppState.AllowedExpensiveFrameCount` (@0xC0; unlimited budgets while > 0), the first realm load from the
main menu (`LoadingToGameStageState`, different path).
