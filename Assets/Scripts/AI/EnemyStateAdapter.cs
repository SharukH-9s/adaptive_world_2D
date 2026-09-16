namespace AdaptiveWorld2D.AI
{
    /// <summary>
    /// Maps the legacy boolean fields on <see cref="EnemyPatrol"/> and the partial-awareness
    /// flag from <see cref="EnemyDetection"/> to an explicit <see cref="EnemyState"/> value
    /// (Architecture Improvement Plan, Phase 3).
    ///
    /// This is a pure adapter: it reads existing state and produces the equivalent enum value.
    /// No behavior is altered. All priority rules reproduce the exact branching order in
    /// <see cref="EnemyPatrol.FixedUpdate"/>.
    /// </summary>
    public static class EnemyStateAdapter
    {
        /// <summary>
        /// Resolves the current <see cref="EnemyState"/> from the existing boolean fields.
        /// </summary>
        /// <param name="isDead">Guard is dead (<c>EnemyPatrol.isDead</c>).</param>
        /// <param name="isAlerted">Guard is fully alerted (<c>EnemyPatrol.isAlerted</c>).</param>
        /// <param name="isBossRetreating">
        ///     Result of <see cref="EnemyDecisionModel.IsBossRetreating"/>; ignored when
        ///     <paramref name="isAlerted"/> is false.
        /// </param>
        /// <param name="isPartiallyAware">
        ///     <c>true</c> when the vision awareness timer inside <see cref="EnemyDetection"/>
        ///     is accumulating but has not yet crossed the alert threshold
        ///     (<see cref="EnemyDetection.IsPartiallyAware"/>).
        /// </param>
        /// <param name="hasPlayerTarget">
        ///     <c>true</c> when <see cref="EnemyPatrol"/> holds a non-null player transform
        ///     reference. Only relevant when <paramref name="isAlerted"/> is true.
        /// </param>
        /// <returns>The <see cref="EnemyState"/> that exactly mirrors the current boolean state.</returns>
        public static EnemyState Resolve(
            bool isDead,
            bool isAlerted,
            bool isBossRetreating,
            bool isPartiallyAware,
            bool hasPlayerTarget)
        {
            if (isDead)                              return EnemyState.Dead;
            if (isAlerted && isBossRetreating)       return EnemyState.Retreating;
            if (isAlerted && hasPlayerTarget)        return EnemyState.Chasing;
            if (isAlerted)                           return EnemyState.Alerted;
            if (isPartiallyAware)                    return EnemyState.Investigating;
            return EnemyState.Patrolling;
        }
    }
}
