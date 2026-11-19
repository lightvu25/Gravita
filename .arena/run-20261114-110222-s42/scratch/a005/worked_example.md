# Worked Example Trace: 10-Tile L-Rebound Kinetic Chain in Gravita

**Verified Baseline Parameters (from `GameScene.unity`, `PlayerMovement.cs`, `VelocityTier.cs`):**
- `PlayerMovement.moveSpeed = 30` units/sec (`cellSize = 1.0`).
- Frame budget at 60 FPS: `dt = 16.67 ms` (`0.50` units/frame -> `2 frames / tile` = `33.33 ms / tile`).
- Velocity tier thresholds (`VelocityTierUtil.FromTiles`): `Drift` = 1–2 tiles (`33.3–66.7 ms`), `Cruise` = 3–4 tiles (`100.0–133.3 ms`), `KineticRam` = 5+ tiles (`166.7+ ms`).

---

## Sequence Geometry
Player starts stationary at `(0, 0)`, swipes **Right** `(1, 0)`:
1. `(0, 0) -> (1, 0)`: Tile 1 (`Drift`, `33.3 ms`)
2. `(1, 0) -> (2, 0)`: Tile 2 (`Drift`, `66.7 ms`)
3. `(2, 0) -> (3, 0)`: Tile 3 (`Cruise` ignition, `100.0 ms`); passes active-warning `PulseLaser` (`HazardActivation.Warning`, `TimeUntilActive = 0.09s <= 0.15s`) at `(3, 0)` -> triggers `CloseCallDetector.OnCloseCallTriggered`.
4. `(3, 0) -> (4, 0)`: Tile 4 hits `DeflectionWedge` (`Right -> Up`, `133.3 ms`, `OnRedirected((0, 1))`, `TilesTraveled` preserved = `4`).
5. `(4, 0) -> (4, 1)`: Tile 5 (`KineticRam` ignition, `166.7 ms`). Player buffers **Left** `(-1, 0)` at `t = 190 ms` (`HasPendingDirection = true`).
6. `(4, 1) -> (4, 3)`: Tile 7 hits `CrackedBlock` at `(4, 3)` (`233.3 ms`) -> `CrackedBlock.Break()` mid-slide while continuing **Up**.
7. `(4, 3) -> (4, 6)`: Tile 10 stops at `(4, 6)` (`333.3 ms`):
   - **Variant A**: Solid wall stop (`OnWallImpact`, `LastImpactTier = KineticRam`).
   - **Variant B**: `BrakeTile` / `GravityBlock` magnetic stop (`HaltAt` -> `StopMoving(wallImpact: false)`).

---

## Timeline Analysis: Current Implementation vs. Required Feel

