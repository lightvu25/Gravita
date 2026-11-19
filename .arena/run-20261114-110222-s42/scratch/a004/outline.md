# Restructured Outline & Defensive Verification — `a004.md`

## Document Structure (Crisp, Senior-Engineer Blueprint)

1. **Title & Scope Header + Architectural Assumptions**
   - Concise framing of **Gravita** (Subject G-0, 32 PPU grid-locked `cellSize = 1` kinetic lab puzzle at `moveSpeed = 30` = `33.3 ms/tile`).
   - State explicit reconciliation of Velocity Tier threshold discrepancy: code (`VelocityTierUtil.FromTiles` in `Assets/Scripts/Gameplay/VelocityTier.cs`) uses `Drift = 1–2`, `Cruise = 3–4`, `KineticRam = 5+` (whereas design summary notes `1–3 / 4–7 / 8+`). All values below calibrate cleanly to the active `1–2 / 3–4 / 5+` thresholds via Inspector-tunable parameters.

2. **Section 1: Core Game Feel Diagnosis (Why Gravita Currently Feels Flat)**
   - **1.1 Temporal Mismatch (`33.3 ms/tile` vs `180–220 ms` curves)**: `PlayerSquashStretch` pulses and snaps back before long slides finish, ignores `OnRedirected`, and applies full impact squash regardless of `LastImpactTier`.
   - **1.2 Bottom-Center Pivot Geometry Inversion**: `Player/Sprite` at `localPosition (0, -0.5, 0)` makes `Vector2.up` squashes pull `-0.175` units (`5.6 px`) away from the ceiling wall and `Vector2.down` squashes sink `-0.125` units (`4 px`) into the floor.
   - **1.3 Event-Ordering & State-Reset Bugs**:
     - `PlayerMovement` fires `OnWallImpact` (L400/L429) *before* `StopMoving()` updates `LastImpactTier = CurrentTier`, forcing `EntityAudio` to play the previous dash's tier.
     - `StopMoving()` triggers `ResetDashTracking()` -> `OnVelocityTierChanged(None)` *before* `OnMoveStopped`, causing `PlayerLineTrail` to snap peak tier colors back to cyan `Drift` on the impact frame; `smoothedMoveSpeed` clamps at `28f` (< `30` normal, `48` PhaseLaunch).
     - `PlayerImpactFeedback.OnDisable()` stops `hitStopRoutine` without restoring `Time.timeScale = preHitStopTimeScale`.
   - **1.4 Dead Channels & Pixel-Perfect Traps**:
     - `OnTileCrossed` and `CloseCallDetector.OnCloseCallTriggered` have 0 external subscribers.
     - `CinemachineCameraZoom2D` has 0 callers and conflicts with `CinemachinePixelPerfect` (`GameScene.unity`) + `PixelPerfectCamera` (`320x180`, `32 PPU`, `GridSnapping: 1`).
     - `GameScene.unity` sets `tier1Shake = 0` and `redirectShake = 0`; `CinemachineCameraShake2D` lacks directional impulses; `HandleWallImpact` uses 3D `LookRotation` on 2D XY normals.
     - `SoundConfig.asset` ships empty `clips: []` arrays and `AudioSystems.prefab` lacks `pooledAudioSourcePrefab` (blocking per-voice pitch ramps); `GameplayHapticsFeedback` leaves 4 of 7 `HapticEvent` values unwired; `Brake`/`Exit`/`Hazard`/`Wedge` `InteractionEffectPlayer`s are empty.

