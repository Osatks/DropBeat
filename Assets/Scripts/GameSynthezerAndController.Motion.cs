using System.Collections.Generic;
using UnityEngine;

namespace BeatDrop
{
    public sealed partial class GameSynthezerAndController
    {
        private void FixedUpdate()
        {
            if (ballBody == null)
            {
                return;
            }

            if (_jerkFlightActive)
            {
                UpdateActiveJerkFlight();
                return;
            }

            Vector3 pos = ballBody.position;
            Vector3 horizontalError = new Vector3(startPosition.x - pos.x, 0f, startPosition.z - pos.z);
            Vector3 horizontalVelocity = new Vector3(ballBody.linearVelocity.x, 0f, ballBody.linearVelocity.z);
            Vector3 accel = (horizontalError * centerSpring) - (horizontalVelocity * centerDamping);
            ballBody.AddForce(accel, ForceMode.Acceleration);

            if (horizontalError.magnitude > maxHorizontalOffset)
            {
                Vector3 clampedOffset = Vector3.ClampMagnitude(new Vector3(pos.x - startPosition.x, 0f, pos.z - startPosition.z), maxHorizontalOffset);
                Vector3 clampedPos = startPosition + clampedOffset;
                ballBody.position = new Vector3(clampedPos.x, pos.y, clampedPos.z);
                Vector3 v = ballBody.linearVelocity;
                v.x = 0f;
                v.z = 0f;
                ballBody.linearVelocity = v;
            }

            Vector3 vel = ballBody.linearVelocity;
            float verticalSpeedLimit = Mathf.Max(maxVerticalSpeed, _runtimeVerticalSpeedCap);
            if (vel.y > verticalSpeedLimit)
            {
                vel.y = verticalSpeedLimit;
                ballBody.linearVelocity = vel;
            }

            if (pos.y > maxHeight)
            {
                float over = pos.y - maxHeight;
                ballBody.AddForce(Vector3.down * (over * verticalBoundarySpring), ForceMode.Acceleration);
            }
            else if (pos.y < minHeight)
            {
                float under = minHeight - pos.y;
                ballBody.AddForce(Vector3.up * (under * verticalBoundarySpring), ForceMode.Acceleration);
            }
        }

        public void SetStartPose(Vector3 position)
        {
            startPosition = position;
            transform.position = position;
            if (ballBody != null)
            {
                CancelActiveJerkFlight();
                ballBody.position = position;
                ballBody.linearVelocity = Vector3.zero;
                ballBody.angularVelocity = Vector3.zero;
                if (clearTrailOnStartPose && ballTrail != null)
                {
                    ballTrail.Clear();
                }
            }
        }

        private void EnsureDefaultFlightConstraintsIfNeeded()
        {
            bool looksUninitialized =
                flightConstraints.maxInitialSpeed <= 0.2f &&
                flightConstraints.maxFinalSpeed <= 0.2f &&
                flightConstraints.maxTravelTimeSeconds <= 0.3f;

            if (looksUninitialized)
            {
                flightConstraints = BeatFlightConstraints.CreateDefault();
            }
        }

        private void EnsureJerkFinalVelocityFitsConstraints()
        {
            if (!useJerkAssistedFlight)
            {
                return;
            }

            float finalSpeed = jerkTargetFinalVelocity.magnitude;
            flightConstraints.minFinalSpeed = Mathf.Min(flightConstraints.minFinalSpeed, finalSpeed);
            flightConstraints.maxFinalSpeed = Mathf.Max(flightConstraints.maxFinalSpeed, finalSpeed + 0.05f);
            flightConstraints.Clamp();
        }

        private void SetupBallRigidbody()
        {
            if (ballBody == null)
            {
                ballBody = FindBestBallRigidbody();
            }

            if (ballBody == null && allowLocalRigidbodyFallback)
            {
                ballBody = gameObject.AddComponent<Rigidbody>();
            }

            if (ballBody == null)
            {
                Debug.LogWarning("[GameSynthezerAndController] Ball Rigidbody is not assigned/found. Assign the ballBody reference in Inspector.", this);
                return;
            }

            if (ballBody.GetComponent<SphereCollider>() == null)
            {
                ballBody.gameObject.AddComponent<SphereCollider>();
            }

            ballBody.useGravity = true;
            ballBody.linearDamping = 0.05f;
            ballBody.angularDamping = 0.12f;
            ballBody.collisionDetectionMode = CollisionDetectionMode.Continuous;
            ballBody.interpolation = RigidbodyInterpolation.Interpolate;

            ballBody.position = startPosition;
            transform.position = ballBody.position;
        }

