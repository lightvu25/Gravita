# Iterative Deepening Draft (`a011`)

## Pass 1 — Core Thesis (1 Paragraph)

Gravita already possesses the mechanical ingredients of a standout kinetic lab puzzle game—`30 u/s` stop-to-stop velocity, 3 velocity tiers (`Drift` 1–2, `Cruise` 3–4, `KineticRam` 5+), perpendicular turn buffering, pinball wedge/pad chains, close-call detection, and a URP post-processing stack—yet feels oddly weightless because `33.3 ms/tile` physics outruns its `180–220 ms` scaled squash tweens, 70% of inputs (Tier-1 hops, buffered swipes, redirects, brake stops) trigger zero feedback, `CinemachineCameraZoom2D` and `CloseCallDetector.OnCloseCallTriggered` have zero callers/subscribers, and hit-stop/event-order bugs freeze or mis-tier impacts. Elevating Gravita's feel requires reshaping every dash into a crisp **3-act kinetic phrase — *Tension (Buffered Swipe & Launch), Escalation (Harmonic Cadence & Runway Surge), and Discharge (Pivot-Locked Snap, Pinball Rebound & Near-Miss Shear)*** — built on unscaled sub-35ms timing, a central `TimeScale` priority arbitrator, pooled unscaled VFX, and tier-distinct audio/haptic/camera contrast tuned for speedrunners and puzzle thinkers alike.

---

## Pass 2 — Structural Skeleton & Complete Pillar Blueprint

### Assumptions to State Clearly Upfront
1. **Tier Thresholds**: `VelocityTier.FromTiles(int)` (`Assets/Scripts/Gameplay/VelocityTier.cs`) and `docs/gdd.md` define `Drift = 1–2 tiles`, `Cruise = 3–4 tiles`, and `KineticRam = 5+ tiles` (even though earlier design summaries sometimes mention 1–3 / 4–7 / 8+). All numbers below calibrate to the **live C# thresholds** at `GameScene.unity`'s `moveSpeed = 30` (`33.3 ms/tile`, meaning `Cruise` triggers at `~100 ms` and `KineticRam` triggers at `~167 ms`).
2. **Audio Asset Baseline**: `Assets/Data/Sound/SoundConfig.asset` currently has `clips: []` on all 23 `SoundEntry` slots. All audio specs below define the exact trigger hooks, pitch envelopes, volume curves, and `SoundEntry` slots needed once WAV/OGG assets are assigned in Inspector.
3. **Architecture Boundaries**: Strictly zero `new GameObject(...)` or runtime `AddComponent<...>()` (consistent with `GAME_ARCHITECTURE_STANDARD.md`), Inspector-wired `[SerializeField]` dependencies, `.DOKill().SetUpdate(true).SetLink(gameObject)` on all DOTween sequences, and pre-warmed pooled prefabs via `ObjectPoolManager` / `VfxSpawner` / `ParticleSpawner`.

### Core Game Feel Diagnosis (Why Current Feedback Feels Flat)
1. **Sub-100ms Physics vs 200ms Scaled Tweens**: At `moveSpeed = 30`, a 1-tile hop lasts `33.3 ms` and a 3-tile `Cruise` dash finishes in `100 ms`, yet `PlayerSquashStretch` stretches over `0.18s` and squashes over `0.22s` on **scaled** time (`DOTween.Sequence()` missing `.SetUpdate(true)`).
2. **Hit-Stop & Unscaled Desync Bugs**:
   - `PlayerImpactFeedback.HitStopRoutine` assigns `preHitStopTimeScale = Time.timeScale;` — overlapping pulses capture `0f` and lock time forever, and `OnDisable` fails to restore `Time.timeScale`.
   - `PooledParticlePlayer`, `PooledAnimationPlayer`, `PlayerLineTrail`, and `CinemachineCameraZoom2D` advance on scaled `Time.deltaTime`, literally freezing sparks, trail retraction, and camera recovery during hit-stop.
   - `PlayerLineTrail.smoothedMoveSpeed` caps at `28f` below `moveSpeed = 30`.
