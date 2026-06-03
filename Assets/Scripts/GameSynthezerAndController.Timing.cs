using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BeatDrop
{
    public sealed partial class GameSynthezerAndController
    {
        private static bool IsHitPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                return true;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                return true;
            }

            if (Touchscreen.current != null)
            {
                var touches = Touchscreen.current.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    if (touches[i].press.wasPressedThisFrame)
                    {
                        return true;
                    }
                }
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0))
            {
                return true;
            }

            for (int i = 0; i < Input.touchCount; i++)
            {
                if (Input.GetTouch(i).phase == TouchPhase.Began)
                {
                    return true;
                }
            }
#endif
            return false;
        }

        private bool TryRunAutoplayer()
        {
            if (!enableAutoplayer || _jerkFlightActive || ballBody == null || targetRingCenter == null || _rings.Count <= 0)
            {
                return false;
            }

            int ringIndex = _rings.CurrentIndex;
            if (ringIndex == _lastAutoplayerRingIndex)
            {
                return false;
            }

            float expected = GetExpectedHitTimeForRing(ringIndex);
            float now = GetSongOrGameplayTimeSeconds();
            float delta = expected - now;
            bool warmupRing = expected < 0f && (beatSync == null || !beatSync.IsRunning);
            bool shouldHit = warmupRing
                ? Time.time - _gameplayStartTime >= Mathf.Max(0f, autoplayerWarmupDelaySeconds)
                : delta <= Mathf.Max(0f, autoplayerHitLeadSeconds) &&
                  delta >= -Mathf.Max(0f, autoplayerLateToleranceSeconds);

            if (!shouldHit)
            {
                return false;
            }

            _lastAutoplayerRingIndex = ringIndex;
            _lastAutoplayerSongTime = now;
            if (logAutoplayer)
            {
                Debug.Log(
                    "[GameSynthezerAndController] AUTOPLAYER hit ring#" + (ringIndex + 1) +
                    " expected=" + expected.ToString("F3") +
                    " now=" + now.ToString("F3") +
                    " delta=" + delta.ToString("+#0.000;-#0.000;0.000") +
                    " forcePerfect=" + autoplayerForcePerfectHits,
                    this);
            }

            HandleHitInput(autoplayerForcePerfectHits, "AUTO");
            return true;
        }

        private void HandleHitInput(bool forcePerfectHit = false, string inputSource = "PLAYER")
        {
            if ((targetRingCenter == null || _rings.Count <= 0) && Time.time >= _nextRingResolveRetryTime)
            {
                ReinitializeLevelFromRingChain();
                _nextRingResolveRetryTime = Time.time + 0.4f;
            }

            if (ballBody == null || targetRingCenter == null || _rings.Count <= 0)
            {
                return;
            }

            bool insideRingRegion = IsInsideCurrentRingRegion();
            if (!forcePerfectHit && requireInputInsideRingRegion && !insideRingRegion)
            {
                if (applyFallbackImpulseOnMiss)
                {
                    ballBody.AddForce(Vector3.up * Mathf.Max(0f, fallbackMissImpulse), ForceMode.Impulse);
                    _lastJudgeMessage = "Off region: fallback impulse.";
                }
                else
                {
                    _lastJudgeMessage = "Input ignored: outside target ring region.";
                }
                return;
            }

            HitQuality quality;
            float quality01;
            string message;
            if (forcePerfectHit)
            {
                quality = HitQuality.Perfect;
                quality01 = 1f;
                float expected = GetExpectedHitTimeForRing(_rings.CurrentIndex);
                float now = GetSongOrGameplayTimeSeconds();
                message = inputSource + " PERFECT" +
                          " | ring=" + (_rings.CurrentIndex + 1) +
                          " | tErr=" + Mathf.Abs(now - expected).ToString("F3") + "s" +
                          " | forced";
            }
            else
            {
                quality = EvaluateHitQuality(out quality01, out message);
            }

            _lastJudgeMessage = message;
            if (quality == HitQuality.Miss)
            {
                if (applyFallbackImpulseOnMiss)
                {
                    float impulse = Mathf.Max(0f, fallbackMissImpulse) * (insideRingRegion ? 1f : 0.85f);
                    ballBody.AddForce(Vector3.up * impulse, ForceMode.Impulse);
                }

                if (logPlanner)
                {
                    Debug.Log("[GameSynthezerAndController] MISS: " + message, this);
                }
                return;
            }

            TriggerHitFx(quality, quality01);

            int previousRingIndex = _rings.CurrentIndex;
            if (_rings.CurrentIndex >= _rings.Count - 1)
            {
                _levelCompleted = true;
                RefreshTargetRingState(false, previousRingIndex);
                if (logPlanner)
                {
                    Debug.Log("[GameSynthezerAndController] Level complete.", this);
                }
                return;
            }

            float songTime = GetSongOrGameplayTimeSeconds();
            int plannedTargetIndex = -1;
            bool appliedFlightPlan = false;

            if (useJerkAssistedFlight)
            {
                IReadOnlyList<float> plannerHitTimes = GetPlannerRingHitTimes();
                BeatJerkFlightPlan jerkPlan = BeatJerkFlightPlanner.BuildPlan(
                    ballBody.position,
                    songTime,
                    _rings.CurrentIndex,
                    _rings,
                    plannerHitTimes,
                    flightConstraints,
                    Physics.gravity,
                    jerkTargetFinalVelocity,
                    maxJerkInitialAcceleration,
                    maxJerkMagnitude);

                if (jerkPlan.isValid)
                {
                    ApplyJerkFlightPlan(jerkPlan, quality);
                    plannedTargetIndex = jerkPlan.targetRingIndex;
                    appliedFlightPlan = true;
                }
                else if (logPlanner)
                {
                    Debug.Log("[GameSynthezerAndController] No valid jerk plan, trying ballistic fallback: " + jerkPlan.reason, this);
                }
            }

            if (!appliedFlightPlan)
            {
                IReadOnlyList<float> plannerHitTimes = GetPlannerRingHitTimes();
                BeatFlightPlan plan = BeatFlightPlanner.BuildPlan(
                    ballBody.position,
                    songTime,
                    _rings.CurrentIndex,
                    _rings,
                    plannerHitTimes,
                    flightConstraints,
                    Physics.gravity);

                if (!plan.isValid)
                {
                    plan = BuildEmergencyPlanToNextRing(songTime);
                }

                if (plan.isValid)
                {
                    ApplyFlightPlan(plan, quality);
                    plannedTargetIndex = plan.targetRingIndex;
                    appliedFlightPlan = true;
                }
                else
                {
                    if (applyFallbackImpulseOnMiss)
                    {
                        ballBody.AddForce(Vector3.up * Mathf.Max(0f, fallbackMissImpulse), ForceMode.Impulse);
                    }

                    if (logPlanner)
                    {
                        Debug.Log("[GameSynthezerAndController] No valid plan: " + plan.reason, this);
                    }
                    return;
                }
            }

            int nextIndex = _rings.CurrentIndex + 1;
            if (appliedFlightPlan && flightConstraints.allowRingSkipRecovery && plannedTargetIndex > nextIndex)
            {
                nextIndex = plannedTargetIndex;
            }

            _rings.SetCurrentIndex(nextIndex);
            targetRingCenter = _rings.CurrentRing;
            RefreshTargetRingState(false, previousRingIndex);
            OnRingAdvanced?.Invoke(previousRingIndex, _rings.CurrentIndex);
        }

        private HitQuality EvaluateHitQuality(out float quality01, out string message)
        {
            quality01 = 0f;
            message = "n/a";
            if (ballBody == null || targetRingCenter == null)
            {
                return HitQuality.Miss;
            }

            float ringY = targetRingCenter.position.y;
            float deltaY = Mathf.Abs(ballBody.position.y - ringY);
            float positionScore = 1f - Mathf.InverseLerp(perfectDistance, greatDistance, deltaY);
            positionScore = Mathf.Clamp01(positionScore);

            float expected = GetExpectedHitTimeForRing(_rings.CurrentIndex);
            float now = GetSongOrGameplayTimeSeconds();
            float timingError = Mathf.Abs(now - expected);

            bool warmupRingBeforeSong = expected < 0f && (beatSync == null || !beatSync.IsRunning);
            if (warmupRingBeforeSong)
            {
                float warmupPerfect = Mathf.Max(perfectDistance, ringRegionVerticalTolerance * 0.65f);
                float warmupGreat = Mathf.Max(greatDistance, ringRegionVerticalTolerance * 1.2f);
                HitQuality warmupQuality;
                if (deltaY <= warmupPerfect)
                {
                    warmupQuality = HitQuality.Perfect;
                    quality01 = 1f;
                }
                else if (deltaY <= warmupGreat)
                {
                    warmupQuality = HitQuality.Great;
                    quality01 = 0.82f;
                }
                else
                {
                    warmupQuality = HitQuality.Late;
                    quality01 = 0.56f;
                }

                message = "WARMUP " + warmupQuality.ToString().ToUpperInvariant() +
                          " | ring=" + (_rings.CurrentIndex + 1) +
                          " | dY=" + deltaY.ToString("F3");
                return warmupQuality;
            }

            float perfectWindow = fallbackPerfectBeatWindowSeconds;
            float greatWindow = Mathf.Max(perfectWindow + 0.01f, fallbackGreatBeatWindowSeconds);
            float lateWindow = Mathf.Max(greatWindow + 0.01f, fallbackLateBeatWindowSeconds);
            TryGetALDJudgeWindows(expected, out perfectWindow, out greatWindow, out lateWindow);
            ApplyRuntimeMinimumJudgeWindows(ref perfectWindow, ref greatWindow, ref lateWindow);

            float timeScore = 0f;
            HitQuality q;
            if (timingError <= perfectWindow && deltaY <= greatDistance)
            {
                q = HitQuality.Perfect;
                timeScore = 1f;
            }
            else if (timingError <= greatWindow && deltaY <= Mathf.Max(greatDistance * 1.2f, ringRegionVerticalTolerance))
            {
                q = HitQuality.Great;
                timeScore = 0.76f;
            }
            else if (timingError <= lateWindow && deltaY <= Mathf.Max(greatDistance * 1.7f, ringRegionVerticalTolerance * 1.4f))
            {
                q = HitQuality.Late;
                timeScore = 0.48f;
            }
            else
            {
                q = HitQuality.Miss;
            }

            quality01 = Mathf.Clamp01((timeScore * 0.75f) + (positionScore * 0.25f));
            message = q.ToString().ToUpperInvariant() +
                      " | ring=" + (_rings.CurrentIndex + 1) +
                      " | tErr=" + timingError.ToString("F3") + "s" +
                      " | dY=" + deltaY.ToString("F3");
            return q;
        }
        private bool IsInsideCurrentRingRegion()
        {
            if (targetRingCenter == null || ballBody == null)
            {
                return false;
            }

            Vector3 ringPos = targetRingCenter.position;
            Vector3 ballPos = ballBody.position;
            float verticalAbs = Mathf.Abs(ballPos.y - ringPos.y);
            Vector2 ringXZ = new Vector2(ringPos.x, ringPos.z);
            Vector2 ballXZ = new Vector2(ballPos.x, ballPos.z);
            float horizontalDistance = Vector2.Distance(ballXZ, ringXZ);

            return verticalAbs <= Mathf.Max(0.01f, ringRegionVerticalTolerance) &&
                   horizontalDistance <= Mathf.Max(0.01f, ringRegionHorizontalRadius);
        }

        private float GetExpectedHitTimeForRing(int ringIndex)
        {
            if (_selectedRingHitTimes.Count > 0)
            {
                int clamped = Mathf.Clamp(ringIndex, 0, _selectedRingHitTimes.Count - 1);
                return ApplyRuntimeTimingOffset(_selectedRingHitTimes[clamped]);
            }

            if (aldLevelAsset != null && aldLevelAsset.Notes != null && aldLevelAsset.Notes.Count > 0)
            {
                int clamped = Mathf.Clamp(ringIndex, 0, aldLevelAsset.Notes.Count - 1);
                return ApplyRuntimeTimingOffset(aldLevelAsset.Notes[clamped].hitTimeSeconds);
            }

            return Mathf.Max(0f, Time.time - _gameplayStartTime);
        }

        private IReadOnlyList<float> GetPlannerRingHitTimes()
        {
            if (_selectedRingHitTimes.Count == 0)
            {
                return _selectedRingHitTimes;
            }

            if (Mathf.Abs(aldRuntimeTimingOffsetSeconds) <= 0.0001f)
            {
                return _selectedRingHitTimes;
            }

            _timingCorrectedRingHitTimes.Clear();
            for (int i = 0; i < _selectedRingHitTimes.Count; i++)
            {
                _timingCorrectedRingHitTimes.Add(ApplyRuntimeTimingOffset(_selectedRingHitTimes[i]));
            }

            return _timingCorrectedRingHitTimes;
        }

        private float ApplyRuntimeTimingOffset(float hitTimeSeconds)
        {
            if (hitTimeSeconds < 0f)
            {
                return hitTimeSeconds;
            }

            return Mathf.Max(0f, hitTimeSeconds + aldRuntimeTimingOffsetSeconds);
        }

        private bool TryGetALDJudgeWindows(float hitTime, out float perfectWindow, out float greatWindow, out float lateWindow)
        {
            perfectWindow = fallbackPerfectBeatWindowSeconds;
            greatWindow = Mathf.Max(perfectWindow + 0.01f, fallbackGreatBeatWindowSeconds);
            lateWindow = Mathf.Max(greatWindow + 0.01f, fallbackLateBeatWindowSeconds);

            if (aldLevelAsset == null || aldLevelAsset.JudgeProfileSections == null || aldLevelAsset.JudgeProfileSections.Count == 0)
            {
                return false;
            }

            var sections = aldLevelAsset.JudgeProfileSections;
            int chosen = -1;
            float chosenStart = float.MinValue;
            for (int i = 0; i < sections.Count; i++)
            {
                float s = sections[i].startTimeSeconds;
                if (s <= hitTime && s >= chosenStart)
                {
                    chosenStart = s;
                    chosen = i;
                }
            }

            if (chosen < 0)
            {
                chosen = 0;
            }

            var section = sections[chosen];
            perfectWindow = Mathf.Max(0.01f, section.perfectWindowSeconds);
            greatWindow = Mathf.Max(perfectWindow + 0.01f, section.greatWindowSeconds);
            lateWindow = Mathf.Max(greatWindow + 0.01f, section.earlyLateWindowSeconds);
            return true;
        }

        private void ApplyRuntimeMinimumJudgeWindows(ref float perfectWindow, ref float greatWindow, ref float lateWindow)
        {
            perfectWindow = Mathf.Max(0.01f, perfectWindow, minimumRuntimePerfectWindowSeconds);
            greatWindow = Mathf.Max(perfectWindow + 0.01f, greatWindow, minimumRuntimeGreatWindowSeconds);
            lateWindow = Mathf.Max(greatWindow + 0.01f, lateWindow, minimumRuntimeLateWindowSeconds);
        }

        private float GetSongOrGameplayTimeSeconds()
        {
            if (beatSync != null)
            {
                if (!beatSync.IsRunning)
                {
                    return -GetPredictedPreSongLeadSeconds();
                }

                return (float)beatSync.CurrentSongTimeSeconds;
            }

            return Mathf.Max(0f, Time.time - _gameplayStartTime);
        }

        private float GetPredictedPreSongLeadSeconds()
        {
            float lead = Mathf.Max(0f, extraPreSongLeadSeconds);
            if (autoUseBeatSyncStartDelayForPreSongTiming && beatSync != null)
            {
                lead += Mathf.Max(0f, (float)beatSync.StartDelaySeconds);
            }

            return lead;
        }

        private void TryResolveMissFail()
        {
            if (_gameOver || _levelCompleted || _jerkFlightActive || ballBody == null || targetRingCenter == null)
            {
                return;
            }

            float failY = targetRingCenter.position.y - Mathf.Max(0.1f, missFallDistanceBelowTarget);
            if (ballBody.position.y <= failY && ballBody.linearVelocity.y < -0.01f)
            {
                _gameOver = true;
                if (logGameOver)
                {
                    Debug.Log("[GameSynthezerAndController] GAME OVER: missed target ring.", this);
                }
            }
        }
    }
}