        private Rigidbody FindBestBallRigidbody()
        {
            GameObject namedBall = GameObject.Find("MechanicBall");
            if (namedBall != null && namedBall.TryGetComponent(out Rigidbody namedRb))
            {
                return namedRb;
            }

            GameObject taggedPlayer = GameObject.FindWithTag("Player");
            if (taggedPlayer != null && taggedPlayer.TryGetComponent(out Rigidbody taggedRb))
            {
                return taggedRb;
            }

            Rigidbody[] allBodies =
#if UNITY_2022_2_OR_NEWER
                Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude);
#else
#pragma warning disable CS0618
                Object.FindObjectsOfType<Rigidbody>();
#pragma warning restore CS0618
#endif
            Rigidbody best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < allBodies.Length; i++)
            {
                Rigidbody rb = allBodies[i];
                if (rb == null || rb.isKinematic)
                {
                    continue;
                }

                string lowerName = rb.gameObject.name.ToLowerInvariant();
                float score = 0f;
                if (lowerName.Contains("ball"))
                {
                    score += 5f;
                }
                if (rb.GetComponent<SphereCollider>() != null)
                {
                    score += 2.5f;
                }
                score -= rb.mass * 0.05f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = rb;
                }
            }

            return best;
        }

        private BeatFlightPlan BuildEmergencyPlanToNextRing(float songTime)
        {
            BeatFlightPlan plan = new BeatFlightPlan
            {
                isValid = false,
                targetRingIndex = -1,
                reason = "No emergency target."
            };

            if (ballBody == null || !_rings.TryGetRing(_rings.CurrentIndex + 1, out Transform nextRing) || nextRing == null)
            {
                return plan;
            }

            float dt = 0.62f;
            IReadOnlyList<float> plannerHitTimes = GetPlannerRingHitTimes();
            if (plannerHitTimes.Count > _rings.CurrentIndex + 1)
            {
                dt = Mathf.Max(0.18f, plannerHitTimes[_rings.CurrentIndex + 1] - songTime);
            }

            dt = Mathf.Clamp(dt, 0.18f, Mathf.Max(0.35f, flightConstraints.maxTravelTimeSeconds));
            if (!BeatFlightPlanner.TrySolveBallistic(
                    ballBody.position,
                    nextRing.position,
                    dt,
                    Physics.gravity,
                    out Vector3 v0,
                    out Vector3 vf))
            {
                plan.reason = "Emergency ballistic solve failed.";
                return plan;
            }

            return new BeatFlightPlan
            {
                isValid = true,
                targetRingIndex = _rings.CurrentIndex + 1,
                timeToTargetSeconds = dt,
                requiredInitialVelocity = v0,
                expectedFinalVelocity = vf,
                initialSpeed = v0.magnitude,
                finalSpeed = vf.magnitude,
                score = 999f,
                reason = "emergency"
            };
        }

        private void ApplyFlightPlan(BeatFlightPlan plan, HitQuality quality)
        {
            if (ballBody == null)
            {
                return;
            }

            float qualityBlend = quality == HitQuality.Perfect
                ? perfectAssistBlend
                : (quality == HitQuality.Great ? greatAssistBlend : lateAssistBlend);
            qualityBlend = Mathf.Clamp(qualityBlend, minimumAssistBlend, 1f);

            Vector3 current = ballBody.linearVelocity;
            float appliedBlend = guaranteeNextRingReach ? 1f : qualityBlend;
            Vector3 desired = Vector3.Lerp(current, plan.requiredInitialVelocity, appliedBlend);
            ProtectSolvedVelocityFromClamp(desired);
            CancelActiveJerkFlight();

            if (forceSetVelocity)
            {
                ballBody.linearVelocity = desired;
            }
            else
            {
                Vector3 deltaV = desired - current;
                ballBody.AddForce(deltaV * ballBody.mass, ForceMode.Impulse);
            }

            if (logPlanner)
            {
                Debug.Log(
                    "[GameSynthezerAndController] PLAN -> ring#" + plan.targetRingIndex +
                    " dt=" + plan.timeToTargetSeconds.ToString("F3") +
                    " v0=" + plan.initialSpeed.ToString("F2") +
                    " vf=" + plan.finalSpeed.ToString("F2") +
                    " qualityBlend=" + qualityBlend.ToString("F2") +
                    " appliedBlend=" + appliedBlend.ToString("F2") +
                    " cap=" + Mathf.Max(maxVerticalSpeed, _runtimeVerticalSpeedCap).ToString("F2") +
                    " guarantee=" + guaranteeNextRingReach,
                    this);
            }
        }

        private void ApplyJerkFlightPlan(BeatJerkFlightPlan plan, HitQuality quality)
        {
            if (ballBody == null)
            {
                return;
            }

            float qualityBlend = quality == HitQuality.Perfect
                ? perfectAssistBlend
                : (quality == HitQuality.Great ? greatAssistBlend : lateAssistBlend);
            qualityBlend = Mathf.Clamp(qualityBlend, minimumAssistBlend, 1f);

            _activeJerkFlight = plan;
            _jerkFlightStartPosition = ballBody.position;
            _jerkFlightStartSongTime = GetSongOrGameplayTimeSeconds();
            _jerkFlightStartRealtime = Time.time;
            _jerkFlightActive = true;

            ProtectSolvedVelocityFromClamp(plan.launchVelocity);
            ballBody.linearVelocity = plan.launchVelocity;

            if (logPlanner)
            {
                Debug.Log(
                    "[GameSynthezerAndController] JERK PLAN -> ring#" + plan.targetRingIndex +
                    " dt=" + plan.timeToTargetSeconds.ToString("F3") +
                    " v0=" + plan.initialSpeed.ToString("F2") +
                    " vf=" + plan.finalSpeed.ToString("F3") +
                    " ai=" + plan.initialAccelerationMagnitude.ToString("F2") +
                    " jerk=" + plan.jerkMagnitude.ToString("F2") +
                    " qualityBlend=" + qualityBlend.ToString("F2") +
                    " finalAcc=" + plan.finalAcceleration.ToString("F2"),
                    this);
            }
        }

        private void UpdateActiveJerkFlight()
        {
            if (ballBody == null)
            {
                _jerkFlightActive = false;
                return;
            }

            if (_gameOver || _levelCompleted || !_activeJerkFlight.isValid)
            {
                _jerkFlightActive = false;
                return;
            }

            float elapsed = GetActiveJerkFlightElapsedSeconds();
            float duration = Mathf.Max(0.0001f, _activeJerkFlight.timeToTargetSeconds);
            bool arrived = elapsed >= duration - Mathf.Max(0f, jerkArrivalSnapSeconds);
            float t = arrived ? duration : Mathf.Clamp(elapsed, 0f, duration);

            Vector3 position = arrived
                ? _activeJerkFlight.targetPosition
                : _activeJerkFlight.EvaluatePosition(_jerkFlightStartPosition, t);
            Vector3 velocity = arrived
                ? _activeJerkFlight.targetFinalVelocity
                : _activeJerkFlight.EvaluateVelocity(t);

            ballBody.position = position;
            ballBody.linearVelocity = velocity;

            if (arrived)
            {
                float expected = GetExpectedHitTimeForRing(_activeJerkFlight.targetRingIndex);
                float now = GetSongOrGameplayTimeSeconds();
                float timingDelta = now - expected;
                float positionError = Vector3.Distance(ballBody.position, _activeJerkFlight.targetPosition);
                _lastArrivalMessage =
                    "Arrived ring=" + (_activeJerkFlight.targetRingIndex + 1) +
                    " dT=" + timingDelta.ToString("+#0.000;-#0.000;0.000") +
                    "s posErr=" + positionError.ToString("F3") +
                    " vf=" + ballBody.linearVelocity.magnitude.ToString("F3");
                if (logPlanner)
                {
                    Debug.Log("[GameSynthezerAndController] " + _lastArrivalMessage, this);
                }

                _jerkFlightActive = false;
            }
        }

        private float GetActiveJerkFlightElapsedSeconds()
        {
            float realtimeElapsed = Mathf.Max(0f, Time.time - _jerkFlightStartRealtime);
            if (beatSync != null && beatSync.IsRunning)
            {
                float songElapsed = GetSongOrGameplayTimeSeconds() - _jerkFlightStartSongTime;
                return Mathf.Max(0f, songElapsed);
            }

            return realtimeElapsed;
        }

        private void CancelActiveJerkFlight()
        {
            _jerkFlightActive = false;
            _activeJerkFlight = default;
        }

        private void ProtectSolvedVelocityFromClamp(Vector3 solvedVelocity)
        {
            if (!float.IsFinite(solvedVelocity.y))
            {
                return;
            }

            float neededCap = Mathf.Max(0f, solvedVelocity.y) + Mathf.Max(0f, solvedVerticalSpeedCapPadding);
            if (neededCap > _runtimeVerticalSpeedCap)
            {
                _runtimeVerticalSpeedCap = neededCap;
            }
        }
    }
}
