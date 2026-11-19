# Acceptance Checks for `r0/a006.md` (Test-First Criteria)

## A. Deliverable Completeness Checks
1. **Core Game Feel Diagnosis**:
   - Explains *why* Gravita currently feels "flat" despite existing shake, squash/stretch, and `LineRenderer` trail.
   - Grounded in verified facts from the codebase (`moveSpeed = 30` -> `33.3 ms/tile`, `PlayerSquashStretch` missing `.SetUpdate(true)` & bottom-center `(0.5, 0)` pivot offset at `(0, -0.5, 0)` & `180-220 ms` durations exceeding 2-5 tile slide times; `PlayerImpactFeedback` having `tier1Shake = 0` and `redirectShake = 0` in `GameScene.unity`; `CinemachineCameraZoom2D` never called anywhere & using `Time.deltaTime`; `CloseCallDetector.OnCloseCallTriggered` having 0 subscribers; `PlayerLineTrail` clamping speed to `28f` and pausing on `Time.deltaTime <= 0`; `EntityAudio` silent on `OnTileCrossed`, `OnVelocityTierChanged`, `BrakeTile`/`GravityBlock`, `DeflectionWedge` blunt hit, `CloseCall`, `CircuitSwitch`/`EnergyGate`, `BrittleFloor`, `FailingTile`; `GameplayHapticsFeedback` ignoring `PadRedirect`, `LaunchStart`, `SwitchToggle`, `Death`; `Brake`, `Exit`, `Hazard`, `Wedge` prefabs having empty `InteractionEffectPlayer` arrays/shake; `Global Volume Profile.asset` Bloom/Vignette/ChromaticAberration static).
   - Explicitly notes the verified discrepancy between task-brief tier wording (`1-3`, `4-7`, `8+`) vs actual code in `VelocityTier.cs` (`1-2` Drift, `3-4` Cruise, `5+` KineticRam) in an Assumptions / Codebase Notes section so no programmer gets tripped up.

2. **4 to 6 High-Impact, Unique Game Feel Pillars** (Aim for 6 razor-sharp, non-overlapping pillars covering the whole loop):
   - Pillar 1: **Kinetic Runway Cadence & Tier-Ignition Escalation** (per-tile micro-ticks `OnTileCrossed`, tier ignition shock + `VelocityTier` color/scale/audio pop at `Cruise` & `KineticRam`, `CinemachineCameraZoom2D` breathing, URP `ChromaticAberration`/`Bloom` impulse, `30 u/s` recalibrated `PlayerSquashStretch` + directional wall-edge pivot anchoring & `.SetUpdate(true)`).
   - Pillar 2: **Rhythmic Chain-Bank Combo & Harmonic Pitch Ladder** (`OnRedirected` across `DeflectionWedge` + `VectorPad` + `VectorLauncher`: ascending semitone/pentatonic pitch ladder, wedge/pad directional recoil & `InteractionEffectPlayer` sparks, redirect `GameHaptics.Play(HapticEvent.PadRedirect)` & `LaunchStart`, `CinemachineCameraShake2D` micro-kick, combo counter reset on `OnMoveStopped`).
   - Pillar 3: **Pre-Cocked Turn Buffer & Directional Wall-Anchor Contrast** (Visual/audio anticipation when `PlayerMovement.HasPendingDirection` is banked during `bufferTurnsUntilStop = true`, subtle directional indicator/lean & tactile latch tick; stark sensory contrast between rigid **Wall Slam** rebound vs **BrakeTile / GravityBlock** magnetic vacuum catch (`AwaitingFreshInput` hum + inward compression) vs **CrackedBlock** armor-piercing shatter-through!).
   - Pillar 4: **Close-Call Electro-Graze & Dynamic Risk Telemetry** (Subscribing to `CloseCallDetector.OnCloseCallTriggered` for world-space/HUD "+CLOSE CALL" snap, unscaled `CinemachineCameraZoom2D` punch, `Global Volume` momentary vignette/aberration flash, dedicated high-frequency arc SFX + haptic tick, and `HazardActivation` / `FailingTile` telegraphing sync—WITHOUT mutating `Time.timeScale` so `LevelClock` & `CloseCallDetector` invariants hold).
   - Pillar 5: **Phase Launch & Skill Kinetic Signatures (`Breaker`, `Freeze`, `Teleport`)** (Fixing `PowerUpController` procedural `new GameObject` anti-pattern into pooled `VfxSpawner.Spawn` prefabs; `Breaker` directional shockwave + camera recoil; `Freeze` cyan cryo-snap & `ColorAdjustments` cool tint pulse + `Hazard.IsFrozen` ice-shimmer; `Teleport` / `VectorLauncher` `OnPhasingChanged` speed-line overdrive at `48 u/s` (`1.6x`), fixing `PlayerLineTrail` speed clamp from `28f` to `60f`, ghost afterimages on pooled sprite ghosts, and landing `KineticRam` shock-ring).
   - Pillar 6: **Stop-to-Stop Acoustic & Tactile Completeness + Micro-World Reactivity** (Extending `SoundManager` with `PlaySFX(SoundEntry entry, float pitchMultiplier)` for dynamic pitch ladders (`Collectible.OnAnyCoinCollected` streak & `OnTileCrossed` & `OnRedirected`), filling missing `SoundConfig` slots (`tileCrossTick`, `tierCruiseEnter`, `tierKineticRamEnter`, `brakeAnchor`, `switchToggle`, `gateOpenClose`, `closeCall`, `bufferBank`, `bluntWallBump`), wiring `GameplayHapticsFeedback` for ALL 7 `HapticEvent` values (`PadRedirect`, `LaunchStart`, `SwitchToggle`, `Death`, plus Tier 3 double-thump), and wiring empty `InteractionEffectPlayer` prefabs (`Brake`, `Exit`, `Hazard`, `Wedge`) + `LevelExit` magnetic goal pull-in).

