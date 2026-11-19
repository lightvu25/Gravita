# Gravita — Kinetic & Tactile Game Feel Upgrade Blueprint

**Target Project:** Puzzle 2D (`Gravita`) — Unity `6000.4.0f1` URP  
**Design Goal:** Transform Gravita's 4-way stop-to-stop inertia, `Drift` / `Cruise` / `KineticRam` escalation, chained wedge rebounds, and hazard near-misses from silent grid transitions into an unmistakable, rhythmic, high-contrast cyber-lab kinetic feel.

---

## 0. Verified Baseline, Timing Math & Assumptions

Before tuning curves or writing hooks, note these verified project numbers and explicit assumptions:

- **Speed-to-Time Reality (`Assets/Scenes/GameScene.unity`):** `PlayerMovement` runs at `moveSpeed: 30` with `cellSize: 1`. Each crossed tile takes **$33.33\text{ ms}$** ($1 / 30\text{ s}$).
- **Velocity Tier Thresholds (`Assets/Scripts/Gameplay/VelocityTier.cs`):** `VelocityTierUtil.FromTiles(int)` maps `<= 0` $\rightarrow$ `None`, `1–2` $\rightarrow$ `Drift`, `3–4` $\rightarrow$ `Cruise` (reached at **$100\text{ ms}$**), `5+` $\rightarrow$ `KineticRam` (reached at **$166.7\text{ ms}$**).
  - *Note on Balance Drift:* If level corridors are later re-tuned toward longer runways (e.g., `1–3` / `4–7` / `8+` tiles, where `Cruise` lands at $133\text{ ms}$ and `KineticRam` at $267\text{ ms}$), every duration and zoom threshold below should read from `VelocityTier` / `VelocityTierUtil` rather than hardcoding tile counts.
- **Unchecked Runtime / Scene Items (Tagged `[Needs Verification]`):**
  - **Pixel-Perfect Camera Quantization `[Needs Verification]`:** `CinemachineCamera` in `GameScene.unity` carries both `CinemachineCameraZoom2D` and `CinemachinePixelPerfect`, while `Main Camera` has `PixelPerfectCamera` (`m_AssetsPPU: 32`, `320x180`, `m_CropFrame: 0`, `m_GridSnapping: 1`, `m_UpscaleRT: 0`). Smooth `Lens.OrthographicSize` breathing (`5.5` $\pm 0.2\text{–}0.42\text{ u}$) may be clamped or quantized by `CinemachinePixelPerfect` until verified in Play Mode.
  - **User Settings Persistence `[Needs Verification]`:** `SettingUI` was not audited in this blueprint; the proposed Shake / Hit-Stop / Flash accessibility multipliers assume clean integration with the project's existing settings persistence layer.
  - **On-Device Haptics `[Needs Verification]`:** `MostHapticsBackend` (`Solo.MOST_IN_ONE`) cooldowns and preset distinction must be validated on physical iOS/Android hardware.

---

## 1. Core Game Feel Diagnosis: Why Gravita Currently Feels "Flat"

Despite having shake, squash/stretch, trails, and post-processing components in `GameScene.unity`, six concrete gaps suppress kinetic payoff:

1. **Zero Audio Reality (`Assets/Data/Sound/SoundConfig.asset`):** Every single one of the `24` `SoundEntry` fields in `SoundConfig.asset` currently has `clips: []` (empty), even though usable sci-fi/impact packs already live under `Assets/Audio/Sounds/` (`Shapeforms Audio Free Sound Effects`, `kenney_sci-fi-sounds`, `sci-fi-sfx`). Furthermore, `SoundManager.PlaySFX(SoundEntry)` lacks an explicit `pitchOverride` parameter, preventing musical pitch climbing across tiles or wedge chains.
2. **Impact Squash Freezes During Hit-Stop & Pulls Away From North Walls (`PlayerSquashStretch.cs`):**
   - `PlayerImpactFeedback` sets `Time.timeScale = 0f` for `33 ms` (`tier2HitStop: 0.033`) on `Cruise` and `83 ms` (`tier3HitStop: 0.083`) on `KineticRam`, while `PlayerSquashStretch.Play()` and `ResetScale()` build DOTween sequences *without* `.SetUpdate(true)`. Visually, the squash freezes motionless during the exact freeze frame meant to sell the impact.
   - `Player`'s visual `Sprite` child sits at `localPosition (0, -0.5, 0)` with bottom-center pivot `(0.5, 0)` (`Player Icon.aseprite.meta`). Scaling height down (`impactHeightPx: 16`, `squashDuration: 0.22`) shrinks *downward* toward the bottom edge—pulling the sprite off North walls instead of pinning flush against the impact face.
   - `PlayerSquashStretch.HandlePhasingChanged` directly overwrites `spriteRenderer.color` (`phaseSpriteTint: {0.72, 0.55, 1, 0.55}`), conflicting with `PlayerCosmeticApplier`.
3. **Colliding `Time.timeScale` Ownership & `Redirect()` Event Double-Fire:**
   - `PlayerImpactFeedback.HitStopRoutine` captures `preHitStopTimeScale` locally. Simultaneous triggers (e.g., `OnWallImpact` overlapping `CrackedBlock.OnAnyBroken` or a skill hit-stop) capture `0f` and trap the game at `Time.timeScale = 0f`.
   - `PlayerMovement.Redirect()` invokes *both* `OnRedirected` and `OnMoveStarted`. Any combo counter or dash-launch feedback listening naively to `OnMoveStarted` resets mid-ricochet instead of only resetting when stationary or stopped (`OnMoveStopped`).