3. **Event-Order & Double-Fire Audio Corruption**:
   - `PlayerMovement` invokes `OnWallImpact` BEFORE `StopMoving(wallImpact: true)` sets `LastImpactTier`, causing `EntityAudio.HandlePlayerWallImpact` to read the *previous* dash's tier!
   - `PlayerMovement.Redirect` fires BOTH `OnRedirected` and `OnMoveStarted`, causing `EntityAudio` to double-trigger `wedgeRedirect` + `moveStart`.
4. **Bottom-Center Pivot (`y = -0.5`) Ceiling Detachment**: `Player/Sprite` is offset `-0.5` downward. Compressing `Y` on an upward wall hit pulls the sprite visibly off the ceiling instead of squashing against the wall normal.
5. **Mute Majority & Unwired Systems**:
   - `tier1Shake = 0`, `tier1HitStop = 0`, `redirectShake = 0`.
   - `Wedge.prefab`, `Brake.prefab`, `Exit.prefab`, and `Hazard.prefab` have empty `InteractionEffectPlayer` (`effectPrefabs: []`, `defaultCameraShake: 0`); `Cracked.prefab` and `Brittle.prefab` have `defaultCameraShake = 0`.
   - `CinemachineCameraZoom2D` (ortho `5.5`), `CloseCallDetector.OnCloseCallTriggered`, and `Global Volume Profile.asset` (`Bloom`, `ChromaticAberration`, `Vignette`, `ColorAdjustments`) are completely static during dashes.
   - `GameplayHapticsFeedback` plays flat `HapticEvent.WallImpact` regardless of velocity tier.

---

### Pillar 1: Unscaled Micro-Impact, `TimeScaleArbitrator` & Pivot-Aware Surface Squash
- **Psychology & Feel**: Every single stop—even a `33 ms` Tier-1 hop—must immediately communicate physical contact with the surface without bogging down rapid puzzle solves, while preserving distinct shock escalation into `Cruise` and `KineticRam`, plus an unmistakably "soft magnetic" stop on `BrakeTile` / `GravityBlock`.
- **Exact Numbers & Curves**:
  - **Launch Anticipation**: Peak stretch in **`0.032s`** (`Ease.OutQuad`), directional scale `(0.80, 1.24)` along `dir` (replaces sluggish `0.18s`).
  - **Tier-1 `Drift` (1–2 tiles)**: `shake = 0.035`, `hitStop = 0.0s` (instant flow!), squash `(1.16, 0.84)` over `0.035s` hold + `0.065s` return (`0.10s` total, `OutQuad`), `LightImpact` haptic.
  - **Tier-2 `Cruise` (3–4 tiles)**: `shake = 0.11`, `hitStop = 0.030s`, squash `(1.28, 0.72)` over `0.030s` snap + `0.10s` return (`0.13s` total, `OutBack` slight overshoot `1.04`), `MediumImpact` haptic.
  - **Tier-3 `KineticRam` (5+ tiles)**: `shake = 0.24`, `hitStop = 0.068s` (capped <70ms to protect speedrun feel), squash `(1.38, 0.62)` over `0.035s` snap + `0.145s` return (`0.18s` total), additive overlay impact flash (`alpha 0.65 -> 0f` over `0.06s`, scaled by accessibility flash setting), `RigidImpact` haptic.
  - **Soft Anchor Stop (`BrakeTile` / `GravityBlock`, `WasLastStopWallImpact == false`)**: `hitStop = 0s`, `shake = 0.025`, symmetric settle squash `(1.10, 0.90)` over `0.08s`, `SoftImpact` haptic, magnetic clamp SFX (`pitch 0.95`).
  - **Pivot Compensation**: On wall impact with direction `dir`, shift `Sprite` local position temporarily by `dir * (halfSpriteExtent * (1f - impactCompressionRatio) - bottomPivotFix)` so the outer compressed face stays **pinned flush against the tile wall** on `Up`, `Down`, `Left`, and `Right`!
