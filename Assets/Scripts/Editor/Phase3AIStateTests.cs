#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEditor;
using AdaptiveWorld2D.AI;

namespace AdaptiveWorld2D.EditorTests
{
    /// <summary>
    /// In-Editor characterization test suite for Phase 3 (Architecture Improvement Plan).
    /// Validates <see cref="EnemyState"/>, <see cref="EnemyStateAdapter"/>, and the
    /// interaction with <see cref="EnemyDecisionModel"/> against the ten validation
    /// scenarios specified in the plan.
    ///
    /// All tests exercise pure functions only; no MonoBehaviour, scene, or Rigidbody2D
    /// is required.
    ///
    /// Run via Menu: AdaptiveWorld > Run Phase 3 AI State Tests
    /// </summary>
    public static class Phase3AIStateTests
    {
        [MenuItem("AdaptiveWorld/Run Phase 3 AI State Tests")]
        public static void RunAllPhase3Tests()
        {
            int passed = 0;
            int total = 0;

            void RunTest(string testName, Action testAction)
            {
                total++;
                try
                {
                    testAction();
                    passed++;
                    Debug.Log($"[PASS] {testName}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FAIL] {testName}: {ex.Message}\n{ex.StackTrace}");
                }
            }

            Debug.Log("==================================================================");
            Debug.Log("  ADAPTIVE WORLD 2D - PHASE 3 AI STATE TEST SUITE");
            Debug.Log("==================================================================");

            // ── EnemyStateAdapter: state mapping ───────────────────────────────
            RunTest("Scenario 1: Enemy patrols with no player present → Patrolling",
                TestScenario1_PatrollingNoPlayer);

            RunTest("Scenario 2: Player enters vision cone (awareness accumulating) → Investigating",
                TestScenario2_InvestigatingVisionCone);

            RunTest("Scenario 3: Player leaves vision cone before alert threshold → Patrolling",
                TestScenario3_AwarenessDecaysToPatrolling);

            RunTest("Scenario 4: Enemy hears a noise (isAlerted set, has target) → Chasing",
                TestScenario4_AlertedByNoise);

            RunTest("Scenario 5: Enemy becomes alerted, has player target → Chasing",
                TestScenario5_AlertedWithTarget);

            RunTest("Scenario 5b: Enemy alerted but no player object in scene → Alerted",
                TestScenario5b_AlertedNoTarget);

            RunTest("Scenario 6: Boss falls below 40% health → IsBossRetreating is true",
                TestScenario6_BossBelowRetreatThreshold);

            RunTest("Scenario 6b: Boss at exactly 40% health → NOT retreating",
                TestScenario6b_BossAtExactly40Percent);

            RunTest("Scenario 7: Boss retreating with player target → Retreating",
                TestScenario7_BossRetreating);

            RunTest("Scenario 8: Enemy dies → Dead",
                TestScenario8_EnemyDies);

            RunTest("Scenario 9: Dead enemy receives another hit → remains Dead",
                TestScenario9_DeadEnemyHit);

            RunTest("Scenario 10: Dead state overrides all other flags",
                TestScenario10_DeadOverridesAlerted);

            // ── Priority ordering ──────────────────────────────────────────────
            RunTest("Priority: Dead takes precedence over Retreating",
                TestPriority_DeadBeforeRetreating);

            RunTest("Priority: Retreating takes precedence over Chasing",
                TestPriority_RetreatingBeforeChasing);

            RunTest("Priority: Chasing takes precedence over Investigating",
                TestPriority_ChasingBeforeInvestigating);

            // ── Preserved AI values (no change from Phase 2 constants) ─────────
            RunTest("Preserved: BossRetreatHealthFraction is 0.4",
                TestPreserved_BossRetreatFraction);

            RunTest("Preserved: WoundedBossChaseSpeedMultiplier is 0.85",
                TestPreserved_WoundedBossSpeedMultiplier);

            RunTest("Preserved: LookAngleOffsetDegrees is -90",
                TestPreserved_LookAngleOffset);

            Debug.Log("------------------------------------------------------------------");
            if (passed == total)
            {
                Debug.Log($"  ALL {passed}/{total} PHASE 3 AI STATE TESTS PASSED!");
            }
            else
            {
                Debug.LogError($"  PHASE 3 AI STATE SUITE FAILED: {passed}/{total} passed.");
            }
            Debug.Log("==================================================================");
        }

        // ── Scenario tests ──────────────────────────────────────────────────────