| Game Time / Tile | Event (`PlayerMovement` / Mechanic) | What Currently Happens (Observed Failure) | Required Kinetic / Sensory Target |
| :--- | :--- | :--- | :--- |
| `0.0 ms` (Start) | `OnMoveStarted((1,0))` | `PlayerSquashStretch` starts `0.18s` stretch (`0.12`). `EntityAudio` plays `moveStart`. `CinemachineCameraZoom2D` untouched (`5.5`). | Instant impulse pop (`1.22, 0.80` -> spring back in `110 ms` unscaled), crisp thruster puff, `HapticEvent.LaunchStart`, subtle camera kick (`0.04`). |
| `33.3 ms` (Tile 1) & `66.7 ms` (Tile 2) | `OnTileCrossed(1..2)` | Silent. `PlayerLineTrail` still ramping `smoothedMoveSpeed` (clamped at `28f` vs `30f` real speed). | Quantized subtle rail-tick audio ascending per tile (`pitch = 1.00 + tile * 0.03`), faint micro-ribbon trail establishes axis. |
| `100.0 ms` (Tile 3) | `OnVelocityTierChanged(Cruise)` | `PlayerSquashStretch.HandleTierChanged` kills Start stretch mid-return and launches `0.153s` X-stretch. **Zero audio cue, zero camera zoom, zero post-process.** | Sonic pitch step-up ("Cruise Lock"), ortho zoom widens `5.5 -> 5.75`, bloom/chromatic edge wakes gently, cyan glow outline brightens. |
| `105.0 ms` (Near `(3,0)`) | `CloseCallDetector.OnCloseCallTriggered` | Spawns `closeCallVfxPrefab` + `0.06f` camera shake only. **`OnCloseCallTriggered` has 0 subscribers**: 0 audio sting, 0 time accent, 0 HUD feedback, `bonusCoins` gated `false`. | **Near-Miss Overdrive**: `Time.timeScale` micro-dip `0.42x` for `70 ms` unscaled (safe vs hit-stop via coordinator), sharp camera zoom-in snap `-> 5.20` (`60 ms`) then release, electric arc VFX + pitched sting + diegetic floating `"GRAZE +1"` / chain multiplier. |
| `133.3 ms` (Tile 4) | `OnRedirected((0,1))` on `DeflectionWedge` | **CRITICAL BUG**: `PlayerSquashStretch` does NOT subscribe to `OnRedirected`! The `0.153s` X-stretch started at `t=100ms` continues squashing Y and stretching X for 2–3 frames *while moving Up*! `PlayerImpactFeedback.redirectShake = 0`. `Wedge.prefab` `defaultCameraShake = 0`. `HapticEvent.PadRedirect` NEVER called. | **Bank-Shot Snap**: `DOTween` kills active deformation immediately, snaps directional stretch to **new** axis `(0,1)`, `25 ms` micro hit-stop, `0.09` diagonal impulse shake, wedge spark burst + harmonic chime (`semitone +2 * chainCount`), `HapticEvent.PadRedirect`. |
| `166.7 ms` (Tile 5) | `OnVelocityTierChanged(KineticRam)` | Plays another `0.153s` stretch. Otherwise completely silent and visually flat—no camera pull-back, no chromatic split, no shockwave ring, no afterimage trail. | **KineticRam Ignition**: Distinct plasma/sonic boom + sustained speed hum, ortho zooms out `-> 6.15`, `ChromaticAberration` ramps `0.20 -> 0.48`, `Afterimage` echoes drop every 1.25 tiles, sprite tint shifts hot cyan/white HDR trim. |
| `190.0 ms` (Mid-dash) | Player queues `Left (-1,0)` (`HasPendingDirection = true`) | **Zero visual anticipation!** Player doesn't know input registered during a fast `30 u/s` slide. | **Diegetic Input-Buffer Lock**: Small crisp directional hologram chevron / sprite micro-cant (`±6°`) points Left along the player chassis until consumed or cleared. |
| `233.3 ms` (Tile 7) | `CrackedBlock` smash at `(4,3)` | **CRITICAL BUG**: `AdvanceSlideStep` calls `TryCrush`, breaking block mid-dash without calling `StopMoving`. `Cracked.prefab` `defaultCameraShake = 0`. Result: **0 ms hit-stop, 0 camera shake, 0 hit-flash, 0 squash!** Breaking stone feels like passing air. | **Breakthrough Punch**: Explicit `CrackedBlock.OnAnyBroken` feedback: `45 ms` unscaled hit-stop, `0.22` directional camera punch, `HitFlashSprite` (`_FlashColor` HDR white flash for `45 ms`), momentary `0.86 / 1.18` armor compression without killing dash flow, `HeavyImpact` haptic! |
| `333.3 ms` (Tile 10, Var A) | Solid Wall Stop (`OnWallImpact` + `KineticRam`) | `PlayerImpactFeedback` sets `Time.timeScale = 0f` for `83 ms`. **CRITICAL BUG**: `PlayerSquashStretch` lacks `.SetUpdate(true)` -> sprite stays frozen at `1.0, 1.0` for all `83 ms`! Plus `Sprite` pivot `(0.5, 0)` at `LocalPos (0,-0.5,0)` causes asymmetric floor-edge deformation on X/Y hits. When hit-stop ends, buffered `Left` fires `OnMoveStarted` and kills the return spring! | **Unscaled Frame-0 Squash + Hit-Flash**: Instant frame-0 HDR white flash (`HitFlashSprite` shader via `MaterialPropertyBlock`) + `SetUpdate(true)` squash compressing toward **actual wall contact edge** (`-hitDir * wallPress`), radial dust ring + debris cone, ortho camera snaps back `6.15 -> 5.42` then settles to `5.50`, `HeavyImpact` haptic. |
| `333.3 ms` (Tile 10, Var B) | `BrakeTile` / `GravityBlock` (`HaltAt`) | `StopMoving(wallImpact: false)` -> `OnWallImpact` never fires. **EntityAudio plays ZERO sound** (no brake/anchor SFX hook). Silent dead-stop except `HapticEvent.GravityAnchor`. | **Magnetic Anchor Signature**: Distinct low-frequency bass/magnetic lock SFX, inward gravity implosion ring + floor clamp pulse, zero wall-ricochet sparks, crisp vertical settle (`0.88, 1.12 -> 1.0`) contrasting sharply with violent wall slams. |

---

## Key Quantitative Insights Derived from Trace
1. **Unscaled Time Is Mandatory on Everything Visual/Camera**: Any tween or lerp running in `Update`/`LateUpdate` using `Time.deltaTime` (`PlayerSquashStretch`, `CinemachineCameraZoom2D`, `PlayerLineTrail`) dies during `PlayerImpactFeedback`'s `33 ms`–`83 ms` hit-stops—causing the exact moment of peak kinetic release to visually freeze.
2. **Axis Continuity Requires `OnRedirected` Intervention**: At `33.3 ms/tile`, a `153 ms` tier stretch lasts **4.6 tiles**! Leaving `PlayerSquashStretch` blind to `OnRedirected` makes cornering look visibly broken.
3. **Mid-Dash Breakthroughs Need Dedicated Micro-Hitstop**: Because `CrackedBlock` no longer halts the player at `KineticRam`, impact feel moved out of `OnWallImpact` into `CrackedBlock.OnAnyBroken`—yet no hit-stop or shake lives there yet.
4. **Speed Compresses Tier Windows to ~167 ms**: At `30 u/s`, the entire ramp from rest to `KineticRam` happens in **10 frames (166.7 ms)**. Feedback cannot rely on slow `0.3s` easing—every tier transition, close-call, and bank-shot needs **frame-0 transients** (flash, zoom kick, audio attack, particle burst) backed by crisp `60–120 ms` unscaled decay tails.