4. **Dormant Framing & Suppressed Close-Call Systems:**
   - `CinemachineCameraZoom2D` (`normalOrthographicSize: 5.5`, `zoomSpeed: 4`) has **zero callers** across `Assets/Scripts/`.
   - `CloseCallDetector.OnCloseCallTriggered` (`Action<Vector3>`) has **zero subscribers** across `Assets/Scripts/`, and `CloseCallDetector`'s `bonusCoins` (`5`) is suppressed because `EconomyBalanceConfig.Active.EnableLegacyExtraRewardSystems` defaults to `false`. Near-misses currently feel accidental rather than heroically narrow.
5. **Mute Mechanic Prefabs & Incomplete Haptics (`InteractionEffectPlayer`, `GameplayHapticsFeedback`):**
   - `Wedge.prefab`, `Brake.prefab`, `Exit.prefab`, and `Hazard.prefab` all have empty `InteractionEffectPlayer` (`effectPrefabs: []`, `defaultCameraShake: 0`, `rules: []`). `Cracked.prefab` and `Coin.prefab` have `defaultCameraShake: 0`.
   - `GameplayHapticsFeedback` does not differentiate `VelocityTier` impacts (`Drift` vs `KineticRam`) and ignores `CrackedBlock`, `CloseCall`, and `LevelCompleted`.
6. **Procedural Object Creation in `PowerUpController.cs`:**
   - `SpawnRadialPulseVFX`, `SpawnDirectionalScanOrBurstVFX` (lines `1448–1503`), and the Echo fallback (`928–952`) call `new GameObject(...)` and `AddComponent<SpriteRenderer>()` at runtime instead of pooling pre-authored prefabs through `VfxSpawner.Spawn`.

---

## 2. Five Cohesive Gravita Game Feel Pillars

All five pillars share a single rhythmic thesis: **Silence/tension during charge, crystalline precision in motion, heavy surgical percussion on contact.**

---

### Pillar 1: Kinetic Runway Ramp & Pre-Turn Buffer Signalling
> *"Momentum you can hear building tile-by-tile, see in peripheral framing, and feel locked into the corner buffer."*

#### Why It Makes Gravita Uniquely Addictive
At `30 u/s` (`33.33 ms/tile`), straight corridors vanish in a fraction of a second. Without progressive escalation *while sliding*, velocity tiers are only experienced retrospectively at wall impact. By pairing per-tile harmonic ticks, subtle ortho-zoom exhale, `KineticRam` ghost silhouettes, and a visible directional chevron for `HasPendingDirection`, players feel velocity *and* gain confidence to buffer turns mid-flight (`bufferTurnsUntilStop: 1`, `turnClearance: 0.35`).

#### Exact Numbers, Curves & Inspector Parameters
| Parameter | Target Component / Config | Value / Curve | Purpose |
| :--- | :--- | :--- | :--- |
| `tileTickPitchCurve` | `GameFeelConfig` | `(1→0.92, 3→1.05, 5→1.22, 10+→1.45)` Linear/EaseOut | Audible acceleration cadence across `OnTileCrossed` |
| `tileTickMinInterval` | `KineticRunwayFeedback` | `0.032 s` (`32 ms`), vol `0.26 → 0.48` | Prevents ear fatigue while catching every crossed cell at `30 u/s` |
| `cruiseTierStinger` | `SoundConfig` / `EntityAudio` | Pitch `1.0`, Vol `0.55`, 1 crisp harmonic blip | Distinct audio landmark at tile 3 (`100 ms`) |
| `kineticRamIgniteSfx` | `SoundConfig` / `EntityAudio` | Pitch `1.08`, Vol `0.72` + sub-punch `0.10` shake | Marks tile 5 (`166.7 ms`) armor-piercing threshold |
| `tierOrthoOffsets` | `CinemachineCameraZoom2D` | `None/Drift: +0.00 u`, `Cruise: +0.20 u` (`5.70`), `KineticRam: +0.42 u` (`5.92`) | Widens corridor foresight on long high-speed slides |
| `zoomLerpSpeed` | `CinemachineCameraZoom2D` | Outbound `7.5`, Stop Recovery `10.0` (unscaled) | Responsive framing without seasick drift |
| `stopInhaleDip` | `CinemachineCameraZoom2D` | `-0.16 u` (`5.34`) for `65 ms` on `OnMoveStopped` (`Cruise+`) | Kinetic "exhale-to-rest" lock on stop |
| `afterimageInterval` | `KineticAfterimageEmitter` | `0.030 s` (~`0.9 u` spacing) active *only* in `KineticRam` | Leaves crisp neon speed silhouettes without cluttering `Drift` |
| `afterimageColor` & Fade | `SpriteGhostEcho` prefab | `#FF52C7` (`1, 0.32, 0.78`), alpha `0.48 → 0.0` over `0.15 s` (`OutQuad`) | Matches `PlayerLineTrail.kineticRamHeadColor` |
| `turnBufferChevron` | `TurnBufferIndicator` | `#59F2FF` (`0.35, 0.95, 1, 0.95`), scale `0.75 → 1.05` over `0.08 s` (`OutBack`), fade `0.07 s` | Instant confirmation that a perpendicular turn is queued |

