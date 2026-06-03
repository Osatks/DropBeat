using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BeatDrop
{
    /// <summary>
    /// Spawns a click/tap-driven neon sine waveform burst using a LineRenderer.
    /// The burst is projected onto a target plane (grid plane by default).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class SineWaveBurstController : MonoBehaviour
    {
        private enum HarmonicMode
        {
            PresetList,
            RandomPerBurst
        }

        private enum HarmonicPreset
        {
            PureSine,
            WarmDual,
            BrightTriple,
            Metallic,
            AggressiveEDM,
            SmoothSynthwave
        }

        private enum AmplitudeEnvelopePreset
        {
            Flat,
            CenterPeak,
            LeftHeavy,
            RightHeavy,
            Bell,
            AmplifiedSignal
        }

        [Header("References")]
        public Camera inputCamera;
        public Material lineMaterialOverride;

        [Header("Wave Shape")]
        [Min(2)] public int pointsCount = 100;
        [Min(0f)] public float maxAmplitude = 1.25f;
        public float frequency = 2.5f;
        public float scrollSpeed = 6f;

        [Header("Amplitude Envelope (By Position)")]
        public bool useAmplitudeEnvelopeByPosition = true;
        public AnimationCurve amplitudeByPosition = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(1f, 1f));
        [SerializeField] private AmplitudeEnvelopePreset amplitudeEnvelopePreset = AmplitudeEnvelopePreset.Flat;
        public bool applyEnvelopePresetInEditor = false;

        [Header("Split Travel")]
        [Min(0f)] public float outwardTravelSpeed = 18f;
        [Min(0.05f)] public float splitWaveWidth = 1.8f;
        [Range(0.25f, 4f)] public float splitWaveSharpness = 1.35f;
        [Range(0f, 1f)] public float centerBleed = 0.12f;

        [Header("Branch Phase")]
        public float rightBranchPhaseOffset = 1.0472f;
        public bool randomizeRightBranchPhasePerBurst = false;
        public Vector2 randomRightBranchPhaseOffsetRange = new Vector2(0.35f, 2.2f);

        [Header("Harmonics")]
        [SerializeField] private HarmonicMode harmonicMode = HarmonicMode.PresetList;
        [SerializeField] private HarmonicPreset harmonicPreset = HarmonicPreset.WarmDual;
        public Vector2 randomHarmonic2StrengthRange = new Vector2(0.15f, 0.55f);
        public Vector2 randomHarmonic3StrengthRange = new Vector2(0.05f, 0.30f);
        public Vector2 randomPhaseOffsetRange = new Vector2(-3.14159f, 3.14159f);

        [Header("Burst")]
        [Min(0.01f)] public float attackDuration = 0.1f;
        [Min(0.01f)] public float fadeDuration = 0.55f;
        public bool requireShiftForMouseClick = false;

        [Header("Projection Plane")]
        public GridPulseOnlyController gridController;
        public bool runOnlyInXYPlane = true;
        public Transform projectionPlaneTransform;
        public float projectionPlaneLocalDepth = 42f;
        [Min(0f)] public float planeSurfaceOffset = 0.015f;
        public bool autoFindGridPlane = true;
        public string autoGridPlanePath = "GridPulseRoot/GridRoot";

        [Header("Visual")]
        [ColorUsage(true, true)]
        public Color neonColor = new Color(1f, 0f, 1f, 1f);
        [Min(0f)] public float hdrIntensity = 5f;
        public bool forceTransparentMaterialForAlphaFade = true;

        private LineRenderer _line;
        private Camera _mainCamera;
        private Vector3[] _positions;
        private Material _lineMaterial;

        private Vector3 _lineStartPoint;
        private Vector3 _lineEndPoint;
        private Vector3 _waveOffsetAxis;
        private Vector3 _projectionNormal;

        private float _centerDistanceAlongLine;
        private float _currentAmplitude;
        private float _currentAlpha;
        private float _burstElapsed;
        private bool _isBurstActive;
        private Coroutine _burstRoutine;

        private float _activeHarmonic2Strength;
        private float _activeHarmonic3Strength;
        private float _activeHarmonic2PhaseOffset;
        private float _activeHarmonic3PhaseOffset;
        private float _activeRightBranchPhaseOffset;

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            TryResolveCamera();
            EnsureAmplitudeEnvelope();

            pointsCount = Mathf.Max(2, pointsCount);
            _positions = new Vector3[pointsCount];

            _line.useWorldSpace = true;
            _line.positionCount = pointsCount;
            _line.enabled = false;
            if (_line.sharedMaterial == null && lineMaterialOverride != null)
            {
                _line.sharedMaterial = lineMaterialOverride;
            }
            _lineMaterial = _line.sharedMaterial != null ? _line.sharedMaterial : _line.material;
            ConfigureMaterialForAlphaFade();

            TryResolveProjectionPlane();
            ConfigureHarmonicsForBurst();
        }

        private void OnValidate()
        {
            pointsCount = Mathf.Max(2, pointsCount);
            maxAmplitude = Mathf.Max(0f, maxAmplitude);
            attackDuration = Mathf.Max(0.01f, attackDuration);
            fadeDuration = Mathf.Max(0.01f, fadeDuration);
            hdrIntensity = Mathf.Max(0f, hdrIntensity);
            planeSurfaceOffset = Mathf.Max(0f, planeSurfaceOffset);
            outwardTravelSpeed = Mathf.Max(0f, outwardTravelSpeed);
            splitWaveWidth = Mathf.Max(0.05f, splitWaveWidth);

            NormalizeRange(ref randomHarmonic2StrengthRange, 0f);
            NormalizeRange(ref randomHarmonic3StrengthRange, 0f);
            NormalizeRange(ref randomPhaseOffsetRange, -100f);
            NormalizeRange(ref randomRightBranchPhaseOffsetRange, -100f);
            EnsureAmplitudeEnvelope();
            ApplyEnvelopePresetIfRequested();

            if (_line != null)
            {
                if (_positions == null || _positions.Length != pointsCount)
                {
                    _positions = new Vector3[pointsCount];
                }
                _line.positionCount = pointsCount;
            }
        }

        private void Update()
        {
            if (!TryResolveCamera())
            {
                return;
            }

            if (projectionPlaneTransform == null)
            {
                TryResolveProjectionPlane();
            }

            if (runOnlyInXYPlane && gridController != null && gridController.IsXZPlaneActive)
            {
                if (_isBurstActive)
                {
                    _isBurstActive = false;
                    _currentAmplitude = 0f;
                    _currentAlpha = 0f;
                    if (_burstRoutine != null)
                    {
                        StopCoroutine(_burstRoutine);
                        _burstRoutine = null;
                    }
                    _line.enabled = false;
                }
                return;
            }

            if (TryGetPointerDownScreenPosition(out Vector2 screenPosition) && !IsPointerOverUi())
            {
                StartBurstAtScreenPosition(screenPosition);
            }

            if (_isBurstActive)
            {
                _burstElapsed += Time.deltaTime;
                RenderWave();
            }
        }

        private bool TryGetPointerDownScreenPosition(out Vector2 screenPosition)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (requireShiftForMouseClick && !IsShiftPressed())
                {
                    screenPosition = default;
                    return false;
                }

                screenPosition = Mouse.current.position.ReadValue();
                return true;
            }

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                screenPosition = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }
#else
            if (Input.GetMouseButtonDown(0))
            {
                if (requireShiftForMouseClick && !IsShiftPressed())
                {
                    screenPosition = default;
                    return false;
                }

                screenPosition = Input.mousePosition;
                return true;
            }

            if (Input.touchCount > 0)
            {
                Touch t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began)
                {
                    screenPosition = t.position;
                    return true;
                }
            }
