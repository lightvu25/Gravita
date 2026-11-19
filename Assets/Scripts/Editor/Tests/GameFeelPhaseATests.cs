using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

[TestFixture]
public class GameFeelPhaseATests
{
    private const float Dt = 0.02f;
    private static readonly Vector3 ArenaOrigin = new Vector3(1600f, 1600f, 0f);

    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly List<UnityEngine.Object> tempAssets = new List<UnityEngine.Object>();
    private SimulationMode2D previousSimMode;
    private float savedTimeScale;

    private sealed class TestPopupPanel : MonoBehaviour, IUIPanel
    {
        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }

    [SetUp]
    public void SetUp()
    {
        previousSimMode = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;
        savedTimeScale = Time.timeScale;
        Time.timeScale = 1f;
    }

    [TearDown]
    public void TearDown()
    {
        SoundManager.UnscaledTimeProvider = () => Time.unscaledTime;
        EntityAudio.UnscaledTimeProvider = () => Time.unscaledTime;
        CinemachineCameraShake2D.FrameCountProvider = () => Time.frameCount;

        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            GameObject go = spawned[i];
            if (go == null)
                continue;

            if (!Application.isPlaying)
            {
                MonoBehaviour[] behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
                for (int b = 0; b < behaviours.Length; b++)
                {
                    MonoBehaviour mb = behaviours[b];
                    if (mb == null)
                        continue;

                    if (mb.enabled && mb.gameObject.activeInHierarchy)
                        InvokeLifecycleIfPresent(mb, "OnDisable");

                    InvokeLifecycleIfPresent(mb, "OnDestroy");
                }
            }

            UnityEngine.Object.DestroyImmediate(go);
        }
        spawned.Clear();

        for (int i = tempAssets.Count - 1; i >= 0; i--)
        {
            if (tempAssets[i] != null)
                UnityEngine.Object.DestroyImmediate(tempAssets[i]);
        }
        tempAssets.Clear();

