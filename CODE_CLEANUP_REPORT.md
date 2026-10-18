# Code Cleanup Report — Kinetic Maze

Scope: **C# code only.** No scenes, prefabs, ScriptableObjects, or other assets were
created, modified, or deleted by this pass. All `.meta` files that moved with `.cs`
files kept their original GUIDs (verified against `HEAD` — see §1).

---

## 1. Files reorganized

Every `.cs` was moved together with its `.meta`; GUIDs were verified unchanged
against `HEAD` for a spot-check set (`EconomyManager`, `SaveManager`, `AutoPlayTest`,
`PlayerMovement`, `ObjectiveSystem`, `PlayerImpactFeedback`, `ShopDatabase`) and the
moves were performed by plain `mv`, which cannot alter meta content.

| Old Path | New Path |
|---|---|
| `Assets/Scripts/Core/Managers/GameManager.cs` | `Assets/Scripts/Core/GameManager.cs` |
| `Assets/Scripts/Core/Managers/GameSession.cs` | `Assets/Scripts/Core/GameSession.cs` |
| `Assets/Scripts/Core/Managers/GameDataManager.cs` | `Assets/Scripts/Core/GameDataManager.cs` |
| `Assets/Scripts/Core/Managers/ObjectPoolManager.cs` | `Assets/Scripts/Core/ObjectPoolManager.cs` |
| `Assets/Scripts/Core/Managers/CutsceneManager.cs` | `Assets/Scripts/Core/CutsceneManager.cs` |
| `Assets/Scripts/Core/Utilities/ReturnToPool.cs` | `Assets/Scripts/Core/ReturnToPool.cs` |
| `Assets/Scripts/Core/Utilities/CinemachineCameraShake2D.cs` | `Assets/Scripts/Feedback/CinemachineCameraShake2D.cs` |
| `Assets/Scripts/Core/Utilities/CinemachineCameraZoom2D.cs` | `Assets/Scripts/Feedback/CinemachineCameraZoom2D.cs` |
| `Assets/Scripts/Core/Services/SaveManager.cs` | `Assets/Scripts/Save/SaveManager.cs` |
| `Assets/Scripts/Gameplay/Player/PlayerMovement.cs` | `Assets/Scripts/Gameplay/PlayerMovement.cs` |
| `Assets/Scripts/Gameplay/Player/PlayerImpactFeedback.cs` | `Assets/Scripts/Feedback/PlayerImpactFeedback.cs` |
| `Assets/Scripts/Gameplay/Player/PlayerLineTrail.cs` | `Assets/Scripts/Feedback/PlayerLineTrail.cs` |
| `Assets/Scripts/Gameplay/Player/PlayerSquashStretch.cs` | `Assets/Scripts/Feedback/PlayerSquashStretch.cs` |
| `Assets/Scripts/Gameplay/Player/StarCollectFeedback.cs` | `Assets/Scripts/Feedback/StarCollectFeedback.cs` |
| `Assets/Scripts/Gameplay/Tiles/{BrakeTile,BrittleFloor,CrackedBlock,DeflectionWedge,ICrushable,MazeTile}.cs` | `Assets/Scripts/Gameplay/` |
| `Assets/Scripts/Objectives/{CollectPickupsObjective,ObjectiveBase,ObjectiveDefinition,ObjectiveSystem,ObjectiveType,ReachExitObjective}.cs` | `Assets/Scripts/Level/` |
| `Assets/Scripts/Economy/EconomyManager.cs` | `Assets/Scripts/Services/EconomyManager.cs` |
| `Assets/Scripts/Progression/{ProgressionManager,MissionService}.cs` | `Assets/Scripts/Services/` |
| `Assets/Scripts/Shop/ShopManager.cs` | `Assets/Scripts/Services/ShopManager.cs` |
| `Assets/Scripts/Shop/{ShopDatabase,ShopItemDefinition,ShopCostType}.cs` | `Assets/Scripts/Data/` |
| `Assets/Scripts/Rewards/{BlindBoxRewardEntry,BlindBoxService,CoinBonusService,DailyRewardService,RewardBundle,RewardCalculator,RewardItem,RewardService,RewardType}.cs` | `Assets/Scripts/Services/` |
| `Assets/Scripts/Monetization/{AdRewardService,IAPManager,IAPResult,IIAPService,IRewardedAdService,MockIAPService,MockRewardedAdService,RewardedAdPlacement,RewardedAdResult}.cs` | `Assets/Scripts/Services/` |
| `Assets/Scripts/Analytics/{AnalyticsEventObserver,AnalyticsEventRecord,AnalyticsEvents,AnalyticsService,IAnalyticsProvider,IAnalyticsService,MockAnalyticsProvider}.cs` | `Assets/Scripts/Services/` |
| `Assets/Scripts/Dev/{AutoPlayTest,FirebaseSmokeTest}.cs` | `Assets/Scripts/Tests/` |