        // Scenario 1: not alerted, not dead, no awareness → Patrolling
        private static void TestScenario1_PatrollingNoPlayer()
        {
            EnemyState state = EnemyStateAdapter.Resolve(
                isDead: false,
                isAlerted: false,
                isBossRetreating: false,
                isPartiallyAware: false,
                hasPlayerTarget: false);

            if (state != EnemyState.Patrolling)
                throw new Exception($"Expected Patrolling, got {state}.");
        }

        // Scenario 2: vision awareness accumulating (IsPartiallyAware=true), not yet alerted
        private static void TestScenario2_InvestigatingVisionCone()
        {
            EnemyState state = EnemyStateAdapter.Resolve(
                isDead: false,
                isAlerted: false,
                isBossRetreating: false,
                isPartiallyAware: true,
                hasPlayerTarget: true);

            if (state != EnemyState.Investigating)
                throw new Exception($"Expected Investigating, got {state}.");
        }

        // Scenario 3: awareness decayed back to zero (player left FOV) → Patrolling
        private static void TestScenario3_AwarenessDecaysToPatrolling()
        {
            // After decay, isPartiallyAware = false, isAlerted = false
            EnemyState state = EnemyStateAdapter.Resolve(
                isDead: false,
                isAlerted: false,
                isBossRetreating: false,
                isPartiallyAware: false,
                hasPlayerTarget: true);

            if (state != EnemyState.Patrolling)
                throw new Exception($"Expected Patrolling after awareness decay, got {state}.");
        }

        // Scenario 4: AlertToNoise sets isAlerted=true immediately, guard has player target
        private static void TestScenario4_AlertedByNoise()
        {
            EnemyState state = EnemyStateAdapter.Resolve(
                isDead: false,
                isAlerted: true,
                isBossRetreating: false,
                isPartiallyAware: false,
                hasPlayerTarget: true);

            if (state != EnemyState.Chasing)
                throw new Exception($"Expected Chasing after noise alert, got {state}.");
        }

        // Scenario 5: fully alerted, has player target → Chasing
        private static void TestScenario5_AlertedWithTarget()
        {
            EnemyState state = EnemyStateAdapter.Resolve(
                isDead: false,
                isAlerted: true,
                isBossRetreating: false,
                isPartiallyAware: false,
                hasPlayerTarget: true);

            if (state != EnemyState.Chasing)
                throw new Exception($"Expected Chasing, got {state}.");
        }

        // Scenario 5b: alerted but no player in scene (hasPlayerTarget=false) → Alerted
        private static void TestScenario5b_AlertedNoTarget()
        {
            EnemyState state = EnemyStateAdapter.Resolve(
                isDead: false,
                isAlerted: true,
                isBossRetreating: false,
                isPartiallyAware: false,
                hasPlayerTarget: false);

            if (state != EnemyState.Alerted)
                throw new Exception($"Expected Alerted (no player object), got {state}.");
        }

        // Scenario 6: boss at 23/60 HP → IsBossRetreating returns true
        private static void TestScenario6_BossBelowRetreatThreshold()
        {
            // 23/60 ≈ 38.3% < 40%
            bool retreating = EnemyDecisionModel.IsBossRetreating(isTargetBoss: true, currentHealth: 23, maxHealth: 60);
            if (!retreating)
                throw new Exception("Expected IsBossRetreating=true at 23/60 HP.");
        }

        // Scenario 6b: boss at exactly 40% HP → NOT retreating (strict < 40%)
        private static void TestScenario6b_BossAtExactly40Percent()
        {
            // 24/60 = exactly 40%
            bool retreating = EnemyDecisionModel.IsBossRetreating(isTargetBoss: true, currentHealth: 24, maxHealth: 60);
            if (retreating)
                throw new Exception("Expected IsBossRetreating=false at exactly 40% HP (strict <, not <=).");
        }

        // Scenario 7: boss alerted, retreating, has player target → Retreating
        private static void TestScenario7_BossRetreating()
        {
            bool isBossRetreating = EnemyDecisionModel.IsBossRetreating(isTargetBoss: true, currentHealth: 20, maxHealth: 60);

            EnemyState state = EnemyStateAdapter.Resolve(
                isDead: false,
                isAlerted: true,
                isBossRetreating: isBossRetreating,
                isPartiallyAware: false,
                hasPlayerTarget: true);

            if (state != EnemyState.Retreating)
                throw new Exception($"Expected Retreating for wounded boss, got {state}.");
        }