        Time.timeScale = savedTimeScale > 0f ? savedTimeScale : 1f;
        Physics2D.simulationMode = previousSimMode;
        Physics2D.SyncTransforms();
    }

    private GameObject Track(GameObject go)
    {
        spawned.Add(go);
        return go;
    }

    private static void InvokeLifecycleIfPresent(object target, string methodName)
    {
        if (target == null)
            return;

        MethodInfo method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        method?.Invoke(target, null);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"Expected field '{fieldName}' on {target.GetType().Name}.");
        field.SetValue(target, value);
    }

    private static Vector2 CellCenter(int cx, int cy) =>
        new Vector2(ArenaOrigin.x + cx + 0.5f, ArenaOrigin.y + cy + 0.5f);

    [Test]
    public void SoundConfigAsset_AllEntries_HaveAssignedClipsAndFastTileTickInterval()
    {
        SoundConfig cfg = AssetDatabase.LoadAssetAtPath<SoundConfig>("Assets/Data/Sound/SoundConfig.asset");
        Assert.IsNotNull(cfg, "Assets/Data/Sound/SoundConfig.asset must exist.");

        FieldInfo[] fields = typeof(SoundConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);
        int checkedCount = 0;
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].FieldType != typeof(SoundEntry))
                continue;

            SoundEntry entry = (SoundEntry)fields[i].GetValue(cfg);
            checkedCount++;
            Assert.IsTrue(entry.HasClips, $"SoundConfig.{fields[i].Name} must have at least one AudioClip assigned.");
        }

        Assert.GreaterOrEqual(checkedCount, 29, "SoundConfig must define all Phase A SoundEntry fields.");
        Assert.LessOrEqual(cfg.tileTick.minInterval, 0.01f, "tileTick.minInterval must be <= 0.01s so 33.3ms ticks never drop.");
    }

    [Test]
    public void PlayerMovement_RedirectAndPhaseLaunch_DoNotInflateMoveStarted_OrMisroutePhaseToRedirected()
    {
        BuildWallBoxArena(width: 10, height: 5);
        PlayerMovement player = CreateTestPlayer(CellCenter(1, 2), speed: 15f);

        int moveStartedCount = 0;
        int redirectedCount = 0;
        player.OnMoveStarted += _ => moveStartedCount++;
        player.OnRedirected += _ => redirectedCount++;

        // 1. Launch from rest -> OnMoveStarted fires once.
        player.QueueDirection(Vector2.right);
        player.SimulateStep(Dt);
        Assert.AreEqual(1, moveStartedCount, "Launching from rest must fire OnMoveStarted exactly once.");
        Assert.AreEqual(0, redirectedCount);

        // 2. Wedge/Pad Redirect -> OnRedirected fires once, OnMoveStarted does NOT re-fire.
        player.Redirect(Vector2.up, CellCenter(3, 2));
        Assert.AreEqual(1, moveStartedCount, "Redirect must not re-fire OnMoveStarted.");
        Assert.AreEqual(1, redirectedCount, "Redirect must fire OnRedirected once.");

        // 3. VectorLauncher StartPhaseLaunch + arrival -> neither OnMoveStarted nor OnRedirected fires.
        player.StartPhaseLaunch(CellCenter(3, 2), CellCenter(6, 2), Vector2.right, VelocityTier.KineticRam, 1.6f);
        Assert.AreEqual(1, moveStartedCount, "StartPhaseLaunch must not fire OnMoveStarted.");
        Assert.AreEqual(1, redirectedCount, "StartPhaseLaunch must not fire OnRedirected.");

        for (int i = 0; i < 12; i++)
            player.SimulateStep(Dt);

        Assert.AreEqual(1, moveStartedCount, "Phase arrival must not fire OnMoveStarted.");
        Assert.AreEqual(1, redirectedCount, "Phase arrival must not fire OnRedirected.");
    }

    [Test]
    public void CinemachineCameraShake2D_SameFrameImpulses_CoalesceToMaxForceWithoutStacking()
    {
        var go = Track(new GameObject("TestCameraShake"));
        var shake = go.AddComponent<CinemachineCameraShake2D>();

        int fakeFrame = 42;
        CinemachineCameraShake2D.FrameCountProvider = () => fakeFrame;
        shake.ResetCoalescingState();

        shake.ShakeCamera(Vector2.right, 0.22f);
        Assert.AreEqual(1, shake.EmittedImpulseCount);
        Assert.AreEqual(0.22f, shake.MaxForceInCurrentFrame, 0.0001f);
        Assert.AreEqual(Vector3.right * 0.22f, shake.LastEmittedVelocity);

        // Equal same-frame impulse is dropped completely (e.g., 2-block Breaker sweep).
        shake.ShakeCamera(Vector2.right, 0.22f);
        Assert.AreEqual(1, shake.EmittedImpulseCount);
        Assert.AreEqual(0.22f, shake.MaxForceInCurrentFrame, 0.0001f);

        // Stronger same-frame impulse emits only the delta (0.28 - 0.22 = 0.06).
        shake.ShakeCamera(Vector2.up, 0.28f);
        Assert.AreEqual(2, shake.EmittedImpulseCount);
        Assert.AreEqual(0.28f, shake.MaxForceInCurrentFrame, 0.0001f);
        Assert.AreEqual(0.06f, shake.LastEmittedVelocity.magnitude, 0.0001f);

        // Next frame starts a fresh budget.
        fakeFrame = 43;
        shake.ShakeCamera(Vector2.down, 0.13f);
        Assert.AreEqual(3, shake.EmittedImpulseCount);
        Assert.AreEqual(0.13f, shake.MaxForceInCurrentFrame, 0.0001f);
    }

    [Test]
    public void PlayerImpactFeedback_HitStopAndUIManager_CoordinateTimeScaleOwnershipCleanly()
    {
        var uiGo = Track(new GameObject("TestUIManager"));
        var pauseGo = Track(new GameObject("PausePopup"));
        pauseGo.SetActive(false);
        pauseGo.AddComponent<TestPopupPanel>();

        var uiManager = uiGo.AddComponent<UIManager>();
        var mappings = new List<UIManager.UIPanelMapping>
        {
            new UIManager.UIPanelMapping
            {
                panelType = UIPanelType.Pause,
                panelObject = pauseGo,
                layer = UILayer.Popup,
                freezeTime = true
            }
        };
        SetPrivateField(uiManager, "panelMappings", mappings);
        InvokeLifecycleIfPresent(uiManager, "Awake");
        Assert.IsTrue(uiManager.IsPanelRegistered(UIPanelType.Pause), "Test Pause panel must be registered in EditMode.");

        var playerGo = Track(new GameObject("TestImpactPlayer"));
        playerGo.AddComponent<Rigidbody2D>();
        playerGo.AddComponent<BoxCollider2D>();
        playerGo.AddComponent<PlayerMovement>();
        var impact = playerGo.AddComponent<PlayerImpactFeedback>();
        InvokeLifecycleIfPresent(impact, "Awake");
        InvokeLifecycleIfPresent(impact, "OnEnable");

        // 1. Trigger hit-stop -> Time.timeScale drops to 0 and IsHitStopActive becomes true.
        impact.TriggerHitStop(0.05f);
        Assert.IsTrue(PlayerImpactFeedback.IsHitStopActive);
        Assert.AreEqual(0f, Time.timeScale, 0.0001f);
        float firstEnd = impact.ActiveHitStopEndRealtime;

        // 2. Chained hit-stop extends end timestamp while still active.
        impact.TriggerHitStop(0.25f);
        Assert.GreaterOrEqual(impact.ActiveHitStopEndRealtime, firstEnd);
        Assert.IsTrue(PlayerImpactFeedback.IsHitStopActive);

        // 3. Opening a freezing Pause popup while hit-stop ends must keep Time.timeScale at 0.
        uiManager.OpenPanel(UIPanelType.Pause);
        Assert.IsTrue(uiManager.HasTimeFreezingPopupOpen);

        impact.CancelHitStop();
        Assert.IsFalse(PlayerImpactFeedback.IsHitStopActive);
        Assert.AreEqual(0f, Time.timeScale, 0.0001f, "Ending hit-stop while Pause modal is open must preserve freeze.");

        // 4. Closing the Pause modal restores Time.timeScale to 1.
        uiManager.CloseTopPopup();
        Assert.IsFalse(uiManager.HasTimeFreezingPopupOpen);
        Assert.AreEqual(1f, Time.timeScale, 0.0001f);
    }

    [Test]
    public void CrackedBlock_BreakWithDirection_StoresNormalizedLastBreakDirection()
    {
        var blockGo = Track(new GameObject("TestCrackedBlock"));
        blockGo.SetActive(false);
        int wallLayer = LayerMask.NameToLayer("Wall");
        if (wallLayer >= 0)
            blockGo.layer = wallLayer;

        blockGo.AddComponent<BoxCollider2D>();
        var cracked = blockGo.AddComponent<CrackedBlock>();
        blockGo.SetActive(true);

        Vector2 observedOnEvent = Vector2.zero;
        Action<CrackedBlock> handler = b => observedOnEvent = b.LastBreakDirection;
        CrackedBlock.OnAnyBroken += handler;
        try
        {
            cracked.Break(new Vector2(3f, 0f));
        }
        finally
        {
            CrackedBlock.OnAnyBroken -= handler;
        }

        Assert.IsTrue(cracked.IsBroken);
        Assert.AreEqual(Vector2.right, cracked.LastBreakDirection);
        Assert.AreEqual(Vector2.right, observedOnEvent);

        cracked.ResetBlock();
        Assert.IsFalse(cracked.IsBroken);
        Assert.AreEqual(Vector2.zero, cracked.LastBreakDirection);
    }

    [Test]
    public void EntityAudio_SameFrameMagnetCoinSweep_AllocatesDistinctVoicesWithAscendingSemitones()
    {
        AudioClip coinClip = AudioClip.Create("TestCoinClip", 4410, 1, 44100, false);
        tempAssets.Add(coinClip);

        SoundConfig cfg = ScriptableObject.CreateInstance<SoundConfig>();
        cfg.coinCollect = new SoundEntry
        {
            clips = new[] { coinClip },
            volume = 0.8f,
            randomizePitch = false,
            pitchVariance = 0f,
            minInterval = 0.01f
        };
        tempAssets.Add(cfg);

        var smGo = Track(new GameObject("TestSoundManager"));
        var poolRoot = new GameObject("SFXPool");
        poolRoot.transform.SetParent(smGo.transform, false);

        var poolSources = new List<AudioSource>();
        for (int i = 0; i < 10; i++)
        {
            var voiceGo = new GameObject($"SFX_{i}");
            voiceGo.transform.SetParent(poolRoot.transform, false);
            poolSources.Add(voiceGo.AddComponent<AudioSource>());
        }

        var sm = smGo.AddComponent<SoundManager>();
        SetPrivateField(sm, "soundConfig", cfg);
        SetPrivateField(sm, "poolContainer", poolRoot.transform);
        InvokeLifecycleIfPresent(sm, "Awake");
        Assert.AreSame(sm, SoundManager.Instance, "SoundManager singleton must be active in EditMode.");

        var entityGo = Track(new GameObject("TestEntityAudio"));
        var entityAudio = entityGo.AddComponent<EntityAudio>();
        InvokeLifecycleIfPresent(entityAudio, "Awake");
        InvokeLifecycleIfPresent(entityAudio, "OnEnable");
        entityAudio.ResetStreakState();
        Assert.IsTrue(EntityAudio.HasActiveInstance, "EntityAudio must register active instance on OnEnable.");

        float fakeTime = 100f;
        SoundManager.UnscaledTimeProvider = () => fakeTime;
        EntityAudio.UnscaledTimeProvider = () => fakeTime;

        // Collect 4 coins on the exact same unscaled timestamp (Magnet sweep): 3 fallback + 1 custom-SFX coin.
        for (int i = 0; i < 4; i++)
        {
            var coinGo = Track(new GameObject($"TestCoin_{i}"));
            var col = coinGo.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            var collectible = coinGo.AddComponent<Collectible>();
            if (i == 3)
                SetPrivateField(collectible, "collectSfx", coinClip);

            coinGo.SetActive(true);
            InvokeLifecycleIfPresent(collectible, "Awake");
            collectible.Collect();
        }

        var playedPitches = new List<float>();
        for (int i = 0; i < poolSources.Count; i++)
        {
            if (poolSources[i].clip != null)
                playedPitches.Add(poolSources[i].pitch);
        }

        Assert.AreEqual(4, playedPitches.Count, "All 4 same-frame Magnet coins must allocate distinct SFXPool voices.");
        playedPitches.Sort();
        Assert.AreEqual(1.0000f, playedPitches[0], 0.002f);
        Assert.AreEqual(Mathf.Pow(1.0594631f, 1), playedPitches[1], 0.002f);
        Assert.AreEqual(Mathf.Pow(1.0594631f, 2), playedPitches[2], 0.002f);
        Assert.AreEqual(Mathf.Pow(1.0594631f, 3), playedPitches[3], 0.002f);
    }

    [Test]
    public void VfxRotationUtility_Splits3DZParticlesFrom2DXAnimations()
    {
        var particleGo = Track(new GameObject("Test3DParticlePrefab"));
        particleGo.AddComponent<ParticleSystem>();

        var animGo = Track(new GameObject("Test2DAnimPrefab"));
        animGo.AddComponent<Animator>();

        Quaternion pRot = VfxRotationUtility.ResolveRotationForPrefab(particleGo, align: true, Vector2.right);
        Vector3 emittedForward = pRot * Vector3.forward;
        Assert.Greater(Vector3.Dot(emittedForward.normalized, Vector3.right), 0.99f, "ParticleSystem prefabs must align local +Z with direction.");

        Quaternion aRot = VfxRotationUtility.ResolveRotationForPrefab(animGo, align: true, Vector2.up);
        Vector3 animatedRight = aRot * Vector3.right;
        Assert.Greater(Vector3.Dot(animatedRight.normalized, Vector3.up), 0.99f, "Animator sprite prefabs must align local +X in XY with direction.");
    }

    private void BuildWallBoxArena(int width, int height)
    {
        int wallLayer = LayerMask.NameToLayer("Wall");
        Assert.GreaterOrEqual(wallLayer, 0);

        var tempTile = ScriptableObject.CreateInstance<Tile>();
        tempTile.colliderType = Tile.ColliderType.Grid;
        tempAssets.Add(tempTile);

        var gridGo = Track(new GameObject("PhaseATestGrid"));
        gridGo.transform.position = ArenaOrigin;
        var grid = gridGo.AddComponent<Grid>();
        grid.cellSize = new Vector3(1f, 1f, 0f);

        var wallGo = new GameObject("WallTilemap");
        wallGo.layer = wallLayer;
        wallGo.transform.SetParent(gridGo.transform, false);

        var tm = wallGo.AddComponent<Tilemap>();
        wallGo.AddComponent<TilemapRenderer>();
        var tmCol = wallGo.AddComponent<TilemapCollider2D>();
        tmCol.compositeOperation = Collider2D.CompositeOperation.Merge;

        var rb = wallGo.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Static;
        var composite = wallGo.AddComponent<CompositeCollider2D>();
        composite.geometryType = CompositeCollider2D.GeometryType.Polygons;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    tm.SetTile(new Vector3Int(x, y, 0), tempTile);
            }
        }

        tm.RefreshAllTiles();
        tmCol.ProcessTilemapChanges();
        composite.GenerateGeometry();
        Physics2D.SyncTransforms();
    }

    private PlayerMovement CreateTestPlayer(Vector2 startPos, float speed)
    {
        var go = Track(new GameObject("PhaseATestPlayer"));
        go.transform.position = startPos;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        var col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.90f, 0.90f);
        col.isTrigger = false;

        var pm = go.AddComponent<PlayerMovement>();
        var so = new SerializedObject(pm);
        so.FindProperty("moveSpeed").floatValue = speed;
        so.FindProperty("wallLayers").intValue = LayerMask.GetMask("Wall");
        so.FindProperty("turnClearance").floatValue = 0.35f;
        so.FindProperty("moveWithHeldDirection").boolValue = false;
        so.FindProperty("bufferTurnsUntilStop").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        pm.EnsureInitialized();
        pm.TeleportTo(startPos);
        Physics2D.SyncTransforms();
        return pm;
    }
}