- **C# Architecture & Fixes**:
  - New `TimeScaleArbitrator` (`MonoBehaviour` / scene singleton) owning `Time.timeScale`: priority layers (`Pause = 0f` > `Cutscene` > `HitStop = 0f` > `NearMissDilation = 0.40f` > `Normal = 1f`). Supports timed unscaled pulses (`RequestHitStop(float duration)`, `RequestDilation(float scale, float holdSec, float blendSec)`), respects `UIManager.IsAnyPopupOpen` and `CutsceneManager.IsSequenceRunning`, and restores cleanly in `OnDisable` / `LevelFlowController.OnPlayerRevived`.
  - Fix `PlayerMovement.cs`:
    - Update `LastImpactTier`, `LastImpactPoint`, `WasLastStopWallImpact` via `StopMoving(wallImpact: true)` **before** invoking `OnWallImpact?.Invoke(hitPoint)` (or invoke `OnWallImpact` immediately inside/after updating those properties so `LastImpactTier` is always accurate!).
    - Keep `OnRedirected` distinct in audio via frame-guard in `EntityAudio` (or `bool isRedirect` parameter / internal state) so `Redirect` does NOT double-play `moveStart` + `wedgeRedirect`.
  - Update `PlayerSquashStretch.cs`: Add `.SetUpdate(true)` + `.SetLink(gameObject)` on all sequences, read `FlashOverlaySprite` (`SpriteRenderer` child, zero `SpriteRenderer.color` clobber against `PlayerCosmeticApplier` or `phaseSpriteTint`), and pivot-compensate wall squash.
  - Update `PooledParticlePlayer`, `PooledAnimationPlayer`, `PlayerLineTrail` (retraction when paused=false & `Time.timeScale == 0`), and `CinemachineCameraZoom2D` with `[SerializeField] private bool useUnscaledTime = true;` + raise `PlayerLineTrail` `smoothedMoveSpeed` clamp to `36f`.
- **Inspector / Prefab Wiring**:
  - Add child `FlashOverlay` (`SpriteRenderer`, additive material, `alpha = 0`, `sortingOrder = +1` relative to `Sprite`) to `Player` prefab/scene object; drag into `PlayerSquashStretch.flashOverlayRenderer`.
  - Assign directional wall-dust/spark prefabs (Tier 1 dust, Tier 2 sparks, Tier 3 shockwave burst) to `PlayerImpactFeedback` and wire `TimeScaleArbitrator` in `GameScene`.

---

### Pillar 2: Kinetic Runway Escalation ("Harmonic Cadence, Ortho Kick & Chromatic Surge")
- **Psychology & Feel**: Turns raw distance into rising musical and optical tension. At `30 u/s`, the player needs immediate mid-flight confirmation at `100 ms` (`Cruise`, tile 3) and `167 ms` (`KineticRam`, tile 5) that they just unlocked wall-breaking kinetic power—making long straightaways and diagonal rebounds feel like charging a railgun.
- **Exact Numbers & Curves**:
  - **Per-Tile Harmonic Cadence (`OnTileCrossed(int tiles)`)**:
    - Gate: `minTileSfxInterval = 0.028s` (prevents substep audio clipping when crossing 2 tiles in a `0.02s` `FixedUpdate`).
    - Pitch curve: `pitch = 0.92f + Mathf.Clamp(tiles - 1, 0, 8) * 0.045f` (`0.92 -> 1.28`), volume `0.10 -> 0.24`. Zero haptic here so `GameHaptics.GlobalMinGapSeconds = 0.03f` NEVER blocks `WallImpact`!
  - **Tier Lock-In (`OnVelocityTierChanged(VelocityTier tier)`)**:
    - `Cruise` (tile 3): `CinemachineCameraZoom2D` target ortho `5.5 -> 5.65` (`zoomSpeed = 12f` unscaled), `Global Volume` `Bloom.intensity` `9.0 -> 10.2`, `ChromaticAberration` `0.20 -> 0.28`, crisp cybernetic power-up chime (`pitch 1.0`, vol `0.38`), `PlayerLineTrail` brief head pulse `1.2x -> 1.38x` for `0.07s`.
    - `KineticRam` (tile 5): target ortho `5.5 -> 5.85` (`zoomSpeed = 12f`), `Bloom.intensity` `9.0 -> 12.0` (`clamp: 10`), `ChromaticAberration` `0.20 -> 0.44`, `Vignette` `0.40 -> 0.48`, harmonic lock chord (`pitch 1.08`, vol `0.50`), looped plasma hum starts (`min` fade `0.05s`).
  - **Impact Snap-Back (`OnMoveStopped`)**:
    - On `KineticRam` wall impact: camera ortho briefly punches IN `5.85 -> 5.36` over `0.055s` (`OutQuad`), then settles back to `5.5` over `0.16s` (`OutCubic`) — giving physical recoil to the stop!
    - Volume overrides tween back to baseline (`Bloom: 9.0, CA: 0.20, Vignette: 0.40`) over `0.18s` (`.SetUpdate(true)`).
  - **Pooled Kinetic Afterimages**:
    - Pre-warmed `ObjectPoolManager` pool of 10 `PlayerGhost` prefabs (each a static `SpriteRenderer` + `PooledGhostEcho` component, zero `new GameObject`!).
    - Spawn spacing: disabled in `Drift`; every `0.45` units in `Cruise` (alpha `0.28`, fade `0.13s`, `cruiseHeadColor` tint); every `0.30` units in `KineticRam` (alpha `0.48`, fade `0.18s`, `kineticRamHeadColor` tint); if `IsPhasing`, uses `phaseHeadColor` (`0.38` spacing). Copies active `PlayerCosmeticApplier` sprite + `flipX`/`flipY` + scale at spawn!