        // Scenario 8: enemy dies → Dead
        private static void TestScenario8_EnemyDies()
        {
            // Lethal hit: 100 damage, 0 resistance, 50 HP → NewHealth = -50
            var result = EnemyDecisionModel.ComputeDamage(rawDamage: 100, damageResistance: 0f, currentHealth: 50);
            if (!result.IsLethal)
                throw new Exception("Expected IsLethal=true for fatal damage.");

            EnemyState state = EnemyStateAdapter.Resolve(
                isDead: true,
                isAlerted: false,
                isBossRetreating: false,
                isPartiallyAware: false,
                hasPlayerTarget: false);

            if (state != EnemyState.Dead)
                throw new Exception($"Expected Dead after lethal damage, got {state}.");
        }

        // Scenario 9: dead enemy hit again → still lethal (already dead), state stays Dead
        private static void TestScenario9_DeadEnemyHit()
        {
            // Current HP is 0 or below; another hit would only make it more negative.
            // EnemyPatrol.TakeDamage returns early when isDead=true, so health doesn't change.
            // We verify that Dead state persists regardless of other flags.
            EnemyState state = EnemyStateAdapter.Resolve(
                isDead: true,
                isAlerted: true,
                isBossRetreating: true,
                isPartiallyAware: true,
                hasPlayerTarget: true);

            if (state != EnemyState.Dead)
                throw new Exception($"Expected Dead state to persist on second hit, got {state}.");
        }

        // Scenario 10: Dead overrides alerted (sector alert reached the dead guard)
        private static void TestScenario10_DeadOverridesAlerted()
        {
            // A dead guard that receives a BroadcastAlert would have isAlerted=true set by
            // Alert() — but Alert() checks isDead early and returns, so isDead and isAlerted
            // cannot both be true in practice. We still verify the adapter priority here.
            EnemyState state = EnemyStateAdapter.Resolve(
                isDead: true,
                isAlerted: true,
                isBossRetreating: false,
                isPartiallyAware: false,
                hasPlayerTarget: true);

            if (state != EnemyState.Dead)
                throw new Exception($"Expected Dead to override Alerted in adapter priority, got {state}.");
        }

        // ── Priority ordering tests ─────────────────────────────────────────────

        private static void TestPriority_DeadBeforeRetreating()
        {
            EnemyState state = EnemyStateAdapter.Resolve(
                isDead: true,
                isAlerted: true,
                isBossRetreating: true,
                isPartiallyAware: false,
                hasPlayerTarget: true);

            if (state != EnemyState.Dead)
                throw new Exception($"Dead should take priority over Retreating, got {state}.");
        }

        private static void TestPriority_RetreatingBeforeChasing()
        {
            EnemyState state = EnemyStateAdapter.Resolve(
                isDead: false,
                isAlerted: true,
                isBossRetreating: true,
                isPartiallyAware: false,
                hasPlayerTarget: true);

            if (state != EnemyState.Retreating)
                throw new Exception($"Retreating should take priority over Chasing, got {state}.");
        }

        private static void TestPriority_ChasingBeforeInvestigating()
        {
            EnemyState state = EnemyStateAdapter.Resolve(
                isDead: false,
                isAlerted: true,
                isBossRetreating: false,
                isPartiallyAware: true,
                hasPlayerTarget: true);

            if (state != EnemyState.Chasing)
                throw new Exception($"Chasing should take priority over Investigating, got {state}.");
        }

        // ── Preserved AI values ────────────────────────────────────────────────

        private static void TestPreserved_BossRetreatFraction()
        {
            if (!Mathf.Approximately(EnemyDecisionModel.BossRetreatHealthFraction, 0.4f))
                throw new Exception($"BossRetreatHealthFraction changed from 0.4 to {EnemyDecisionModel.BossRetreatHealthFraction}.");
        }

        private static void TestPreserved_WoundedBossSpeedMultiplier()
        {
            if (!Mathf.Approximately(EnemyDecisionModel.WoundedBossChaseSpeedMultiplier, 0.85f))
                throw new Exception($"WoundedBossChaseSpeedMultiplier changed from 0.85 to {EnemyDecisionModel.WoundedBossChaseSpeedMultiplier}.");
        }

        private static void TestPreserved_LookAngleOffset()
        {
            if (!Mathf.Approximately(EnemyDecisionModel.LookAngleOffsetDegrees, -90f))
                throw new Exception($"LookAngleOffsetDegrees changed from -90 to {EnemyDecisionModel.LookAngleOffsetDegrees}.");
        }
    }
}
#endif