#### C# Integration Hooks
- **Minimal Read-Only Exposure (`Assets/Scripts/Gameplay/PlayerMovement.cs`):**
  Expose `public Vector2 PendingDirection => pendingDirection;` right below `public bool HasPendingDirection => hasPendingDirection;` (`FixedUpdate` physics untouched).
- **Runway Audio, Afterimages & Framing (`Assets/Scripts/Feedback/KineticRunwayFeedback.cs`):**
  - Subscribe to `PlayerMovement.OnMoveStarted`, `OnTileCrossed(int tilesTraveled)`, `OnVelocityTierChanged(VelocityTier tier)`, and `OnMoveStopped`.
  - Distinguish fresh launch from corner redirect using a `bool wasMoving` guard: reset `lastTickTime = -1f` only when `!wasMoving` on `OnMoveStarted`, set `wasMoving = false` in `OnMoveStopped`.
  - On `OnVelocityTierChanged(VelocityTier.Cruise / KineticRam)`: push additive offset to `CinemachineCameraZoom2D.SetVelocityOffset(float)`, fire tier stinger via `SoundManager.Instance.PlaySFX`, and toggle the pooled afterimage timer (`VfxSpawner.Spawn(afterimagePrefab, ...)`).
  - *Crucial Trail Cohesive Match:* Keep `PlayerLineTrail` colors (`headColor`, `cruiseHeadColor`, `kineticRamHeadColor`) as the master palette source so trail, afterimage, and chevron share exact hues.
- **Queued Turn Signifier (`Assets/Scripts/Feedback/TurnBufferIndicator.cs`):**
  - Reads `playerMovement.IsMoving && playerMovement.HasPendingDirection` cleanly in `LateUpdate` (avoiding per-frame allocations); when active state or `PendingDirection` changes, rotates pre-wired arrow transform (`Atan2`) and drives DOTween:
    ```csharp
    chevronTransform.DOKill();
    chevronTransform.localScale = Vector3.one * 0.75f;
    chevronTransform.DOScale(1.05f, 0.08f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(gameObject);
    ```

#### Unity Inspector & Prefab Wiring
- Create `SpriteGhostEcho.prefab` (single `SpriteRenderer` + pooled auto-fade helper, `Order in Layer` `Player - 1`) and register in `ObjectPoolManager` / assign to `KineticRunwayFeedback.afterimagePrefab`.
- Add `TurnBufferChevron` child under `Player` (pre-authored sprite, inactive by default, assigned via `[SerializeField]` on `TurnBufferIndicator`—never created via `new GameObject`).

---

### Pillar 2: Stop-to-Stop Impact Choreography & Surface Weight Differentiation
> *"Hard walls arrest you, `BrakeTile` absorbs you, `GravityBlock` clamps you, and `CrackedBlock` shatters underneath your unbroken speed."*

#### Why It Makes Gravita Uniquely Addictive
Gravita's core loop is **launch $\rightarrow$ collide $\rightarrow$ plan**. Right now, almost every stop shares the same `PlayerImpactFeedback` flow, and frozen DOTween animations + upward pivot offsets soften hits. Making *each mechanical tile type* feel physically opposite transforms collision from dead time into tactile punctuation.

#### Exact Numbers, Curves & Inspector Parameters
| Surface / Event | Hit-Stop (`unscaled`) | Camera Shake | Squash / Sprite Choreography (`SetUpdate(true)`) | Audio & Haptics |
| :--- | :--- | :--- | :--- | :--- |
| **Solid Wall (`Drift`)** | `0 ms` | `0.025` | Scale `(1.12, 0.88)` relative to impact axis, `0.11 s OutQuad` | Crisp low tap (`pitch 0.90`); `HapticEvent.WallImpact` |
| **Solid Wall (`Cruise`)** | `33 ms` (`0.033`) | `0.10` | Scale `(1.26, 0.72)` + `18 ms` `_FlashColor` `#FFFFFF` flash, `0.15 s OutBack` | Punchy metallic thud (`pitch 1.0`); `WallImpact` medium |
| **Solid Wall (`KineticRam`)** | `75 ms` (`0.075`) | `0.22` | Max clamp `±0.30` scale + `45 ms` `_FlashColor` `#FF52C7`→`#FFFFFF` flash + `-0.16 u` ortho slam pulse (`70 ms`) | Heavy sub-bass boom + metallic ring (`pitch 0.85`); `RigidImpact` |
| **`BrakeTile` Capture** | `0 ms` | `0.015` | Elongated dampened deceleration squash (`0.86` along travel, `0.16 s OutCubic`, zero rebound bounce) | Downward magnetic scrub `pitch 1.15 → 0.65` (`0.12 s`); `GravityAnchor` |
| **`GravityBlock` Anchor** | `18 ms` (`0.018`) | `0.05` (`0.08` at Ram) | Rapid symmetric inward clamp `scale 0.84 → 1.0` over `0.10 s InOutQuad` + cyan lock ring prefab | Precision magnetic latch click; `GravityAnchor` |
| **`CrackedBlock` Breakthrough** | `45 ms` (`0.045`) | `0.18` | **No stop!** `1.22x` forward re-acceleration stretch (`0.09 s OutQuad`) + `#FF52C7`/`#FFE066` shard burst | High-energy ceramic/crystal shatter (`pitch 1.08`, vol `0.85`); `CrackedBreak` haptic |

