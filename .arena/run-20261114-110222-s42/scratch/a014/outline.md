# Contrarian Critique + Skeleton Outline (Working Draft — a014)

## 1. Contrarian Reality Checks (What breaks in generic advice)
- **Myth 1: "Hook up `CinemachineCameraZoom2D` on every velocity tier (`Drift -> Cruise -> KineticRam`)!"**
  - Reality in `GameScene.unity`: `CinemachineCamera` has `CinemachinePixelPerfect` (`m_Enabled: 1`) AND `Main Camera` has `PixelPerfectCamera` (`m_AssetsPPU: 32, m_RefResolutionX: 320, m_RefResolutionY: 180`). `CinemachinePixelPerfect` overwrites `Lens.OrthographicSize` every frame! AND at `moveSpeed = 30 u/s` (`33.3 ms/tile`), a slide lasts `0.06s–0.25s`, so zooming on `Drift/Cruise` produces nauseating yo-yo motion AND `zoomSpeed = 4` with `Time.deltaTime` freezes during Hit-Stop.
  - Trade-off & Surviving Design: Do NOT zoom on routine slides. Either (A) keep `CinemachinePixelPerfect` locked for 32-PPU sharpness and replace ortho zoom with **pixel-safe directional Camera Look-Ahead + Impact Recoil (`CinemachinePositionComposer.TargetOffset`)**, OR (B) reserve `CinemachineCameraZoom2D` (converted to `Time.unscaledDeltaTime`) strictly for **macro climax events** (`CrackedBlock` shatter, `VectorLauncher` launch, `LevelExit` dock) with `CinemachinePixelPerfect` disabled/toggled around those spikes. Choose (A) as default for puzzle clarity + pixel sharpness, and provide the exact `CinemachineCameraZoom2D` fix if the team explicitly disables `CinemachinePixelPerfect` for smooth camera breathing.
- **Myth 2: "Add `0.25x` slow-motion (`Time.timeScale`) on `CloseCallDetector.OnCloseCallTriggered`!"**
  - Reality: `CloseCallDetector.cs` explicitly documents `never modifies Time.timeScale`, and at `30 u/s` with `bufferTurnsUntilStop = true`, players time consecutive swipes by cadence (`100–200 ms`). Time-dilation mid-slide destroys muscle memory and makes chained turns feel stuttered.
  - Surviving Design: **Zero time-scale dilation on Close Calls**. Replace with an **unscaled 33ms micro-arc tether + harmonic graze sting + transient URP Chromatic Aberration / Vignette flash + directional spark**, preserving deterministic `FixedUpdate` cadence 100%.
- **Myth 3: "Pump up `tier1Shake`, `redirectShake`, and longer Hit-Stop everywhere!"**
  - Reality in `GameScene.unity`: `tier1Shake = 0` and `redirectShake = 0` were deliberately zeroed; `PlayerSquashStretch` lacks `.SetUpdate(true)` AND `CinemachineBrain.IgnoreTimeScale = 0`, so current `tier2HitStop (0.033s)` / `tier3HitStop (0.083s)` actually **freeze the squash tween and camera shake**, making hit-stop feel like a frame drop! And `visualTransform` sits at `(0, -0.5, 0)` with bottom-center pivot, distorting horizontal wall squashes.
  - Surviving Design: **Keep `tier1Shake = 0` and `tier1HitStop = 0`** so short hops feel snap-clean; make Hit-Stop *alive* by adding `.SetUpdate(true)` to `PlayerSquashStretch`, enabling `CinemachineBrain.IgnoreTimeScale` (or unscaled directional recoil), and adding a 2-frame (`33ms` unscaled) white silhouette flash + directional spark fan on `KineticRam` / `CrackedBlock` smash.
- **Myth 4: "Put a big speed/tier HUD gauge in the screen corner!"**
  - Reality: On a portrait `320x180` ref resolution (`10-12` tiles wide) at `30 tiles/sec`, eyes never leave Subject G-0.
  - Surviving Design: **Diegetic, player-centered feedback ONLY**: (1) Buffered-turn ghost chevron (`PendingDirection`) + `AwaitingFreshInput` magnetic anchor ring on Subject G-0; (2) Sustained posture/streak deformation during `Cruise`/`KineticRam` instead of immediately snapping back to `baseScale`; (3) Corridor-level tile cadence audio + floor wake ripples.
- **Myth 5: "Drop the same spark prefab on `Brake`, `Exit`, `Wedge`, `Hazard`!"**
  - Reality: `GravityBlock`/`BrakeTile` are inertia sinks; `DeflectionWedge`/`VectorPad` are momentum conduits; `VectorLauncher` is a phase tunnel. Identical bursts blur mechanical readability.
  - Surviving Design: **Opposing mechanical signatures** (centripetal clamp vs diagonal bisector ricochet + musical pinball pitch vs phase coil + sonic boom).