#endif

            screenPosition = default;
            return false;
        }

        private bool IsPointerOverUi()
        {
            if (EventSystem.current == null)
            {
                return false;
            }

#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                int touchId = Touchscreen.current.primaryTouch.touchId.ReadValue();
                return EventSystem.current.IsPointerOverGameObject(touchId);
            }
#else
            if (Input.touchCount > 0)
            {
                return EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
            }
#endif

            return EventSystem.current.IsPointerOverGameObject();
        }

        private bool IsShiftPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null &&
                   (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
#else
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
        }

        public void TriggerBurstAtWorldPoint(Vector3 worldPoint)
        {
            if (!TryResolveCamera())
            {
                return;
            }

            Vector3 screenPoint = _mainCamera.WorldToScreenPoint(worldPoint);
            StartBurstAtScreenPosition(new Vector2(screenPoint.x, screenPoint.y));
        }

        public void TriggerBurstAtScreenPosition(Vector2 screenPosition)
        {
            if (!TryResolveCamera())
            {
                return;
            }

            StartBurstAtScreenPosition(screenPosition);
        }

        private void StartBurstAtScreenPosition(Vector2 screenPosition)
        {
            if (!TryBuildProjectionPlane(out Plane plane))
            {
                return;
            }

            if (!TryProjectRayToPlane(_mainCamera.ScreenPointToRay(screenPosition), plane, out Vector3 clickWorld))
            {
                return;
            }

            if (!TryGetLineEndpointsOnPlane(screenPosition.y, plane, clickWorld, out Vector3 leftWorld, out Vector3 rightWorld))
            {
                return;
            }

            _lineStartPoint = leftWorld;
            _lineEndPoint = rightWorld;

            Vector3 lineVector = _lineEndPoint - _lineStartPoint;
            float lineLength = lineVector.magnitude;
            if (lineLength < 0.001f)
            {
                return;
            }

            Vector3 lineDirection = lineVector / lineLength;
            _centerDistanceAlongLine = Mathf.Clamp(Vector3.Dot(clickWorld - _lineStartPoint, lineDirection), 0f, lineLength);

            _waveOffsetAxis = Vector3.Cross(_projectionNormal, lineDirection);
            if (_waveOffsetAxis.sqrMagnitude < 0.0001f)
            {
                _waveOffsetAxis = Vector3.Cross(lineDirection, _mainCamera.transform.forward);
            }
            if (_waveOffsetAxis.sqrMagnitude < 0.0001f)
            {
                _waveOffsetAxis = Vector3.up;
            }

            _waveOffsetAxis.Normalize();
            if (Vector3.Dot(_waveOffsetAxis, _mainCamera.transform.up) < 0f)
            {
                _waveOffsetAxis = -_waveOffsetAxis;
            }

            _burstElapsed = 0f;
            _currentAmplitude = 0f;
            _currentAlpha = 0f;
            _isBurstActive = true;
            _line.enabled = true;

            ConfigureHarmonicsForBurst();

            if (_burstRoutine != null)
            {
                StopCoroutine(_burstRoutine);
            }
            _burstRoutine = StartCoroutine(BurstEnvelopeRoutine());

            ApplyLineColor(_currentAlpha);
        }

        private void RenderWave()
        {
            Vector3 lineVector = _lineEndPoint - _lineStartPoint;
            float lineLength = lineVector.magnitude;
            if (lineLength < 0.0001f)
            {
                return;
            }

            Vector3 lineDirection = lineVector / lineLength;
            float stepDistance = lineLength / (pointsCount - 1);
            float phase = Time.time * scrollSpeed;
            float travel = _burstElapsed * outwardTravelSpeed;
            float width = Mathf.Max(0.05f, splitWaveWidth);
            float twoSigmaSq = 2f * width * width;

            float maxTravel = lineLength * 0.5f;
            float centerBlendFactor = 1f - Mathf.Clamp01(travel / Mathf.Max(0.0001f, maxTravel));

            for (int i = 0; i < pointsCount; i++)
            {
                float distanceAlongLine = stepDistance * i;
                float signedDistance = distanceAlongLine - _centerDistanceAlongLine;
                float absDistance = Mathf.Abs(signedDistance);
                Vector3 basePoint = _lineStartPoint + (lineDirection * distanceAlongLine);

                // Base phase uses absolute distance so the branch pattern travels outward from the click.
                float theta = (absDistance * frequency) - phase;
                float leftWave = SampleHarmonicWave(theta);
                float rightWave = SampleHarmonicWave(theta + _activeRightBranchPhaseOffset);
                float centerWave = SampleHarmonicWave(theta + (_activeRightBranchPhaseOffset * 0.5f));

                float rightDelta = signedDistance - travel;
                float leftDelta = signedDistance + travel;

                float rightPulse = Mathf.Clamp01(Mathf.Exp(-(rightDelta * rightDelta) / twoSigmaSq) * splitWaveSharpness);
                float leftPulse = Mathf.Clamp01(Mathf.Exp(-(leftDelta * leftDelta) / twoSigmaSq) * splitWaveSharpness);
                float centerPulse = Mathf.Clamp01(Mathf.Exp(-(signedDistance * signedDistance) / twoSigmaSq) * centerBleed * centerBlendFactor);

                float branchWave = (leftWave * leftPulse) + (rightWave * rightPulse) + (centerWave * centerPulse);
                float pulseWeight = leftPulse + rightPulse + centerPulse;
                if (pulseWeight > 1f)
                {
                    branchWave /= pulseWeight;
                }

                float normalizedPosition = lineLength > 0.0001f ? distanceAlongLine / lineLength : 0f;
                float positionalEnvelope = 1f;
                if (useAmplitudeEnvelopeByPosition && amplitudeByPosition != null)
                {
                    positionalEnvelope = Mathf.Max(0f, amplitudeByPosition.Evaluate(normalizedPosition));
                }

                float wave = _currentAmplitude * positionalEnvelope * branchWave;
                _positions[i] = basePoint + (_waveOffsetAxis * wave);
            }

            _line.SetPositions(_positions);
        }

        private float SampleHarmonicWave(float theta)
        {
            float fundamental = Mathf.Sin(theta);
            float harmonic2 = Mathf.Sin((theta * 2f) + _activeHarmonic2PhaseOffset) * _activeHarmonic2Strength;
            float harmonic3 = Mathf.Sin((theta * 3f) + _activeHarmonic3PhaseOffset) * _activeHarmonic3Strength;
            float normalization = Mathf.Max(1f, 1f + _activeHarmonic2Strength + _activeHarmonic3Strength);
            return (fundamental + harmonic2 + harmonic3) / normalization;
        }

        private IEnumerator BurstEnvelopeRoutine()
        {
            float attackElapsed = 0f;
            while (attackElapsed < attackDuration)
            {
                attackElapsed += Time.deltaTime;
                float t = Mathf.Clamp01(attackElapsed / attackDuration);
                float shaped = Mathf.SmoothStep(0f, 1f, t);

                _currentAmplitude = Mathf.Lerp(0f, maxAmplitude, shaped);
                _currentAlpha = Mathf.Lerp(0f, 1f, shaped);
                ApplyLineColor(_currentAlpha);

                yield return null;
            }

            float decayElapsed = 0f;
            while (decayElapsed < fadeDuration)
            {
                decayElapsed += Time.deltaTime;
                float t = Mathf.Clamp01(decayElapsed / fadeDuration);
                float shaped = Mathf.SmoothStep(0f, 1f, t);

                _currentAmplitude = Mathf.Lerp(maxAmplitude, 0f, shaped);
                _currentAlpha = Mathf.Lerp(1f, 0f, shaped);
                ApplyLineColor(_currentAlpha);

                yield return null;
            }

            _currentAmplitude = 0f;
            _currentAlpha = 0f;
            _isBurstActive = false;
            _line.enabled = false;
            _burstRoutine = null;
        }

        private void ConfigureHarmonicsForBurst()
        {
            _activeRightBranchPhaseOffset = randomizeRightBranchPhasePerBurst
                ? Random.Range(randomRightBranchPhaseOffsetRange.x, randomRightBranchPhaseOffsetRange.y)
                : rightBranchPhaseOffset;

            if (harmonicMode == HarmonicMode.RandomPerBurst)
            {
                _activeHarmonic2Strength = Random.Range(randomHarmonic2StrengthRange.x, randomHarmonic2StrengthRange.y);
                _activeHarmonic3Strength = Random.Range(randomHarmonic3StrengthRange.x, randomHarmonic3StrengthRange.y);
                _activeHarmonic2PhaseOffset = Random.Range(randomPhaseOffsetRange.x, randomPhaseOffsetRange.y);
                _activeHarmonic3PhaseOffset = Random.Range(randomPhaseOffsetRange.x, randomPhaseOffsetRange.y);
                return;
            }

            // Preset list mode.
            switch (harmonicPreset)
            {
                case HarmonicPreset.PureSine:
                    _activeHarmonic2Strength = 0f;
                    _activeHarmonic3Strength = 0f;
                    _activeHarmonic2PhaseOffset = 0f;
                    _activeHarmonic3PhaseOffset = 0f;
                    break;

                case HarmonicPreset.BrightTriple:
                    _activeHarmonic2Strength = 0.42f;
                    _activeHarmonic3Strength = 0.24f;
                    _activeHarmonic2PhaseOffset = 0.35f;
                    _activeHarmonic3PhaseOffset = 1.1f;
                    break;

                case HarmonicPreset.Metallic:
                    _activeHarmonic2Strength = 0.55f;
                    _activeHarmonic3Strength = 0.18f;
                    _activeHarmonic2PhaseOffset = 1.2f;
                    _activeHarmonic3PhaseOffset = 2.35f;
                    break;

                case HarmonicPreset.AggressiveEDM:
                    _activeHarmonic2Strength = 0.72f;
                    _activeHarmonic3Strength = 0.46f;
                    _activeHarmonic2PhaseOffset = 0.9f;
                    _activeHarmonic3PhaseOffset = 2.2f;
                    break;

                case HarmonicPreset.SmoothSynthwave:
                    _activeHarmonic2Strength = 0.24f;
                    _activeHarmonic3Strength = 0.08f;
                    _activeHarmonic2PhaseOffset = 0.28f;
                    _activeHarmonic3PhaseOffset = 0.72f;
                    break;

                default:
                    _activeHarmonic2Strength = 0.33f;
                    _activeHarmonic3Strength = 0.16f;
                    _activeHarmonic2PhaseOffset = 0.45f;
                    _activeHarmonic3PhaseOffset = 1.05f;
                    break;
            }
        }

        private static void NormalizeRange(ref Vector2 range, float minClamp)
        {
            float min = Mathf.Max(minClamp, Mathf.Min(range.x, range.y));
            float max = Mathf.Max(minClamp, Mathf.Max(range.x, range.y));
            range = new Vector2(min, max);
        }

        private void EnsureAmplitudeEnvelope()
        {
            if (amplitudeByPosition == null || amplitudeByPosition.length == 0)
            {
                amplitudeByPosition = new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(1f, 1f));
            }
        }

        private void ApplyEnvelopePresetIfRequested()
        {
            if (!applyEnvelopePresetInEditor)
            {
                return;
            }

            applyEnvelopePresetInEditor = false;
            amplitudeByPosition = BuildEnvelopePreset(amplitudeEnvelopePreset);
        }

        private static AnimationCurve BuildEnvelopePreset(AmplitudeEnvelopePreset preset)
        {
            switch (preset)
            {
                case AmplitudeEnvelopePreset.CenterPeak:
                    return new AnimationCurve(
                        new Keyframe(0f, 0.2f),
                        new Keyframe(0.5f, 1f),
                        new Keyframe(1f, 0.2f));

                case AmplitudeEnvelopePreset.LeftHeavy:
                    return new AnimationCurve(
                        new Keyframe(0f, 1f),
                        new Keyframe(0.35f, 0.85f),
                        new Keyframe(1f, 0.2f));

                case AmplitudeEnvelopePreset.RightHeavy:
                    return new AnimationCurve(
                        new Keyframe(0f, 0.2f),
                        new Keyframe(0.65f, 0.85f),
                        new Keyframe(1f, 1f));

                case AmplitudeEnvelopePreset.Bell:
                    return new AnimationCurve(
                        new Keyframe(0f, 0.05f),
                        new Keyframe(0.25f, 0.5f),
                        new Keyframe(0.5f, 1f),
                        new Keyframe(0.75f, 0.5f),
                        new Keyframe(1f, 0.05f));

                case AmplitudeEnvelopePreset.AmplifiedSignal:
                    return new AnimationCurve(
                        new Keyframe(-0.016667176f, 1.00952375f),
                        new Keyframe(0.172888473f, 0.704891264f),
                        new Keyframe(0.286376894f, 0.509451032f),
                        new Keyframe(0.498012662f, 0.00406086445f),
                        new Keyframe(0.704663992f, 0.509068727f),
                        new Keyframe(0.816115618f, 0.706134498f),
                        new Keyframe(1f, 1.01428604f));

                default:
                    return new AnimationCurve(
                        new Keyframe(0f, 1f),
                        new Keyframe(1f, 1f));
            }
        }

        private void ApplyLineColor(float alpha)
        {
            Color hdrNeon = neonColor * hdrIntensity;
            hdrNeon.a = alpha;

            _line.startColor = hdrNeon;
            _line.endColor = hdrNeon;

            if (_lineMaterial == null)
            {
                return;
            }

            if (_lineMaterial.HasProperty("_BaseColor"))
            {
                _lineMaterial.SetColor("_BaseColor", hdrNeon);
            }
            else if (_lineMaterial.HasProperty("_Color"))
            {
                _lineMaterial.SetColor("_Color", hdrNeon);
            }

            // Also fade emission intensity, otherwise bloom can look "always on" even at low alpha.
            if (_lineMaterial.HasProperty("_EmissionColor"))
            {
                Color emission = neonColor * (hdrIntensity * Mathf.Clamp01(alpha));
                emission.a = alpha;
                _lineMaterial.SetColor("_EmissionColor", emission);
            }
        }

        private void ConfigureMaterialForAlphaFade()
        {
            if (!forceTransparentMaterialForAlphaFade || _lineMaterial == null)
            {
                return;
            }

            // URP/Lit/Unlit common transparent setup so vertex/material alpha actually fades.
            if (_lineMaterial.HasProperty("_Surface"))
            {
                _lineMaterial.SetFloat("_Surface", 1f); // Transparent
            }
            if (_lineMaterial.HasProperty("_Blend"))
            {
                _lineMaterial.SetFloat("_Blend", 0f); // Alpha
            }
            if (_lineMaterial.HasProperty("_ZWrite"))
            {
                _lineMaterial.SetFloat("_ZWrite", 0f);
            }

            _lineMaterial.renderQueue = 3000;
        }

        private bool TryResolveProjectionPlane()
        {
            if (projectionPlaneTransform != null)
            {
                return true;
            }

            if (gridController == null && autoFindGridPlane)
            {
                GameObject gridControllerObject = GameObject.Find("GridPulseRoot");
                if (gridControllerObject != null)
                {
                    gridController = gridControllerObject.GetComponent<GridPulseOnlyController>();
                }
            }

            if (gridController != null)
            {
                Transform gridRoot = gridController.transform.Find("GridRoot");
                if (gridRoot != null)
                {
                    projectionPlaneTransform = gridRoot;
                    projectionPlaneLocalDepth = gridController.planeZ;
                    return true;
                }
            }

            if (autoFindGridPlane)
            {
                GameObject planeObject = GameObject.Find(autoGridPlanePath);
                if (planeObject != null)
                {
                    projectionPlaneTransform = planeObject.transform;
                    return true;
                }
            }

            return false;
        }

        private bool TryResolveCamera()
        {
            if (inputCamera != null)
            {
                _mainCamera = inputCamera;
                return true;
            }

            if (_mainCamera != null)
            {
                return true;
            }

            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                _mainCamera = Object.FindAnyObjectByType<Camera>();
            }

            return _mainCamera != null;
        }

        private bool TryBuildProjectionPlane(out Plane plane)
        {
            Vector3 normal;
            Vector3 point;

            if (projectionPlaneTransform != null)
            {
                normal = projectionPlaneTransform.forward;
                point = projectionPlaneTransform.TransformPoint(new Vector3(0f, 0f, projectionPlaneLocalDepth));
            }
            else
            {
                // Fallback: use this object's forward plane if no external plane is configured.
                normal = transform.forward;
                point = transform.position;
            }

            if (normal.sqrMagnitude < 0.0001f)
            {
                plane = default;
                return false;
            }

            _projectionNormal = normal.normalized;

            // Push slightly toward the camera to avoid z-fighting with the grid surface.
            Vector3 toCamera = _mainCamera != null
                ? (_mainCamera.transform.position - point)
                : Vector3.zero;
            float side = Mathf.Sign(Vector3.Dot(_projectionNormal, toCamera));
            if (Mathf.Approximately(side, 0f))
            {
                side = 1f;
            }
            point += _projectionNormal * (planeSurfaceOffset * side);

            plane = new Plane(_projectionNormal, point);
            return true;
        }

        private bool TryProjectRayToPlane(Ray ray, Plane plane, out Vector3 worldPoint)
        {
            if (!plane.Raycast(ray, out float enter))
            {
                worldPoint = default;
                return false;
            }

            worldPoint = ray.GetPoint(enter);
            return true;
        }

        private bool TryGetLineEndpointsOnPlane(float screenY, Plane plane, Vector3 clickWorld, out Vector3 leftWorld, out Vector3 rightWorld)
        {
            Ray leftRay = _mainCamera.ScreenPointToRay(new Vector3(0f, screenY, 0f));
            Ray rightRay = _mainCamera.ScreenPointToRay(new Vector3(Screen.width, screenY, 0f));

            bool hasLeft = TryProjectRayToPlane(leftRay, plane, out leftWorld);
            bool hasRight = TryProjectRayToPlane(rightRay, plane, out rightWorld);
            if (hasLeft && hasRight)
            {
                return true;
            }

            // Fallback when edge rays are near-parallel to the plane.
            Vector3 fallbackDirection = Vector3.ProjectOnPlane(_mainCamera.transform.right, _projectionNormal);
            if (fallbackDirection.sqrMagnitude < 0.0001f)
            {
                fallbackDirection = Vector3.ProjectOnPlane(Vector3.right, _projectionNormal);
            }

            if (fallbackDirection.sqrMagnitude < 0.0001f)
            {
                leftWorld = default;
                rightWorld = default;
                return false;
            }

            fallbackDirection.Normalize();
            float fallbackHalfWidth = 20f;
            leftWorld = clickWorld - (fallbackDirection * fallbackHalfWidth);
            rightWorld = clickWorld + (fallbackDirection * fallbackHalfWidth);
            return true;
        }
    }
}
