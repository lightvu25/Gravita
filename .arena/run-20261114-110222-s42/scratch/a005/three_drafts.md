# Three Architectural Drafts for Gravita Game Feel

## Draft 1: "Pure Arcade Kinetic Punch" (Physics & Impact-First)
**Core Thesis**: Maximize raw physical weight and speed sensation through frame-perfect impact physics, hit-stop layering, screen shake, and trail/particle amplification.
- **Pillars**:
  1. Unscaled `PlayerSquashStretch` + centered-pivot offset fix + `OnRedirected` axis snap.
  2. Breakthrough Hit-Stop & `HitFlashSprite` MaterialPropertyBlock flash on `CrackedBlock.OnAnyBroken` and Tier 2/3 wall slams.
  3. High-velocity `Afterimage` Ghost Pool + widened `PlayerLineTrail` (remove `28f` clamp, add impact shockwaves).
  4. Camera recoil (`CinemachineCameraZoom2D` pull-back on Tier 2/3 + directional camera kick).
- **Strengths**: Instantly fixes the "floaty/broken during freeze-frame" bugs; straightforward, super snappy on `KineticRam` smashes.
- **Weaknesses**: Heavy on visual impact, light on **rhythm, audio personality, Close-Call drama, and Gravita's lab identity**. Still feels like a good generic physics block slider rather than an unforgettable signature cyber-lab puzzle game. Neglects `CloseCallDetector`, `HasPendingDirection` input buffering anticipation, and tactile divergence (`BrakeTile` vs `Wall` vs `VectorLauncher`).

---

## Draft 2: "Cyber-Lab Telemetry & Rhythmic Flow" (Audio-Visual Diegetic Puzzle Identity)
**Core Thesis**: Treat Subject G-0 as an unstable high-energy kinetic probe inside an observation chamber; make movement read like accelerating telemetry & harmonic synth feedback.
- **Pillars**:
  1. Musical Bank-Shot Ladder (`OnTileCrossed` rhythmic ticks + `OnRedirected` rising pentatonic synth chain + harmonic `KineticRam` hum).
  2. Diegetic Lab Telemetry (`CloseCallDetector` triggers floating HUD/world `"GRAZE"` gauge & chamber alarm pulse; `HasPendingDirection` displays clean directional aim chevron).
  3. Dynamic Lens & Chamber Overdrive (`Global Volume` ChromaticAberration/Vignette/Bloom tied to `VelocityTier`, `CinemachineCameraZoom2D` subtle breathing).
  4. Distinct Mechanic Micro-Signatures (`BrakeTile` magnetic bass clamp, `BrittleFloor` glass-crack pitch, `FailingTile` countdown telemetry ping).
- **Strengths**: Deeply unique identity! Makes bank-shots and Close Calls extraordinarily readable and "flow-state" addictive (like *Neon White* meets *Celeste* meets *Portal*).
- **Weaknesses**: Without the frame-0 physical hit-stop, centered-pivot squash fix, `HitFlashSprite` punch, and breakthrough freeze on `CrackedBlock`, audio/telemetry alone can feel "UI-heavy" instead of viscerally *crushing* through stone walls at Mach 3.

---

## Draft 3: "Kinetic Containment Reactor" (Cohesive Hybrid: Visceral Impact Physics + Rhythmic Bank-Shot Flow + Lab Telemetry)
**Core Thesis**: Every single slide is a **charge-discharge kinetic cycle**: swipe builds pressure (`Drift` -> `Cruise` -> `KineticRam`), `DeflectionWedge`/`VectorPad` chains amplify a musical & chromatic combo ladder, `CloseCall` triggers razor-edge overdrive, and stops/smashes release that stored energy with unscaled physical recoil—all grounded cleanly inside Gravita's existing `Event` + `Volume` + `Shader` + `Haptics` architecture.
- **5 Tight Pillars**:
  1. **Unscaled Kinetic Body & Breakthrough Punch**: Fix the 4 physical bugs exposed in the worked example (`SetUpdate(true)` on `PlayerSquashStretch`, `(0.5, 0)` pivot-compensation so X/Y squash never anchors to floor edge, `OnRedirected` axis snap, unscaled `PlayerLineTrail` speed >= 30 + `CrackedBlock.OnAnyBroken` dedicated breakthrough hit-stop/shake + `HitFlashSprite.shader` `MaterialPropertyBlock` white/cyan flash).
  2. **Bank-Shot Harmonic Ladder & Spatial Audio/Haptics**: Turn `OnTileCrossed`, `OnVelocityTierChanged`, and chained `OnRedirected` into an escalating rhythmic instrument (quantized rail ticks -> Tier ignition booms -> wedge rebound semitone ladder preserving chain combo until `OnMoveStopped`); fix `SoundManager` multi-source pitch safety and activate the 4 orphaned `HapticEvent`s (`PadRedirect`, `LaunchStart`, `SwitchToggle`, `Death`) + Tier 3 `HeavyImpact`.
  3. **Kinetic Lens Breathing & Post-Process Overdrive**: Upgrade `CinemachineCameraZoom2D` to `Time.unscaledDeltaTime`, drive ortho zoom-out curves across `Drift -> Cruise -> KineticRam` + snap recoil on impact, and pulse a dedicated additive high-priority `Volume` (`ChromaticAberration`, `Bloom`, `Vignette` matching `Global Volume Profile.asset`) so speed physically bends the lab lens.
  4. **Close-Call "Razor Overdrive" (`CloseCallDetector.OnCloseCallTriggered`)**: Wire the completely silent `OnCloseCallTriggered` event into a coordinated micro-bullet-time dip (`0.42x` for `70 ms` via a shared `TimeScaleCoordinator` that never fights Hit-Stop), instant camera zoom-in snap (`-0.30` ortho), cyan/amber Volume flash, cyber graze sting SFX, and pooled floating `"CLOSE CALL"` / streak world-callout + spark burst.
  5. **Anticipation, Afterimages & Tactile Tile Contrast**: Give players confidence at `30 u/s` via a pooled `HasPendingDirection` diegetic turn-chevron (`PlayerTurnPreview`), Tier 3 `PlayerAfterimageEmitter` sprite ghosts (`HitFlashSprite` cyan silhouette fading smoothly on unscaled time), and sharp sensory contrast between `Wall Impact` (explosive sparks + directional recoil) vs `BrakeTile`/`GravityBlock` (silent -> deep magnetic bass anchor + inward clamp ring) vs `BrittleFloor`/`FailingTile`/`VectorLauncher`.

---

## Selection Decision
**Selected: Draft 3 ("Kinetic Containment Reactor").**
**Rationale**:
- Draft 1 fixes raw physics feel but leaves Gravita's unique mechanics (`DeflectionWedge` chains, `CloseCallDetector`, `BrakeTile` anchors, `Phase` shift) muted.
- Draft 2 nails atmosphere and rhythm but risks feeling light if `CrackedBlock` breakthrough hit-stop and unscaled squash aren't solved first.
- Draft 3 fuses both at zero architectural bloat: every pillar maps directly to already-existing events (`PlayerMovement`, `CrackedBlock`, `CloseCallDetector`, `MazeTile`) and underused assets (`CinemachineCameraZoom2D`, `HitFlashSprite.shader`, `Global Volume`, `HapticEvent`), strictly obeys SRP Layer 4 & prefab-only instantiation, and splits cleanly into immediate code Quick Wins vs Content/Shader upgrades.
