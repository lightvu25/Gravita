# Socratic Interrogation & Free-Draft Sandbox — Gravita Game Feel Blueprint

## Part 1: Socratic Self-Interrogation

### Q1: Gravita already has `PlayerSquashStretch`, `PlayerLineTrail`, `PlayerImpactFeedback`, `CinemachineCameraShake2D`, `CinemachineCameraZoom2D`, `CloseCallDetector`, `InteractionEffectPlayer`, `EntityAudio`, and `GameplayHapticsFeedback`. Why does it STILL feel flat in play?
Because nine separate systems either operate at the wrong timescale for `moveSpeed = 30`, sabotage each other through event-ordering bugs, distort around the wrong sprite pivot, or sit completely unwired in `GameScene.unity` and Prefabs:
1. **33.3 ms Tile Cadence vs. 180–220 ms Animation Curves**: At `moveSpeed = 30` and `cellSize = 1`, each cell takes `33.3 ms`. A `Drift` slide (1–2 tiles) lasts `33–67 ms`; a `Cruise` slide (3–4 tiles) lasts `100–133 ms`. Yet `PlayerSquashStretch` runs `stretchDuration = 0.18s` (72 ms peak + 108 ms return to `baseScale`) and `squashDuration = 0.22s` (88 ms press + 132 ms return). Launch stretch returns to idle scale mid-slide instead of sustaining aerodynamic tension, while short `Drift` dashes collide before launch stretch finishes. Worse, `HandleMoveStopped()` applies full `impactHeightPx = 16 / movingHeightPx = 28` deformation identically for a 1-tile tap and a 10-tile `KineticRam`, and ignores `OnRedirected` completely.
2. **Bottom-Center Pivot (`(0, -0.5, 0)`) Inverts Wall-Contact Squash**: `Player/Sprite` sits at `localPosition = (0, -0.5, 0)` (bottom-center pivot for `32 PPU` 1x1 sprite). Scaling `y` from `y = -0.5`:
   - On an upward slam (`Vector2.up`), compressing `sy` to `0.70` drops the top face by `0.30` world units (`9.6 px`), whereas `wallPressPx = 4` (`0.125` units) shifts it upward—producing a **net `0.175`-unit (`5.6 px`) gap AWAY from the wall**.
   - On a downward slam (`Vector2.down`), shifting `localPosition.y` down by `0.125` pushes the bottom edge **4 px inside the solid floor**.
   - On horizontal runs, `y` shrinks toward `-0.5` instead of center `(0, 0)`, making Subject G-0 droop toward its feet.
3. **Critical Event Ordering Bugs Mute Tier Payoff & Trail Clarity**:
   - **Audio Tier Off-by-One**: `PlayerMovement.AdvanceSlideStep` invokes `OnWallImpact?.Invoke(stopHit.normal)` *before* `StopMoving(wallImpact: true)` writes `LastImpactTier = CurrentTier`. `EntityAudio.HandlePlayerWallImpact` reads `playerMovement.LastImpactTier`, so every wall hit plays the *previous* dash's impact sound (or falls back to `Drift` on dash #1).
   - **Trail Premature Color Snap**: `PlayerMovement.StopMoving()` invokes `ResetDashTracking()` -> `OnVelocityTierChanged(VelocityTier.None)` *before* `OnMoveStopped`. `PlayerLineTrail.HandleTierChanged` refreshes color on `None`, snapping the retracting trail back to cyan `Drift` on the impact frame instead of holding `Cruise` gold or `KineticRam` magenta during retraction. Also `PlayerLineTrail` clamps `smoothedMoveSpeed` at `28f`, below base `moveSpeed = 30` and `PhaseLaunch = 48`.
