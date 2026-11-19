# Adversarial Attack Register & Edge-Case Matrix (`a011`)

## 1. Harshest Expert Reviewer Attacks (Targets to Defeat Before Writing `a011.md`)

1. **Time-Budget Blindness (`30 tiles/sec` = `33.3 ms/tile`)**:
   - `GameScene.unity` configures `PlayerMovement.moveSpeed = 30`.
   - `VelocityTier.FromTiles` defines `Drift (1–2 tiles = 33–67 ms)`, `Cruise (3–4 tiles = 100–133 ms)`, `KineticRam (5+ tiles = 167+ ms)`.
   - Current `PlayerSquashStretch` uses `stretchDuration = 0.18s` (180 ms) and `squashDuration = 0.22s` (220 ms) without `.SetUpdate(true)`. Almost every slide finishes before stretch completes, and hit-stop (`33–83 ms`) freezes the tween!
   - **Requirement**: All launch/glide/impact timings must be engineered around `33.3 ms/tile` and unscaled time.

2. **Catastrophic `Time.timeScale` Collisions & Frozen VFX**:
   - `PlayerImpactFeedback.HitStopRoutine` captures `preHitStopTimeScale = Time.timeScale`. Two rapid impacts (e.g., short deflection into wall) capture `0f` and lock `Time.timeScale = 0f` permanently; `OnDisable` during hit-stop also strands `timeScale = 0f`.
   - Adding near-miss slow-mo (`CloseCallDetector`) without central arbitration collides with `UIManager.UpdateTimeScale()` and `CutsceneManager`.
   - `PooledParticlePlayer`, `PooledAnimationPlayer`, `PlayerLineTrail`, `CinemachineCameraZoom2D`, and `PlayerSquashStretch` all advance via scaled `Time.deltaTime` / scaled DOTween — hit-stop literally freezes sparks, trail retraction, squash, and zoom!
   - **Requirement**: Propose a clean `TimeScaleArbitrator` / guarded hit-stop pattern AND switch impact VFX/squash/zoom/trail retraction to unscaled time.

3. **Silent 70% Majority (`Drift`, `Redirect`, `BrakeTile`, Buffered Input)**:
   - `tier1Shake = 0`, `tier1HitStop = 0`, `redirectShake = 0`, plus `Wedge.prefab`, `Brake.prefab`, `Exit.prefab`, `Hazard.prefab` have empty `InteractionEffectPlayer`, and `Cracked.prefab` / `Brittle.prefab` have `defaultCameraShake = 0`.
   - With `bufferTurnsUntilStop = 1`, most player inputs are crisp 1–2 tile hops, mid-flight perpendicular swipes, wedge redirects, and brake stops — all currently numb.
   - **Requirement**: Give Tier-1, Redirect, Brake, and Buffered Turn micro-tactile contrast without fatiguing or muddying Tier-3 `KineticRam`.

4. **Broken Event-Order & Double-Fire Audio Bugs**:
   - `PlayerMovement` fires `OnWallImpact?.Invoke(...)` BEFORE `StopMoving(wallImpact: true)` assigns `LastImpactTier`, so `EntityAudio.HandlePlayerWallImpact` reads the PREVIOUS dash's tier!
   - `PlayerMovement.Redirect` invokes BOTH `OnRedirected` and `OnMoveStarted`, causing `EntityAudio` to double-trigger `wedgeRedirect` + `moveStart`.
   - `SoundConfig.asset` currently has `clips: []` across all 23 `SoundEntry` slots.
   - **Requirement**: Exact code fix for event order/signatures + concrete `SoundConfig` additions/curves + non-overlapping `Redirect` handling.

5. **Bottom-Center Pivot `(0.5, 0)` Wall/Ceiling Detachment**:
   - `Player/Sprite` sits at `m_LocalPosition: (0, -0.5, 0)` in `GameScene.unity`. Squashing downward on `Vector2.up` or horizontal impacts pulls the sprite edge off the impacted tile face.
   - **Requirement**: Pivot-aware normal compensation or dedicated directional squash anchor so sprites always compress *into* the wall normal.

