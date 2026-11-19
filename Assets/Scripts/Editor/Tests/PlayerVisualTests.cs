using System;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Tilemaps;

[TestFixture]
public class PlayerVisualTests
{
    private const float Dt = 0.02f;
    private static readonly Vector3 ArenaOrigin = new Vector3(1900f, 1900f, 0f);

    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly List<UnityEngine.Object> tempAssets = new List<UnityEngine.Object>();
    private SimulationMode2D previousSimMode;

    [SetUp]
    public void SetUp()
    {
        previousSimMode = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            GameObject go = spawned[i];
            if (go == null)
                continue;

            DOTween.Kill(go);

            if (!Application.isPlaying)
            {
                MonoBehaviour[] behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
                for (int b = 0; b < behaviours.Length; b++)
                {
                    MonoBehaviour mb = behaviours[b];
                    if (mb == null)
                        continue;

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

        Physics2D.simulationMode = previousSimMode;
        Physics2D.SyncTransforms();
    }

    [Test]
    public void OrientationMath_AllFourDirections_MatchTombOfTheMaskTwoStageSpec()
    {
        Assert.AreEqual(0f, PlayerVisual.ComputeInFlightAngle(Vector2.up), 0.001f);
        Assert.AreEqual(-90f, PlayerVisual.ComputeInFlightAngle(Vector2.right), 0.001f);
        Assert.AreEqual(180f, PlayerVisual.ComputeInFlightAngle(Vector2.down), 0.001f);
        Assert.AreEqual(90f, PlayerVisual.ComputeInFlightAngle(Vector2.left), 0.001f);

        Assert.AreEqual(180f, PlayerVisual.ComputeWallStopAngle(0f), 0.001f);
        Assert.AreEqual(90f, PlayerVisual.ComputeWallStopAngle(-90f), 0.001f);
        Assert.AreEqual(0f, PlayerVisual.ComputeWallStopAngle(180f), 0.001f);
        Assert.AreEqual(-90f, PlayerVisual.ComputeWallStopAngle(90f), 0.001f);

        Vector3 bottomPivotOffset = new Vector3(0f, -0.5f, 0f);
        AssertVectorAlmostEqual(new Vector3(0f, -0.5f, 0f), PlayerVisual.ComputeRotatedBaseLocalPosition(bottomPivotOffset, 0f));
        AssertVectorAlmostEqual(new Vector3(-0.5f, 0f, 0f), PlayerVisual.ComputeRotatedBaseLocalPosition(bottomPivotOffset, -90f));
        AssertVectorAlmostEqual(new Vector3(0f, 0.5f, 0f), PlayerVisual.ComputeRotatedBaseLocalPosition(bottomPivotOffset, 180f));
        AssertVectorAlmostEqual(new Vector3(0.5f, 0f, 0f), PlayerVisual.ComputeRotatedBaseLocalPosition(bottomPivotOffset, 90f));

        AssertVectorAlmostEqual(Vector3.zero, PlayerVisual.ComputeRotatedBaseLocalPosition(Vector3.zero, -90f));
        AssertVectorAlmostEqual(Vector3.zero, PlayerVisual.ComputeRotatedBaseLocalPosition(Vector3.zero, 90f));
    }

    [Test]
    public void GridSlideAndWallImpact_FourDirections_FlipInFlightThen180OnWallWhileKeepingCellCenter()
    {
        BuildWallBoxArena(7, 7);
        CreateTestPlayerWithVisual(
            CellCenter(3, 3),
            speed: 12f,
            out PlayerMovement movement,
            out PlayerVisual visual,
            out Transform spriteTransform,
            out SpriteRenderer spriteRenderer,
            out _);

        Assert.AreEqual(0f, visual.BaseAngleDegrees, 0.001f);
        AssertVectorAlmostEqual(new Vector3(0f, -0.5f, 0f), spriteTransform.localPosition);

        // 1. Right slide (-90°) -> Right wall (+90°)
        SlideAndAssertTwoStage(movement, visual, spriteTransform, spriteRenderer, Vector2.right, -90f, 90f);

        // 2. Up slide (0°) -> Top wall (180°)
        SlideAndAssertTwoStage(movement, visual, spriteTransform, spriteRenderer, Vector2.up, 0f, 180f);

        // 3. Left slide (+90°) -> Left wall (-90°)
        SlideAndAssertTwoStage(movement, visual, spriteTransform, spriteRenderer, Vector2.left, 90f, -90f);

        // 4. Down slide (180°) -> Bottom wall (0°)
        SlideAndAssertTwoStage(movement, visual, spriteTransform, spriteRenderer, Vector2.down, 180f, 0f);
    }

    [Test]
    public void NonWallStop_HaltAtFloorTile_ResetsUprightWithout180Flip()
    {
        BuildWallBoxArena(9, 7);
        CreateTestPlayerWithVisual(
            CellCenter(2, 3),
            speed: 10f,
            out PlayerMovement movement,
            out PlayerVisual visual,
            out Transform spriteTransform,
            out _,
            out _);

        movement.QueueDirection(Vector2.right);
        StepPhysics(movement);

        Assert.IsTrue(movement.IsMoving);
        Assert.IsTrue(visual.IsMovingAnimationActive);
        Assert.AreEqual(-90f, visual.BaseAngleDegrees, 0.001f);

        // BrakeTile / GravityBlock / Maze Runner StartNode halt (not a wall impact)
        movement.HaltAt(CellCenter(4, 3), requireFreshInput: false);

        Assert.IsFalse(movement.WasLastStopWallImpact);
        Assert.IsFalse(visual.IsMovingAnimationActive);
        Assert.AreEqual(0f, visual.BaseAngleDegrees, 0.001f);
        AssertVectorAlmostEqual(new Vector3(0f, -0.5f, 0f), spriteTransform.localPosition);
    }

    [Test]
    public void MidFlightRedirect_UpdatesStage1AndFlipsFromRedirectedDirectionOnWall()
    {
        BuildWallBoxArena(9, 9);
        CreateTestPlayerWithVisual(
            CellCenter(2, 2),
            speed: 12f,
            out PlayerMovement movement,
            out PlayerVisual visual,
            out Transform spriteTransform,
            out _,
            out _);

        movement.QueueDirection(Vector2.right);
        StepPhysics(movement);
        Assert.AreEqual(-90f, visual.BaseAngleDegrees, 0.001f);

        // Redirect upward mid-corridor (Wedge / VectorPad / Portal throat)
        movement.Redirect(Vector2.up, CellCenter(4, 2));
        Assert.AreEqual(0f, visual.BaseAngleDegrees, 0.001f);
        Assert.IsTrue(visual.IsMovingAnimationActive);

        // Complete slide until hitting Top wall
        for (int i = 0; i < 60 && movement.IsMoving; i++)
            StepPhysics(movement);

        Assert.IsFalse(movement.IsMoving);
        Assert.IsTrue(movement.WasLastStopWallImpact);
        Assert.AreEqual(180f, visual.BaseAngleDegrees, 0.001f);
        AssertVectorAlmostEqual(new Vector3(0f, 0.5f, 0f), spriteTransform.localPosition);
        Assert.AreEqual(180f, PlayerVisual.NormalizeSignedAngle(spriteTransform.localEulerAngles.z), 0.01f);
    }

    [Test]
    public void AnimatorControllerAndClips_Match5StateSpecAndCoordinateWithPlayerVisual()
    {
        AnimatorController controller = PlayerAnimationAssetBootstrap.EnsurePlayerAnimationAssetsExist(forceRebuildController: false);
        Assert.IsNotNull(controller, "PlayerAnimator.controller must exist in Assets/Animation/Player.");

        Assert.IsTrue(HasParameter(controller, "IsMoving", AnimatorControllerParameterType.Bool));
        Assert.IsTrue(HasParameter(controller, "Start", AnimatorControllerParameterType.Trigger));
        Assert.IsTrue(HasParameter(controller, "End", AnimatorControllerParameterType.Trigger));
        Assert.IsTrue(HasParameter(controller, "Death", AnimatorControllerParameterType.Trigger));
        Assert.IsTrue(HasParameter(controller, "Reset", AnimatorControllerParameterType.Trigger));

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        Assert.IsNotNull(sm.defaultState);
        Assert.AreEqual("Idle", sm.defaultState.name);

        AnimatorState idle = FindState(sm, "Idle");
        AnimatorState movementState = FindState(sm, "Movement");
        AnimatorState start = FindState(sm, "Start");
        AnimatorState end = FindState(sm, "End");
        AnimatorState death = FindState(sm, "Death");

        Assert.IsNotNull(idle);
        Assert.IsNotNull(movementState);
        Assert.IsNotNull(start);
        Assert.IsNotNull(end);
        Assert.IsNotNull(death);

        AssertClipValid(idle.motion as AnimationClip, expectedLoop: true);
        AssertClipValid(movementState.motion as AnimationClip, expectedLoop: true);
        AssertClipValid(start.motion as AnimationClip, expectedLoop: false);
        AssertClipValid(end.motion as AnimationClip, expectedLoop: false);
        AssertClipValid(death.motion as AnimationClip, expectedLoop: false);

        BuildWallBoxArena(7, 7);
        CreateTestPlayerWithVisual(
            CellCenter(3, 3),
            speed: 12f,
            out PlayerMovement movement,
            out PlayerVisual visual,
            out Transform spriteTransform,
            out _,
            out Animator animator,
            controller);

        animator.Rebind();
        animator.Update(0f);
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Default Animator state must be Idle.");

        // Start trigger -> Start state -> Movement when moving
        visual.PlayStartAnimation();
        animator.Update(0.02f);
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Start"), "PlayStartAnimation must enter Start state.");

        movement.QueueDirection(Vector2.right);
        StepPhysics(movement);
        animator.Update(0.02f);
        Assert.IsTrue(animator.GetBool(PlayerVisual.IsMovingHash));
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Movement"), "Sliding must transition Animator to Movement state.");

        // Freeze mid-flight + Death preserves flight rotation while entering Death state
        movement.Freeze();
        visual.PlayDeathAnimation();
        animator.Update(0.02f);
        Assert.AreEqual(-90f, visual.BaseAngleDegrees, 0.001f);
        Assert.IsFalse(visual.IsMovingAnimationActive);
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Death"), "PlayDeathAnimation must enter Death state.");

        // ResetToIdle returns upright 0° and back to Idle
        visual.ResetToIdle();
        animator.Update(0.02f);
        Assert.AreEqual(0f, visual.BaseAngleDegrees, 0.001f);
        AssertVectorAlmostEqual(new Vector3(0f, -0.5f, 0f), spriteTransform.localPosition);
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "ResetToIdle must return Animator to Idle state.");

        // PlayEndAnimation resets upright 0° and enters End state
        visual.PlayEndAnimation();
        animator.Update(0.02f);
        Assert.AreEqual(0f, visual.BaseAngleDegrees, 0.001f);
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("End"), "PlayEndAnimation must enter End state.");
    }

