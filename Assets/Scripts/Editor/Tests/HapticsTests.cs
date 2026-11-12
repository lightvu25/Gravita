using System;
using System.Collections.Generic;
using NUnit.Framework;
using Solo.MOST_IN_ONE;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

/// <summary>
/// Automated EditMode test suite for Gravita's mobile haptics service (<see cref="GameHaptics"/>),
/// <see cref="GameplayHapticsFeedback"/>, and representative UI integration (<see cref="PauseMenuUI"/>).
/// Uses a fake backend, deterministic unscaled clock, and in-memory settings store so Editor
/// <see cref="PlayerPrefs"/> are never modified.
/// </summary>
[TestFixture]
public class HapticsTests
{
    private const float Dt = 0.02f;
    private static readonly Vector3 ArenaOrigin = new Vector3(1200f, 1200f, 0f);

    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly List<GameplayHapticsFeedback> boundFeedbacks = new List<GameplayHapticsFeedback>();
    private readonly List<PauseMenuUI> boundPauseMenus = new List<PauseMenuUI>();

    private RecordingHapticsBackend backend;
    private InMemorySettingsStore store;
    private float fakeUnscaledTime;
    private SimulationMode2D previousSimMode;

    private sealed class RecordingHapticsBackend : IHapticsBackend
    {
        public bool Supported = true;
        public bool ThrowOnPlay;
        public int IsSupportedQueryCount;
        public readonly List<HapticEvent> PlayedEvents = new List<HapticEvent>();

        public bool IsSupported
        {
            get
            {
                IsSupportedQueryCount++;
                return Supported;
            }
        }

        public void Play(HapticEvent hapticEvent)
        {
            if (ThrowOnPlay)
                throw new InvalidOperationException("Simulated native vibrator failure.");

            PlayedEvents.Add(hapticEvent);
        }
    }

    private sealed class InMemorySettingsStore : IHapticsSettingsStore
    {
        public readonly Dictionary<string, bool> Values = new Dictionary<string, bool>();
        public int WriteCount;

        public bool GetBool(string key, bool defaultValue)
        {
            return Values.TryGetValue(key, out bool stored) ? stored : defaultValue;
        }

        public void SetBool(string key, bool value)
        {
            Values[key] = value;
            WriteCount++;
        }
    }

    [SetUp]
    public void SetUp()
    {
        previousSimMode = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;

        backend = new RecordingHapticsBackend { Supported = true };
        store = new InMemorySettingsStore();
        fakeUnscaledTime = 10f;

        GameHaptics.ConfigureForTests(backend, () => fakeUnscaledTime, store);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = boundFeedbacks.Count - 1; i >= 0; i--)
        {
            if (boundFeedbacks[i] != null)
                boundFeedbacks[i].UnbindListeners();
        }
        boundFeedbacks.Clear();

