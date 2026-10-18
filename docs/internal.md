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

## How the old area unloads (verified in disassembly)

* `Moon.Streaming.UpdateStreamingGraph.Update` (0x8840CA0): a graph node nobody needs gets `UnloadTimer = Time.time`
  and its payloads are unloaded `UnloadTimerSeconds` = 10 s later (const), or at once when
  `StreamingGraphProcessor.ForceProcessUnloadsOnce` (@0x98) is set. The flag is cleared at the end of every Update.
  During a teleport only `CheckLoadingComplete` sets it, on the first idle frame: the old area waits for the new one.
* Streaming points during the screen (`SessionViewBinding.UpdateStreamingPositions`): `TeleportPreloadLocation` (@0x78)
  set and `LocalHeroPosition` (@0x38) null, so everything outside the destination is the old area.
* `UpdateWorldStreaming.UpdateStreaming` (0x8842670) starts at most one payload per frame: a load from the high or low
  priority queue, else one unload (`StartUnloadingPayloads` 0x88437C0 moves one payload to
  `LastFrameWillUnloadPayloads`; `FinishWorldStreaming.Unload` handles the whole list). Hard-coded, no setting.
* Il2CppInterop cannot read `Nullable<struct>` fields (the generated getter builds an `Il2CppSystem.Nullable` from the
  field address and crashes in `CreateGCHandle`): read the HasValue byte at `il2cpp_field_get_offset` instead.

## Levers (all view side, applied from the first CheckLoadingComplete call until HideLoadingScreen)

| Lever | Game | Mod | Where |
|---|---|---|---|
| Parallel payloads | `UpdateWorldStreaming.ShouldLoadPayloadsSequentially` (static) = true: nothing new starts while a load or unload is in flight | false | static field |
| Early unload | old area unloads once the new one is loaded | from the start | prefix on `CheckLoadingComplete`: set `ForceProcessUnloadsOnce` every frame while streaming is busy, `TeleportPreloadLocation` is set and `LocalHeroPosition` is not; stop at the first idle frame |
| Extra unloads | 1 per frame, only without a load to start | +8 per frame | postfix on `UpdateWorldStreaming.UpdateStreaming`: call `StartUnloadingPayloads` up to 8 more times (only with parallel payloads) |
| Idle wait | 1.0 s | 0.1 s and >= 3 idle frames | prefix on `CheckLoadingComplete`: count frames with `m_lastStreamingBusyTime` unchanged; when both are met, move it back by 1 s |

Every value is put back at `HideLoadingScreen`, on a 60 s watchdog (no CheckLoadingComplete call and no hide), and on
unload / hot reload. The game also calls `HideLoadingScreen` right at the start of a teleport, before the screen shows;
we are not active yet then. The 3-frame minimum keeps one slow frame from ending the idle wait before the unloads the
first idle frame asked for have started.

Removed in 1.1.0: the per-frame work budget (`TimeslicerState.TotalFrameBudget` 1 -> 12 ms,
`UnloadSceneTimeslicedState.TimesliceBudget` 0.8 -> 12 ms). The unloads were never budget-bound (one start per frame,
see above): 55 unloads took 1.11-1.16 s with it and 1.12 s without; the 1.1.0 A/B with and without it gave 5.8 s both.

Hooks: prefix `PlayerTeleportationHandlerQ3.CheckLoadingComplete(Frame)`, postfix `LoadingScreenAPI.HideLoadingScreen()`,
postfix `UpdateWorldStreaming.UpdateStreaming(GameStreamingState)`. No ref/struct parameters. No in-game setting (the
mod does nothing else, so off = uninstalled).

## Measurements 1.1.0 (2026-10-19, Mountain Gate <-> Marketplace, setups alternating every two trips)

Test PC (Ryzen 9 5900X, RTX 4060 Ti, NVMe SSD), seconds of loading screen:

| Setup | One way (92 loads, 52 unloads) | Back (49 loads, 93 unloads) | Per trip |
|---|---|---|---|
| Vanilla | 12.0, 11.9 | 8.0, 7.8 | 9.9 |
| 1.0.0 | 8.5, 8.8 | 6.0, 5.5, 5.5 | 7.2 |
| 1.1.0 without early unload | 7.5, 7.7, 7.8 | 3.9, 4.0 | 5.8 |
| 1.1.0 | 6.5, 6.6, 6.5 | 4.3, 4.4, 4.4 | 5.4 (-45%) |

With early unload the first old-area scene is gone 0.44 s into the screen and the last before the new area is loaded;
without it they start after the last load. 1.1.0 back-trips here carried the game's memory clean-up (~1 s, every
other trip at this pace), the vanilla/1.0.0 ones mostly on the other direction.

Steam Deck, second (warm) round:

| Setup | One way | Back | Per trip |
|---|---|---|---|
| Vanilla | 20.9 (first round: 27.2) | 13.4 (14.7) | 17.2 |
| 1.1.0 | 11.6 (11.7) | 5.7 (5.6) | 8.7 (-49%) |

On the Deck the memory clean-up takes ~2 s. Raw data: LoadTimer trips.csv files in the author's workspace.

## Measurements 1.0.0 (2026-10-08/09, author's PC, Mountain Gate <-> Marketplace; timed with a separate diagnostic mod, LoadTimer, not in this repo)

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