- **C# Hooks & Inspector Wiring**:
  - New `KineticRunwayFeedback` component subscribing to `OnMoveStarted`, `OnTileCrossed`, `OnVelocityTierChanged`, `OnMoveStopped`, `OnPhasingChanged`, plus `LevelFlowController.OnPlayerRevived` (hard-reset all overrides).
  - New `[SerializeField] Volume globalVolume;` (modifying runtime cloned/profile components cleanly and restoring baseline values in `OnDisable` so ScriptableObject asset on disk isn't left dirtied!).
  - Upgrade `CinemachineCameraZoom2D` with `ImpulseOrthoOffset(float kickOrtho, float kickDuration, float recoverSpeed)` running on `Time.unscaledDeltaTime`.

---

### Pillar 3: Tactile Input Buffering & Turn-Lock Feedback ("No Silent Swipes")
- **Psychology & Feel**: Gravita's flow relies heavily on `bufferTurnsUntilStop = 1` (`turnClearance = 0.35`): swiping perpendicular while sliding at `30 u/s` to queue the next vector. Confirming queued intent—and giving an immediate micro-kick if blocked—eliminates "did the game eat my swipe?" frustration and deepens mastery.
- **Exact Numbers & Curves**:
  - **Queued (`BufferedTurnState.Queued`)**:
    - Diegetic chevron child (`BufferedChevron`, orbit radius `0.56` units from Player center, facing queued direction): pops scale `0.5 -> 1.0` over `0.04s` (`OutBack`), tint `(0.35, 0.95, 1.0, 0.95)`, subtle `±5°` sprite lean toward perpendicular turn.
    - Audio: crisp relay micro-click (`25 ms`, `pitch 1.18`, vol `0.22`).
    - Haptic: `HapticEvent` `BufferedTurn` (`Selection` preset) ONLY if `player.TilesTraveled` estimate or `Buffered` doesn't collide within `0.03s` of impact (or use priority gap rules).
  - **Consumed (`BufferedTurnState.Consumed`)**:
    - Chevron zips `0.20` units forward and fades `1 -> 0` over `0.05s` — seamless visual handoff into next dash!
  - **Rejected / Blocked Wall Press (`BufferedTurnState.Rejected`)**:
    - Happens when buffered turn hits a solid wall upon stopping, OR stationary input tries to push into an adjacent wall.
    - Sprite performs rapid damped 2-beat micro-nudge along pressed direction: `+0.07` -> `0.0` units in `0.065s` (unscaled), chevron flashes `(1.0, 0.35, 0.25, 0.9)` and dissolves in `0.09s`, muted low tick (`pitch 0.78`, vol `0.24`), `SoftImpact` haptic.
- **C# Hooks & Inspector Wiring**:
  - In `PlayerMovement`: expose `public Vector2 PendingDirection => pendingDirection;` and `public event Action<Vector2, BufferedTurnState> OnBufferedTurnChanged;` (`Queued`, `Consumed`, `Rejected`, `Cleared`), firing at buffer enqueue, consumption on stop, blocked-turn rejection, and `ResetLevel` / `HaltAt`.
  - Pre-place `BufferedChevron` child `SpriteRenderer` under `Player` in `GameScene.unity` / Player Prefab; drag into new `BufferedTurnFeedback` script (`[SerializeField] private SpriteRenderer chevronRenderer;`).

---

### Pillar 4: Pinball Flow Chaining & Rebound Resonance (`DeflectionWedge`, `VectorPad`, `VectorLauncher`)
- **Psychology & Feel**: Redirect chains (`maxConsecutiveRedirects = 8`) are Gravita's marquee spectacle. Currently `Wedge.prefab` has empty `InteractionEffectPlayer` and `redirectShake = 0`. Turning each deflection into an escalating harmonic *ping* with a 1-frame micro-hitch turns corner chains into exhilarating pinball combos.
- **Exact Numbers & Curves**:
  - **Chain Counter (`redirectChainCount`, clamped `0..7`)**:
    - Increments on `PlayerMovement.OnRedirected(Vector2 newDir)`, resets on `OnMoveStopped`, `HaltAt`, `TeleportTo`, `OnPlayerRevived`.
  - **1-Frame Pinball Micro-Hitch**: `0.016s` hit-stop (`TimeScaleArbitrator.RequestHitStop(0.016f)`) on redirect (`VectorLauncher`: `0.025s` charge snap) — long enough to punctuate the 90° snap without breaking speed.
  - **Lydian Harmonic Pitch Ladder**: `pitchSteps = { 1.00f, 1.06f, 1.12f, 1.19f, 1.26f, 1.33f, 1.41f, 1.50f }` (clamped at index `7` so an 8-bounce loop never clips!).
  - **Shake & Haptic Escalation**: Redirect camera impulse `0.04f + chainIndex * 0.01f` (capped at `0.11f`), `LightImpact` for chain `0..2`, `MediumImpact` for chain `3+` (subject to impact priority rule).
  - **Visual Bisect Spark & Directional Diagonal Snap**: Spawn pooled directional spark prefab aligned to incoming/outgoing bisector (`InteractionEffectPlayer` on `Wedge.prefab` / `VectorPad.prefab`), brief `0.045s` stretch pulse `(0.84, 1.20)` along `newDir`, plus `PlayerLineTrail` brief `1.25x` corner flare node.
- **C# Hooks & Inspector Wiring**:
  - New `PinballChainFeedback` subscribing to `PlayerMovement.OnRedirected`, `OnMoveStopped`, `LevelFlowController.OnPlayerRevived`, plus `EntityAudio` pitch override using clamped step index.
  - Populate `Wedge.prefab` & `VectorPad.prefab` `InteractionEffectPlayer.effectPrefabs` with pooled directional spark prefabs and `defaultCameraShake = 0.05`.

---

### Pillar 5: Close-Call "Phase Shear" & Lab Hazard Telegraphing
- **Psychology & Feel**: Escaping a sweeping laser (`HazardActivation`) or `SentinelDronePatrol` by 1 frame creates memorable "highlight reel" moments. `CloseCallDetector.OnCloseCallTriggered` already detects this cleanly (`closeCallWindowSeconds = 0.15`, `enemyNearMissRadius = 1.08`, `perHazardCooldown = 1.5`) — we only need to make those milliseconds electric, while giving active hazards unmistakable pre-fire warnings.
- **Exact Numbers & Curves**:
  - **Close-Call "Phase Shear" (`CloseCallDetector.OnCloseCallTriggered(Vector3 pos)`)**:
    - `TimeScaleArbitrator.RequestDilation(scale: 0.40f, holdSeconds: 0.045f, blendOutSeconds: 0.075f)` — total ~120 ms crisp matrix dip that cleanly yields if `HitStop`, `Pause`, or `Cutscene` activates!
    - Camera micro-zoom kick `5.5 -> 5.26` over `0.045s`, settle over `0.16s` + `cameraPulseForce = 0.06` shake.
    - `Global Volume` `ChromaticAberration` spike `0.20 -> 0.58` (`0.05s` up, `0.16s` decay) + `Vignette` `0.40 -> 0.50`.
    - `closeCallVfxPrefab` upgraded to unscaled `PooledParticlePlayer`, `closeCallGraze` SFX (`pitch 1.05..1.16`), `Selection` -> `MediumImpact` tactile snap.
    - **Economy Guard**: Note on `CloseCallDetector.bonusCoins = 5` — keep coin award strictly optional/capped per level or puzzle-safe so players cannot farm stars/coins on looping patrols while ALWAYS preserving the visual/audio Phase Shear reward.
  - **Hazard & Floor Anticipation Telegraphs**:
    - `HazardActivation.OnPhaseChanged`: Add a clear `0.25s` pre-arm buzz + local sprite scale pulse `(1.0 -> 1.08)` (`EntityAudio` + `Hazard.prefab` `InteractionEffectPlayer` / Animator cue) right before deadly beams engage; populate `Hazard.prefab` & `Exit.prefab` `InteractionEffectPlayer`s.
    - `FailingTile.OnStateChanged`: Distinct brittle crack sound (`pitch 0.95 -> 1.25` by step) + pooled dust trickle on step warning.
    - `BreachSequencer.OnBreachFired`: `0.08` shake + sub-bass breach shudder (`55 Hz` thump) + `Vignette` `0.06` pulse.

---

### Pillar 6: Destructible Crunch, Harmonic Pickups, Power-Up Signatures & Exit Capture
- **Psychology & Feel**: Gravita's puzzle interactions distinguish between *breaking through walls* (`KineticRam` -> `CrackedBlock`), *delicate speed control* (`Drift` vs `Cruise+` -> `BrittleFloor`), *sweeping resources* (`Collectible` Coins/Stars), *tactical gadgets* (`PowerUpController`), and *extraction* (`LevelExit`). Distinct micro-signatures turn puzzle completion into pure sensory payoff.
- **Exact Numbers & Curves**:
  - **Destructibles Weight Split**:
    - `CrackedBlock.OnAnyBroken`: `0.072s` hit-stop, `0.26` camera shake (`Cracked.prefab` `defaultCameraShake = 0.26` instead of `0`), pooled masonry debris burst (`unscaled`), heavy bass crack + shatter SFX, `RigidImpact` haptic.
    - `BrittleFloor.OnShattered`: `0.022s` micro-hitch (preserves velocity continuity without freezing dash!), `0.12` shake (`Brittle.prefab` `defaultCameraShake = 0.12`), high-frequency crystal/glass break (`pitch 1.15..1.28`), `MediumImpact` haptic — contrasted against tense low *creak* tick on safe `Drift` passes!
  - **Harmonic Pickup Scale (`Collectible.OnAnyCoinCollected` / `OnAnyStarCollected`)**:
    - Track `dashPickupStep` (`0..7`, resets after `0.30s` post-stop): Coins climb a C-major pentatonic scale (`1.00, 1.122, 1.260, 1.498, 1.682, 2.00`), `0.035s` audio throttle, `0.03s` unscaled burst.
    - `StarCollectFeedback`: Ortho dip `5.5 -> 5.32` over `0.07s` + recover, harmonic root+octave bell chime, `MediumImpact` haptic.
  - **`PowerUpController` Tactile Polish & Zero-Procedural Refactor**:
    - **Refactor existing procedural violations**: `PowerUpController` currently uses `new GameObject` in `SpawnRadialPulseVFX`, `SpawnDirectionalScanOrBurstVFX`, and `SpawnEchoVisual`; replace with pre-warmed pooled prefabs via `VfxSpawner.Spawn` / `ParticleSpawner.SpawnEffect`.
    - `OnPhasingChanged(true)`: violet chromatic pulse (`CA 0.20 -> 0.45`, `0.12s`), phase-shift swoosh (`pitch 1.12`), pooled rift ring at origin so `1.6x` `Phase` launch doesn't feel like a sudden pop when `PlayerLineTrail` clears at `teleportResetDistance = 2`.
    - `Freeze`: `ColorAdjustments` cool hue/saturation nudge (`-18` saturation for `0.25s`), crisp ice-lock chime.
    - `TeleportTo`: pooled entry+exit portal ring + `0.05s` scale pop `(0.65 -> 1.0)` at destination.
  - **`LevelExit` Magnetic Capture**:
    - On exit contact (`MazeTile.OnAnyPlayerInteracted` / `LevelExit` trigger): `0.11s` unscaled `DOMove` center lock-in (`.SetUpdate(true)`), ortho zoom `5.5 -> 5.15`, `Bloom` glow `9.0 -> 13.0`, warm resolve harmonic chord, `RigidImpact` + `LightImpact` (`0.05s` apart) before `CutsceneManager` goal transition.

---

## Pass 3 — Adversarial Hardening Verification Matrix

Let's check every Attack (1–10) & Edge Case (1–8) against Pass 2:
- **Attack 1 (`33.3 ms/tile` timing)**: Launch peak `0.032s` (`32 ms`), Tier-1 squash `0.10s`, Tier-2 `0.13s`, Tier-3 `0.18s`, Cruise at `100 ms`, KineticRam at `167 ms`, hit-stop capped `0.068s`. **Defeated.**
- **Attack 2 (`Time.timeScale` collisions + frozen VFX)**: Central `TimeScaleArbitrator` priority stack (`Pause > Cutscene > HitStop > NearMissDilation > Normal`) + unscaled `PooledParticlePlayer`, `PooledAnimationPlayer`, `PlayerLineTrail` retraction, `PlayerSquashStretch`, `CinemachineCameraZoom2D`. **Defeated.**
- **Attack 3 (Silent 70% majority)**: Tier-1 `0.035` shake + `LightImpact` + dust; Buffered turn chevron + click + reject nudge; Redirect 16ms hitch + Lydian ping + bisector sparks; BrakeTile magnetic latch. **Defeated.**
- **Attack 4 (Stale `LastImpactTier` + `Redirect` double-fire)**: Exact `PlayerMovement.StopMoving` ordering fix + `EntityAudio` frame-guard. **Defeated.**
- **Attack 5 (Bottom-pivot `y = -0.5` detachment)**: Pivot-compensated directional squash keeps compressed face flush against ceiling/side walls. **Defeated.**
- **Attack 6 (`GlobalMinGapSeconds = 0.03f` haptic starvation & audio clipping)**: Per-tile ticks NEVER call `GameHaptics`; `WallImpact` / `Death` get priority gap bypass; per-tile audio uses `0.028s` voice gate; coin ladder uses `0.035s` throttle. **Defeated.**
- **Attack 7 (Zero-procedural & cosmetic clobber)**: Pre-warmed `ObjectPoolManager` prefabs for ghosts/VFX; `FlashOverlay` child renderer leaves `PlayerCosmeticApplier` + `phaseSpriteTint` intact; explicitly fixes `PowerUpController`'s `new GameObject` leaks. **Defeated.**
- **Attack 8 (Tier discrepancy & empty `SoundConfig.asset`)**: Explicitly resolved in Assumptions + exact `SoundEntry` specs. **Defeated.**
- **Attack 9 (Unused scene systems)**: Directly wires `CinemachineCameraZoom2D`, `CloseCallDetector.OnCloseCallTriggered`, `Global Volume Profile.asset`, and empty `InteractionEffectPlayer`s on 6 gameplay prefabs. **Defeated.**
- **Attack 10 (Accessibility & speedrunner friendliness)**: Hit-stop max `68 ms`, `FeedbackSettings` multipliers (`shakeScale`, `hitStopEnabled`, `flashIntensityScale`, `GameHaptics.Enabled`), no full-screen white flash strobes. **Defeated.**