4. **Momentum Between Start and Stop Is Invisible**: `OnTileCrossed` has zero subscribers anywhere in the codebase. Crossing 1, 2, 3, 4, 5+ tiles produces no ascending pitch ticks, no afterimage shedding, no tier-up lock-in chime, and no sustained silhouette shift.
5. **Dormant Hooks & Disabled Scene Wiring**:
   - `GameScene.unity` zeroes out micro-impacts (`tier1Shake = 0`, `redirectShake = 0`).
   - `CinemachineCameraShake2D` only generates scalar omnidirectional noise (`GenerateImpulse(float)`), losing directional impact recoil.
   - `PlayerImpactFeedback.HandleWallImpact` uses `Quaternion.LookRotation((Vector3)normal)` on 2D XY normals, twisting local axes out of the 2D XY plane.
   - `CloseCallDetector.OnCloseCallTriggered` has zero external subscribers.
   - `CinemachineCameraZoom2D` (`normalOrthographicSize = 5.5`) has zero callers—AND sits on `CinemachineCamera` alongside `CinemachinePixelPerfect` + `PixelPerfectCamera` (`320x180`, `32 PPU`, `GridSnapping: 1`), which overrides `OrthographicSize` and causes pixel-grid conflicts if tweened continuously.
   - `Global Volume` (`ChromaticAberration`, `Vignette`, `Bloom`, `ColorAdjustments`) and `BlurVolume` sit static during gameplay.
   - `SoundConfig.asset` currently has empty `clips: []` across all 23 entries and lacks slots for progressive ticks, tier-ups, close calls, brakes, blocked nudges, or switches; `AudioSystems.prefab` lacks `pooledAudioSourcePrefab`; `GameplayHapticsFeedback` ignores `PadRedirect`, `LaunchStart`, `SwitchToggle`, and `Death`; `Brake`, `Exit`, `Hazard`, and `Wedge` `InteractionEffectPlayer`s have empty rules.

### Q2: What makes Gravita *uniquely Gravita* rather than generic platformer/arcade juice?
- Gravita is a **high-speed kinetic lab slide-puzzle** (`30 u/s` grid-locked stop-to-stop bursts) built on five interlocking experiences:
  1. **Zero-Friction Acceleration & Tier Thresholds (`Drift` 1-2 / `Cruise` 3-4 / `KineticRam` 5+)**: Longer straightaways are *weaponized*—earning Tier 3 lets you shatter `CrackedBlock` obstacles. Game feel must turn every tile crossed into a tactile "railgun charge-up."
  2. **Queued Corner Mastery (`bufferTurnsUntilStop = true`)**: Players fire directional inputs *while* sliding so Subject G-0 whips 90 degrees instantaneously at the next wall. Confirming a queued turn mid-dash makes high-speed routing feel telepathic instead of accidental.
  3. **Zero-Loss Pinball Chains (`maxConsecutiveRedirects = 8`)**: `DeflectionWedge`, `VectorPad`, and `VectorLauncher` redirect without wiping `TilesTraveled`. Chaining bounces should sing like a rising harmonic instrument.
  4. **Contrast Between Violent Wall Stops & Magnetic Anchors**: Slams (`WallImpact` / `CrackedBlock`) smash into solid lab walls; `BrakeTile` and `GravityBlock` catch Subject G-0 magnetically in open space. Contrasting crisp recoil against silky dampening elevates both.
  5. **Surgical Lab Danger (`PulseLaser`, `OverloadedFloor`, `SentinelDronePatrol`, `CloseCallDetector`)**: Brushing past lethal hazards at `30 u/s` triggers `+5` Close Call bonuses—demanding razor-sharp audio/VFX telemetry without corrupting `FixedUpdate` determinism or `Time.timeScale` pauses.