        for (int i = boundPauseMenus.Count - 1; i >= 0; i--)
        {
            if (boundPauseMenus[i] != null)
                boundPauseMenus[i].UnbindButtons();
        }
        boundPauseMenus.Clear();

        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            if (spawned[i] != null)
                UnityEngine.Object.DestroyImmediate(spawned[i]);
        }
        spawned.Clear();

        GameHaptics.ResetForTests();
        Physics2D.simulationMode = previousSimMode;
        Physics2D.SyncTransforms();
    }

    private GameObject Track(GameObject go)
    {
        spawned.Add(go);
        return go;
    }

    private static Vector2 CellCenter(int cx, int cy) =>
        new Vector2(ArenaOrigin.x + cx + 0.5f, ArenaOrigin.y + cy + 0.5f);

    // ─────────────────────────────────────────────────────────────
    //  1. Core GameHaptics Service Tests
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Disabled_SuppressesAllEvents_AndReEnablingRestoresPlayback()
    {
        GameHaptics.Enabled = false;

        GameHaptics.Play(HapticEvent.UISelect);
        fakeUnscaledTime += 1f;
        GameHaptics.Play(HapticEvent.WallImpact);
        fakeUnscaledTime += 1f;
        GameHaptics.Play(HapticEvent.Death);

        Assert.IsEmpty(backend.PlayedEvents, "No haptic events should reach the backend when GameHaptics.Enabled is false.");
        Assert.AreEqual(0, backend.IsSupportedQueryCount, "Disabled check must short-circuit before querying platform support.");

        GameHaptics.Enabled = true;
        GameHaptics.Play(HapticEvent.WallImpact);

        Assert.AreEqual(1, backend.PlayedEvents.Count);
        Assert.AreEqual(HapticEvent.WallImpact, backend.PlayedEvents[0]);
    }

    [Test]
    public void UnsupportedBackend_NeverCallsPlay_AndCachesSupportQuery()
    {
        backend.Supported = false;
        GameHaptics.ConfigureForTests(backend, () => fakeUnscaledTime, store);

        for (int i = 0; i < 10; i++)
        {
            fakeUnscaledTime += 0.5f;
            GameHaptics.Play(HapticEvent.UISelect);
        }

        Assert.IsFalse(GameHaptics.IsSupported);
        Assert.AreEqual(1, backend.IsSupportedQueryCount, "Hardware support check must be cached rather than queried every frame.");
        Assert.IsEmpty(backend.PlayedEvents);
    }

    [Test]
    public void ThrowingBackend_LogsWarningAtMostOnce_WithoutPropagatingException()
    {
        backend.ThrowOnPlay = true;
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[GameHaptics\].*Simulated native vibrator failure"));

        for (int i = 0; i < 15; i++)
        {
            fakeUnscaledTime += 1f;
            Assert.DoesNotThrow(() => GameHaptics.Play(HapticEvent.WallImpact));
        }

        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void PerEventCooldown_DeduplicatesRapidRepeats_UsingUnscaledClock()
    {
        float savedTimeScale = Time.timeScale;
        try
        {
            // Simulate hit-stop freezing Time.timeScale to 0 while unscaled clock advances.
            Time.timeScale = 0f;

            GameHaptics.Play(HapticEvent.WallImpact); // t = 10.00 -> plays
            fakeUnscaledTime += 0.04f;                // t = 10.04 (< 0.08s WallImpact cooldown, > 0.03s global gap)
            GameHaptics.Play(HapticEvent.WallImpact); // suppressed by per-event cooldown!

            fakeUnscaledTime += 0.05f;                // t = 10.09 (0.09s >= 0.08s WallImpact cooldown)
            GameHaptics.Play(HapticEvent.WallImpact); // plays!

            CollectionAssert.AreEqual(
                new[] { HapticEvent.WallImpact, HapticEvent.WallImpact },
                backend.PlayedEvents);
        }
        finally
        {
            Time.timeScale = savedTimeScale;
        }
    }

    [Test]
    public void GlobalMinGap_DeduplicatesCrossEventBurst_WhileDeathBypassesAllCooldowns()
    {
        GameHaptics.Play(HapticEvent.WallImpact);      // t = 10.000 -> plays
        fakeUnscaledTime += 0.01f;                     // t = 10.010 (< 0.03s global gap)
        GameHaptics.Play(HapticEvent.GravityAnchor);   // suppressed by global min gap!

        fakeUnscaledTime += 0.005f;                    // t = 10.015 (still inside global gap, but Death must bypass!)
        GameHaptics.Play(HapticEvent.Death);           // plays immediately!

        CollectionAssert.AreEqual(
            new[] { HapticEvent.WallImpact, HapticEvent.Death },
            backend.PlayedEvents);
    }

    [Test]
    public void EnabledSetting_DefaultsTrue_PersistsToStore_AndFiresChangeEventOnlyOnMutation()
    {
        int changeNotifications = 0;
        bool lastObserved = true;
        GameHaptics.OnEnabledChanged += val =>
        {
            changeNotifications++;
            lastObserved = val;
        };

        Assert.IsTrue(GameHaptics.Enabled, "Default haptics setting must be enabled (true).");

        // Assigning the same value must be a no-op (no redundant PlayerPrefs.Save or event spam).
        GameHaptics.Enabled = true;
        Assert.AreEqual(0, store.WriteCount);
        Assert.AreEqual(0, changeNotifications);

        // Mutating to false persists and notifies once.
        GameHaptics.Enabled = false;
        Assert.AreEqual(1, store.WriteCount);
        Assert.IsFalse(store.Values[GameHaptics.PrefKey]);
        Assert.AreEqual(1, changeNotifications);
        Assert.IsFalse(lastObserved);

        // Simulate fresh session reload against the same persistent store.
        GameHaptics.ConfigureForTests(backend, () => fakeUnscaledTime, store);
        Assert.IsFalse(GameHaptics.Enabled, "Reloaded session must honor persisted false value.");
    }

    [Test]
    public void ClockRewind_ResetsCooldownStateSoSubsequentEventsStillFire()
    {
        fakeUnscaledTime = 500f;
        GameHaptics.Play(HapticEvent.UISelect);
        Assert.AreEqual(1, backend.PlayedEvents.Count);

        // Simulate Enter Play Mode without domain reload resetting Time.unscaledTime back near 0.
        fakeUnscaledTime = 0.02f;
        GameHaptics.Play(HapticEvent.UISelect);
        Assert.AreEqual(2, backend.PlayedEvents.Count, "Clock rewind must reset stale timestamps so haptics still fire.");
    }

    [Test]
    public void MostHapticsBackend_PresetMapping_MatchesApprovedDesignTable()
    {
        Assert.AreEqual(MOST_HapticFeedback.HapticTypes.Selection,    MostHapticsBackend.MapPreset(HapticEvent.UISelect));
        Assert.AreEqual(MOST_HapticFeedback.HapticTypes.LightImpact,  MostHapticsBackend.MapPreset(HapticEvent.WallImpact));
        Assert.AreEqual(MOST_HapticFeedback.HapticTypes.RigidImpact,  MostHapticsBackend.MapPreset(HapticEvent.GravityAnchor));
        Assert.AreEqual(MOST_HapticFeedback.HapticTypes.SoftImpact,   MostHapticsBackend.MapPreset(HapticEvent.PadRedirect));
        Assert.AreEqual(MOST_HapticFeedback.HapticTypes.MediumImpact, MostHapticsBackend.MapPreset(HapticEvent.LaunchStart));
        Assert.AreEqual(MOST_HapticFeedback.HapticTypes.Selection,    MostHapticsBackend.MapPreset(HapticEvent.SwitchToggle));
        Assert.AreEqual(MOST_HapticFeedback.HapticTypes.Failure,      MostHapticsBackend.MapPreset(HapticEvent.Death));
    }

    // ─────────────────────────────────────────────────────────────
    //  2. Step 1 Representative Gameplay & UI Integration Tests
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void WallSlide_InRealTilemapArena_FiresExactlyOneWallImpactHaptic()
    {
        string[] map =
        {
            "#######",
            "#.....#",
            "#######",
        };
        BuildAsciiWallTilemap(map);

        PlayerMovement player = CreateTestPlayerWithHaptics(CellCenter(1, 1), speed: 15f);
        player.QueueDirection(Vector2.right);

        // Run 40 physics frames (0.80s): player slides across 4 cells, hits East wall, and rests against it.
        for (int i = 0; i < 40; i++)
        {
            fakeUnscaledTime += Dt;
            player.SimulateStep(Dt);
            Physics2D.Simulate(Dt);
            Physics2D.SyncTransforms();
        }

        Assert.IsFalse(player.IsMoving);
        CollectionAssert.AreEqual(
            new[] { HapticEvent.WallImpact },
            backend.PlayedEvents,
            "Colliding with and resting against a wall must emit exactly one WallImpact haptic across all physics ticks.");
    }

    [Test]
    public void GravityBlockAndBrakeTile_FireOneAnchorHaptic_NoWallImpact_AndNoRepeatWhileResting()
    {
        string[] map =
        {
            "#########",
            "#.......#",
            "#########",
        };
        BuildAsciiWallTilemap(map);

        // Place GravityBlock at (3, 1) and BrakeTile at (5, 1)
        var gbGo = Track(new GameObject("TestGravityBlock"));
        gbGo.transform.position = CellCenter(3, 1);
        var gbCol = gbGo.AddComponent<BoxCollider2D>();
        gbCol.size = new Vector2(0.90f, 0.90f);
        gbCol.isTrigger = true;
        gbGo.AddComponent<GravityBlock>();

        var brakeGo = Track(new GameObject("TestBrakeTile"));
        brakeGo.transform.position = CellCenter(5, 1);
        var brakeCol = brakeGo.AddComponent<BoxCollider2D>();
        brakeCol.size = new Vector2(0.90f, 0.90f);
        brakeCol.isTrigger = true;
        brakeGo.AddComponent<BrakeTile>();

        Physics2D.SyncTransforms();

        PlayerMovement player = CreateTestPlayerWithHaptics(CellCenter(1, 1), speed: 12f);

        // 1. Slide East into GravityBlock at (3, 1), then remain resting on it for 30 frames (0.60s >> 0.08s cooldown)
        player.QueueDirection(Vector2.right);
        for (int i = 0; i < 40; i++)
        {
            fakeUnscaledTime += Dt;
            player.SimulateStep(Dt);
            Physics2D.Simulate(Dt);
            Physics2D.SyncTransforms();
        }

        Assert.IsFalse(player.IsMoving);
        Assert.IsTrue(player.IsAwaitingFreshInput);
        CollectionAssert.AreEqual(
            new[] { HapticEvent.GravityAnchor },
            backend.PlayedEvents,
            "GravityBlock must emit exactly one GravityAnchor haptic (no WallImpact and no repeat while resting).");

        // 2. Slide East again into BrakeTile at (5, 1)
        player.QueueDirection(Vector2.right);
        for (int i = 0; i < 40; i++)
        {
            fakeUnscaledTime += Dt;
            player.SimulateStep(Dt);
            Physics2D.Simulate(Dt);
            Physics2D.SyncTransforms();
        }

        Assert.IsFalse(player.IsMoving);
        CollectionAssert.AreEqual(
            new[] { HapticEvent.GravityAnchor, HapticEvent.GravityAnchor },
            backend.PlayedEvents,
            "BrakeTile must emit one GravityAnchor haptic per approved Q4.");
    }

    [Test]
    public void PauseMenuUI_ButtonsAndHapticsToggle_TriggerSingleUISelectAndRespectToggle()
    {
        var panelGo = Track(new GameObject("TestPauseMenu"));
        var pauseMenu = panelGo.AddComponent<PauseMenuUI>();
        boundPauseMenus.Add(pauseMenu);

        Button resumeBtn = CreateChildButton(panelGo.transform, "ResumeBtn");
        Button soundBtn = CreateChildButton(panelGo.transform, "SoundBtn");
        Button hapticsBtn = CreateChildButton(panelGo.transform, "HapticsBtn");
        Image hapticsIcon = hapticsBtn.gameObject.AddComponent<Image>();

        Texture2D tex = Texture2D.whiteTexture;
        Sprite onSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
        Sprite offSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));

        try
        {
            var so = new SerializedObject(pauseMenu);
            so.FindProperty("resumeButton").objectReferenceValue = resumeBtn;
            so.FindProperty("soundButton").objectReferenceValue = soundBtn;
            so.FindProperty("hapticsButton").objectReferenceValue = hapticsBtn;
            so.FindProperty("hapticsIcon").objectReferenceValue = hapticsIcon;
            so.FindProperty("hapticsOnSprite").objectReferenceValue = onSprite;
            so.FindProperty("hapticsOffSprite").objectReferenceValue = offSprite;
            so.ApplyModifiedPropertiesWithoutUndo();

            pauseMenu.BindButtons();

            // 1. Clicking Resume and Sound (spaced > 0.05s apart) triggers one UISelect each
            resumeBtn.onClick.Invoke();
            fakeUnscaledTime += 0.2f;
            soundBtn.onClick.Invoke();

            CollectionAssert.AreEqual(
                new[] { HapticEvent.UISelect, HapticEvent.UISelect },
                backend.PlayedEvents);

            // 2. Clicking Haptics toggle turns haptics OFF immediately (no vibration on disable) and swaps icon
            fakeUnscaledTime += 0.2f;
            hapticsBtn.onClick.Invoke();
            Assert.IsFalse(GameHaptics.Enabled);
            Assert.AreSame(offSprite, hapticsIcon.sprite);
            Assert.AreEqual(2, backend.PlayedEvents.Count);

            // Subsequent UI clicks while disabled emit nothing
            fakeUnscaledTime += 0.2f;
            resumeBtn.onClick.Invoke();
            Assert.AreEqual(2, backend.PlayedEvents.Count);

            // 3. Clicking Haptics toggle again turns haptics ON, swaps icon, and plays confirmation tick
            fakeUnscaledTime += 0.2f;
            hapticsBtn.onClick.Invoke();
            Assert.IsTrue(GameHaptics.Enabled);
            Assert.AreSame(onSprite, hapticsIcon.sprite);
            Assert.AreEqual(3, backend.PlayedEvents.Count);
            Assert.AreEqual(HapticEvent.UISelect, backend.PlayedEvents[2]);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(onSprite);
            UnityEngine.Object.DestroyImmediate(offSprite);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private static Button CreateChildButton(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<Button>();
    }

    private void BuildAsciiWallTilemap(string[] rowsTopToBottom)
    {
        int wallLayer = LayerMask.NameToLayer("Wall");
        Assert.GreaterOrEqual(wallLayer, 0, "Project must define a 'Wall' layer.");

        TileBase wallTile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Tilemap/Ground Palette.asset");
        bool createdTempTile = false;
        if (wallTile == null)
        {
            var temp = ScriptableObject.CreateInstance<Tile>();
            temp.colliderType = Tile.ColliderType.Grid;
            wallTile = temp;
            createdTempTile = true;
        }

        var gridGo = Track(new GameObject("HapticsTestGrid"));
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

        int height = rowsTopToBottom.Length;
        for (int r = 0; r < height; r++)
        {
            string row = rowsTopToBottom[r];
            int cy = height - 1 - r;
            for (int cx = 0; cx < row.Length; cx++)
            {
                if (row[cx] == '#')
                    tm.SetTile(new Vector3Int(cx, cy, 0), wallTile);
            }
        }

        tm.RefreshAllTiles();
        tmCol.ProcessTilemapChanges();
        composite.GenerateGeometry();
        Physics2D.SyncTransforms();

        if (createdTempTile)
            UnityEngine.Object.DestroyImmediate(wallTile);
    }

    private PlayerMovement CreateTestPlayerWithHaptics(Vector2 startWorldPos, float speed)
    {
        var go = Track(new GameObject("HapticsTestPlayer"));
        go.transform.position = startWorldPos;

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
        pm.TeleportTo(startWorldPos);

        var feedback = go.AddComponent<GameplayHapticsFeedback>();
        feedback.BindListeners();
        boundFeedbacks.Add(feedback);

        Physics2D.SyncTransforms();
        return pm;
    }
}

