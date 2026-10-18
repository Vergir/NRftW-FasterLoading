# Changelog

## 1.1.0

* The area you left now unloads during the loading screen, alongside loading the new one, and up to 9 parts start per
  frame instead of 1. The game started it only after the new area was loaded.
* The loading screen ends 0.1 s after loading is done (was 0.25 s), and not before 3 idle frames.
* Removed the per-frame work budget (`WorkBudgetMs`): measured, it made no difference. Its cfg entry is deleted.
* A saved `IdleWaitSeconds` of 0.25 (1.0.0's default) moves to the new default.

## 1.0.0

First release.

* Shorter fast-travel and respawn loading screens: parallel loading, a bigger per-frame budget for unloading the area
  you left, and 0.25 s instead of 1 s of idle wait at the end. Only while the loading screen is up.
* Logs the length of every loading screen.