3. **Section 2: Six Gravita-Specific Game Feel Pillars**
   Each pillar formatted consistently: **Kinetic Psychology -> Exact Numbers, Curves & Parameters -> C# Event Hooks & Script Specs -> Inspector / Prefab Wiring (Zero Procedural Objects)**:
   - **Pillar 1 — Kinetic Runway Engine (Momentum Escalation & Ghost Echoes)**
     - Center-compensated `PlayerSquashStretch` formula (`localPos = baseLocalPos + pivotToCenter*(1-s) + wallCenterTarget`), `0.04s` launch stretch held while moving (`+8%/+16%/+24%/+28%`), `0.035s` redirect flip, hit-stop-synced tiered squash (`45%/80%/100%` over `0.11s/0.14s/0.17s`).
     - `PlayerLineTrail` speed clamp raised to `55f` + latched `peakRetractTier` until `pointCount <= 2`.
     - New `KineticAfterimageSpawner` using pooled `GhostEcho` prefab (`ObjectPoolManager.PoolType.GameObject`): off in Drift, `1.0` tile gold in Cruise, `0.65` tiles magenta in KineticRam, `0.70` tiles violet in PhaseLaunch.
     - `EntityAudio` + `SoundManager.PlaySFXWithPitch`: `OnTileCrossed` ascending `+1` semitone/tile ticks + `OnVelocityTierChanged` Cruise/KineticRam lock-in chimes (requires `pooledAudioSourcePrefab` on `AudioSystems.prefab`).
   - **Pillar 2 — Directional Impact & Stop Polarity (Wall-Slam vs. Mag-Brake Contrast)**
     - Fix `PlayerMovement` `LastImpactTier` assignment timing + `PlayerImpactFeedback.OnDisable` `Time.timeScale` restore guard.
     - Upgrade `CinemachineCameraShake2D` with `ShakeDirectional(Vector2 dir, float force, float perpJitter = 0.15f)` using `GenerateImpulse(Vector3)`; configure `tier1Shake=0.07`, `tier2Shake=0.18`, `tier3Shake=0.34`, `blockBreakBonus=+0.14`, `tier3HitStop=0.075s`, `blockBreakHitStop=+0.025s`.
     - Fix 2D impact burst rotation (`Atan2` Z-axis instead of `LookRotation`) + tier-scaled burst + `55 ms` white flash on shatter/tier 3 slam.
     - Contrast `BrakeTile` (`InteractionKind.Brake`) & `GravityBlock` (`OnPlayerAnchored`): zero shake/hit-stop, magnetic `1.0 -> 0.90 -> 1.0` damped catch, vacuum-down SFX, `HapticEvent.GravityAnchor`.
   - **Pillar 3 — Pinball Ricochet Chain Symphony (`DeflectionWedge`, `VectorPad`, `VectorLauncher`)**
     - `RicochetChainTracker` tracking consecutive `OnRedirected` hits between `OnMoveStarted` and `OnMoveStopped` (up to `maxConsecutiveRedirects = 8`).
     - Major pentatonic ascending scale (`{0, 2, 4, 7, 9, 12, 14, 16}` semitones) on redirects + scaled directional corner kick (`0.08 + 0.015 * min(chain-1, 6)`).
     - Interactable bumper punch (`DOPunchScale` `0.20`, `0.14s`) + directional chevron VFX (`InteractionEffectPlayer` `Deflect`/`Launch`) + `HapticEvent.PadRedirect` / `LaunchStart`.
   - **Pillar 4 — Pre-Armed Turn Telegraph & Blocked-Move Tactility (`BufferTurnsUntilStop`)**
     - Expose read-only `OnPendingDirectionChanged` and `OnBlockedDirectionInput` on `PlayerMovement` (zero physics changes).
     - New `PlayerTurnTelegraph` driving a serialized child sprite anchor (`0.48` units toward `pendingDir`, `0.55 -> 1.15 -> 1.0` `OutBack` over `0.085s`, `tickQueued` pitch `1.35` + `HapticEvent.UISelect`, snap-flash on execution).
     - `OnBlockedDirectionInput` `3 px` (`0.094` u) micro-nudge punch (`0.09s`) + muted `wallBlocked` thump.
   - **Pillar 5 — Near-Miss Adrenaline & Pixel-Safe Overdrive (Close Call + Hazard Telegraph + Volume FX)**
     - Resolve `CinemachineCameraZoom2D` vs `CinemachinePixelPerfect`: keep camera pixel-locked and replace continuous ortho zoom with a high-priority `OverdriveVolume` (`ChromaticAberration 0.38`, `Vignette 0.52`, `Bloom +2.5`) blending `volume.weight` (`0 -> 0.55` in `KineticRam`, `1.0` spike `0.05s` -> `0.22s` decay on Tier 3 smash / `CloseCall`).
     - `CloseCallFeedback` subscribing to `CloseCallDetector.OnCloseCallTriggered` (zero `Time.timeScale` mutation): pooled radial graze ring, high-voltage zip chime (`1.15f` pitch), `MusicManager.DuckPulse(0.45f, 0.04f, 0.16f)`, pooled `+5` world badge (`+0.65` u drift, `0.45s`).
     - `HazardTelegraphFeedback` subscribing to `HazardActivation.OnPhaseChanged`: `0.25s` pre-fire electric hum + emitter scale/glow pulse (`1.0 -> 1.18 -> 1.0`).
   - **Pillar 6 — Tactile Circuitry, Destructibles & Complete Haptic-UI Harmony**
     - Two-stage `BrittleFloor` (`Crack` `-2 px` sink + sharp snap `1.0` vs `Break` shard burst + `0.78` collapse + `0.08` shake).
     - `CircuitSwitch` -> `EnergyGate` relay snap (`DOPunchScale 0.25, 0.14s`, `switchToggle`, `HapticEvent.SwitchToggle`, gate open/lock `0.12s OutBack`).
     - `LevelExit` magnetic center-pull (`visualTransform.DOLocalMove` `0.14s InOutCubic` into exit center) + 3-note triumph chime + warm bloom burst.
     - Complete `GameplayHapticsFeedback` coverage for all 7 `HapticEvent` values + settings toggle respect (`HitStopEnabled` / `ScreenShakeEnabled` prefs cleanly integrated without altering UI hierarchy procedurally).