Empty source folders and their folder `.meta` files were removed:
`Analytics/`, `Economy/`, `Monetization/`, `Objectives/`, `Progression/`, `Rewards/`,
`Shop/`, `Dev/`, `Dialogue/` (meta only — folder was already empty),
`Core/Managers/`, `Core/Services/`, `Core/Utilities/`, `Core/Interfaces/`,
`Gameplay/Player/`, `Gameplay/Tiles/`.

`UI/Core`, `UI/Panels`, `UI/Widgets`, `Audio`, `Data`, `Firebase`, `Input`,
`Level`, `Editor` were already at target locations and were not moved.

Final structure:

```
Assets/Scripts/
├── Core/          (GameManager, GameSession, GameDataManager, ObjectPoolManager, CutsceneManager, ReturnToPool)
├── Gameplay/      (movement, tiles, collectibles, hazards, exit, spawn, tiers)
├── Level/         (flow, loader, root, state, difficulty + all objective classes)
├── Input/
├── UI/            (Core/, Panels/, Widgets/)
├── Services/      (economy, progression, missions, shop, rewards, monetization, analytics)
├── Save/          (SaveManager)
├── Firebase/
├── Audio/
├── Feedback/      (impact feedback, squash/stretch, trail, star VFX, camera shake/zoom)
├── Data/          (ProfileData, LevelData, CampaignDatabase, shop definitions)
├── Editor/
└── Tests/         (AutoPlayTest, FirebaseSmokeTest)
```

## 2. Files modified

### `Assets/Scripts/UI/Panels/PauseMenuUI.cs`
- **Reason:** `UIPanelType.Pause` was mapped in the scene but the component did not
  implement `IUIPanel`, so `UIManager` skipped registration and the HUD menu button
  could not open pause.
- **Changed:** added `IUIPanel`; `IsPaused` now queries `UIManager` registration
  instead of panel `activeSelf`; `Show()`/`Hide()` are null-safe; removed unused
  `using System;` and the never-subscribed `OnSettingsRequested` event;
  `Show()` now sets the level title (previously done in `Awake`, where it could
  read a null `CurrentLevelData`); Settings button routes through
  `UIManager.OpenPanel(UIPanelType.Setting)`.
- **Behavior impact:** Pause registers and opens via `UIManager`. Ownership is
  preserved: Resume → `UIManager.ClosePanelIfOpen`, Restart →
  `LevelFlowController.RestartLevel`, Quit → `LevelFlowController.ReturnToMap`.

### `Assets/Scripts/UI/Panels/InGameHUD.cs`
- **Reason:** dual move-counter writers (`OnTileCrossed → UpdateMoves` vs
  `OnMoveStopped`) produced inconsistent values; `movesUsed` was never reset;
  `StarCollectible.OnAnyStarCollected` was subscribed but never unsubscribed.
- **Changed:** `movesUsed`/`moveLimit`/`starEmpty`/`starFilled` fields made
  explicit; single writer `RefreshMovesText()` driven only by
  `OnMoveStopped → HandleMoveCompleted`; `movesUsed = 0` reset in
  `HandleLevelStarted`; missing `OnDestroy` unsubscribes added
  (`OnAnyStarCollected`, `OnMoveStopped`, flow/economy/progression events);
  `HandleStarCollected` reads `LevelRoot.CollectedStarCount` for the star icons;
  menu button null-guards `UIManager` and opens `UIPanelType.Pause`.
- **Behavior impact:** one authoritative move counter (shows `movesUsed`, or
  `moveLimit - movesUsed` when a limit is configured), correct on restart; no
  more event accumulation. `OnTileCrossed` remains on `PlayerMovement` for
  gameplay/VFX use.