3. **Every Pillar Must Explicitly Contain**:
   - **Why it makes Gravita uniquely addictive** (player psychology + rhythmic kinetic feel).
   - **Exact numbers, curves, durations, and Inspector parameters** (ms, units, ortho delta, pitch curves, colors, cooldowns).
   - **Exact C# integration hooks** referencing REAL classes/events/methods verified in this repo (zero invented names; any proposed new class/method/field clearly labeled as `[NEW]`).
   - **Unity Inspector / Prefab wiring notes** (zero procedural `new GameObject` or runtime `AddComponent`).

4. **Hard Technical Constraints Compliance Check**:
   - No `new GameObject(...)` or runtime `AddComponent<...>()` anywhere in proposed solutions (use `[SerializeField]` Inspector refs or `VfxSpawner.Spawn` / `ObjectPoolManager.SpawnObject`).
   - Every DOTween call/snippet includes `.DOKill()`, `.SetUpdate(true)`, and `.SetLink(gameObject)` (and pooled returns kill tweens on despawn).
   - `FixedUpdate` deterministic cell-center physics (`PlayerMovement.SimulateStep`, `moveSpeed`, `LevelClock`, soft-lock rules) completely untouched.
   - No procedural UI hierarchy creation.
   - SRP / 4-layer architecture respected (Layer 4 `Feedback/` / `Audio/` / `UI/` observes Layer 3 events without modifying domain logic).

5. **Implementation Priority Roadmap**:
   - Ranked clearly into **Tier A: Quick Wins (Code & Inspector Tuning Only — Immediate Impact)** vs **Tier B: Medium Effort (Modular Feedback Scripts + Prefab Wiring)** vs **Tier C: Content / Audio / Shader Upgrades (Extra Sprites, SFX Clips, URP Material/Shader Polish)**.

6. **Accuracy & Cleanliness**:
   - No mention of arena, competitor number, or strategy card.
   - Every symbol name (`PlayerMovement`, `VelocityTier`, `PlayerImpactFeedback`, `PlayerSquashStretch`, `PlayerLineTrail`, `CinemachineCameraZoom2D`, `CinemachineCameraShake2D`, `CloseCallDetector`, `InteractionEffectPlayer`, `GameplayHapticsFeedback`, `GameHaptics`, `HapticEvent`, `EntityAudio`, `SoundManager`, `SoundConfig`, `SoundEntry`, `PowerUpController`, `CrackedBlock`, `BrittleFloor`, `FailingTile`, `BrakeTile`, `GravityBlock`, `DeflectionWedge`, `VectorPad`, `VectorLauncher`, `CircuitSwitch`, `EnergyGate`, `HazardActivation`, `SentinelDronePatrol`, `Collectible`, `LevelExit`, `LevelFlowController`, `VfxSpawner`, `ObjectPoolManager`, `ParticleSpawner`, `PrefabAnimationSpawner`) verified verbatim.
   - Tight, actionable, zero generic filler.
