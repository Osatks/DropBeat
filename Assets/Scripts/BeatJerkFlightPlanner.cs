using System.Collections.Generic;
using UnityEngine;

namespace BeatDrop
{
    public struct BeatJerkFlightPlan
    {
        public bool isValid;
        public int targetRingIndex;
        public float timeToTargetSeconds;
        public Vector3 targetPosition;
        public Vector3 launchVelocity;
        public Vector3 targetFinalVelocity;
        public Vector3 initialAcceleration;
        public Vector3 finalAcceleration;
        public Vector3 jerk;
        public float initialSpeed;
        public float finalSpeed;
        public float initialAccelerationMagnitude;
        public float jerkMagnitude;
        public float score;
        public string reason;

        public Vector3 EvaluatePosition(Vector3 startPosition, float elapsedSeconds)
        {
            float t = Mathf.Clamp(elapsedSeconds, 0f, Mathf.Max(0.0001f, timeToTargetSeconds));
            float t2 = t * t;
            float t3 = t2 * t;
            return startPosition +
                   (launchVelocity * t) +
                   (0.5f * initialAcceleration * t2) +
                   ((jerk * t3) / 6f);
        }

        public Vector3 EvaluateVelocity(float elapsedSeconds)
        {
            float t = Mathf.Clamp(elapsedSeconds, 0f, Mathf.Max(0.0001f, timeToTargetSeconds));
            return launchVelocity +
                   (initialAcceleration * t) +
                   (0.5f * jerk * t * t);
        }

        public Vector3 EvaluateAcceleration(float elapsedSeconds)
        {
            float t = Mathf.Clamp(elapsedSeconds, 0f, Mathf.Max(0.0001f, timeToTargetSeconds));
            return initialAcceleration + (jerk * t);
        }
    }

    /// <summary>
    /// Constant-jerk flight solve:
    /// p(T) = target, v(T) = requested final velocity, a(T) = finalAcceleration.
    /// This gives the ring enough freedom to arrive on the beat with near-zero Vf.
    /// </summary>
    public static class BeatJerkFlightPlanner
    {
        public static BeatJerkFlightPlan BuildPlan(
            Vector3 currentPosition,
            float songTimeSeconds,
            int currentRingIndex,
            RingSequenceRuntime rings,
            IReadOnlyList<float> ringHitTimes,
            BeatFlightConstraints constraints,
            Vector3 finalAcceleration,
            Vector3 targetFinalVelocity,
            float maxInitialAccelerationMagnitude,
            float maxJerkMagnitude)
        {
            constraints.Clamp();

            BeatJerkFlightPlan best = new BeatJerkFlightPlan
            {
                isValid = false,
                targetRingIndex = -1,
                score = float.MaxValue,
                reason = "No jerk candidate ring."
            };

            if (rings == null || rings.Count <= 0 || currentRingIndex >= rings.Count - 1)
            {
                best.reason = "No next ring.";
                return best;
            }

            int maxLookAhead = constraints.allowRingSkipRecovery ? constraints.recoveryLookAheadRings : 1;
            int lastRingIndex = rings.Count - 1;
            int maxCandidateIndex = Mathf.Min(lastRingIndex, currentRingIndex + Mathf.Max(1, maxLookAhead));

            for (int candidateIndex = currentRingIndex + 1; candidateIndex <= maxCandidateIndex; candidateIndex++)
            {
                if (!rings.TryGetRing(candidateIndex, out Transform targetRing) || targetRing == null)
                {
                    continue;
                }

                float dt = GetTimeToTarget(songTimeSeconds, currentRingIndex, candidateIndex, ringHitTimes, constraints);
                if (dt < constraints.minTravelTimeSeconds || dt > constraints.maxTravelTimeSeconds)
                {
                    continue;
                }

                BeatJerkFlightPlan candidate = BuildCandidate(
                    currentPosition,
                    targetRing.position,
                    dt,
                    candidateIndex,
                    targetFinalVelocity,
                    finalAcceleration,
                    constraints,
                    maxInitialAccelerationMagnitude,
                    maxJerkMagnitude,
                    "ok");

                if (!candidate.isValid)
                {
                    continue;
                }

                float skipPenalty = (candidateIndex - (currentRingIndex + 1)) * 0.25f;
                candidate.score += skipPenalty;
                if (!best.isValid || candidate.score < best.score)
                {
                    best = candidate;
                }
            }

            if (!best.isValid)
            {
                int fallbackIndex = Mathf.Min(lastRingIndex, currentRingIndex + 1);
                if (rings.TryGetRing(fallbackIndex, out Transform fallbackRing) && fallbackRing != null)
                {
                    float fallbackDt = Mathf.Clamp(
                        GetTimeToTarget(songTimeSeconds, currentRingIndex, fallbackIndex, ringHitTimes, constraints),
                        constraints.minTravelTimeSeconds,
                        constraints.maxTravelTimeSeconds);

                    best = BuildCandidate(
                        currentPosition,
                        fallbackRing.position,
                        fallbackDt,
                        fallbackIndex,
                        targetFinalVelocity,
                        finalAcceleration,
                        constraints,
                        maxInitialAccelerationMagnitude: 0f,
                        maxJerkMagnitude: 0f,
                        reason: "fallback");
                }
            }

            return best;
        }

