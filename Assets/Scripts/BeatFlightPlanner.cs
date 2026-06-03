using System.Collections.Generic;
using UnityEngine;

namespace BeatDrop
{
    [System.Serializable]
    public struct BeatFlightConstraints
    {
        [Min(0.05f)] public float minTravelTimeSeconds;
        [Min(0.08f)] public float maxTravelTimeSeconds;
        [Min(0f)] public float minInitialSpeed;
        [Min(0f)] public float maxInitialSpeed;
        [Min(0f)] public float minFinalSpeed;
        [Min(0f)] public float maxFinalSpeed;
        [Min(1)] public int recoveryLookAheadRings;
        public bool allowRingSkipRecovery;

        public static BeatFlightConstraints CreateDefault()
        {
            return new BeatFlightConstraints
            {
                minTravelTimeSeconds = 0.16f,
                maxTravelTimeSeconds = 3.5f,
                minInitialSpeed = 0.1f,
                maxInitialSpeed = 24f,
                minFinalSpeed = 0f,
                maxFinalSpeed = 0.05f,
                recoveryLookAheadRings = 2,
                allowRingSkipRecovery = true
            };
        }

        public void Clamp()
        {
            minTravelTimeSeconds = Mathf.Max(0.05f, minTravelTimeSeconds);
            maxTravelTimeSeconds = Mathf.Max(minTravelTimeSeconds + 0.02f, maxTravelTimeSeconds);
            minInitialSpeed = Mathf.Max(0f, minInitialSpeed);
            maxInitialSpeed = Mathf.Max(minInitialSpeed + 0.05f, maxInitialSpeed);
            minFinalSpeed = Mathf.Max(0f, minFinalSpeed);
            maxFinalSpeed = Mathf.Max(minFinalSpeed + 0.05f, maxFinalSpeed);
            recoveryLookAheadRings = Mathf.Clamp(recoveryLookAheadRings, 1, 8);
        }
    }

    public struct BeatFlightPlan
    {
        public bool isValid;
        public int targetRingIndex;
        public float timeToTargetSeconds;
        public Vector3 requiredInitialVelocity;
        public Vector3 expectedFinalVelocity;
        public float initialSpeed;
        public float finalSpeed;
        public float score;
        public string reason;
    }

    /// <summary>
    /// Solves beat-locked projectile flights and selects the most stable candidate ring.
    /// </summary>
    public static class BeatFlightPlanner
    {
        public static BeatFlightPlan BuildPlan(
            Vector3 currentPosition,
            float songTimeSeconds,
            int currentRingIndex,
            RingSequenceRuntime rings,
            IReadOnlyList<float> ringHitTimes,
            BeatFlightConstraints constraints,
            Vector3 gravity)
        {
            constraints.Clamp();

            BeatFlightPlan best = new BeatFlightPlan
            {
                isValid = false,
                targetRingIndex = -1,
                score = float.MaxValue,
                reason = "No candidate ring."
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

                if (!TrySolveBallistic(
                    currentPosition,
                    targetRing.position,
                    dt,
                    gravity,
                    out Vector3 vInitial,
                    out Vector3 vFinal))
                {
                    continue;
                }

                float initialSpeed = vInitial.magnitude;
                float finalSpeed = vFinal.magnitude;
                if (initialSpeed < constraints.minInitialSpeed || initialSpeed > constraints.maxInitialSpeed)
                {
                    continue;
                }

                if (finalSpeed < constraints.minFinalSpeed || finalSpeed > constraints.maxFinalSpeed)
                {
                    continue;
                }

                float targetFinal = 0.5f * (constraints.minFinalSpeed + constraints.maxFinalSpeed);
                float finalError = Mathf.Abs(finalSpeed - targetFinal);
                float speedPressure = Mathf.InverseLerp(constraints.minInitialSpeed, constraints.maxInitialSpeed, initialSpeed);
                float skipPenalty = (candidateIndex - (currentRingIndex + 1)) * 0.25f;
                float score = finalError + speedPressure + skipPenalty;

                if (!best.isValid || score < best.score)
                {
                    best = new BeatFlightPlan
                    {
                        isValid = true,
                        targetRingIndex = candidateIndex,
                        timeToTargetSeconds = dt,
                        requiredInitialVelocity = vInitial,
                        expectedFinalVelocity = vFinal,
                        initialSpeed = initialSpeed,
                        finalSpeed = finalSpeed,
                        score = score,
                        reason = "ok"
                    };
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

                    if (TrySolveBallistic(
                        currentPosition,
                        fallbackRing.position,
                        fallbackDt,
                        gravity,
                        out Vector3 fallbackVInitial,
                        out Vector3 fallbackVFinal))
                    {
                        best = new BeatFlightPlan
                        {
                            isValid = true,
                            targetRingIndex = fallbackIndex,
                            timeToTargetSeconds = fallbackDt,
                            requiredInitialVelocity = fallbackVInitial,
                            expectedFinalVelocity = fallbackVFinal,
                            initialSpeed = fallbackVInitial.magnitude,
                            finalSpeed = fallbackVFinal.magnitude,
                            score = 999f,
                            reason = "fallback"
                        };
                    }
                }
            }

            return best;
        }

        public static bool TrySolveBallistic(
            Vector3 startPosition,
            Vector3 targetPosition,
            float timeToTargetSeconds,
            Vector3 gravity,
            out Vector3 requiredInitialVelocity,
            out Vector3 expectedFinalVelocity)
        {
            float dt = Mathf.Max(0.0001f, timeToTargetSeconds);
            requiredInitialVelocity = (targetPosition - startPosition - (0.5f * gravity * dt * dt)) / dt;
            expectedFinalVelocity = requiredInitialVelocity + (gravity * dt);
            return float.IsFinite(requiredInitialVelocity.x) &&
                   float.IsFinite(requiredInitialVelocity.y) &&
                   float.IsFinite(requiredInitialVelocity.z);
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
    }
}