#### C# Integration Hooks
- **Pivot-Compensated Unscaled Squash (`Assets/Scripts/Feedback/PlayerSquashStretch.cs`):**
  - Add `.SetUpdate(true)` and `.SetLink(gameObject)` after `.DOKill()` on *both* `Play()` and `ResetScale()` sequences so the squash curve immediately slams into peak compression *during* hit-stop rather than freezing at 0%.
  - **Fix North/South Pivot Drift:** Because `Sprite` has pivot `(0.5, 0)` and `localPosition (0, -0.5, 0)`, calculate a surface-pin `localPosition` offset proportional to `1f - scaleY` when `impactDir.y > 0.5f` (shifting upward by `+0.5f * (1f - targetScaleY)` toward the North ceiling) and zero shift when `impactDir.y < -0.5f` (already anchored at bottom). Keep `impactDuration` tight (`0.13–0.16 s`, down from `0.22 s`) so `moveSpeed = 30` fast chains never fight lingering tweens.
  - **Hit-Flash Shader Drive:** Assign a dedicated `Material` (using `Echoes/VFX/Solid White Sprite`, shader property `_FlashColor` and `_FlashAmount` if added—otherwise swap pre-assigned cached `hitFlashMaterial` onto `SpriteRenderer` for `35–45 ms` unscaled time, then restore `defaultMaterial`).
- **Unified Time Coordinator (`Assets/Scripts/Feedback/HitStopCoordinator.cs`):**
  - Replaces raw `Time.timeScale` manipulation in `PlayerImpactFeedback` (and avoids colliding with `CutsceneManager.OnDisable`).
  - Exposes `RequestHitStop(float unscaledDuration)` and `RequestTimeDilation(float targetScale, float unscaledDuration)`. Guards strictly against `PauseMenuUI.IsPaused` / `LevelFlowController.State != LevelState.Playing`; uses `Time.unscaledTime` deadline tracking (`hitStopUntil = Mathf.Max(hitStopUntil, Time.unscaledTime + duration)`), guaranteeing `Time.timeScale` cleanly restores to `1f` (`0` overlap deadlock).
- **Surface Discrimination (`PlayerImpactFeedback.cs` & `GameplayHapticsFeedback.cs`):**
  - Distinguish `CrackedBlock.OnAnyBroken` (triggering breakthrough micro-stretch + shard VFX *without* halting `PlayerMovement`), `GravityBlock.OnPlayerAnchored`, and `BrakeTile` via `MazeTile.OnAnyPlayerInteracted`.
  - Add `HapticEvent.CrackedBreak` and tier-aware `WallImpact` intensity mapping (`VelocityTier.Drift` $\rightarrow$ `LightImpact`, `Cruise` $\rightarrow$ `MediumImpact`, `KineticRam` $\rightarrow$ `RigidImpact`) in `MostHapticsBackend` / `GameplayHapticsFeedback` (keep `GlobalMinGapSeconds = 0.03f` and per-event cooldowns intact).

#### Unity Inspector & Prefab Wiring
- Populate empty `InteractionEffectPlayer.effectPrefabs` on `Brake.prefab` (cyan magnetic ground arcs, shake `0.015`), `Cracked.prefab` (increase shake `0` $\rightarrow$ `0.18`), and `GravityBlock.prefab` (tune shake `0.04` $\rightarrow$ `0.06` + clamp ring prefab).
- Assign `Hit Flash White.mat` (already in `Assets/`) via `[SerializeField]` reference on `PlayerImpactFeedback` / `PlayerHitFlash`—zero runtime shader lookup or material instantiation spam if toggling shared/instance material cleanly.

---

### Pillar 3: Pinball Rebound & Bank-Shot Chain Engine
> *"Chain `DeflectionWedge` and `VectorPad` ricochets into a rising cyber-synth arpeggio with snapping corner sparks."*

#### Why It Makes Gravita Uniquely Addictive
Linked `DeflectionWedge` and `VectorPad` corridors are Gravita's signature skill expression—one directional swipe sets off 2–8 automated bank shots (`maxConsecutiveRedirects: 8`). Currently `Wedge.prefab` has zero effects, `redirectShake` is `0`, and `Redirect()` fires `OnMoveStarted`, wiping any naive dash state. Turning consecutive rebounds into an escalating rhythmic instrument makes chained bank shots euphoric.

#### Exact Numbers, Curves & Inspector Parameters
| Chain / Surface Parameter | Value / Formula | Notes |
| :--- | :--- | :--- |
| **Pentatonic Pitch Ladder (`chainCount 1..8`)** | Semitones `[0, 2, 4, 7, 9, 12, 14, 16]` $\rightarrow$ `Mathf.Pow(2f, s / 12f)` (`1.00, 1.122, 1.260, 1.498, 1.682, 2.000, 2.245, 2.520`) | Fits cleanly inside `maxConsecutiveRedirects: 8`; musical across any chain length |
| **Redirect Micro-Shake** | `0.05 + 0.015f * Mathf.Min(chainCount, 6)` (caps at `0.14`) | Subtle on 1st bank, crunchy crescendo by 6th bank |
| **Corner-Whip Squash** | `scale` perpendicular `0.84`, forward `1.18` over `0.08 s OutQuad` | Keeps crisp edge without blurring alignment |
| **Wedge / Pad Physical Recoil** | Child visual `DOPunchScale(Vector3.one * 0.16f, 0.12f, 1, 0.4f)` | Reacts physically to the 30 u/s bounce |
| **Corner Bisector Spark Angle** | Spawn rotated to `(-incomingDir + outgoingDir).normalized` | Visualizes exact deflection physics vector |
| **Trail Corner Bloom Pop** | `widthMultiplier` pulse `1.0 → 1.35` over `0.09 s` on `OnRedirected` | Highlights ricochet vertices in `PlayerLineTrail` |
| **Blunt-Side Wedge Thud** | `pitch 0.72` muffled clack + sprite `DOShakePosition(0.09f, 0.05f, 20)` | Teaches angled deflection vs flat-back collision instantly |

