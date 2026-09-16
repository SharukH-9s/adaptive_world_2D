namespace AdaptiveWorld2D.AI
{
    /// <summary>
    /// Explicit state representation for enemy AI (Architecture Improvement Plan, Phase 3).
    ///
    /// States mirror the previous implicit combination of <c>isAlerted</c>, <c>isDead</c>,
    /// the boss-retreat condition, and partial vision awareness in <see cref="EnemyPatrol"/>
    /// and <see cref="EnemyDetection"/>. No AI tuning values are changed.
    /// </summary>
    public enum EnemyState
    {
        /// <summary>Guard is following its waypoint route; no player contact and no awareness building.</summary>
        Patrolling,

        /// <summary>
        /// Guard has partial vision awareness (the awareness timer is accumulating inside
        /// <see cref="EnemyDetection"/> but has not yet crossed the alert threshold).
        /// The guard continues to patrol; it is not yet actively pursuing the player.
        /// </summary>
        Investigating,

        /// <summary>
        /// Guard is fully alerted but has no reference to the player (edge case: player
        /// object is absent from the scene). The guard continues to patrol its route.
        /// </summary>
        Alerted,

        /// <summary>Guard is fully alerted and actively pursuing the player.</summary>
        Chasing,

        /// <summary>
        /// Boss-type guard is wounded below the retreat threshold and is backing away
        /// from the player while calling sector reinforcements.
        /// </summary>
        Retreating,

        /// <summary>Guard is dead; no further AI processing occurs.</summary>
        Dead
    }
}