6. **Haptic Starvation & Audio Clipping at `30 tiles/sec`**:
   - `GameHaptics.GlobalMinGapSeconds = 0.03f` (30 ms). Firing a haptic per `OnTileCrossed` (1 tile every `33.3 ms`, or 2 tiles in a `0.02s` `FixedUpdate` substep) risks causing `GameHaptics` to suppress the crucial `WallImpact` haptic immediately after!
   - **Requirement**: Reserve `GameHaptics` for `MoveStart`/`BufferedInput`, `TierThresholdCrossed`, `Redirect`, `CloseCall`, and `WallImpact` (or add priority/bypass rules) — keep per-tile cadence visual + melodic audio with monotonic pitch + strict cooldowns.

7. **Violating Zero-Procedural & Cosmetic-Color Rules**:
   - Never use `new GameObject(...)` or runtime `AddComponent<...>()` (unlike the legacy violation inside `PowerUpController`). All ghosts, sparks, ring pulses must use pooled prefabs via `ObjectPoolManager` / `VfxSpawner` / `ParticleSpawner` and `[SerializeField]` references.
   - Never hardcode `SpriteRenderer.color = Color.white` over `PlayerCosmeticApplier` skins or `PlayerSquashStretch.phaseSpriteTint`. Use an additive overlay child `SpriteRenderer`, `MaterialPropertyBlock` (`_FlashAmount` / `_Tint`), or clean state restoration.

8. **Tier Discrepancy (`VelocityTier.cs` vs Brief Text) & Unused Scene Systems**:
   - Address `1–2 / 3–4 / 5+` (actual `VelocityTier.FromTiles` + `gdd.md`) vs `1–3 / 4–7 / 8+` explicitly in **Assumptions**.
   - Wire unused `CinemachineCameraZoom2D` (currently fixed at `5.5`), `CloseCallDetector.OnCloseCallTriggered` (0 subscribers), and existing `Global Volume Profile.asset` (`Bloom`, `ChromaticAberration`, `Vignette`, `ColorAdjustments`).

---

## 2. Gameplay & Technical Edge-Case Matrix

| Edge Case / Failure Mode | Root Risk | Mandatory Design Guard |
|---|---|---|
| Redirect loop up to `maxConsecutiveRedirects = 8` (or `HaltAt` emergency stop) | Audio pitch screams to infinity; combo counters leak | Cap combo pitch index `Mathf.Clamp(redirectChainCount, 0, 7)`; reset on `OnMoveStopped`, `HaltAt`, `TeleportTo`, `OnPlayerRevived` |
| 1-tile hop (`~33 ms`, 2 `FixedUpdate` ticks) | Launch tween (`180 ms`) hasn't reached peak before impact cancels it | Peak launch stretch in `0.03s` (`OutQuad`), call `.DOKill()` on impact so squash snaps cleanly from current scale |
| Buffered perpendicular turn blocked at target cell | Silent swipe feels like dropped input; or queued visual lingers | Expose `PendingDirection` + `OnBufferedTurnChanged(Vector2, BufferState)`; show directional chevron/antenna micro-tilt; play crisp "blocked tick" if move stops with unconsumed invalid turn |
| `BrakeTile` / `GravityBlock` vs Wall impact | Both invoke `OnMoveStopped`, but `WasLastStopWallImpact` differs | Gate harsh crunch/dust strictly on `WasLastStopWallImpact == true`; play low-frequency "magnetic latch / skid" + gentle settle squash when `false` |
| `PowerUpController` `Phase` (`1.6x`) & `TeleportTo` | `PlayerLineTrail` clears on `> 2f`; missing `OnTileCrossed` | Subscribe to `OnPhasingChanged` & `TeleportTo` hooks; trigger dedicated rift shimmer instead of wall debris |
| Pause / Cutscene / Death during Hit-Stop or Zoom Kick | `timeScale` permanently stuck or camera stuck zoomed in/out | Central `TimeScaleArbitrator` priority stack (`Pause > Cutscene > HitStop > NearMissDilate > Normal`); `OnDisable` & `OnPlayerRevived` restore defaults |
| `Collectible` multi-pickup in 1 frame (line of coins) | Identical sound stacks and clips amplitude | Musical pentatonic/major scale pitch ladder keyed to `TilesTraveled` or `dashCollectCount` with `0.035s` voice gate |
| Accessibility toggles (`gdd.md §12.2-12.4`) | Shake/flash/hit-stop annoys speedrunners or photosensitive players | Route all impulses through `FeedbackSettings` multipliers (`shakeScale`, `hitStopEnabled`, `flashIntensityScale`) |
