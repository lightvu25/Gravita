using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

[TestFixture]
public class GameplayPerformanceOptimizationTests
{
    private const float Dt = 0.02f;
    private static readonly Vector3 ArenaOrigin = new Vector3(2200f, 2200f, 0f);

    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly List<UnityEngine.Object> tempAssets = new List<UnityEngine.Object>();
    private SimulationMode2D previousSimMode;
    private float savedTimeScale;
    private string tempSaveDir;

    [SetUp]
    public void SetUp()
    {
        previousSimMode = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;
        savedTimeScale = Time.timeScale;
        Time.timeScale = 1f;

        tempSaveDir = Path.Combine(Path.GetTempPath(), "GravitaPerfOptTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempSaveDir);
        SaveManager.CustomSaveDirectoryForTesting = tempSaveDir;
        SaveManager.SimulateSaveFailureForTesting = false;
        SaveManager.ForceSlideSaveDeferralForTesting = false;
        SaveManager.ClearMemoryCacheForTesting();
        MissionService.AbortCurrentLevelRun();
    }

    [TearDown]
    public void TearDown()
    {
        SaveManager.ForceSlideSaveDeferralForTesting = false;
        SaveManager.SimulateSaveFailureForTesting = false;
        MissionService.AbortCurrentLevelRun();
        SaveManager.ClearMemoryCacheForTesting();
        SaveManager.CustomSaveDirectoryForTesting = null;

        if (!string.IsNullOrEmpty(tempSaveDir) && Directory.Exists(tempSaveDir))
        {
            try { Directory.Delete(tempSaveDir, true); }
            catch { /* Ignore temp cleanup failures on locked files */ }
        }

        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            GameObject go = spawned[i];
            if (go == null) continue;

            if (!Application.isPlaying)
            {
                MonoBehaviour[] behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
                for (int b = 0; b < behaviours.Length; b++)
                {
                    MonoBehaviour mb = behaviours[b];
                    if (mb == null) continue;
                    if (mb.enabled && mb.gameObject.activeInHierarchy)
                        InvokeLifecycle(mb, "OnDisable");
                    InvokeLifecycle(mb, "OnDestroy");
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

    [Test]
    public void CinemachineCameraZoom2D_OnChildObject_DisablesParentCinemachinePixelPerfect_AndSwitchesBrainToLateUpdate()
    {
        var camGo = Track(new GameObject("TestMainCamera"));
        Camera cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        CinemachineBrain brain = camGo.AddComponent<CinemachineBrain>();
        brain.UpdateMethod = CinemachineBrain.UpdateMethods.SmartUpdate;

        var vcamGo = Track(new GameObject("TestCinemachineCamera"));
        CinemachineCamera vcam = vcamGo.AddComponent<CinemachineCamera>();
        CinemachinePixelPerfect cmPixelPerfect = vcamGo.AddComponent<CinemachinePixelPerfect>();
        Assert.IsTrue(cmPixelPerfect.enabled);

        var zoomGo = new GameObject("Zoom");
        zoomGo.transform.SetParent(vcamGo.transform, false);
        CinemachineCameraZoom2D zoom2d = zoomGo.AddComponent<CinemachineCameraZoom2D>();

        InvokeLifecycle(zoom2d, "Awake");
        zoom2d.ApplySmoothTrackingOverrides(cam);

        Assert.IsFalse(cmPixelPerfect.enabled,
            "CinemachineCameraZoom2D on child '/Zoom' must locate and disable CinemachinePixelPerfect on parent CinemachineCamera.");
        Assert.AreEqual(CinemachineBrain.UpdateMethods.LateUpdate, brain.UpdateMethod,
            "CinemachineBrain must align to LateUpdate so camera movement matches interpolated physics seamlessly.");
        Assert.Greater(vcam.Lens.OrthographicSize, 0f);
    }

    [Test]
    public void PlayerImpactFeedback_DefaultWallHitStopDisabled_EmitsCameraShakeWithoutFreezingTimeScale()
    {
        var shakeGo = Track(new GameObject("TestCamShake"));
        CinemachineCameraShake2D shake = shakeGo.AddComponent<CinemachineCameraShake2D>();
        InvokeLifecycle(shake, "Awake");
        shake.ResetCoalescingState();

        var playerGo = Track(new GameObject("TestImpactPlayer"));
        playerGo.AddComponent<Rigidbody2D>();
        playerGo.AddComponent<BoxCollider2D>();
        PlayerMovement pm = playerGo.AddComponent<PlayerMovement>();
        pm.EnsureInitialized();

        PlayerImpactFeedback impact = playerGo.AddComponent<PlayerImpactFeedback>();
        var so = new SerializedObject(impact);
        so.FindProperty("tier3Shake").floatValue = 0.25f;
        so.FindProperty("blockBreakShake").floatValue = 0.22f;
        so.FindProperty("tier3HitStop").floatValue = 0.08f;
        so.FindProperty("blockBreakHitStop").floatValue = 0.05f;
        so.ApplyModifiedPropertiesWithoutUndo();

        Assert.IsFalse(impact.WallImpactHitStopEnabled, "wallImpactHitStopEnabled must default to false for smooth gameplay.");

        InvokeLifecycle(impact, "Awake");
        InvokeLifecycle(impact, "OnEnable");

        // Trigger KineticRam wall stop via reflection on LastImpactTier + OnMoveStopped
        pm.EnforceMinimumTier(VelocityTier.KineticRam);
        MethodInfo stopMethod = typeof(PlayerMovement).GetMethod("StopMoving", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(stopMethod);
        stopMethod.Invoke(pm, new object[] { true });

        Assert.AreEqual(1, shake.EmittedImpulseCount, "Wall impact must still emit Camera Shake when Hit-Stop is disabled.");
        Assert.IsFalse(PlayerImpactFeedback.IsHitStopActive);
        Assert.AreEqual(1f, Time.timeScale, 0.0001f);

        // Trigger CrackedBlock break: must also emit shake without freezing Time.timeScale
        var blockGo = Track(new GameObject("TestCrackedBlock"));
        blockGo.AddComponent<BoxCollider2D>();
        CrackedBlock cracked = blockGo.AddComponent<CrackedBlock>();
        InvokeLifecycle(cracked, "Awake");
        CinemachineCameraShake2D.FrameCountProvider = () => 999;
        try
        {
            cracked.Break(Vector2.right);
        }
        finally
        {
            CinemachineCameraShake2D.FrameCountProvider = () => Time.frameCount;
        }

        Assert.AreEqual(2, shake.EmittedImpulseCount);
        Assert.IsFalse(PlayerImpactFeedback.IsHitStopActive);
        Assert.AreEqual(1f, Time.timeScale, 0.0001f);
    }

    [Test]
    public void SaveManager_DeferDuringSlide_SkipsMidSlideDiskWriteAndFlushesOnceOnWallStop()
    {
        BuildWallBoxArena(9, 7);
        PlayerMovement pm = CreateTestPlayer(CellCenter(2, 3), speed: 10f);
        InvokeLifecycle(pm, "Awake");
        Assert.AreSame(pm, PlayerMovement.Instance);

        SaveManager.ForceSlideSaveDeferralForTesting = true;
        MissionService.EnsureWeeklyStateAndRecordLogin();
        MissionService.BeginLevelRun(101);
        int writesBeforeSlide = SaveManager.DiskSaveWriteCountForTesting;

        pm.QueueDirection(Vector2.right);
        StepPhysics(pm);
        Assert.IsTrue(pm.IsMoving, "Player must be sliding before testing mid-slide save deferral.");

        // Star pickup while sliding -> updates memory immediately, defers disk I/O until wall stop!
        MissionService.RecordStarPickupImmediate(1, uniqueStarInstanceId: 777);

        Assert.AreEqual(writesBeforeSlide, SaveManager.DiskSaveWriteCountForTesting,
            "Mid-slide star pickup must not stall the frame with synchronous disk I/O.");
        Assert.IsTrue(SaveManager.HasPendingDeferredSave);
        Assert.AreEqual(1, SaveManager.loadProfile().lifetimeStarsCollected,
            "In-memory profile must reflect the collected star immediately.");

        // Step until player hits the right wall and stops -> OnMoveStopped must auto-flush once!
        for (int i = 0; i < 80 && pm.IsMoving; i++)
            StepPhysics(pm);

        Assert.IsFalse(pm.IsMoving);
        Assert.IsFalse(SaveManager.HasPendingDeferredSave);
        Assert.AreEqual(writesBeforeSlide + 1, SaveManager.DiskSaveWriteCountForTesting,
            "Deferred profile save must flush cleanly to disk once the slide stops.");

        SaveManager.ClearMemoryCacheForTesting();
        ProfileData reloaded = SaveManager.ReloadProfileFromDisk();
        Assert.AreEqual(1, reloaded.lifetimeStarsCollected);
    }

    [Test]
    public void FramePacingBootstrap_And_GameInputKeyCache_ResolveCleanly()
    {
        FramePacingBootstrap.ResolveFramePacing(isMobilePlatform: true, screenRefreshHz: 59.94, out int mobVsync, out int mobFps);
        Assert.AreEqual(0, mobVsync);
        Assert.AreEqual(60, mobFps);

        FramePacingBootstrap.ResolveFramePacing(isMobilePlatform: true, screenRefreshHz: 120.0, out mobVsync, out mobFps);
        Assert.AreEqual(0, mobVsync);
        Assert.AreEqual(120, mobFps);

        FramePacingBootstrap.ResolveFramePacing(isMobilePlatform: true, screenRefreshHz: 0.0, out mobVsync, out mobFps);
        Assert.AreEqual(0, mobVsync);
        Assert.AreEqual(60, mobFps);

        FramePacingBootstrap.ResolveFramePacing(isMobilePlatform: false, screenRefreshHz: 144.0, out int pcVsync, out int pcFps);
        Assert.AreEqual(1, pcVsync);
        Assert.AreEqual(-1, pcFps);

        Assert.AreEqual(Key.F, GameInput.ResolveInputSystemKey(KeyCode.F));
        Assert.AreEqual(Key.Space, GameInput.ResolveInputSystemKey(KeyCode.Space));
        Assert.AreEqual(Key.Escape, GameInput.ResolveInputSystemKey(KeyCode.Escape));
        Assert.AreEqual(Key.Digit1, GameInput.ResolveInputSystemKey(KeyCode.Alpha1));
        Assert.AreEqual(Key.Enter, GameInput.ResolveInputSystemKey(KeyCode.Return));
        // Repeated call exercises cached dictionary lookup
        Assert.AreEqual(Key.Space, GameInput.ResolveInputSystemKey(KeyCode.Space));
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private void BuildWallBoxArena(int width, int height)
    {
        int wallLayer = LayerMask.NameToLayer("Wall");
        Assert.GreaterOrEqual(wallLayer, 0);

        var tempTile = ScriptableObject.CreateInstance<Tile>();
        tempTile.colliderType = Tile.ColliderType.Grid;
        tempAssets.Add(tempTile);

        var gridGo = Track(new GameObject("PerfOptTestGrid"));
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
        var go = Track(new GameObject("PerfOptTestPlayer"));
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

    private static void StepPhysics(PlayerMovement movement, float dt = Dt)
    {
        movement.SimulateStep(dt);
        Physics2D.Simulate(dt);
        Physics2D.SyncTransforms();
    }

    private static Vector2 CellCenter(int cx, int cy) =>
        new Vector2(ArenaOrigin.x + cx + 0.5f, ArenaOrigin.y + cy + 0.5f);

    private GameObject Track(GameObject go)
    {
        spawned.Add(go);
        return go;
    }

    private static void InvokeLifecycle(object target, string methodName)
    {
        MethodInfo method = target?.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        method?.Invoke(target, null);
    }
}