### `Assets/Scripts/Level/ObjectiveSystem.cs` (was `Objectives/`)
- **Reason:** `ResetObjective()` re-subscribed `OnCompleted` handlers after each
  reset, accumulating one duplicate callback per restart.
- **Changed:** `ResetObjective()` now only calls `ObjectiveBase.Reset()`
  (progress only); the single `OnCompleted` subscription lives in `Initialize()`;
  `OnDestroy` unsubscribes `OnCompleted` and `LevelRoot.OnCollectibleCollected`;
  removed a duplicated optional-objective re-subscription loop.
- **Behavior impact:** identical level-completion behavior; repeated restarts no
  longer multiply completion callbacks.

### `Assets/Scripts/Core/GameManager.cs` (was `Core/Managers/`)
- **Reason:** dead pause infrastructure duplicated `UIManager` ownership.
- **Changed:** removed `PauseGame()`, `ResumeGame()`, `PauseResumeGame()`,
  `OnGamePaused`, `OnGameResume`, and the unused `CinemachineCamera` field.
  Kept `Instance`, scene-load `timeScale = 1` reset, and the input re-enable
  coroutine — still required scene-initialization behavior.
- **Behavior impact:** none — all removed members had zero call sites. Pause
  ownership is solely `UIManager`.

### `Assets/Scripts/UI/Core/UIManager.cs`
- **Reason:** invalid panel mappings were silently skipped or logged only as
  warnings, which is what hid the Pause defect.
- **Changed:** registration now logs explicit `Debug.LogError` entries naming the
  panel type and GameObject for: unassigned `panelObject`, non-scene objects,
  missing `IUIPanel`, and duplicate `UIPanelType` mappings.
- **Behavior impact:** same registration outcome, but misconfiguration is now
  loud and identifiable instead of silent.

### `Assets/Scripts/UI/Panels/MainMenuUI.cs`
- **Reason:** duplicated navigation fallbacks next to `UIManager` ownership;
  level header was always empty (`CampaignDatabase.GetNextLevel(null)` returns
  null for the first level).
- **Changed:** removed `SetPanelVisibleFallback`, the `comingSoonModal`/
  `comingSoonText`/`closeComingSoonButton`/`powerupsPanel`/`missionsPanel`/
  `settingsPanel` serialized fields and `ShowComingSoon`; tab selection now goes
  straight through `UIManager.OpenPanel`. Verified safe first: the scene maps
  Map/Shop/Powerups/Mission/Setting/DailyReward/CoinBonus in `UIManager` and all
  target panel scripts implement `IUIPanel`. Level header now uses
  `FindNextUncompletedLevel()` (first uncompleted campaign level).
- **Behavior impact:** identical UI flow on the shipped scene; header actually
  displays the next level name.
- **Note:** removed fields were serialized — if any *other* scene (e.g.
  `TotMTest`) wired them, it now relies on the `UIManager` path instead.

### `Assets/Scripts/Gameplay/PlayerMovement.cs` (was `Gameplay/Player/`)
- **Reason:** dead code.
- **Changed:** removed `OnTierChanged` event (zero subscribers — tiers remain
  readable via `CurrentTier`), its `lastTier` tracking field, and the
  invoke sites in dash-progress/stop handling.
- **Behavior impact:** none — nothing subscribed. `OnTileCrossed` kept (used by
  tier/dash logic consumers).

### `Assets/Scripts/Services/ProgressionManager.cs` (was `Progression/`)
- **Changed:** removed `OnLevelCompleted` event and its invoke — zero
  subscribers; completion already flows through `LevelFlowController`.
- **Behavior impact:** none.

### `Assets/Scripts/Audio/SoundManager.cs`
- **Changed:** removed `evolutionSounds` field + `PlayEvolutionSound()`
  (HouseFlow remnant, no callers), `ChangeSoundVolume()` cycling helper
  (no callers; `SetSoundVolume` retained), `using System.Collections.Generic`
  adjusted to match remaining members.
- **Behavior impact:** none — no UnityEvent references existed in scenes/prefabs
  (verified by grep on `m_MethodName`).

### `Assets/Scripts/Audio/MusicManager.cs`
- **Changed:** removed `MusicTrack` struct, `tracks` field, name-based
  `GetClipFromName`/`PlayMusic(string)`/`PlayAmbient(string)` (legacy HouseFlow
  lookup, no callers), `ChangeMusicVolume()` helper, `DuckMusic()` (DOTween —
  only remaining DG.Tweening usage, no callers), and `using DG.Tweening;`.