4. **Section 3: Technical Guardrails & Legacy Anti-Patterns to Avoid**
   - Concise checklist of constraints: no `new GameObject()` / runtime `AddComponent<>()` (call out `PowerUpController.cs` L928–952 & L1448–1504 as legacy debt never to repeat), DOTween `.DOKill()/.SetUpdate(true)/.SetLink(gameObject)`, `FixedUpdate` determinism invariant, pool cleanup on `OnDisable` / level reset.

5. **Section 4: Implementation Priority Roadmap**
   - **Phase A — Quick Wins (Code & Existing Scene Tuning, Zero New Art/Audio Assets Required)**: exact ordered list with impact vs effort.
   - **Phase B — Audio, VFX Prefab & Shader/Volume Content Upgrades**: exact ordered list of assets/entries (`SoundConfig` entries + `pooledAudioSourcePrefab`, `GhostEcho` prefab, `OverdriveVolume` profile, `TurnTelegraph` child sprite, `InteractionEffectPlayer` prefabs, `GrazeBadge` UI prefab).

## Final Defensive Self-Audit Before Writing `a004.md`
- Word check & density: Tight, razor-sharp engineering prose—every sentence earns its place. Zero generic filler.
- Forbidden leakage check: ZERO mention of arena, strategy cards, competitor IDs, or scratch paths.
- Symbol verification: Every class, event, property, enum value, prefab, and scene object referenced matches exact verified names (`PlayerMovement`, `VelocityTier`, `VelocityTierUtil`, `PlayerSquashStretch`, `PlayerLineTrail`, `PlayerImpactFeedback`, `CinemachineCameraShake2D`, `CinemachineCameraZoom2D`, `CinemachinePixelPerfect`, `PixelPerfectCamera`, `CloseCallDetector`, `InteractionEffectPlayer`, `InteractionKind`, `IPlayerInteractable`, `MazeTile`, `GravityBlock`, `BrakeTile`, `DeflectionWedge`, `VectorPad`, `VectorLauncher`, `CrackedBlock`, `BrittleFloor`, `CircuitSwitch`, `EnergyGate`, `HazardActivation`, `HazardCyclePhase`, `LevelExit`, `EntityAudio`, `SoundManager`, `SoundConfig`, `SoundEntry`, `MusicManager`, `GameplayHapticsFeedback`, `GameHaptics`, `HapticEvent`, `ObjectPoolManager`, `VfxSpawner`, `ParticleSpawner`, `PrefabAnimationSpawner`, `PooledParticlePlayer`, `PowerUpController`, `PlayerCosmeticApplier`, `GameScene.unity`, `AudioSystems.prefab`).