#### C# Integration Hooks
- **SoundManager Pitch-Override Overload (`Assets/Scripts/Audio/SoundManager.cs`):**
  Add non-breaking overload alongside existing `PlaySFX(SoundEntry)`:
  ```csharp
  public void PlaySFX(SoundEntry entry, float pitchOverride, float volumeMultiplier = 1f)
  ```
  Bypass random pitch variance when `pitchOverride > 0f` so pentatonic chains play exact musical intervals.
- **Chain Tracker (`Assets/Scripts/Feedback/RedirectChainFeedback.cs`):**
  - Tracks `int currentChain` and `Vector2 previousDirection`.
  - **Critical Event Rule:** Increment `currentChain` inside `PlayerMovement.OnRedirected(Vector2 newDir)` and `MazeTile.OnAnyPlayerInteracted` (for `VectorPad`), and reset `currentChain = 0` **only** inside `PlayerMovement.OnMoveStopped`, `LevelFlowController.OnLevelFailed`, and `OnLevelRestarted`—**never** inside `OnMoveStarted` (because `PlayerMovement.Redirect()` invokes `OnMoveStarted` right after `OnRedirected`).
  - Pass `previousDirection` + `newDir` to compute bisector rotation for `VfxSpawner.Spawn(cornerSparkPrefab, ...)` and drive `SoundManager.Instance.PlaySFX(redirectChimeEntry, pitch)` + `CinemachineCameraShake2D.Instance.ShakeCamera(shake)`.
- **Wedge / Pad Recoil (`DeflectionWedge` & `VectorPad` visual child):**
  Hook `InteractionEffectPlayer` or a lightweight `TileImpactRecoil` listening to `IPlayerInteractable.OnPlayerInteracted` on `Wedge.prefab` / `VectorPad.prefab`, animating only the `[SerializeField] Transform visualRoot` (never the tile collider root!) with `.DOKill().SetUpdate(true).SetLink(gameObject)`.

#### Unity Inspector & Prefab Wiring
- Wire `Wedge.prefab`'s `InteractionEffectPlayer`: assign `RedirectCornerSpark.prefab` and set `defaultCameraShake: 0.05` (or defer shake exclusively to `RedirectChainFeedback` to avoid double-shaking—pick **one** owner).
- Set `PlayerImpactFeedback.redirectShake` to `0` (already `0` in `GameScene.unity`) if `RedirectChainFeedback` owns escalating redirect shake, preventing duplicate impulse stacking.

---

### Pillar 4: Laser Razor Close-Call Adrenaline & Hazard Telegraphing
> *"Turn lethal timing puzzles into breathless survival highlights."*

#### Why It Makes Gravita Uniquely Addictive
`CloseCallDetector` already computes precise `0.15 s` hazard timing windows (`closeCallWindowSeconds: 0.15`) and `1.08 u` drone sweeps (`enemyNearMissRadius: 1.08`) with a `1.5 s` per-hazard cooldown—yet nothing happens visually or aurally when it triggers because `OnCloseCallTriggered` has zero subscribers and `bonusCoins` (`5`) is gated behind `EnableLegacyExtraRewardSystems == false`. Making every razor-thin escape detonate a sleek sensory sting creates unforgettable "I shouldn't have survived that!" moments.

#### Exact Numbers, Curves & Inspector Parameters
| Event / Effect | Parameter | Exact Values |
| :--- | :--- | :--- |
| **Close-Call Micro Time-Dilate** | `HitStopCoordinator` | Scale `0.38x` for `50 ms` unscaled, then snap to `1.0x` (skipped if hit-stop `0x` is active) |
| **Global Volume Pulse** (`Global Volume Profile.asset` baseline: Bloom `9`, Vignette `0.4`, Chromatic `0.2`, PostExposure `-0.4`) | `VolumePulseController` | `ChromaticAberration`: `0.20 → 0.58` in `30 ms`, recover in `170 ms`; `ColorAdjustments.postExposure`: `-0.40 → +0.10` in `30 ms`, recover in `150 ms`; `Vignette`: `0.40 → 0.48` |
| **Camera Punch** | `CinemachineCameraZoom2D` & `Shake` | Ortho offset `-0.14 u` for `85 ms` + `cameraPulseForce: 0.06` (existing field in `CloseCallDetector`) |
| **Audio Duck & Razor Sting** | `MusicManager` + `SoundManager` | `closeCallZip` SFX (`vol 0.80`, pitch `0.97–1.05`) + `MusicManager.DuckMusic(true)` for `180 ms` |
| **Pooled World Callout** | `CloseCallCallout.prefab` (`VfxSpawner`) | Expanding cyan-white ring (`scale 0.4 → 1.4`, `0.28 s`) + small floating `CLOSE!` / `RAZOR` TMP label rising `+0.40 u` over `0.42 s`, `.SetUpdate(true)` |
| **Hazard Pre-Fire Telegraph** (`PulseLaser`, `OverloadedFloor`, `FailingTile`) | `HazardTelegraphFeedback` | In warning phase before fire/collapse: visual child `DOShakePosition` `±0.025 u` (freq `28`), subtle warning hum `vol 0.22`, tint ramp to `#FF9E3D` $\rightarrow$ `#FF3B5C` over warning duration |