- **Behavior impact:** none — clip-based `PlayMusicClip`/`PlayAmbientClip` and
  `SceneMusicEntry` flow are the live paths; no serialized UnityEvent targets
  removed.

### `Assets/Scripts/Level/LevelLoader.cs`
- **Changed:** removed `ReloadCurrentLevel()` — zero callers;
  `LevelFlowController` drives resets through `LevelRoot`.
- **Behavior impact:** none.

### `Assets/Scripts/Core/GameSession.cs`, `Assets/Scripts/UI/Panels/RewardOverlayUI.cs`
- **Changed:** removed unused `using` directives only
  (`SceneManagement`, `Playables`, `Collections`; `System.Linq`).
- **Behavior impact:** none.

### Cosmetic/comment-only cleanups (earlier round of this pass)
- `Assets/Scripts/Firebase/AuthResult.cs`, `Assets/Scripts/Firebase/IAuthService.cs`,
  `Assets/Scripts/Level/LevelDifficulty.cs`: removed stale/duplicated doc
  comments; no code changes.

## 3. Dead code removed

**Deleted files:**
- `Assets/Scripts/Core/Interfaces/IInteractable.cs` — unimplemented remnant
  interface, zero implementations/references.
- `Assets/Scripts/UI/Core/UIPanelAnimator.cs` — zero references anywhere in
  code; no serialized usage (component not present on any mapped panel).

**Removed members (zero callers/subscribers, verified no serialized
`UnityEvent`/`m_MethodName` references in `.unity`/`.prefab`):**

| File | Removed |
|---|---|
| `GameManager` | `PauseGame()`, `ResumeGame()`, `PauseResumeGame()`, `OnGamePaused`, `OnGameResume`, `CinemachineCamera` field |
| `PauseMenuUI` | `OnSettingsRequested` event, `using System;` |
| `PlayerMovement` | `OnTierChanged` event, `lastTier` field |
| `ProgressionManager` | `OnLevelCompleted` event + invoke |
| `SoundManager` | `evolutionSounds` field, `PlayEvolutionSound()`, `ChangeSoundVolume()` |
| `MusicManager` | `MusicTrack` struct, `tracks` field, `GetClipFromName()`, `PlayMusic(string)`, `PlayAmbient(string)`, `ChangeMusicVolume()`, `DuckMusic()`, `using DG.Tweening;` |
| `LevelLoader` | `ReloadCurrentLevel()` |
| `MainMenuUI` | `SetPanelVisibleFallback()`, `ShowComingSoon()`, `comingSoonModal`, `comingSoonText`, `closeComingSoonButton`, `powerupsPanel`, `missionsPanel`, `settingsPanel` fields, `dailyRewardUI`/`coinBonusUI` direct-open fallbacks |
| `InGameHUD` | `UpdateMoves(int)` tile-crossed writer (replaced by single `RefreshMovesText()` path) |
| `GameSession`, `RewardOverlayUI` | unused `using` directives |

## 4. Bugs fixed

1. **Pause registration** — `PauseMenuUI` now implements `IUIPanel`; the scene's
   `UIPanelType.Pause` mapping registers, `InGameHUD`'s menu button opens it,
   Resume closes via `UIManager`, Restart/Quit delegate to
   `LevelFlowController`. No scene/prefab changes required.
2. **InGameHUD move counter** — single writer (`HandleMoveCompleted →
   RefreshMovesText`), `movesUsed` reset on `OnLevelStarted`, tile-crossing no
   longer writes the counter.
3. **ObjectiveSystem subscription leak** — `OnCompleted` subscribed once in
   `Initialize()`, never in `ResetObjective()`; `OnDestroy` unsubscribes both
   objective and layout events. Restarts no longer accumulate callbacks.
4. **GameManager dead pause path** — pause methods/events/field removed;
   scene-init responsibilities (timescale reset, input re-enable) preserved.
5. **MainMenuUI duplicated navigation** — verified-safe fallbacks removed
   (all tab panels are `IUIPanel` and mapped in `UIManager`); next-level header
   fixed to actually resolve the first uncompleted level.
6. **UIManager diagnostics** — invalid mappings now `LogError` with panel type
   and GameObject name instead of being silently skipped.

## 5. Code intentionally NOT removed

