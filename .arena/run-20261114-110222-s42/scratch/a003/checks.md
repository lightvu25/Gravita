# Test-First Acceptance Checklist (a003)

Before finalizing `r0/a003.md`, verify every criterion passes:

## A. Task & Deliverable Completeness
1. **Core Game Feel Diagnosis**: Pinpoints specific causes of flatness grounded in real code/scene state (`moveSpeed = 30`, `VelocityTier` mismatch note, missing `.SetUpdate(true)` on `PlayerSquashStretch`, `Time.deltaTime` in `CinemachineCameraZoom2D`, `CinemachineCameraZoom2D` never called, `CloseCallDetector.OnCloseCallTriggered` having 0 subscribers, `tier1Shake = 0` & `redirectShake = 0` in `GameScene.unity`, `HandleBlockBroken` missing shake/hit-stop/zoom, `GameplayHapticsFeedback` missing 4 of 7 `HapticEvent`s, empty `InteractionEffectPlayer` slots on `Brake`/`Exit`/`Hazard`/`Wedge`).
2. **4 to 6 High-Impact Pillars**: Exactly 5 distinct, cohesive Gravita-tailored pillars covering stop-to-stop inertia, 3 velocity tiers, chained wedge/pad/launcher rebounds, buffered turns & floor anchors, close-call timing, and lab/ability/pickup rhythm.
3. **Each Pillar Contains All 4 Sub-Sections**:
   - Why it makes Gravita uniquely addictive (psychology + rhythmic kinetic feel).
   - Exact numbers, curves, durations, and Inspector parameters (ms, world units, ortho delta, pitch curves, colors, cooldowns).
   - Exact C# integration hooks referencing verified repo classes/events/methods (`PlayerMovement`, `PlayerSquashStretch`, `PlayerImpactFeedback`, `PlayerLineTrail`, `CinemachineCameraZoom2D`, `CinemachineCameraShake2D`, `CloseCallDetector`, `EntityAudio`, `SoundManager`, `SoundConfig`, `GameplayHapticsFeedback`, `CrackedBlock`, `GravityBlock`, `PowerUpController`, `LevelFlowController`, `MusicManager`, `Collectible`, `VfxSpawner`).
   - Unity Inspector / Prefab wiring notes with ZERO procedural `new GameObject` or runtime `AddComponent`.
4. **Actionable Code Changes (Speed Strategy)**: Include clean, copy-paste-ready C# snippets/additions for the Quick Wins so the developer can implement immediately today without guessing.
5. **Implementation Priority Roadmap**: Ranked clearly into **Quick Wins (Code-Only / Existing Assets, Today)** vs **Content / Shader / Asset Upgrades (Requires New Art, SFX, or URP Volume Profiles)**.

## B. Hard Technical Constraints Verification
1. **Zero procedural hierarchy creation**: No `new GameObject(...)` or runtime `AddComponent<...>()` anywhere in recommended code. All UI badges, chevrons, AudioSources, and Volumes are `[SerializeField]` Inspector references; transient effects use `VfxSpawner.Spawn(...)` / `ObjectPoolManager`.
2. **DOTween discipline**: Every tween/sequence in snippets explicitly calls `.DOKill()` before creation, `.SetUpdate(true)`, and `.SetLink(gameObject)`.
3. **Deterministic physics & LevelClock preserved**: Zero modification to `FixedUpdate` slide positions, cell-center resolution, `VelocityTier` thresholds, or `Time.timeScale` during `CloseCallDetector` (hit-stop only used briefly on impact/block smash/star, honoring `hitStopEnabled` setting).
4. **Verify every cited symbol/path**: Every file path, class, method, event, and Inspector field in `r0/a003.md` was directly read in this session.
5. **No meta-leakage**: Zero mention of arena, strategy card, dialectical/test-first labels, or competitor id in `r0/a003.md`.
6. **Clean assumptions section**: Note the `VelocityTier.cs` (`1-2 / 3-4 / 5+`) vs prose (`1-3 / 4-7 / 8+`) discrepancy and confirm all hooks rely dynamically on `VelocityTier` enum events.