#### C# Integration Hooks
- **Close-Call Sensory Listener (`Assets/Scripts/Feedback/CloseCallFeedback.cs`):**
  - Subscribe to `CloseCallDetector.OnCloseCallTriggered(Vector3 hazardWorldPos)` **unconditionally** (never gate visual/audio juice behind `EconomyBalanceConfig.Active.EnableLegacyExtraRewardSystems`).
  - Trigger `HitStopCoordinator.Instance.RequestTimeDilation(0.38f, 0.05f)`, `VolumePulseController.Instance.PulseCloseCall()`, `CinemachineCameraZoom2D.Instance.PulseOrtho(-0.14f, 0.085f)`, `SoundManager.Instance.PlaySFX(closeCallZip)`, `GameHaptics.Play(HapticEvent.CloseCall)`, and `VfxSpawner.Spawn(closeCallCalloutPrefab, hazardWorldPos, ...)`.
- **Pooled Volume Pulse Driver (`Assets/Scripts/Feedback/VolumePulseController.cs`):**
  - Caches `ChromaticAberration`, `ColorAdjustments`, and `Vignette` component references from `Global Volume`'s runtime `profile` on `Awake` (restoring exact asset baseline values `0.20f`, `-0.40f`, `0.40f` in `OnDisable` so Scene/Asset values never pollute between scenes or editor stops).
  - Drives transient spikes using `DOTween.To(...)` with `.DOKill()`, `.SetUpdate(true)`, `.SetLink(gameObject)`.
- **Hazard Pre-Fire Warning (`Assets/Scripts/Feedback/HazardTelegraphFeedback.cs`):**
  - Subscribe directly to `HazardActivation.OnPhaseChanged` (for `PulseLaser` / `OverloadedFloor`) and `FailingTile.OnStateChanged` (for cracking floor tiles). Run shake/glow strictly on `[SerializeField] Transform visualChild` / `SpriteRenderer`, stopping cleanly in `OnDisable`.
  - Optionally listen to `BreachSequencer.OnBreachFired` to trigger a subtle perimeter warning vignette pulse when timed breach waves escalate.

#### Unity Inspector & Prefab Wiring
- Author `CloseCallCallout.prefab` (`SpriteRenderer` shock ring + `TextMeshPro` world text + pooled lifetime return) and register it in `ObjectPoolManager` / assign to `CloseCallFeedback`.
- Wire `Hazard.prefab` empty `InteractionEffectPlayer` with lethal electric/laser hit VFX so deaths carry the same visual weight as `PulseLaser.prefab` (`0.12` shake) and `SentinelDrone.prefab` (`0.14` shake).

---

### Pillar 5: Tactile Power-Ups, Phase-Launch Warp & Harmonic Corridor Flow
> *"Unify `VectorLauncher`, Campaign Skills (`Breaker`, `Freeze`, `Teleport`), `Coin`/`Star` pickups, `CircuitSwitch`/`EnergyGate`, and `LevelExit` into one cohesive cyber-lab language."*

#### Why It Makes Gravita Uniquely Addictive
Puzzle-action flow breaks when pickups, switches, skills, and exits feel bolted on—especially when `PowerUpController` currently spawns bare `new GameObject` circles/rects for `Breaker` and `Teleport` while `Exit.prefab` and `Coin.prefab` lack tactile punctuation. Giving every interaction the same pooled, rhythmic feedback closes the loop from first swipe to goal slide.

#### Exact Numbers, Curves & Inspector Parameters
| Feature | Target Event / Class | Exact Spec & Timing |
| :--- | :--- | :--- |
| **`Coin` Streak Chime** | `Collectible.OnAnyCoinCollected` | Rolling streak window `1.5 s` (unscaled): semitones `[0, 2, 4, 7, 9, 12]` (`pitch 1.0 → 2.0`), vol `0.45`, pooled spark pop (`scale 0.5 → 1.2`, `0.15 s`) + HUD counter `DOPunchScale(0.15, 0.14 s)` |
| **`Star` Milestone Celebration** | `Collectible.OnAnyStarCollected` | `20 ms` crisp freeze + `0.06` shake (matches `Star.prefab`) + bright two-note fifth chord + `Exposure` micro-bump `-0.40 → -0.05` (`0.14 s`) + HUD Star slot bounce (`SetUpdate(true)`) |
| **`VectorLauncher` & Phasing Warp** | `PlayerMovement.OnPhasingChanged(bool)` + `VectorLauncher` | Pre-launch clamp `0.06 s` $\rightarrow$ `IsPhasing == true`: trail switches to `phaseHeadColor` (`0.72, 0.42, 1, 0.95`), vignette `0.40 → 0.50`, needle stretch `(0.74, 1.28)` + subtle phase shimmer; on exit (`false`): solid `DOPunchScale(0.18, 0.12 s)` + `0.08` re-entry thump |
| **`Breaker` Pooled Shockwave** | `PowerUpController` (`Breaker`) | Replace procedural `new GameObject` lines `1448–1503` with `VfxSpawner.Spawn(breakerWavePrefab, ...)` + `45 ms` hit-stop + `0.18` shake + sub-bass EMP boom |
| **`Freeze` Time-Lock Tint & Expiry Warning** | `PowerUpController` (`Freeze`) | Activation: downward pitch-sweep SFX (`1.25 → 0.45` over `0.28 s`), subtle cool-blue `ColorAdjustments.colorFilter` shift; final `1.5 s` before thaw: `3` rhythmic `0.5 s` ticking pulses (`pitch 1.1 → 1.3`) + HUD slot pulse |
| **`Teleport` Dissolve/Materialize** | `PowerUpController` (`Teleport`) | `75 ms` warp-out ring at source + warp-in snap at destination via `VfxSpawner.Spawn`, `-0.20 u` ortho snap (`0.09 s`), `Sci Fi Warp Speed` SFX |
| **`CircuitSwitch` $\rightarrow$ `EnergyGate` Feedback** | `CircuitSwitch.OnSwitchStateChanged` / `EnergyGate.OnGateOpenStateChanged` | Switch: crisp mechanical relay click + `0.04` shake; Gate unlock: audible pitch-up harmonic unlock chord + pooled gate-beam dissipate burst so off-screen doors communicate clearly |
| **`LevelExit` Goal Pull** | `LevelFlowController.OnLevelCompleted` / `CutsceneManager.GoalRoutine` (`0.5 s`) | Smooth ortho zoom-in `5.5 → 4.75` over `0.45 s` (`OutCubic`), ascending victory arpeggio, `Exit.prefab` core implosion/bloom burst (`VfxSpawner.Spawn`), `HapticEvent.LevelWin` |