        public static bool TrySolveFinalAccelerationJerk(
            Vector3 startPosition,
            Vector3 targetPosition,
            float timeToTargetSeconds,
            Vector3 targetFinalVelocity,
            Vector3 finalAcceleration,
            out Vector3 launchVelocity,
            out Vector3 initialAcceleration,
            out Vector3 jerk)
        {
            float dt = Mathf.Max(0.0001f, timeToTargetSeconds);
            float dt2 = dt * dt;
            float dt3 = dt2 * dt;
            Vector3 delta = targetPosition - startPosition;

            jerk = 6f * (delta - (targetFinalVelocity * dt) + (0.5f * finalAcceleration * dt2)) / dt3;
            launchVelocity = targetFinalVelocity - (finalAcceleration * dt) + (0.5f * jerk * dt2);
            initialAcceleration = finalAcceleration - (jerk * dt);

            return IsFinite(launchVelocity) &&
                   IsFinite(initialAcceleration) &&
                   IsFinite(jerk);
        }

        private static BeatJerkFlightPlan BuildCandidate(
            Vector3 currentPosition,
            Vector3 targetPosition,
            float dt,
            int targetRingIndex,
            Vector3 targetFinalVelocity,
            Vector3 finalAcceleration,
            BeatFlightConstraints constraints,
            float maxInitialAccelerationMagnitude,
            float maxJerkMagnitude,
            string reason)
        {
            if (!TrySolveFinalAccelerationJerk(
                    currentPosition,
                    targetPosition,
                    dt,
                    targetFinalVelocity,
                    finalAcceleration,
                    out Vector3 launchVelocity,
                    out Vector3 initialAcceleration,
                    out Vector3 jerk))
            {
                return new BeatJerkFlightPlan
                {
                    isValid = false,
                    targetRingIndex = targetRingIndex,
                    reason = "Jerk solve failed."
                };
            }

            float initialSpeed = launchVelocity.magnitude;
            float finalSpeed = targetFinalVelocity.magnitude;
            float initialAccelerationMagnitude = initialAcceleration.magnitude;
            float jerkMagnitude = jerk.magnitude;

            if (initialSpeed < constraints.minInitialSpeed || initialSpeed > constraints.maxInitialSpeed)
            {
                return new BeatJerkFlightPlan
                {
                    isValid = false,
                    targetRingIndex = targetRingIndex,
                    reason = "Launch speed outside range."
                };
            }

            if (finalSpeed < constraints.minFinalSpeed || finalSpeed > constraints.maxFinalSpeed)
            {
                return new BeatJerkFlightPlan
                {
                    isValid = false,
                    targetRingIndex = targetRingIndex,
                    reason = "Final speed outside range."
                };
            }

            if (maxInitialAccelerationMagnitude > 0f && initialAccelerationMagnitude > maxInitialAccelerationMagnitude)
            {
                return new BeatJerkFlightPlan
                {
                    isValid = false,
                    targetRingIndex = targetRingIndex,
                    reason = "Initial acceleration too high."
                };
            }

            if (maxJerkMagnitude > 0f && jerkMagnitude > maxJerkMagnitude)
            {
                return new BeatJerkFlightPlan
                {
                    isValid = false,
                    targetRingIndex = targetRingIndex,
                    reason = "Jerk too high."
                };
            }

            float targetFinalSpeed = 0.5f * (constraints.minFinalSpeed + constraints.maxFinalSpeed);
            float finalError = Mathf.Abs(finalSpeed - targetFinalSpeed);
            float launchPressure = Mathf.InverseLerp(constraints.minInitialSpeed, constraints.maxInitialSpeed, initialSpeed);
            float accelPressure = maxInitialAccelerationMagnitude > 0f
                ? Mathf.Clamp01(initialAccelerationMagnitude / maxInitialAccelerationMagnitude)
                : 0f;
            float jerkPressure = maxJerkMagnitude > 0f
                ? Mathf.Clamp01(jerkMagnitude / maxJerkMagnitude)
                : 0f;

            return new BeatJerkFlightPlan
            {
                isValid = true,
                targetRingIndex = targetRingIndex,
                timeToTargetSeconds = dt,
                targetPosition = targetPosition,
                launchVelocity = launchVelocity,
                targetFinalVelocity = targetFinalVelocity,
                initialAcceleration = initialAcceleration,
                finalAcceleration = finalAcceleration,
                jerk = jerk,
                initialSpeed = initialSpeed,
                finalSpeed = finalSpeed,
                initialAccelerationMagnitude = initialAccelerationMagnitude,
                jerkMagnitude = jerkMagnitude,
                score = finalError + (launchPressure * 0.55f) + (accelPressure * 0.35f) + (jerkPressure * 0.25f),
                reason = reason
            };
        }

        private static float GetTimeToTarget(
            float songTimeSeconds,
            int currentRingIndex,
            int targetRingIndex,
            IReadOnlyList<float> ringHitTimes,
            BeatFlightConstraints constraints)
        {
            if (ringHitTimes != null && ringHitTimes.Count > 0)
            {
                int clampedCurrent = Mathf.Clamp(currentRingIndex, 0, ringHitTimes.Count - 1);
                int clampedTarget = Mathf.Clamp(targetRingIndex, 0, ringHitTimes.Count - 1);
                if (clampedTarget > clampedCurrent)
                {
                    float absoluteTargetTime = ringHitTimes[clampedTarget];
                    float remaining = absoluteTargetTime - songTimeSeconds;
                    if (remaining > 0.01f)
                    {
                        return remaining;
                    }
                }
            }

            return Mathf.Clamp(0.6f, constraints.minTravelTimeSeconds, constraints.maxTravelTimeSeconds);
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) &&
                   float.IsFinite(value.y) &&
                   float.IsFinite(value.z);
        }
    }
}
