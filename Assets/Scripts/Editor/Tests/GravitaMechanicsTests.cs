using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Automated EditMode unit & asset validation suite for Gravita's deterministic mechanics,
/// signature systems (S1/S4/S6/S7), prefabs, and sandbox level.
/// </summary>
[TestFixture]
public class GravitaMechanicsTests
{
    private readonly List<GameObject> tempObjects = new List<GameObject>();

    private GameObject Track(GameObject go)
    {
        tempObjects.Add(go);
        return go;
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = tempObjects.Count - 1; i >= 0; i--)
        {
            if (tempObjects[i] != null)
                Object.DestroyImmediate(tempObjects[i]);
        }
        tempObjects.Clear();
    }

    // ─────────────────────────────────────────────────────────────
    //  1. HazardActivation — TimedCycle & StepPattern
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void HazardActivation_TimedCycle_EvaluatesExactPhaseBoundaries()
    {
        // Cycle = 1.4s Inactive + 0.5s Warning + 1.1s Active = 3.0s total
        float until;

        Assert.AreEqual(HazardCyclePhase.Inactive,
            HazardActivation.EvaluateTimedPhase(0.0f, 1.4f, 0.5f, 1.1f, 0f, out until));
        Assert.AreEqual(1.9f, until, 0.0001f);

        Assert.AreEqual(HazardCyclePhase.Warning,
            HazardActivation.EvaluateTimedPhase(1.45f, 1.4f, 0.5f, 1.1f, 0f, out until));
        Assert.AreEqual(0.45f, until, 0.0001f);

        Assert.AreEqual(HazardCyclePhase.Active,
            HazardActivation.EvaluateTimedPhase(2.0f, 1.4f, 0.5f, 1.1f, 0f, out until));
        Assert.AreEqual(0f, until, 0.0001f);

        // Wraps cleanly at t = 3.1s -> 0.1s into next cycle -> Inactive
        Assert.AreEqual(HazardCyclePhase.Inactive,
            HazardActivation.EvaluateTimedPhase(3.1f, 1.4f, 0.5f, 1.1f, 0f, out until));
    }

    [Test]
    public void HazardActivation_TimedCycle_PhaseOffsetShiftsCycleDeterministically()
    {
        // With offset = 1.5s on a (1.4, 0.5, 1.1) cycle, t = 0 starts in Warning (0.4s before Active)
        var phase = HazardActivation.EvaluateTimedPhase(0f, 1.4f, 0.5f, 1.1f, 1.5f, out float until);
        Assert.AreEqual(HazardCyclePhase.Warning, phase);
        Assert.AreEqual(0.4f, until, 0.0001f);
    }

    [Test]
    public void HazardActivation_StepPattern_RespectsStepsAndWarningLead()
    {
        bool[] pattern = { false, true };
        float until;

        // Step 0 [0..1.0): Inactive until 0.7s, Warning during (0.7..1.0]
        Assert.AreEqual(HazardCyclePhase.Inactive,
            HazardActivation.EvaluatePatternPhase(0.2f, pattern, 1.0f, 0.3f, 0f, out until));

        Assert.AreEqual(HazardCyclePhase.Warning,
            HazardActivation.EvaluatePatternPhase(0.8f, pattern, 1.0f, 0.3f, 0f, out until));
        Assert.AreEqual(0.2f, until, 0.0001f);

        // Step 1 [1.0..2.0): Active
        Assert.AreEqual(HazardCyclePhase.Active,
            HazardActivation.EvaluatePatternPhase(1.25f, pattern, 1.0f, 0.3f, 0f, out until));
        Assert.AreEqual(0f, until, 0.0001f);
    }

    // ─────────────────────────────────────────────────────────────
    //  2. Overloaded Floor — Skate Velocity Gate (D2)
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void OverloadedFloor_SkateMode_SafeOnlyWhileSlidingAtOrAboveThreshold()
    {
        // Stationary is ALWAYS lethal even if tier were high
        Assert.IsTrue(HazardActivation.EvaluateSkateLethality(false, VelocityTier.None, VelocityTier.Cruise));
        Assert.IsTrue(HazardActivation.EvaluateSkateLethality(false, VelocityTier.KineticRam, VelocityTier.Cruise));

        // Sliding slower than Cruise (Drift) is lethal
        Assert.IsTrue(HazardActivation.EvaluateSkateLethality(true, VelocityTier.Drift, VelocityTier.Cruise));

        // Sliding at Cruise or KineticRam is safe
        Assert.IsFalse(HazardActivation.EvaluateSkateLethality(true, VelocityTier.Cruise, VelocityTier.Cruise));
        Assert.IsFalse(HazardActivation.EvaluateSkateLethality(true, VelocityTier.KineticRam, VelocityTier.Cruise));
    }

    // ─────────────────────────────────────────────────────────────
    //  3. Failing Tile — Collapse Lifecycle & Reset Safety (D3)
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void FailingTile_EvaluateState_FollowsApprovedCountdown()
    {
        Assert.AreEqual(FailingTileState.Intact, FailingTile.EvaluateState(false, 10f, 0.6f, 0.3f));
        Assert.AreEqual(FailingTileState.Warning, FailingTile.EvaluateState(true, 0.0f, 0.6f, 0.3f));
        Assert.AreEqual(FailingTileState.Warning, FailingTile.EvaluateState(true, 0.59f, 0.6f, 0.3f));
        Assert.AreEqual(FailingTileState.Collapsing, FailingTile.EvaluateState(true, 0.60f, 0.6f, 0.3f));
        Assert.AreEqual(FailingTileState.Collapsing, FailingTile.EvaluateState(true, 0.89f, 0.6f, 0.3f));
        Assert.AreEqual(FailingTileState.Collapsed, FailingTile.EvaluateState(true, 0.90f, 0.6f, 0.3f));
    }

    [Test]
    public void FailingTile_ResetMidCountdown_CancelsCollapseAndDisablesPitHazard()
    {
        var go = Track(new GameObject("FailingTileTest"));
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;

        var pitGo = new GameObject("CollapsedPitHazard");
        pitGo.transform.SetParent(go.transform, false);
        var pitCol = pitGo.AddComponent<BoxCollider2D>();
        pitCol.isTrigger = true;
        var pitHazard = pitGo.AddComponent<Hazard>();
        pitGo.SetActive(false);

        var tile = go.AddComponent<FailingTile>();
        var so = new SerializedObject(tile);
        so.FindProperty("collapsedPitHazard").objectReferenceValue = pitHazard;
        so.FindProperty("warningDuration").floatValue = 0.6f;
        so.FindProperty("collapseDuration").floatValue = 0.3f;
        so.ApplyModifiedPropertiesWithoutUndo();

        tile.ResolveAtCenter(null, Vector2.right);
        Assert.IsTrue(tile.IsArmed);
        Assert.AreEqual(FailingTileState.Warning, tile.State);

        tile.OnLevelClockTick(0.7f, 0.7f);
        Assert.AreEqual(FailingTileState.Collapsing, tile.State);
        Assert.IsFalse(pitGo.activeSelf);

        // Reset mid-countdown -> back to Intact, never collapses on subsequent ticks unless re-armed
        tile.OnLevelClockReset();
        Assert.IsFalse(tile.IsArmed);
        Assert.AreEqual(FailingTileState.Intact, tile.State);

        tile.OnLevelClockTick(2.0f, 2.0f);
        Assert.AreEqual(FailingTileState.Intact, tile.State);
        Assert.IsFalse(pitGo.activeSelf);

        // Re-arm and complete full 0.9s -> Collapsed pit enabled
        tile.ArmNow();
        tile.OnLevelClockTick(3.0f, 1.0f);
        Assert.AreEqual(FailingTileState.Collapsed, tile.State);
        Assert.IsTrue(pitGo.activeSelf);
    }

    // ─────────────────────────────────────────────────────────────
    //  4. Sentinel Drone — Deterministic Patrol & Freeze Pause
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void SentinelDrone_EvaluatePatrolPosition_PingPongAndLoopAreDeterministic()
    {
        var wps = new List<Vector2> { new Vector2(0f, 0f), new Vector2(4f, 0f) };
        // Speed = 2 u/s -> 2.0s move; wait = 0.5s; delay = 0.2s
        // Cycle: [0..0.5 wait@0] [0.5..2.5 move 0->4] [2.5..3.0 wait@4] [3.0..5.0 move 4->0]

        Assert.AreEqual(new Vector2(0f, 0f),
            SentinelDronePatrol.EvaluatePatrolPosition(wps, DronePatrolMode.PingPong, 2f, 0.5f, 0.2f, 0.1f));

        // patrolTime = 0.2 delay + 0.5 wait + 1.0 half-move = 1.7s -> x = 2.0
        Vector2 midForward = SentinelDronePatrol.EvaluatePatrolPosition(wps, DronePatrolMode.PingPong, 2f, 0.5f, 0.2f, 1.7f);
        Assert.AreEqual(2f, midForward.x, 0.001f);

        // Repeat same query -> bit-identical result
        Vector2 midForwardAgain = SentinelDronePatrol.EvaluatePatrolPosition(wps, DronePatrolMode.PingPong, 2f, 0.5f, 0.2f, 1.7f);
        Assert.AreEqual(midForward, midForwardAgain);

        // patrolTime = 0.2 delay + 2.7s (inside wait at wp[1]) -> x = 4.0
        Vector2 atTurnaround = SentinelDronePatrol.EvaluatePatrolPosition(wps, DronePatrolMode.PingPong, 2f, 0.5f, 0.2f, 2.9f);
        Assert.AreEqual(4f, atTurnaround.x, 0.001f);

        // Loop mode with 3 waypoints wraps wp[2] -> wp[0]
        var loopWps = new List<Vector2> { new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 2f) };
        // wait=0, delay=0, speed=2: seg0=1s, seg1=1s, seg2=sqrt(8)/2 = 1.4142s
        Vector2 onClosingLeg = SentinelDronePatrol.EvaluatePatrolPosition(loopWps, DronePatrolMode.Loop, 2f, 0f, 0f, 2.7071f);
        Assert.AreEqual(1f, onClosingLeg.x, 0.02f);
        Assert.AreEqual(1f, onClosingLeg.y, 0.02f);
    }

    // ─────────────────────────────────────────────────────────────
    //  5. GridSlideSolver — Discrete Slide & Velocity Tiers
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void GridSlideSolver_StopsBeforeWallAndComputesImpactTier()
    {
        // Wall at (3, 0): sliding Right from (0, 0) stops at (2, 0) -> 2 tiles -> Drift
        var drift = GridSlideSolver.SolveSlide(
            Vector2Int.zero,
            Vector2Int.right,
            cell => cell == new Vector2Int(3, 0));
        Assert.AreEqual(new Vector2Int(2, 0), drift.StopCell);
        Assert.AreEqual(2, drift.TilesTraveled);
        Assert.AreEqual(VelocityTier.Drift, drift.ImpactTier);

        // Wall at (5, 0): sliding Right from (0, 0) stops at (4, 0) -> 4 tiles -> Cruise
        var res = GridSlideSolver.SolveSlide(
            Vector2Int.zero,
            Vector2Int.right,
            cell => cell == new Vector2Int(5, 0));

        Assert.AreEqual(new Vector2Int(4, 0), res.StopCell);
        Assert.AreEqual(4, res.TilesTraveled);
        Assert.AreEqual(VelocityTier.Cruise, res.ImpactTier);
        Assert.IsFalse(res.ReachedStepCap);

        // Wall at (6, 0): 5 tiles -> KineticRam
        var ram = GridSlideSolver.SolveSlide(
            Vector2Int.zero,
            Vector2Int.right,
            cell => cell == new Vector2Int(6, 0));
        Assert.AreEqual(5, ram.TilesTraveled);
        Assert.AreEqual(VelocityTier.KineticRam, ram.ImpactTier);
    }

    // ─────────────────────────────────────────────────────────────
    //  6. Same-Cell Resolution Order & Switch/Gate Mechanics (D4/D7)
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void CellResolver_ExecutesSwitchBeforeMovementAndRespectsPriority()
    {
        var gateGo = Track(new GameObject("Gate"));
        var gateCol = gateGo.AddComponent<BoxCollider2D>();
        var gate = gateGo.AddComponent<EnergyGate>();
        var gateSo = new SerializedObject(gate);
        gateSo.FindProperty("solidBarrierCollider").objectReferenceValue = gateCol;
        gateSo.FindProperty("defaultOpen").boolValue = false;
        gateSo.ApplyModifiedPropertiesWithoutUndo();
        gate.ResetSwitchState();
        Assert.IsFalse(gate.IsLogicalOpen);

        var switchGo = Track(new GameObject("Switch"));
        switchGo.AddComponent<BoxCollider2D>().isTrigger = true;
        var sw = switchGo.AddComponent<CircuitSwitch>();
        var swSo = new SerializedObject(sw);
        var tProp = swSo.FindProperty("targets");
        tProp.arraySize = 1;
        tProp.GetArrayElementAtIndex(0).objectReferenceValue = gate;
        swSo.ApplyModifiedPropertiesWithoutUndo();

        var padGo = Track(new GameObject("Pad"));
        padGo.AddComponent<BoxCollider2D>().isTrigger = true;
        var pad = padGo.AddComponent<VectorPad>();
        pad.SetDirection(CardinalDirection.Up);

        var failingGo = Track(new GameObject("Failing"));
        failingGo.AddComponent<BoxCollider2D>().isTrigger = true;
        var failing = failingGo.AddComponent<FailingTile>();

        // Intentionally unsorted input list
        var list = new List<ICellInteraction> { failing, pad, sw };
        CellResolver.SortByPriority(list);

        Assert.AreSame(sw, list[0]);
        Assert.AreSame(pad, list[1]);
        Assert.AreSame(failing, list[2]);

        CellOutcome outcome = CellResolver.ResolveSorted(list, null, Vector2.right);

        // 1. Switch ran and opened the EnergyGate
        Assert.IsTrue(gate.IsLogicalOpen);
        // 2. VectorPad claimed Redirect(Up)
        Assert.AreEqual(CellOutcomeType.Redirect, outcome.Type);
        Assert.AreEqual(Vector2.up, outcome.Direction);
        // 3. FailingTile still armed passively
        Assert.IsTrue(failing.IsArmed);
    }

    [Test]
    public void CellResolver_GravityBlockOutranksVectorLauncherAndVectorPad()
    {
        var gbGo = Track(new GameObject("GravityBlock"));
        gbGo.AddComponent<BoxCollider2D>().isTrigger = true;
        var gb = gbGo.AddComponent<GravityBlock>();

        var padGo = Track(new GameObject("VectorPad"));
        padGo.AddComponent<BoxCollider2D>().isTrigger = true;
        var pad = padGo.AddComponent<VectorPad>();
        pad.SetDirection(CardinalDirection.Down);

        var list = new List<ICellInteraction> { pad, gb };
        CellResolver.SortByPriority(list);
        CellOutcome outcome = CellResolver.ResolveSorted(list, null, Vector2.right);

        Assert.AreEqual(CellOutcomeType.Stop, outcome.Type);
        Assert.IsTrue(outcome.RequireFreshInput);
    }

    // ─────────────────────────────────────────────────────────────
    //  7. VectorLauncher — Editable LandingTarget & PhaseLaunch
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void VectorLauncher_ResolvesEditableLandingTargetAndGrantsKineticRam()
    {
        var go = Track(new GameObject("Launcher"));
        go.transform.position = new Vector3(1.5f, 0.5f, 0f);
        go.AddComponent<BoxCollider2D>().isTrigger = true;

        var landing = new GameObject("LandingTarget");
        landing.transform.SetParent(go.transform, false);
        landing.transform.localPosition = new Vector3(0f, 4f, 0f);

        var launcher = go.AddComponent<VectorLauncher>();
        var so = new SerializedObject(launcher);
        so.FindProperty("landingTarget").objectReferenceValue = landing.transform;
        so.FindProperty("autoDirectionFromLanding").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        Assert.AreEqual(new Vector2(1.5f, 4.5f), launcher.GetResolvedLandingPosition());
        Assert.AreEqual(Vector2.up, launcher.GetResolvedPostDirection());

        CellOutcome first = launcher.ResolveAtCenter(null, Vector2.right);
        Assert.AreEqual(CellOutcomeType.Launch, first.Type);
        Assert.IsTrue(first.IsPhaseLaunch);
        Assert.AreEqual(new Vector2(1.5f, 4.5f), first.PhaseLandingPosition);
        Assert.AreEqual(Vector2.up, first.Direction);
        Assert.AreEqual(VelocityTier.KineticRam, first.MinTier);

        // Disarmed during same occupancy -> Continue until ResetTile
        CellOutcome duplicate = launcher.ResolveAtCenter(null, Vector2.right);
        Assert.AreEqual(CellOutcomeType.Continue, duplicate.Type);

        launcher.ResetTile();
        CellOutcome afterReset = launcher.ResolveAtCenter(null, Vector2.right);
        Assert.AreEqual(CellOutcomeType.Launch, afterReset.Type);
    }

    // ─────────────────────────────────────────────────────────────
    //  8. Signature Systems — BreachSequencer (S4), CloseCall (S6), Clock
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void BreachSequencer_FiresAtTimestampOnceAndResetsOnRestart()
    {
        var gateGo = Track(new GameObject("BreachGate"));
        gateGo.AddComponent<BoxCollider2D>();
        var gate = gateGo.AddComponent<EnergyGate>();
        gate.ResetSwitchState();
        Assert.IsFalse(gate.IsPowered);

        var seqGo = Track(new GameObject("Sequencer"));
        var seq = seqGo.AddComponent<BreachSequencer>();
        var so = new SerializedObject(seq);
        var arr = so.FindProperty("timelineEvents");
        arr.arraySize = 1;
        var ev = arr.GetArrayElementAtIndex(0);
        ev.FindPropertyRelative("triggerTime").floatValue = 5.0f;
        ev.FindPropertyRelative("action").enumValueIndex = (int)BreachActionType.PowerOn;
        ev.FindPropertyRelative("cameraShakeForce").floatValue = 0f;
        var targets = ev.FindPropertyRelative("targets");
        targets.arraySize = 1;
        targets.GetArrayElementAtIndex(0).objectReferenceValue = gate;
        so.ApplyModifiedPropertiesWithoutUndo();

        int fireCount = 0;
        seq.OnBreachFired += (_, __) => fireCount++;

        seq.OnLevelClockTick(4.9f, 4.9f);
        Assert.AreEqual(0, fireCount);
        Assert.IsFalse(gate.IsPowered);

        seq.OnLevelClockTick(5.0f, 0.1f);
        Assert.AreEqual(1, fireCount);
        Assert.IsTrue(gate.IsPowered);

        // Subsequent ticks do not double-fire
        seq.OnLevelClockTick(8.0f, 3.0f);
        Assert.AreEqual(1, fireCount);

        // Level reset clears fired state so replay fires deterministically again
        seq.OnLevelClockReset();
        gate.ResetSwitchState();
        Assert.IsFalse(seq.HasEventFired(0));

        seq.OnLevelClockTick(5.2f, 5.2f);
        Assert.AreEqual(2, fireCount);
        Assert.IsTrue(gate.IsPowered);
    }

    [Test]
    public void CloseCallWindow_And_LevelClockPauseReset_WorkDeterministically()
    {
        Assert.IsFalse(CloseCallDetector.IsWithinCloseCallWindow(0f, 0.15f));
        Assert.IsTrue(CloseCallDetector.IsWithinCloseCallWindow(0.01f, 0.15f));
        Assert.IsTrue(CloseCallDetector.IsWithinCloseCallWindow(0.15f, 0.15f));
        Assert.IsFalse(CloseCallDetector.IsWithinCloseCallWindow(0.16f, 0.15f));

        var clock = new LevelClock();
        clock.Tick(0.5f);
        Assert.AreEqual(0.5f, clock.Time, 0.0001f);
        Assert.AreEqual(1, clock.TickCount);

        clock.Tick(0f);
        Assert.AreEqual(0.5f, clock.Time, 0.0001f);
        Assert.AreEqual(1, clock.TickCount);

        clock.Tick(0.25f);
        Assert.AreEqual(0.75f, clock.Time, 0.0001f);
        Assert.AreEqual(2, clock.TickCount);

        clock.Reset();
        Assert.AreEqual(0f, clock.Time, 0.0001f);
        Assert.AreEqual(0, clock.TickCount);
    }

    // ─────────────────────────────────────────────────────────────
    //  9. Asset Integrity — Prefabs, S7 PowerUps, LevelDataValidator
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void AllTenGameplayPrefabs_And_S7PowerUps_AreValidAndClean()
    {
        string[] prefabs =
        {
            "Assets/Prefabs/Gameplay/SolidBlock.prefab",
            "Assets/Prefabs/Gameplay/GravityBlock.prefab",
            "Assets/Prefabs/Gameplay/VectorPad.prefab",
            "Assets/Prefabs/Gameplay/VectorLauncher.prefab",
            "Assets/Prefabs/Gameplay/EnergyGate.prefab",
            "Assets/Prefabs/Gameplay/CircuitSwitch.prefab",
            "Assets/Prefabs/Gameplay/PulseLaser.prefab",
            "Assets/Prefabs/Gameplay/OverloadedFloor.prefab",
            "Assets/Prefabs/Gameplay/FailingTile.prefab",
            "Assets/Prefabs/Gameplay/SentinelDrone.prefab",
        };

        foreach (string path in prefabs)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(go, $"Missing gameplay prefab at {path}");
            Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go),
                $"Prefab {path} has missing scripts!");
        }

        Assert.AreEqual("Phase Skin", AssetDatabase.LoadAssetAtPath<PowerUpData>("Assets/Data/PowerUps/Shield.asset").DisplayName);
        Assert.AreEqual("Data Siphon", AssetDatabase.LoadAssetAtPath<PowerUpData>("Assets/Data/PowerUps/Magnet.asset").DisplayName);
        Assert.AreEqual("EMP Pulse", AssetDatabase.LoadAssetAtPath<PowerUpData>("Assets/Data/PowerUps/Freeze.asset").DisplayName);
        Assert.AreEqual("Quantum Echo", AssetDatabase.LoadAssetAtPath<PowerUpData>("Assets/Data/PowerUps/Echo.asset").DisplayName);
        Assert.AreEqual("Phase Shift", AssetDatabase.LoadAssetAtPath<PowerUpData>("Assets/Data/PowerUps/Teleport.asset").DisplayName);
        Assert.AreEqual("Kinetic Burst", AssetDatabase.LoadAssetAtPath<PowerUpData>("Assets/Data/PowerUps/Breaker.asset").DisplayName);
    }

    [Test]
    public void LevelDataValidator_PassesLevel001AndMechanicsSandboxWithZeroErrors()
    {
        foreach (string path in new[] { "Assets/Data/Levels/Level_001.asset", "Assets/Data/Levels/Level_MechanicsSandbox.asset" })
        {
            var ld = AssetDatabase.LoadAssetAtPath<LevelData>(path);
            Assert.IsNotNull(ld, $"Missing LevelData at {path}");

            var checks = LevelDataValidator.Validate(ld);
            foreach (var check in checks)
            {
                Assert.AreNotEqual(LevelDataValidator.CheckResult.Error, check.Result,
                    $"Validator error on {path} [{check.Label}]: {check.Message}");
            }
        }
    }
}