#### C# Integration Hooks
- **Refactor `PowerUpController.cs` VFX Spawning:** Delete procedural `new GameObject` + `AddComponent<SpriteRenderer>()` blocks (`SpawnRadialPulseVFX`, `SpawnDirectionalScanOrBurstVFX` at lines `1448–1503` and Echo fallback `928–952`). Replace with `[SerializeField] private GameObject breakerRadialVfxPrefab;`, `breakerScanVfxPrefab`, `teleportOriginVfxPrefab`, `teleportDestVfxPrefab`, spawned strictly via `VfxSpawner.Spawn(...)` with null-safe early returns.
- **Collectible & Exit Harmony (`Assets/Scripts/Feedback/CollectibleFeedback.cs` & `LevelOutcomeFeedback.cs`):**
  - `CollectibleFeedback` subscribes to `Collectible.OnAnyCoinCollected` and `Collectible.OnAnyStarCollected`, resetting `coinStreak` in `LevelFlowController.OnLevelStarted` / `OnLevelRestarted`.
  - `LevelOutcomeFeedback` subscribes to `LevelFlowController.OnLevelCompleted` (triggering `CinemachineCameraZoom2D.SetCutsceneOffset(-0.75f)` and victory audio/haptics) and `OnLevelFailed` / `OnPlayerRevived` (resetting zoom offset and volume overrides cleanly).

#### Unity Inspector & Prefab Wiring
- Author and pool `BreakerWaveVFX.prefab`, `BreakerScanVFX.prefab`, `TeleportOutVFX.prefab`, `TeleportInVFX.prefab`, and `ExitGoalVFX.prefab`.
- Wire `Exit.prefab` and `Coin.prefab` `InteractionEffectPlayer` fields, plus `[SerializeField]` prefab slots on `PowerUpController`.

---

## 3. Built-to-Last Architecture & Refactor Specs

To keep `Puzzle 2D` clean for 6+ months without turning `PlayerMovement` or `PlayerImpactFeedback` into spaghetti monoliths, enforce these five structural upgrades alongside the pillars:

1. **Central `GameFeelConfig` (`ScriptableObject`):**
   - Path: `Assets/Scripts/Data/Feedback/GameFeelConfig.cs` (`[CreateAssetMenu]`).
   - Holds `AnimationCurve tilePitchCurve`, `float[] redirectPentatonicSemitones`, tier ortho zoom deltas, hit-stop durations, close-call post-FX targets, and tier accent colors, all validated inside `OnValidate()` (clamping non-negative durations, non-empty curves, safe zoom bounds `[-1.5f, +1.5f]`).
2. **Additive `CinemachineCameraZoom2D` Offset Channels (`Assets/Scripts/Feedback/CinemachineCameraZoom2D.cs`):**
   - Never let gameplay systems fight over `targetOrthographicSize`. Split into three additive float offsets: `velocityOffset`, `impulsePunchOffset` (auto-decays via unscaled timer), and `cutsceneOffset`:
     ```csharp
     float desiredSize = Mathf.Clamp(
         normalOrthographicSize + velocityOffset + impulsePunchOffset + cutsceneOffset,
         minOrthographicSize, maxOrthographicSize);
     ```
   - Step interpolation with `Time.unscaledDeltaTime` so zoom punches work smoothly even during `0.075 s` hit-stop or `0.38x` close-call slow-mo.
   - **Critical Pixel-Perfect Audit Check:** Verify `CinemachinePixelPerfect` on `CinemachineCamera` in Play Mode; if `CinemachinePixelPerfect` overrides `Lens.OrthographicSize`, either disable `CinemachinePixelPerfect` during zoom transitions or drive a subtle camera trauma/crop-safe zoom range aligned with `32 PPU`.