## 2. Check Against Every Deliverable Requirement
- [x] Assumptions section (resolving `VelocityTier.cs` 1-2 / 3-4 / 5+ vs GDD/prompt 1-3 / 4-7 / 8+, `moveSpeed = 30`, `SoundConfig.asset` currently empty clips, `CinemachinePixelPerfect` active).
- [x] **1. Core Game Feel Diagnosis**: Concise, surgical, citing actual codebase bugs/gaps (`PlayerSquashStretch` missing `.SetUpdate(true)`, `CinemachineBrain.IgnoreTimeScale = 0`, `CinemachinePixelPerfect` blocking `CinemachineCameraZoom2D`, `OnTileCrossed` / `OnCloseCallTriggered` 0 subscribers, `Global Volume` static `ChromaticAberration = 0.2`, `EconomyBalanceConfig.EnableLegacyExtraRewardSystems = false` disabling close-call reward).
- [x] **2. 5 High-Impact, Unique Game Feel Pillars** (between 4 and 6 — 5 tight, non-overlapping pillars):
  - **Pillar 1: Tactile Turn-Lock & Anchor Contrast ("Magnetic Gun-Chamber" Input Feel)** — Buffered perpendicular turn preview (`PendingDirection`), instant wall-snap micro-recoil, and `AwaitingFreshInput` magnetic anchor lock on `GravityBlock` / `BrakeTile`.
  - **Pillar 2: Harmonic Runway Cadence & Sustained Kinetic Posture ("The 30-Tile/s Accelerando")** — Subscribing to `OnTileCrossed` + `OnVelocityTierChanged` for a rising musical-interval tick (not random pitch!), sustained speed deformation + sprite afterimages on `KineticRam`, and `PlayerLineTrail` head-surge pulses.
  - **Pillar 3: Alive Hit-Stop & Directional Kinetic Wall Punch ("Zero-Lag Impact Compression")** — Fixing `.SetUpdate(true)` + impact-normal wall-anchored squash (solving the `(0.5, 0)` pivot bug), `CinemachineBrain.IgnoreTimeScale` + unscaled directional recoil, 2-frame white silhouette hit-flash (`MaterialPropertyBlock`), and dynamic `Global Volume` `Bloom`/`ChromaticAberration` transients (removing static `0.2` aberration mud).
  - **Pillar 4: Pinball Bisector Ricochet & Phase-Tunnel Sonic Boom (Wedge / Pad / Launcher Differentiation)** — Diagonal bisector spark fans + tier-indexed musical pitch escalation across chained `OnRedirected` bank-shots + `VectorLauncher` 33ms anticipation/departure/arrival boom (`OnPhasingChanged`).
  - **Pillar 5: Rhythm-Safe "Electro-Graze" Close-Call System (Zero Time-Dilation Risk Reward)** — Subscribing to `CloseCallDetector.OnCloseCallTriggered` with an electric arc tether VFX, rising combo harmonic chime, haptic/camera micro-snap, and `HazardActivation` warning-to-active rhythmic pulse (keeping `Time.timeScale` untouched!).
- [x] Each Pillar MUST include:
  - Why it makes Gravita uniquely addictive (player psychology + rhythmic kinetic feel)
  - Explicit trade-off made & what it gives up
  - Exact numbers, curves, durations, and Inspector parameters
  - Exact C# integration hooks referencing real classes/events (plus clean additive read-only hooks where missing, e.g., `SoundManager.PlaySFX(..., float explicitPitch)`, `PlayerMovement.PendingDirection` / `OnDirectionBuffered`)
  - Unity Inspector / Prefab wiring notes (strictly NO procedural `new GameObject(...)` or runtime `AddComponent`)
- [x] **3. Implementation Priority Roadmap**:
  - Phase A: Quick Wins (pure code/Inspector tweaks executable immediately with existing assets)
  - Phase B: Prefab & Material Wiring (pooled VFX prefabs, `InteractionEffectPlayer` rules, URP `Volume` & `MaterialPropertyBlock` shader properties)
  - Phase C: Audio & Content Upgrades (new SFX clips in `SoundConfig`, harmonic tiles, custom sprites)
- [x] **Constraints check**:
  - SRP & event-driven `Assets/Scripts/Feedback/`
  - Zero `new GameObject(...)` / runtime `AddComponent` (even flag existing tech debt in `PowerUpController.cs:1448-1504` so devs don't imitate it)
  - DOTween discipline: `.DOKill()`, `.SetUpdate(true)`, `.SetLink(gameObject)`
  - Zero alteration to `FixedUpdate` cell-center grid physics or soft-lock rules
  - No mention of arena, strategy card, or competitor number!

## 3. Outline Refinement Check
- Are all 5 pillars distinct, Gravita-specific, and non-generic? YES:
  1. Magnetic Gun-Chamber Input & Anchor Contrast (`BufferTurnsUntilStop` + `AwaitingFreshInput`)
  2. Harmonic Runway Cadence & Sustained Kinetic Posture (`OnTileCrossed` + `VelocityTier` + `PlayerSquashStretch` + afterimages)
  3. Alive Hit-Stop, Pivot-Corrected Compression & Dynamic URP Pulse (`PlayerImpactFeedback` + `PlayerSquashStretch` + `CinemachinePositionComposer` + `Global Volume`)
  4. Bisector Pinball Ricochet & Phase-Tunnel Sonic Boom (`DeflectionWedge` / `VectorPad` / `VectorLauncher` / `OnRedirected` / `OnPhasingChanged`)
  5. Rhythm-Safe Electro-Graze & Hazard Beat Telegraphing (`CloseCallDetector.OnCloseCallTriggered` + `HazardActivation.OnPhaseChanged` + `FailingTile`)
- Wait: what about `CinemachineCameraZoom2D`? Make sure it's directly addressed with exact numbers AND the explicit `CinemachinePixelPerfect` trade-off! Where does it fit best? In Pillar 3 (Impact / Camera Rig) AND Pillar 4 (Phase Launch), clearly explaining Option A (pixel-perfect directional offset, recommended) vs Option B (unscaled `CinemachineCameraZoom2D` punch if `CinemachinePixelPerfect` is disabled).
- Let's also make sure we verify every file path and symbol name we cite against what we actually read in this session!
