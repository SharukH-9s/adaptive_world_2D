using UnityEngine;

namespace AdaptiveWorld2D.AI
{
    /// <summary>
    /// Pure, deterministic enemy decision/combat calculations extracted from
    /// <see cref="EnemyPatrol"/> (Architecture Improvement Plan, Phase 2).
    ///
    /// This class must not reference GameObject, Transform, Rigidbody2D, Physics2D,
    /// singletons, or scene objects. All obstacle-avoidance raycasts, physics writes,
    /// and side effects (decals, colliders, mission callbacks, reinforcement broadcasts)
    /// remain the responsibility of <see cref="EnemyPatrol"/>.
    /// </summary>
    public static class EnemyDecisionModel
    {
        /// <summary>Boss health threshold (fraction of max health) below which retreat begins. Strictly less-than.</summary>
        public const float BossRetreatHealthFraction = 0.4f;

        /// <summary>Chase speed multiplier applied while a boss is retreating wounded.</summary>
        public const float WoundedBossChaseSpeedMultiplier = 0.85f;

        /// <summary>Sprite forward-facing correction applied to the raw direction angle.</summary>
        public const float LookAngleOffsetDegrees = -90f;

        public struct DamageResult
        {
            /// <summary>Damage after resistance mitigation, before the minimum-one-damage rule.
            /// This is the value the existing component logs.</summary>
            public int MitigatedDamage;

            /// <summary>Damage actually subtracted from health, after the minimum-one-damage rule.</summary>
            public int AppliedDamage;

            /// <summary>Health remaining after the hit. May be negative; not clamped to zero.</summary>
            public int NewHealth;

            /// <summary>True when the resulting health is at or below zero.</summary>
            public bool IsLethal;
        }

        /// <summary>
        /// Computes mitigated/applied damage and the resulting health, matching the existing
        /// <c>Mathf.RoundToInt</c> mitigation and minimum-one-damage rule in
        /// <c>EnemyPatrol.TakeDamage</c>.
        /// </summary>
        public static DamageResult ComputeDamage(int rawDamage, float damageResistance, int currentHealth)
        {
            int mitigated = Mathf.RoundToInt(rawDamage * (1.0f - damageResistance));
            int applied = Mathf.Max(1, mitigated);
            int newHealth = currentHealth - applied;

            return new DamageResult
            {
                MitigatedDamage = mitigated,
                AppliedDamage = applied,
                NewHealth = newHealth,
                IsLethal = newHealth <= 0
            };
        }

        /// <summary>
        /// True when a boss should be executing its tactical retreat, using the existing
        /// strict <c>&lt; 40%</c> threshold (not <c>&lt;=</c>).
        /// </summary>
        public static bool IsBossRetreating(bool isTargetBoss, int currentHealth, int maxHealth)
        {
            // Compare the exact 2/5 ratio to avoid float rounding at the 40% boundary.
            return isTargetBoss && (long)currentHealth * 5L < (long)maxHealth * 2L;
        }

        /// <summary>
        /// Selects the chase speed, applying the existing <c>0.85f</c> multiplier while the
        /// boss is retreating wounded.
        /// </summary>
        public static float ComputeChaseSpeed(float chaseSpeed, bool isBossRetreating)
        {
            return isBossRetreating ? chaseSpeed * WoundedBossChaseSpeedMultiplier : chaseSpeed;
        }

        /// <summary>
        /// Advances the waypoint index using the existing modulo-cycle behavior.
        /// <paramref name="waypointCount"/> must be greater than zero.
        /// </summary>
        public static int AdvanceWaypointIndex(int currentIndex, int waypointCount)
        {
            return (currentIndex + 1) % waypointCount;
        }

        /// <summary>
        /// Calculates the existing look angle (degrees) for a movement/steer direction,
        /// including the <c>-90f</c> sprite orientation offset.
        /// </summary>
        public static float ComputeLookAngleDegrees(Vector2 direction)
        {
            return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + LookAngleOffsetDegrees;
        }
    }
}