3. **Coordinated `PlayerCosmeticApplier` + `PlayerSquashStretch` Color Ownership:**
   - Do not let `PlayerSquashStretch.HandlePhasingChanged` write raw white/tint into `spriteRenderer.color` directly. Maintain `baseSkinTint` via `PlayerCosmeticApplier` (or multiply phase tint onto the skin's restored tint) and restore exact skin tint on `OnPhasingChanged(false)` and `OnDisable()`.
4. **DOTween & Cleanup Discipline Everywhere:**
   - Every new or updated feedback tween (`PlayerSquashStretch`, `TurnBufferIndicator`, `VolumePulseController`, `TileImpactRecoil`, `CloseCallCallout`, HUD counters) **must** follow the project standard already used in `PowerUpSlotWidget`:
     ```csharp
     target.DOKill();
     target.DOScale(...).SetEase(...).SetUpdate(true).SetLink(gameObject);
     ```
   - All static C# event subscriptions (`OnMoveStopped`, `OnAnyBroken`, `OnCloseCallTriggered`, `OnAnyCoinCollected`, `OnPhaseChanged`, `OnLevelCompleted`) unsubscribe in `OnDisable()` / `OnDestroy()`.
5. **Accessibility / Comfort Toggles (Zero Extra Friction):**
   - Expose static/config multipliers on `GameFeelConfig` or `HitStopCoordinator` / `CinemachineCameraShake2D` / `VolumePulseController`:
     - `ScreenShakeScale` (`0f` / `0.5f` / `1f`)
     - `HitStopEnabled` (`bool`, already serialized on `PlayerImpactFeedback`)
     - `ScreenFlashIntensity` (`0f`..`1f`, gating `_FlashColor` / `postExposure` spikes)

---

## 4. Implementation Priority Roadmap

### Phase 1: Quick Wins (Code + Existing Audio Packs & Inspector Only — Immediate Transformation)
1. **Wire `SoundConfig.asset` Using Existing `Assets/Audio/Sounds/` Packs:** Populate the 24 empty `clips: []` arrays from `Shapeforms Audio Free Sound Effects`, `kenney_sci-fi-sounds`, and `sci-fi-sfx`, and add `SoundManager.PlaySFX(SoundEntry, float pitchOverride, float volumeMultiplier = 1f)`. *(Single biggest perceptual jump in the project.)*
2. **Fix `PlayerSquashStretch.cs` Unscaled DOTween + North/South Pivot Compensation + Cosmetic Tint:** Add `.DOKill()`, `.SetUpdate(true)`, `.SetLink(gameObject)` and surface-pin offset.
3. **Introduce `HitStopCoordinator.cs`:** Eliminate `Time.timeScale = 0f` race conditions and enable safe `0x` wall impacts vs `0.38x` close-call slow-mo.
4. **Activate `CinemachineCameraZoom2D` Offset Channels + Verify `CinemachinePixelPerfect`:** Wire `KineticRunwayFeedback` (`Cruise +0.20`, `KineticRam +0.42`, Stop `-0.16`) and `LevelOutcomeFeedback` (`Goal -0.75`).
5. **Wire `CloseCallDetector.OnCloseCallTriggered` Unconditionally:** Connect `CloseCallFeedback` $\rightarrow$ `VolumePulseController` (`ChromaticAberration` / `ColorAdjustments`) + `MusicManager.DuckMusic(true)` + micro time-dilation + camera punch.
6. **Build `RedirectChainFeedback`:** Track chains on `OnRedirected`, reset exclusively on `OnMoveStopped` / level reset, drive pentatonic `SoundManager.PlaySFX(..., pitchOverride)` + escalating shake.
7. **Upgrade `GameplayHapticsFeedback` & `HapticEvent`:** Add `CrackedBreak`, `CloseCall`, `LevelWin`, and `VelocityTier`-scaled `WallImpact` intensity.

### Phase 2: Content, Prefab & Shader Upgrades (Authored Sprites, Custom VFX Prefabs & Shaders)
1. **Replace `PowerUpController` Procedural `new GameObject` VFX (`Lines 928–952, 1448–1503`):** Author pooled prefabs (`BreakerWaveVFX`, `BreakerScanVFX`, `TeleportOutVFX`, `TeleportInVFX`) and spawn strictly via `VfxSpawner.Spawn`.
2. **Author `InteractionEffectPlayer` Prefabs for Empty Tiles:** Populate `Wedge.prefab` (directional corner spark), `Brake.prefab` (cyan scrub arcs), `Exit.prefab` (goal bloom pull), `Hazard.prefab` (electric kill spark), and tune `Cracked.prefab` / `Coin.prefab`.
3. **Add `KineticRam` `SpriteGhostEcho.prefab` & `TurnBufferChevron` Child Sprite:** Expose `PlayerMovement.PendingDirection` and wire pooled afterimages + corner buffer HUD chevron.
4. **Author `CloseCallCallout.prefab` & `HazardTelegraphFeedback`:** World-space shock ring + `CLOSE!` TMP popup and pre-fire tremor/glow for `PulseLaser`, `OverloadedFloor`, and `FailingTile`.
5. **Enhance `HitFlashSprite.shader` (`Echoes/VFX/Solid White Sprite`):** Add controllable `_FlashAmount` (`0..1`) property alongside `_FlashColor` for smooth `45 ms` unscaled decay instead of hard material snap.
6. **Curate Dedicated Gravita Foley / Synth Stingers:** Commission or trim ultra-snappy (`35–90 ms` transient) custom UI/tile-step ticks and pentatonic deflection chimes tuned specifically to `30 u/s` (`33.33 ms/tile`) speed.