- **`CutsceneManager`, `GameDataManager`, `ObjectPoolManager`, `ReturnToPool`** —
  dormant but likely referenced by scene/prefab MonoBehaviour bindings; deleting
  them would create missing-script references. Kept (moved to `Core/`), blocked
  pending an authorized asset pass.
- **`ResultPanelUI` + `RewardOverlayUI`** — product duplication (both react to
  completion/rewards) documented per instructions; no decision made. Both kept.
- **`MockRewardedAdService`, `MockIAPService`, `AdRewardService`, `IAPManager`** —
  ad/IAP infrastructure retained for the future AdMob→Unity Ads migration.
- **Firebase layer** (`FirebaseService`, `FirebaseAuthService`,
  `FirebaseCloudSaveProvider`, `IAuthService`, `ICloudSaveProvider`,
  `FirebaseBootstrap`, `AuthResult`, `AuthState`) — retained; cloud-save sync
  intentionally unwired per scope.
- **`AutoPlayTest`, `FirebaseSmokeTest`** (now `Tests/`), `LevelDataValidator`
  (`Editor/`), `InputActions` stub — test/dev infrastructure retained.
- **`PlayerMovement.OnTileCrossed`** — kept for gameplay/VFX/tier consumers even
  though the HUD no longer uses it for the move counter.
- **`GameSession`, `ProfileData`, `SaveManager`** — persistence architecture
  untouched; no field/format changes.
- **Legacy shop IDs** (`FixItTool`, `VacuumPump`) and `powerUpIds`/profile fields —
  serialized data/save-format compatibility.
- **`OnShieldClicked`** (empty) — button listener target; left as a stub.

## 6. Blocked cleanup — requires asset/scene/prefab changes

- **`Level_KM_001.asset`**: `layoutPrefab` GUID is dangling — asset edit needed.
- **Tutorial mismatch**: `TutorialUI.tutorialLevel` points at `Level_KM_001`
  while the scene auto-starts `Level_001` — scene/asset wiring needed.
- **`BlindBoxUI`**: `CampaignMapUI` chest button needs a `BlindBoxUI` component
  in the scene — scene change needed.
- **`CutsceneManager`** — likely has serialized references in `GameScene.unity`;
  safe removal requires an asset pass (see §5).
- **MainMenuUI removed serialized fields** — if `TotMTest` (or any non-main
  scene) wired `powerupsPanel`/`missionsPanel`/`comingSoonModal` etc., those
  references are now dangling *data* in the scene file (harmless — Unity ignores
  unbound serialized data — but only an asset pass can clean it).

## 7. Validation

- **Compilation: PASS** — verified via `console_status` ground truth
  (`compilationFailed: false`, `compiling: false`) after a clean-build
  recompilation post-reorganization.
- **Missing-script / missing-meta check: PASS** — Unity `console` shows no
  missing-script warnings; all moved metas verified to carry original GUIDs.
  During the pass, `AssetDatabase` desynced on two deleted files (CS2001);
  reconciled by re-importing GUID-matched stubs and deleting via
  `AssetDatabase.DeleteAsset` — final state has no DB or disk remnants.
- **Removed-API reference check: PASS** — `m_MethodName` grep across all
  `.unity`/`.prefab` found zero serialized UnityEvent references to removed
  methods; C# call-site grep confirmed zero callers/subscribers.
- **Runtime tests:** project has no automated play/edit-mode test suite;
  `AutoPlayTest`/`FirebaseSmokeTest` are scene-driven utilities (not run here —
  they require the editor session's scene state and Firebase console config).
- **Asset changes (this pass):** NONE — all modifications confined to `.cs`
  files, `.cs` locations, moved `.meta` companions, and this report.
- **Scene changes:** NONE.
- **Prefab changes:** NONE.
- **Forbidden file changes:** NONE (no `ProjectSettings`, packages, Firebase
  config, `manifest.json`, `Resources`, `StreamingAssets`, or asset edits).

> Note: the working tree already contained modifications from previous sessions
> (e.g. `GameScene.unity`, `Sparkle.aseprite`, `Puzzle 2D.slnx`,
> `ProjectSettings.asset`, Firebase Google sign-in code in
> `FirebaseAuthService.cs`/`LoginUI.cs`/`IAuthService.cs`, and `SettingUI.cs`).
> Timestamps confirm they predate this pass; they were left untouched.
