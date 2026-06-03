using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeatDrop
{
    public sealed partial class GameSynthezerAndController
    {
        public void ReinitializeLevelFromRingChain()
        {
            GameObject chain = GameObject.Find("RingChain");
            if (chain != null)
            {
                ringChainRoot = chain.transform;
            }

            _rings.Rebuild(ringChainRoot, targetRingCenter, sortByHeight: true);
            _rings.SetCurrentIndex(0);
            targetRingCenter = _rings.CurrentRing;
            if (targetRingCenter == null && ringChainRoot != null && ringChainRoot.childCount > 0)
            {
                targetRingCenter = ringChainRoot.GetChild(0);
            }
            CancelActiveJerkFlight();
            _gameOver = false;
            _levelCompleted = false;
            _runtimeVerticalSpeedCap = maxVerticalSpeed;
            _lastAutoplayerRingIndex = -1;
            _lastAutoplayerSongTime = float.NegativeInfinity;
            _lastArrivalMessage = string.Empty;
            RefreshTargetRingState(true);
        }

        public void ConfigureFromALD(ALDLevelAsset asset, float recommendedTravelTime)
        {
            if (asset != null)
            {
                aldLevelAsset = asset;
            }

            if (recommendedTravelTime > 0.05f)
            {
                flightConstraints.minTravelTimeSeconds = Mathf.Min(flightConstraints.minTravelTimeSeconds, recommendedTravelTime * 0.82f);
                flightConstraints.maxTravelTimeSeconds = Mathf.Max(flightConstraints.maxTravelTimeSeconds, recommendedTravelTime * 1.45f);
                flightConstraints.Clamp();
            }
        }

        public void ConfigureFromALDSelection(ALDLevelAsset asset, float recommendedTravelTime, IReadOnlyList<float> ringHitTimes)
        {
            ConfigureFromALD(asset, recommendedTravelTime);
            _selectedRingHitTimes.Clear();
            if (ringHitTimes != null)
            {
                for (int i = 0; i < ringHitTimes.Count; i++)
                {
                    _selectedRingHitTimes.Add(ringHitTimes[i]);
                }
            }
        }

        public float CountdownSecondsForExternalSync => delayStartWithCountdown ? Mathf.Max(0f, startDelaySeconds) : 0f;
        public int CurrentRingIndex => _rings.CurrentIndex;
        public int RingCount => _rings.Count;

        private void EnsureReferences()
        {
            if (ballBody == null)
            {
                ballBody = GetComponent<Rigidbody>();
            }

            if (ballBody == null)
            {
                ballBody = FindBestBallRigidbody();
            }

            if (targetRingCenter == null && ringChainRoot != null && ringChainRoot.childCount > 0)
            {
                targetRingCenter = ringChainRoot.GetChild(0);
            }

            if (gridPulseController == null)
            {
                gridPulseController = Object.FindAnyObjectByType<GridPulseOnlyController>();
            }

            if (effectManager == null)
            {
                effectManager = Object.FindAnyObjectByType<BeatDropEffectManager>();
            }

            if (effectManager == null && autoCreateEffectManager)
            {
                effectManager = BeatDropEffectManager.FindOrCreate();
            }

            if (effectManager != null)
            {
                Camera cam = gridPulseController != null ? gridPulseController.ActiveCamera : Camera.main;
                effectManager.Configure(
                    gridPulseController,
                    cam,
                    projectGridPulseLikeClick,
                    gridPulseBehindBallDistance,
                    gridWaveformSpacing);
            }

            if (beatSync == null)
            {
                beatSync = Object.FindAnyObjectByType<DspBeatSync>();
            }
        }

        private void BeginGameplay()
        {
            if (_rings.Count == 0 || targetRingCenter == null)
            {
                ReinitializeLevelFromRingChain();
                TryAutoResyncWithLevelBuilderIfNeeded();
            }

            _gameplayStartTime = Time.time;
            _gameStarted = true;
            if (applyInitialImpulseOnStart && ballBody != null)
            {
                ballBody.AddForce(Vector3.up * initialStartImpulse, ForceMode.Impulse);
            }
        }

        private void TriggerHitFx(HitQuality quality, float quality01)
        {
            bool isWarmupRing = GetExpectedHitTimeForRing(_rings.CurrentIndex) < 0f;
            if (targetRingCenter != null)
            {
                RingHitPulseSplitEffect ringFx = targetRingCenter.GetComponent<RingHitPulseSplitEffect>();
                if (ringFx != null)
                {
                    if (lightweightWarmupRingClear && isWarmupRing)
                    {
                        ClearWarmupRingLightweight(targetRingCenter, ringFx, quality01);
                    }
                    else
                    {
                        ringFx.BreakAndFall(quality01);
                    }
                }
            }

            if (isWarmupRing && !pulseGridOnWarmupHit)
            {
                return;
            }

            bool shouldPulseGrid = quality == HitQuality.Perfect ||
                                   (!pulseGridOnlyOnPerfect && pulseGridOnGreatHit && quality == HitQuality.Great);
            if (!shouldPulseGrid)
            {
                return;
            }

            Vector3 source = ballBody != null ? ballBody.position : transform.position;
            if (effectManager == null && autoCreateEffectManager)
            {
                effectManager = BeatDropEffectManager.FindOrCreate();
            }

            if (effectManager != null)
            {
                Camera cam = gridPulseController != null ? gridPulseController.ActiveCamera : Camera.main;
                effectManager.Configure(
                    gridPulseController,
                    cam,
                    projectGridPulseLikeClick,
                    gridPulseBehindBallDistance,
                    gridWaveformSpacing);
                effectManager.TriggerPerfectHit(source, quality01);
                return;
            }

            if (gridPulseController == null)
            {
                return;
            }

            Camera fallbackCam = gridPulseController.ActiveCamera;
            Vector3 backDirection = fallbackCam != null ? fallbackCam.transform.forward : Vector3.forward;
            Vector3 pulsePoint = source + (backDirection.normalized * Mathf.Max(0f, gridPulseBehindBallDistance));
            if (projectGridPulseLikeClick)
            {
                gridPulseController.TryProjectWorldPointToGrid(source, out pulsePoint);
            }

            gridPulseController.TriggerPulseAtWorldPoint(pulsePoint);
            gridPulseController.TriggerWaveformAtWorldPoint(pulsePoint, gridWaveformSpacing);
        }

        private void ClearWarmupRingLightweight(Transform ring, RingHitPulseSplitEffect ringFx, float quality01)
        {
            if (ring == null)
            {
                return;
            }

            ringFx?.TriggerPulse(quality01);

            Collider[] colliders = ring.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = false;
                }
            }

            RingAutoRotate rotate = ring.GetComponent<RingAutoRotate>();
            if (rotate != null)
            {
                rotate.enabled = false;
            }

            Destroy(ring.gameObject, 0.16f);
        }

        private void RefreshTargetRingState(bool refreshAll = false, int previousTargetIndex = -1)
        {
            IReadOnlyList<Transform> rings = _rings.Rings;
            if (refreshAll)
            {
                for (int i = 0; i < rings.Count; i++)
                {
                    ApplyRingTargetState(i, !_levelCompleted && i == _rings.CurrentIndex);
                }

                _lastVisualTargetIndex = _rings.CurrentIndex;
                return;
            }

            if (previousTargetIndex >= 0)
            {
                ApplyRingTargetState(previousTargetIndex, false);
            }

            if (_lastVisualTargetIndex >= 0 && _lastVisualTargetIndex != previousTargetIndex && _lastVisualTargetIndex != _rings.CurrentIndex)
            {
                ApplyRingTargetState(_lastVisualTargetIndex, false);
            }

            if (!_levelCompleted)
            {
                ApplyRingTargetState(_rings.CurrentIndex, true);
            }

            _lastVisualTargetIndex = _rings.CurrentIndex;
        }

        private void ApplyRingTargetState(int ringIndex, bool isTarget)
        {
            IReadOnlyList<Transform> rings = _rings.Rings;
            if (ringIndex < 0 || ringIndex >= rings.Count)
            {
                return;
            }

            Transform ring = rings[ringIndex];
            if (ring == null)
            {
                return;
            }

            RingHitPulseSplitEffect ringFx = ring.GetComponent<RingHitPulseSplitEffect>();
            if (ringFx != null)
            {
                ringFx.SetTargeted(isTarget);
            }

            RingGlowController glow = ring.GetComponent<RingGlowController>();
            if (glow != null)
            {
                Color baseColor = isTarget ? targetRingColor : normalRingColor;
                glow.innerBaseGlowColor = baseColor;
                glow.outerBaseGlowColor = baseColor;
                glow.Apply();
            }
        }

        private void UpdateTargetRingTimingGradient()
        {
            if (!enableTargetTimingGradient || targetRingCenter == null)
            {
                return;
            }

            float expected = GetExpectedHitTimeForRing(_rings.CurrentIndex);
            float now = GetSongOrGameplayTimeSeconds();

            float perfect = fallbackPerfectBeatWindowSeconds;
            float great = fallbackGreatBeatWindowSeconds;
            float late = fallbackLateBeatWindowSeconds;
            TryGetALDJudgeWindows(expected, out perfect, out great, out late);
            ApplyRuntimeMinimumJudgeWindows(ref perfect, ref great, ref late);

            float timingError = Mathf.Abs(now - expected);
            float blend = 1f - Mathf.InverseLerp(perfect, great, timingError);
            blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(blend));
            float boost = Mathf.Lerp(1f, Mathf.Max(1f, timingGradientPerfectBoost), blend);
            Color targetColor = Color.Lerp(timingGradientCyan, timingGradientPink, blend) * boost;

            RingGlowController glow = targetRingCenter.GetComponent<RingGlowController>();
            if (glow != null)
            {
                glow.innerBaseGlowColor = targetColor;
                glow.outerBaseGlowColor = targetColor;
                glow.Apply();
            }
        }

        private void TryAutoResyncWithLevelBuilderIfNeeded()
        {
            if (_attemptedAutoBuilderResync)
            {
                return;
            }

            if (aldLevelAsset == null || _rings.Count <= 0)
            {
                return;
            }

            if (_selectedRingHitTimes.Count == _rings.Count && _selectedRingHitTimes.Count > 0)
            {
                return;
            }

            ALDNeonOrbitLevelBuilder builder = Object.FindAnyObjectByType<ALDLevelBuilder>();
            if (builder == null)
            {
                builder = Object.FindAnyObjectByType<ALDNeonOrbitLevelBuilder>();
            }
            if (builder == null)
            {
                return;
            }

            _attemptedAutoBuilderResync = true;
            List<float> hitTimes = builder.BuildSelectedHitTimesOnly();
            if (hitTimes != null && hitTimes.Count > 0)
            {
                ConfigureFromALDSelection(aldLevelAsset, -1f, hitTimes);
            }
        }

        private void SetupTrail()
        {
            if (attachTrailToBallBody && ballBody != null)
            {
                TrailRenderer ballOwnedTrail = ballBody.GetComponent<TrailRenderer>();
                if (ballTrail == null || ballTrail.transform != ballBody.transform)
                {
                    ballTrail = ballOwnedTrail != null
                        ? ballOwnedTrail
                        : ballBody.gameObject.AddComponent<TrailRenderer>();
                }
            }

            if (ballTrail == null)
            {
                ballTrail = GetComponent<TrailRenderer>();
            }
            if (ballTrail == null)
            {
                ballTrail = gameObject.AddComponent<TrailRenderer>();
            }

            ballTrail.time = trailTime;
            ballTrail.startWidth = trailStartWidth;
            ballTrail.endWidth = trailEndWidth;
            ballTrail.minVertexDistance = trailMinVertexDistance;
            ballTrail.numCornerVertices = trailCornerVertices;
            ballTrail.numCapVertices = trailCapVertices;
            ballTrail.alignment = LineAlignment.View;
            ballTrail.textureMode = LineTextureMode.Stretch;
            ballTrail.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            ballTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ballTrail.receiveShadows = false;
            ballTrail.emitting = true;

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(trailStartColor, 0f),
                    new GradientColorKey(Color.Lerp(trailStartColor, trailEndColor, 0.35f), 0.45f),
                    new GradientColorKey(trailEndColor, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(Mathf.Clamp01(trailStartColor.a), 0f),
                    new GradientAlphaKey(Mathf.Clamp01(trailEndColor.a), 1f)
                });
            ballTrail.colorGradient = gradient;

            Shader trailShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (trailShader == null)
            {
                trailShader = Shader.Find("Sprites/Default");
            }

            if (trailShader != null)
            {
                _trailMaterial = new Material(trailShader) { name = "Runtime_NeonTrail_Mat" };
                if (_trailMaterial.HasProperty("_BaseColor"))
                {
                    _trailMaterial.SetColor("_BaseColor", trailStartColor);
                }
                else if (_trailMaterial.HasProperty("_Color"))
                {
                    _trailMaterial.SetColor("_Color", trailStartColor);
                }
                ballTrail.material = _trailMaterial;
            }
        }

        private void UpdateTrailVisual()
        {
            if (ballTrail == null || ballBody == null)
            {
                return;
            }

            float speed = ballBody.linearVelocity.magnitude;
            float speedBoost = 1f + (speed * Mathf.Max(0f, trailSpeedBoost));
            float pulse = 1f + Mathf.Sin(Time.time * Mathf.Max(0.1f, trailPulseSpeed)) * Mathf.Clamp(trailPulseAmount, 0f, 1f);
            float targetWidthMul = Mathf.Clamp(speedBoost * pulse, 0.55f, Mathf.Max(0.55f, trailMaxWidthMultiplier));
            _trailWidthMultiplier = Mathf.SmoothDamp(
                _trailWidthMultiplier,
                targetWidthMul,
                ref _trailWidthVelocity,
                Mathf.Max(0.001f, trailWidthSmoothing));
            ballTrail.widthMultiplier = _trailWidthMultiplier;

            if (_trailMaterial != null)
            {
                Color liveColor = Color.Lerp(trailStartColor, trailEndColor, 0.35f + Mathf.Sin(Time.time * (trailPulseSpeed * 0.55f)) * 0.25f);
                liveColor *= Mathf.Min(speedBoost, 1.75f);
                if (_trailMaterial.HasProperty("_BaseColor"))
                {
                    _trailMaterial.SetColor("_BaseColor", liveColor);
                }
                else if (_trailMaterial.HasProperty("_Color"))
                {
                    _trailMaterial.SetColor("_Color", liveColor);
                }
            }
        }

        private void OnGUI()
        {
            if (_startSequenceRunning && !_gameStarted)
            {
                GUIStyle countdownStyle = new GUIStyle(GUI.skin.label);
                countdownStyle.alignment = TextAnchor.MiddleCenter;
                countdownStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.08f);
                countdownStyle.fontStyle = FontStyle.Bold;
                countdownStyle.normal.textColor = Color.white;

                int secondsLeft = Mathf.Max(1, Mathf.CeilToInt(_startTimerRemaining));
                Rect rect = new Rect(0f, 0f, Screen.width, Screen.height);
                GUI.Label(rect, secondsLeft.ToString(), countdownStyle);
            }

            DrawNoteRingSyncHud();
        }

        private void DrawNoteRingSyncHud()
        {
            if (!showNoteRingSyncHud)
            {
                return;
            }
            if (showHudOnlyDuringGameplay && !_gameStarted)
            {
                return;
            }

            int ringDisplayIndex = _rings.CurrentIndex + 1;
            int ringCount = Mathf.Max(0, _rings.Count);
            int noteCount = _selectedRingHitTimes.Count > 0
                ? _selectedRingHitTimes.Count
                : (aldLevelAsset != null && aldLevelAsset.Notes != null ? aldLevelAsset.Notes.Count : 0);
            int mappedNoteIndex = Mathf.Clamp(ringDisplayIndex, 1, Mathf.Max(1, noteCount));

            float expected = GetExpectedHitTimeForRing(_rings.CurrentIndex);
            float now = GetSongOrGameplayTimeSeconds();
            float delta = expected - now;
            float ringY = targetRingCenter != null ? targetRingCenter.position.y : 0f;
            float ballY = ballBody != null ? ballBody.position.y : 0f;

            string hudText =
                "Ring " + ringDisplayIndex + "/" + ringCount +
                "  <->  Note " + mappedNoteIndex + "/" + Mathf.Max(1, noteCount) + "\n" +
                "Expected=" + expected.ToString("F3") + "s  Now=" + now.ToString("F3") +
                "s  Delta=" + delta.ToString("+#0.000;-#0.000;0.000") + "s\n" +
                "BallY=" + ballY.ToString("F2") + "  TargetY=" + ringY.ToString("F2") + "\n" +
                (_jerkFlightActive ? "JerkFlight=ACTIVE\n" : "") +
                (enableAutoplayer ? "Autoplayer=" + (autoplayerForcePerfectHits ? "FORCE PERFECT" : "TIMING") + "\n" : "") +
                (!string.IsNullOrEmpty(_lastArrivalMessage) ? _lastArrivalMessage + "\n" : "") +
                _lastJudgeMessage;

            GUIStyle hudStyle = new GUIStyle(GUI.skin.box);
            hudStyle.alignment = TextAnchor.UpperLeft;
            hudStyle.fontSize = Mathf.Clamp(hudFontSize, 10, 40);
            hudStyle.normal.textColor = Color.white;
            hudStyle.padding = new RectOffset(10, 10, 8, 8);

            float width = Mathf.Min(Screen.width * 0.84f, 820f);
            float height = hudStyle.CalcHeight(new GUIContent(hudText), width);
            Rect hudRect = new Rect(
                Mathf.Max(0f, hudMargin.x),
                Mathf.Max(0f, hudMargin.y),
                width,
                height + 8f);
            GUI.Box(hudRect, hudText, hudStyle);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureDefaultFlightConstraintsIfNeeded();
            startDelaySeconds = Mathf.Max(0f, startDelaySeconds);
            initialStartImpulse = Mathf.Max(0f, initialStartImpulse);
            centerSpring = Mathf.Max(0f, centerSpring);
            centerDamping = Mathf.Max(0f, centerDamping);
            maxHorizontalOffset = Mathf.Max(0.1f, maxHorizontalOffset);
            maxVerticalSpeed = Mathf.Max(1f, maxVerticalSpeed);
            minHeight = Mathf.Clamp(minHeight, -100f, 1000f);
            maxHeight = Mathf.Max(minHeight + 0.1f, maxHeight);
            verticalBoundarySpring = Mathf.Max(0f, verticalBoundarySpring);
            ringRegionHorizontalRadius = Mathf.Max(0.05f, ringRegionHorizontalRadius);
            ringRegionVerticalTolerance = Mathf.Max(0.05f, ringRegionVerticalTolerance);
            perfectDistance = Mathf.Clamp(perfectDistance, 0.01f, 2f);
            greatDistance = Mathf.Max(perfectDistance, greatDistance);
            fallbackPerfectBeatWindowSeconds = Mathf.Clamp(fallbackPerfectBeatWindowSeconds, 0.01f, 0.4f);
            fallbackGreatBeatWindowSeconds = Mathf.Clamp(fallbackGreatBeatWindowSeconds, fallbackPerfectBeatWindowSeconds + 0.01f, 0.8f);
            fallbackLateBeatWindowSeconds = Mathf.Clamp(fallbackLateBeatWindowSeconds, fallbackGreatBeatWindowSeconds + 0.01f, 1.2f);
            aldRuntimeTimingOffsetSeconds = Mathf.Clamp(aldRuntimeTimingOffsetSeconds, -0.5f, 0.5f);
            minimumRuntimePerfectWindowSeconds = Mathf.Clamp(minimumRuntimePerfectWindowSeconds, 0.01f, 0.4f);
            minimumRuntimeGreatWindowSeconds = Mathf.Clamp(minimumRuntimeGreatWindowSeconds, minimumRuntimePerfectWindowSeconds + 0.01f, 0.8f);
            minimumRuntimeLateWindowSeconds = Mathf.Clamp(minimumRuntimeLateWindowSeconds, minimumRuntimeGreatWindowSeconds + 0.01f, 1.2f);
            fallbackMissImpulse = Mathf.Max(0f, fallbackMissImpulse);
            perfectAssistBlend = Mathf.Clamp01(perfectAssistBlend);
            greatAssistBlend = Mathf.Clamp01(greatAssistBlend);
            lateAssistBlend = Mathf.Clamp01(lateAssistBlend);
            minimumAssistBlend = Mathf.Clamp(minimumAssistBlend, 0.1f, 1f);
            solvedVerticalSpeedCapPadding = Mathf.Max(0f, solvedVerticalSpeedCapPadding);
            maxJerkInitialAcceleration = Mathf.Max(0f, maxJerkInitialAcceleration);
            maxJerkMagnitude = Mathf.Max(0f, maxJerkMagnitude);
            jerkArrivalSnapSeconds = Mathf.Clamp(jerkArrivalSnapSeconds, 0f, 0.08f);
            timingGradientPerfectBoost = Mathf.Clamp(timingGradientPerfectBoost, 1f, 3f);
            gridPulseBehindBallDistance = Mathf.Max(0f, gridPulseBehindBallDistance);
            gridWaveformSpacing = Mathf.Max(0.05f, gridWaveformSpacing);
            autoplayerHitLeadSeconds = Mathf.Clamp(autoplayerHitLeadSeconds, 0f, 0.2f);
            autoplayerLateToleranceSeconds = Mathf.Clamp(autoplayerLateToleranceSeconds, 0f, 0.35f);
            autoplayerWarmupDelaySeconds = Mathf.Max(0f, autoplayerWarmupDelaySeconds);
            trailTime = Mathf.Max(0.02f, trailTime);
            trailStartWidth = Mathf.Max(0.01f, trailStartWidth);
            trailEndWidth = Mathf.Clamp(trailEndWidth, 0.001f, trailStartWidth);
            trailMinVertexDistance = Mathf.Max(0.001f, trailMinVertexDistance);
            trailCornerVertices = Mathf.Clamp(trailCornerVertices, 0, 32);
            trailCapVertices = Mathf.Clamp(trailCapVertices, 0, 32);
            trailPulseSpeed = Mathf.Max(0.1f, trailPulseSpeed);
            trailPulseAmount = Mathf.Clamp01(trailPulseAmount);
            trailSpeedBoost = Mathf.Max(0f, trailSpeedBoost);
            trailMaxWidthMultiplier = Mathf.Max(0.55f, trailMaxWidthMultiplier);
            trailWidthSmoothing = Mathf.Max(0.001f, trailWidthSmoothing);
            hudFontSize = Mathf.Clamp(hudFontSize, 10, 40);
            hudMargin.x = Mathf.Max(0f, hudMargin.x);
            hudMargin.y = Mathf.Max(0f, hudMargin.y);
            missFallDistanceBelowTarget = Mathf.Max(0.1f, missFallDistanceBelowTarget);
            extraPreSongLeadSeconds = Mathf.Max(0f, extraPreSongLeadSeconds);
            flightConstraints.Clamp();
            EnsureJerkFinalVelocityFitsConstraints();
        }
#endif
    }
}