### Q3: What happens if a feature breaks under defensive / edge-case stress?
- **`CinemachineCameraZoom2D` vs `CinemachinePixelPerfect`**: Tweaking `OrthographicSize` smoothly causes shimmering or gets overwritten every frame by `CinemachinePixelPerfect`. Fix: DO NOT zoom continuously under `CinemachinePixelPerfect`; use directional `CinemachineImpulseSource` positional kick + pixel-neutral **URP Overdrive Volume** (`weight` blend of `ChromaticAberration`, `Bloom`, `Vignette` on a separate high-priority `Volume` so the shared `.asset` isn't dirtied).
- **`Time.timeScale` Contention**: `PlayerImpactFeedback` writes `Time.timeScale = 0f`; Pause UI / `CutsceneManager` also control `timeScale`. Never let `CloseCallDetector`, `PlayerSquashStretch`, or trails fight over `Time.timeScale`. Centralize hit-stop inside `TimeScaleCoordinator` (or upgrade `PlayerImpactFeedback` to check pause state cleanly and cancel cleanly in `OnDisable`, restoring `1f` if interrupted mid-coroutine—right now `PlayerImpactFeedback.OnDisable` stops `hitStopRoutine` on L76 *without* restoring `Time.timeScale = preHitStopTimeScale`!! HUGE BUG CATCH: if the player restarts/dies/disables during `tier3HitStop` (`0.083s`), `Time.timeScale` stays stuck at `0f`!).
- **Single `AudioSource` Pitch Bleed**: Playing pitched `OnTileCrossed` ticks every `33 ms` via `PlayOneShot` while modulating `AudioSource.pitch` bends already-ringing impact/pickup sounds! Fix: wire a pooled `AudioSource` prefab onto `SoundManager.pooledAudioSourcePrefab` (in `AudioSystems.prefab`) and add `SoundManager.PlaySFXWithPitch(SoundEntry, float pitchMultiplier, float volumeScale)`.
- **No Procedural Hierarchy Creation**: Zero `new GameObject()` or runtime `AddComponent<T>()` (and explicitly note `PowerUpController.cs` lines 928-952 & 1448-1504 as legacy debt not to imitate). Everything spawns via `ObjectPoolManager` / `VfxSpawner` / `ParticleSpawner` / `PrefabAnimationSpawner` or serialized child transforms.
- **DOTween Safety**: Every tween uses `.DOKill()`, `.SetUpdate(true)`, `.SetLink(gameObject)`—and never calls `.DOKill()` across transforms without clear target isolation.
- **Scale & Reset Safety on `OnPlayerDied` / Level Reset**: When `LevelFlowController` teleports/restarts Subject G-0, `KineticAfterimageSpawner`, `TurnTelegraph`, `OverdriveVolume`, and `RicochetChainTracker` must subscribe to reset/disable cleanup and return pooled ghosts cleanly (`PooledParticlePlayer` / pooled component on `OnDisable`).

---

## Part 2: Free-Draft Blueprint Content

### Core Diagnosis
Gravita has strong bones—clean `PlayerMovement` events (`OnMoveStarted`, `OnTileCrossed`, `OnVelocityTierChanged`, `OnPhasingChanged`, `OnRedirected`, `OnWallImpact`, `OnMoveStopped`), `CloseCallDetector`, `InteractionEffectPlayer`, `GameHaptics`, and `SoundManager`—yet plays like an early graybox because:
1. **Temporal calibration ignores `moveSpeed = 30` (`33.3 ms/tile`)**: 180–220 ms stretch/squash curves outlast short slides and return to rest mid-long dash; `Squash` isn't scaled by `LastImpactTier`.
2. **Broken Ordering & Pivot Math**: `OnWallImpact` fires before `LastImpactTier` updates (wrong SFX tier), `ResetDashTracking()` resets trail tier color before retraction begins, `(0, -0.5, 0)` pivot makes ceiling squashes pull off walls (`-5.6 px` net offset), `HandleWallImpact` uses 3D `LookRotation` for 2D particle bursts, `PlayerImpactFeedback.OnDisable` can leak `Time.timeScale = 0f` if killed during hit-stop.
3. **Dormant Momentum, Combo, and Danger Channels**: `OnTileCrossed` has 0 listeners; `CloseCallDetector.OnCloseCallTriggered` has 0 listeners; `CinemachineCameraZoom2D` fights `CinemachinePixelPerfect` and has 0 callers; `GameScene.unity` zeroes `tier1Shake` and `redirectShake`; buffered turns are silent/invisible; `Global Volume` overrides sit static; `SoundConfig.asset` lacks populated clips and kinetic progression entries; `GameplayHapticsFeedback` ignores redirects/launches/switches/deaths.

### The 6 Pillars
1. **Pillar 1: Kinetic Runway Engine (Momentum Escalation, Center-Compensated Squash & Ghost Echoes)**
   - Why addictive: Turns `33.3 ms` corridors into rising kinetic anticipation (`Drift` -> `Cruise` -> `KineticRam`).
   - Exact numbers:
     - `PlayerSquashStretch`: `stretchInDuration = 0.040s` (`OutQuad`), sustain glide deformation while `IsMoving` (`Drift`: `+8%` length / `-4%` width; `Cruise`: `+16%` / `-8%`; `KineticRam`: `+24%` / `-12%`; `PhaseLaunch`: `+28%` / `-14%`), immediate `0.035s` axis flip on `OnRedirected`. Center-compensated `localPosition` math for bottom-pivot sprites so wall-facing edge presses into the wall by `2 px` (`Drift`), `4 px` (`Cruise`), `6 px` (`KineticRam`). Tiered squash magnitudes (`45% / 80% / 100%` of `impactWidthPx=28 / impactHeightPx=16` over `0.11s / 0.14s / 0.17s`), holding peak compression through hit-stop.
     - `PlayerLineTrail`: Raise speed clamp from `28f` to `55f`. Latch `peakRetractTier` across `StopMoving` so the trail retains `Cruise` gold / `KineticRam` magenta (`headColor`) while retracting (`stopRetractSpeedMultiplier = 3.2`), resetting only when `pointCount <= 2`.
     - `KineticAfterimageSpawner` (new, pooled `GhostEcho` prefab via `ObjectPoolManager.PoolType.GameObject`): `Drift` = none; `Cruise` = every `1.0` tile, tint `#FFE14D`, alpha `0.45 -> 0` over `0.11s` (`OutQuad`); `KineticRam` = every `0.65` tiles (`21.7 ms`), tint `#FF52C7`, alpha `0.72 -> 0`, scale `1.08 -> 0.85` over `0.16s`; `PhaseLaunch` = every `0.70` tiles, `#B86BFF`, alpha `0.68 -> 0`, scale `1.15 -> 0.90` over `0.18s`.
     - Progressive Rail Ticks & Tier Chimes (`EntityAudio` + `SoundManager.PlaySFXWithPitch`): `OnTileCrossed(n)` plays crisp rail tick (`vol = 0.22 + 0.03 * min(n, 6)`, pitch = `0.94 * 2^(min(n-1, 7)/12)`, i.e. `+1` semitone per tile); `OnVelocityTierChanged` triggers harmonic lock chime at Tile 3 (`Cruise`, pitch `1.0`) and overcharge lock chime at Tile 5 (`KineticRam`, pitch `1.189`).

2. **Pillar 2: Directional Impact & Stop Polarity (Wall-Slam vs. Mag-Brake Contrast)**
   - Why addictive: Heavy kinetic collisions contrast sharply with smooth `BrakeTile` / `GravityBlock` magnetic catches.
   - Exact numbers:
     - Fix `PlayerMovement.StopMoving` / `AdvanceSlideStep` ordering so `LastImpactTier = CurrentTier` BEFORE `OnWallImpact`.
     - Fix `PlayerImpactFeedback.OnDisable` timeScale leak (restore `preHitStopTimeScale` if interrupted during active `hitStopRoutine`, guard against pause menu).
     - `CinemachineCameraShake2D`: Add `ShakeDirectional(Vector2 travelOrNormal, float force, float perpendicularJitter = 0.15f)` using `impulseSource.GenerateImpulse(Vector3)` so camera snaps along collision axis. Values: `tier1Shake = 0.07f`, `tier2Shake = 0.18f`, `tier3Shake = 0.34f`, `blockBreakBonusShake = 0.14f`, `redirectShake = 0.08f`; Hit-stop `tier1 = 0s`, `tier2 = 0.033s` (2 frames), `tier3 = 0.075s` (4.5 frames), `blockBreakBonus = +0.025s`.
     - 2D burst rotation fix: `Quaternion.Euler(0f, 0f, Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg + burstAngleOffset)` + tier scale `0.65 / 1.0 / 1.45`. Plus `55 ms` additive white hit-flash on `CrackedBlock` shatter & wall slams >= `Cruise` (using sprite shader `_HitFlash` or tint pulse with original cosmetic restore!).
     - Stop Polarity (`BrakeTile` / `GravityBlock`): Zero camera shake, zero hit-stop. Gentle `1.0 -> 0.90 -> 1.0` (`0.06s OutQuad` / `0.10s OutSine`) magnetic sink, radial catch ring (`InteractionEffectPlayer` `InteractionKind.Brake`), descending damped mag-hum SFX (`vol 0.55`, `pitch 0.92 -> 0.78`), `HapticEvent.GravityAnchor`.

3. **Pillar 3: Pinball Ricochet Chain Symphony (`DeflectionWedge`, `VectorPad`, `VectorLauncher`)**
   - Why addictive: Celebrates `maxConsecutiveRedirects = 8` no-stop routing combos as musical pinball streaks.
   - Exact numbers:
     - `RicochetChainTracker` (or inside `PlayerImpactFeedback` + `EntityAudio`): tracks `chainCount` from `OnMoveStarted` until `OnMoveStopped`.
     - Ascending major pentatonic scale (`semitones = {0, 2, 4, 7, 9, 12, 14, 16}` -> multipliers `1.00, 1.122, 1.260, 1.498, 1.682, 2.000, 2.245, 2.520`) via `SoundManager.PlaySFXWithPitch`.
     - Directional impulse along `Vector2.Lerp(-oldDir, newDir, 0.5f).normalized` with `force = 0.08f + 0.015f * Mathf.Clamp(chainCount - 1, 0, 6)`.
     - Bumper reaction on `DeflectionWedge` / `VectorPad` / `VectorLauncher` via `InteractionEffectPlayer` / dedicated `InteractableBounceVisual` listening to `IPlayerInteractable.OnPlayerInteracted` (`Deflect` / `Launch`): `DOPunchScale(new Vector3(0.20f, 0.20f, 0f), 0.14f, 6, 0.6f).SetUpdate(true).SetLink(gameObject)` + `45 ms` bright flash + directional chevron sparks (`VfxSpawner.Spawn`).
     - `GameplayHapticsFeedback` wires `OnRedirected` -> `HapticEvent.PadRedirect`, `VectorLauncher` -> `HapticEvent.LaunchStart`.

4. **Pillar 4: Pre-Armed Turn Telegraph & Blocked-Move Thud**
   - Why addictive: Makes `bufferTurnsUntilStop = true` visible and responsive at `30 u/s`, eliminating "did my swipe work?" doubt.
   - Exact numbers:
     - Expose lightweight events on `PlayerMovement`: `event Action<Vector2> OnPendingDirectionChanged` and `event Action<Vector2> OnBlockedDirectionInput` (zero gameplay side effects!).
     - **`PlayerTurnTelegraph`** component controlling a pre-placed serialized child `Transform` + `SpriteRenderer` (`arrowAnchor`, hidden when `Vector2.zero`, NOT procedurally spawned!):
       - Offset `0.48` world units (`15.4 px`) along `pendingDir`, rotated cleanly at 90° steps.
       - Pop animation: `scale 0.55 -> 1.15` in `0.045s` (`OutBack`) then settle `1.0` in `0.04s`, pulsing alpha `0.75..1.0` (`.DOKill().SetUpdate(true).SetLink(gameObject)`).
       - Plays crisp `tickQueued` (`pitch 1.35f`, `vol 0.28f`) + `HapticEvent.UISelect`.
       - Clears immediately on `OnMoveStarted` (briefly flashes `1.25x` outward `0.05s` to confirm snap execution) or `OnMoveStopped` if rejected.
     - Blocked-input micro-thud (`OnBlockedDirectionInput(dir)`): `visualTransform` local-position punch `dir * 0.094f` (`3 px`) for `0.09s` (`vibrato = 1`, `elasticity = 0`, `.DOKill().SetUpdate(true).SetLink()`) + low muted thump (`wallBlocked`, `vol 0.35`, `pitch 0.85`).

5. **Pillar 5: Near-Miss Adrenaline & Pixel-Safe Overdrive (Close Call + Hazard Telegraph + Volume FX)**
   - Why addictive: Makes brushing `PulseLaser` and `SentinelDronePatrol` at `30 u/s` thrilling, while fixing the `CinemachinePixelPerfect` vs `CinemachineCameraZoom2D` trap.
   - Exact numbers:
     - Fix/avoid the `CinemachineCameraZoom2D` trap: Since `GameScene.unity` runs `CinemachinePixelPerfect` (`1780404332`) + `PixelPerfectCamera` (`320x180`, `32 PPU`), do NOT tween `OrthographicSize` directly. Instead add a dedicated **Overdrive Post-FX Volume** (`OverdriveVolume`, high priority, profile holding `ChromaticAberration` `0.38`, `Vignette` `0.52`, `Bloom` `intensity +2.5`), blending `volume.weight` (`0 -> 0.55` during `KineticRam`, spiking to `1.0` for `0.05s` and decaying over `0.22s OutCubic` on `KineticRam` wall/block smash or `CloseCall`, `.DOKill().SetUpdate(true).SetLink()`).
     - `CloseCallFeedback` subscriber to `CloseCallDetector.OnCloseCallTriggered(Vector3 hazardPos)`:
       - Keeps `Time.timeScale` completely untouched (respecting `CloseCallDetector` contract and zero `HitStop` race!).
       - Spawns pooled additive graze ring (`PrefabAnimationSpawner`) + electric zip-chime (`closeCall`, `pitch 1.15f`) + `MusicManager.DuckPulse(0.45f, hold: 0.04f, fadeOut: 0.16f)` + pooled floating `+5` badge (`ObjectPoolManager.PoolType.UI`, drifting `+0.65` units up over `0.45s OutCubic`, fading last `0.18s`).
     - `HazardTelegraphFeedback` listening to `HazardActivation.OnPhaseChanged`: plays `0.25s` pre-charge tick/hum (`telegraphPulse`) + additive emitter glow pulse (`scale 1.0 -> 1.18 -> 1.0` during `Telegraph`), snapping sharply on `Active`.

6. **Pillar 6: Tactile World Circuitry, Destructibles & Complete Haptic-Audio Loop**
   - Why addictive: Every interactive lab prop (`BrittleFloor`, `CircuitSwitch`, `EnergyGate`, `LevelExit`) feels physically coupled to a high-voltage subterranean lab.
   - Exact numbers:
     - `BrittleFloor`: Two-stage contrast—stepping across (`InteractionKind.Crack`) spawns micro-dust + subtle vertical sink (`-2 px` for `0.06s`) + sharp ceramic crack pitch `1.0f`; breaking (`InteractionKind.Break`) triggers shard burst + pitch `0.78f` collapse + `0.08f` shake.
     - `CircuitSwitch` & `EnergyGate`: Toggling (`OnSwitchStateChanged`) punches switch core (`DOPunchScale 0.25f, 0.14s`), triggers `HapticEvent.SwitchToggle`, plays crisp relay snap (`switchToggle`), and drives `EnergyGate.OnGateOpenStateChanged` electric retract/lock pulse (`0.12s OutBack`) + barrier hum cutoff.
     - `LevelExit`: Replacing flat trigger stop with **Magnetic Core Pull**: tween `visualTransform` smoothly into portal center over `0.14s` (`InOutCubic`, `.DOKill().SetUpdate(true).SetLink()`), ascending 3-note triumph arpeggio + bloom flash (`OverdriveVolume` warm tint / burst VFX via `InteractionEffectPlayer` `InteractionKind.Exit`).
     - Complete `GameplayHapticsFeedback` wiring: cover all 7 existing `HapticEvent` enum values (`WallImpact`, `GravityAnchor`, `PadRedirect`, `LaunchStart`, `SwitchToggle`, `Death`, plus `UISelect` for turn queue) without inventing unsupported backend calls.
