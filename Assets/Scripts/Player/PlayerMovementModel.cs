using UnityEngine;

namespace AdaptiveWorld2D.Player
{
    /// <summary>
    /// Pure, deterministic movement calculations extracted from
    /// <see cref="PlayerController"/> (Architecture Improvement Plan, Phase 2).
    ///
    /// This class must not reference GameObject, Transform, Rigidbody2D, Physics2D,
    /// singletons, scene objects, or Unity time. All timing and distances are supplied
    /// as explicit parameters so the calculations remain EditMode-testable.
    /// </summary>
    public static class PlayerMovementModel
    {
        /// <summary>Movement input magnitude threshold below which the sprite does not rotate.</summary>
        public const float RotationThreshold = 0.05f;

        /// <summary>Sprite forward-facing correction applied to the raw input angle.</summary>
        public const float FacingAngleOffsetDegrees = -90f;

        /// <summary>
        /// Selects the effective movement speed. Driving takes precedence over sprinting,
        /// matching the existing <c>PlayerController.MoveCharacter</c>/OnGUI speed selection.
        /// </summary>
        public static float SelectSpeed(bool isDriving, bool isSprinting, float walkSpeed, float runSpeed, float driveSpeed)
        {
            return isDriving ? driveSpeed : (isSprinting ? runSpeed : walkSpeed);
        }

        /// <summary>
        /// Calculates velocity from the already-normalized input direction and effective speed.
        /// </summary>
        public static Vector2 ComputeVelocity(Vector2 normalizedInput, float speed)
        {
            return normalizedInput * speed;
        }

        /// <summary>
        /// Calculates the kinematic displacement for one fixed-update step, matching the
        /// existing <c>Rigidbody2D.MovePosition</c> and positional-fallback formulas.
        /// </summary>
        public static Vector2 ComputeKinematicDisplacement(Vector2 currentPosition, Vector2 normalizedInput, float speed, float fixedDeltaTime)
        {
            return currentPosition + normalizedInput * speed * fixedDeltaTime;
        }

        /// <summary>
        /// True when the input magnitude is large enough to trigger a sprite rotation,
        /// matching the existing <c>0.05f</c> threshold in <c>HandleInput</c>.
        /// </summary>
        public static bool ShouldRotate(Vector2 normalizedInput)
        {
            return normalizedInput.magnitude > RotationThreshold;
        }

        /// <summary>
        /// Calculates the existing facing angle (degrees), including the <c>-90f</c> sprite offset.
        /// </summary>
        public static float ComputeFacingAngleDegrees(Vector2 normalizedInput)
        {
            return Mathf.Atan2(normalizedInput.y, normalizedInput.x) * Mathf.Rad2Deg + FacingAngleOffsetDegrees;
        }

        /// <summary>
        /// Determines the current movement type label used for telemetry reporting.
        /// </summary>
        public static string DetermineMovementType(bool isDriving, bool isSprinting)
        {
            return isDriving ? "DRIVE" : (isSprinting ? "RUN" : "WALK");
        }

        /// <summary>
        /// Calculates average speed from accumulated distance and the actual elapsed interval.
        /// </summary>
        public static float ComputeAverageSpeed(float accumulatedDistance, float interval)
        {
            return accumulatedDistance / interval;
        }
    }
}