    [Test]
    public void SquashStretchCoordination_RedirectWhipAndStop_PreserveBaseOrientation()
    {
        BuildWallBoxArena(9, 9);
        CreateTestPlayerWithVisual(
            CellCenter(2, 4),
            speed: 12f,
            out PlayerMovement movement,
            out PlayerVisual visual,
            out Transform spriteTransform,
            out _,
            out _);

        // Start moving Up (0°), then redirect Right (-90°) so CornerWhip triggers around -90°.
        movement.QueueDirection(Vector2.up);
        StepPhysics(movement);
        Assert.AreEqual(0f, visual.BaseAngleDegrees, 0.001f);

        movement.Redirect(Vector2.right, CellCenter(2, 5));
        Assert.AreEqual(-90f, visual.BaseAngleDegrees, 0.001f);

        // Completing active DOTween tweens must settle onto -90° and (-0.5, 0, 0), NEVER Quaternion.identity.
        DOTween.Complete(spriteTransform);
        DOTween.Complete(movement.gameObject);
        Assert.AreEqual(-90f, PlayerVisual.NormalizeSignedAngle(spriteTransform.localEulerAngles.z), 0.1f);
        AssertVectorAlmostEqual(new Vector3(-0.5f, 0f, 0f), spriteTransform.localPosition);

        // Continue to Right wall -> hits wall -> +90° flip -> completing Squash settles cleanly onto +90° and (+0.5, 0, 0).
        for (int i = 0; i < 80 && movement.IsMoving; i++)
            StepPhysics(movement);

        DOTween.Complete(spriteTransform);
        DOTween.Complete(movement.gameObject);
        Assert.IsFalse(movement.IsMoving);
        Assert.AreEqual(90f, PlayerVisual.NormalizeSignedAngle(spriteTransform.localEulerAngles.z), 0.1f);
        AssertVectorAlmostEqual(new Vector3(0.5f, 0f, 0f), spriteTransform.localPosition);
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private static void SlideAndAssertTwoStage(
        PlayerMovement movement,
        PlayerVisual visual,
        Transform spriteTransform,
        SpriteRenderer spriteRenderer,
        Vector2 dir,
        float expectedStage1Angle,
        float expectedStage2Angle)
    {
        movement.QueueDirection(dir);
        StepPhysics(movement);

        Assert.IsTrue(movement.IsMoving, $"Expected player to be sliding {dir}.");
        Assert.IsTrue(visual.IsMovingAnimationActive);
        Assert.AreEqual(expectedStage1Angle, visual.BaseAngleDegrees, 0.001f, $"Stage 1 angle mismatch for {dir}.");
        Assert.AreEqual(expectedStage1Angle, PlayerVisual.NormalizeSignedAngle(spriteTransform.localEulerAngles.z), 0.01f);

        for (int i = 0; i < 80 && movement.IsMoving; i++)
            StepPhysics(movement);

        Assert.IsFalse(movement.IsMoving, $"Expected player to stop at wall for {dir}.");
        Assert.IsTrue(movement.WasLastStopWallImpact);
        Assert.IsFalse(visual.IsMovingAnimationActive);
        Assert.AreEqual(expectedStage2Angle, visual.BaseAngleDegrees, 0.001f, $"Stage 2 wall flip mismatch for {dir}.");
        Assert.AreEqual(expectedStage2Angle, PlayerVisual.NormalizeSignedAngle(spriteTransform.localEulerAngles.z), 0.01f);

        Vector3 expectedLocalPos = PlayerVisual.ComputeRotatedBaseLocalPosition(new Vector3(0f, -0.5f, 0f), expectedStage2Angle);
        AssertVectorAlmostEqual(expectedLocalPos, spriteTransform.localPosition);

        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            Vector3 boundsCenter = spriteRenderer.bounds.center;
            Vector3 playerPos = movement.transform.position;
            Assert.AreEqual(playerPos.x, boundsCenter.x, 0.01f, $"Sprite bounds center X drifted at {dir} wall.");
            Assert.AreEqual(playerPos.y, boundsCenter.y, 0.01f, $"Sprite bounds center Y drifted at {dir} wall.");
        }
    }

