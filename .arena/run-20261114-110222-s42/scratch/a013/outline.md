# Outline Check — Gravita Game Feel Upgrade Blueprint

## Requirements Audit
1. **Assumptions & Verified Baseline**:
   - Reconcile tier discrepancy: `VelocityTier.cs` (`1–2` Drift, `3–4` Cruise, `5+` KineticRam) vs prompt summary (`1–3` / `4–7` / `8+`) -> target `VelocityTier` enum & `PlayerMovement.CurrentTier` so logic stays automatic regardless of future threshold tuning.
   - `PlayerMovement.moveSpeed = 30` in `GameScene.unity` (vs `10f` script default) -> 1 tile = `33.3 ms`.
   - Strict SRP, Event-Driven Layer 4 (`Assets/Scripts/Feedback/`, `Assets/Scripts/Audio/`), zero runtime `new GameObject` / `AddComponent`, DOTween `.DOKill()` + `.SetUpdate(true)` + `.SetLink(gameObject)`, deterministic `FixedUpdate` untouched.
2. **Core Game Feel Diagnosis** (5 root causes grounded in repo):
   - Hit-stop freeze deadlocks visuals & camera (`CinemachineBrain.IgnoreTimeScale: 0`, `CinemachineImpulseManager.IgnoreTimeScale == false`, `PlayerSquashStretch` missing `.SetUpdate(true)`, `CinemachineCameraZoom2D` using `Time.deltaTime`).
   - Time-scale mismatch at 30 tiles/sec (`stretchDuration = 0.18s` > typical 2–4 tile slide `0.067–0.133s`; `PlayerLineTrail` clamping `smoothedMoveSpeed` at `28f`).
   - Unidirectional downward camera impulse & misoriented 2D impact burst (`DefaultVelocity = (0, -1, 0)` on `CinemachineImpulseSource`; `Quaternion.LookRotation(normal)` tilts Z instead of Z-angle; `tier1Shake = 0`, `redirectShake = 0` in `GameScene.unity`).
   - Pivot-induced detachment on `PlayerSquashStretch` (`Sprite` child has bottom-center `(0.5, 0)` pivot at `localPosition (0, -0.5, 0)` — scaling shrinks away from ceiling/side walls).
   - Silent high-skill & rhythmic hooks (`OnTileCrossed` unused, `CinemachineCameraZoom2D` unused, `CloseCallDetector.OnCloseCallTriggered` has 0 subscribers, `OnRedirected` flat single-pitch, `Brake`/`Wedge`/`Exit`/`Hazard` prefabs lack configured effects).
3. **5 High-Impact, Gravita-Specific Game Feel Pillars**:
   - **Pillar 1: Kinetic Runway Cadence & Tier-Break Escalation** (Mid-slide anticipation: tile-crossing synth ladder, Cruise/KineticRam threshold surge, dynamic ortho pull-back, trail overcharge).
   - **Pillar 2: Unscaled Hit-Stop, Directional Recoil & Pivot-True Wall Crush** (Fix `IgnoreTimeScale` & `.SetUpdate(true)`, directional impulse along `-normal`, pivot-compensated crush flush to wall face, 2D Z-rotation sparks, tier-scaled double-thump haptics).
   - **Pillar 3: Billiard Bank-Shot Chain Resonance (`DeflectionWedge` & `VectorPad`)** (Combo pitch ladder across preserved-momentum rebounds, diagonal corner-whip squash, escalating bisector camera kick, wedge spark/glow via `InteractionEffectPlayer`).
   - **Pillar 4: "Razor's Edge" Close-Call Adrenaline Spike (`CloseCallDetector`)** (Subscribe to `OnCloseCallTriggered`: `CinemachineCameraZoom2D` tight snap-in, brief `0.35x` unscaled micro-dilation guarded by `hitStopEnabled`, electric snap SFX, pooled world ring + optional HUD callout widget).
   - **Pillar 5: Weight Contrast — Magnetic Anchors, Phase Tunneling & Coin Melody** (`BrakeTile` / `GravityBlock` heavy magnetic sink vs wall rebound; `VectorLauncher` / `OnPhasingChanged` spectral pooled afterimages + FOV breath; `Collectible` ascending coin combo pitch; `BrittleFloor` crack vs shatter).
   - *Each pillar explicitly includes*:
     1. Why it makes Gravita uniquely addictive (psychology + kinetic rhythm)
     2. Exact numbers, curves, durations, pitch curves, ortho deltas, colors, cooldowns, Inspector fields
     3. Exact C# integration hooks (real classes, events, properties + minimal ready-to-apply code changes/extensions)
     4. Unity Inspector / Prefab wiring notes (zero procedural `new GameObject`)
4. **Implementation Priority Roadmap**:
   - **Phase 1: Quick Wins (Shippable Today — Code & Scene Inspector Tweaks Only, Zero New Art/Audio)**
   - **Phase 2: Audio & Pooled Prefab Polish (Low-Medium Art/Audio Effort)**
   - **Phase 3: Shader & URP Volume Upgrades (Signature Visual Identity + Accessibility Guards)**
   - **Verification Checklist & Probabilistically Weighted Risks** (covering Pause isolation, Speedrun Minimal parity, EditMode parity tests, pooling safety).