    private void CreateTestPlayerWithVisual(
        Vector2 startPos,
        float speed,
        out PlayerMovement movement,
        out PlayerVisual visual,
        out Transform spriteTransform,
        out SpriteRenderer spriteRenderer,
        out Animator animator,
        RuntimeAnimatorController controller = null)
    {
        var rootGo = Track(new GameObject("PlayerVisualTestActor"));
        rootGo.transform.position = startPos;

        var rb = rootGo.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        var col = rootGo.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.90f, 0.90f);
        col.isTrigger = false;

        movement = rootGo.AddComponent<PlayerMovement>();
        var pmSo = new SerializedObject(movement);
        pmSo.FindProperty("moveSpeed").floatValue = speed;
        pmSo.FindProperty("wallLayers").intValue = LayerMask.GetMask("Wall");
        pmSo.FindProperty("turnClearance").floatValue = 0.35f;
        pmSo.FindProperty("moveWithHeldDirection").boolValue = false;
        pmSo.FindProperty("bufferTurnsUntilStop").boolValue = true;
        pmSo.ApplyModifiedPropertiesWithoutUndo();

        var spriteGo = new GameObject("Sprite");
        spriteGo.transform.SetParent(rootGo.transform, false);
        spriteGo.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        spriteTransform = spriteGo.transform;

        var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        tempAssets.Add(tex);
        Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0f), 32f);
        tempAssets.Add(sprite);

        spriteRenderer = spriteGo.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;

        animator = spriteGo.AddComponent<Animator>();
        if (controller != null)
            animator.runtimeAnimatorController = controller;

        visual = rootGo.AddComponent<PlayerVisual>();
        var visualSo = new SerializedObject(visual);
        visualSo.FindProperty("playerMovement").objectReferenceValue = movement;
        visualSo.FindProperty("visualTransform").objectReferenceValue = spriteTransform;
        visualSo.FindProperty("targetAnimator").objectReferenceValue = animator;
        visualSo.ApplyModifiedPropertiesWithoutUndo();

        var squash = rootGo.AddComponent<PlayerSquashStretch>();
        var squashSo = new SerializedObject(squash);
        squashSo.FindProperty("visualTransform").objectReferenceValue = spriteTransform;
        squashSo.ApplyModifiedPropertiesWithoutUndo();

        movement.EnsureInitialized();
        visual.EnsureInitialized();
        InvokeLifecycle(squash, "Awake");
        InvokeLifecycle(visual, "OnEnable");
        InvokeLifecycle(squash, "OnEnable");

        movement.TeleportTo(startPos);
        Physics2D.SyncTransforms();
    }

    private void BuildWallBoxArena(int width, int height)
    {
        int wallLayer = LayerMask.NameToLayer("Wall");
        Assert.GreaterOrEqual(wallLayer, 0);

        var tempTile = ScriptableObject.CreateInstance<Tile>();
        tempTile.colliderType = Tile.ColliderType.Grid;
        tempAssets.Add(tempTile);

        var gridGo = Track(new GameObject("PlayerVisualTestGrid"));
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

    private static bool HasParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name == name && parameters[i].type == type)
                return true;
        }
        return false;
    }

    private static AnimatorState FindState(AnimatorStateMachine sm, string stateName)
    {
        ChildAnimatorState[] states = sm.states;
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].state != null && states[i].state.name == stateName)
                return states[i].state;
        }
        return null;
    }

    private static void AssertClipValid(AnimationClip clip, bool expectedLoop)
    {
        Assert.IsNotNull(clip);
        Assert.Greater(clip.length, 0f);
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        Assert.AreEqual(expectedLoop, settings.loopTime, $"Clip '{clip.name}' loopTime mismatch.");

        EditorCurveBinding[] floatCurves = AnimationUtility.GetCurveBindings(clip);
        for (int i = 0; i < floatCurves.Length; i++)
            Assert.AreNotEqual(typeof(Transform), floatCurves[i].type, $"Clip '{clip.name}' must not animate Transform.");

        EditorCurveBinding[] objCurves = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        for (int i = 0; i < objCurves.Length; i++)
            Assert.AreNotEqual("m_Sprite", objCurves[i].propertyName, $"Placeholder clip '{clip.name}' must not override m_Sprite until art is added.");
    }

    private static void AssertVectorAlmostEqual(Vector3 expected, Vector3 actual, float tolerance = 0.001f)
    {
        Assert.AreEqual(expected.x, actual.x, tolerance, $"X mismatch: expected {expected}, actual {actual}");
        Assert.AreEqual(expected.y, actual.y, tolerance, $"Y mismatch: expected {expected}, actual {actual}");
        Assert.AreEqual(expected.z, actual.z, tolerance, $"Z mismatch: expected {expected}, actual {actual}");
    }
}
