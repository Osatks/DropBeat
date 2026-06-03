using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BeatDrop
{
    /// <summary>
    /// Click/tap-driven circular burst for XZ plane mode.
    /// Circle expands over time while its circumference is deformed by a traveling sine wave.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class CircularWaveBurstController : MonoBehaviour
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
            SmoothSynthwave,
            Custom
        }

        [System.Serializable]
        private struct HarmonicTerm
        {
            public int harmonicNumber;
            public float interHarmonicOffset;
            public float strength;
            public float phaseOffset;
        }

        [Header("References")]
        public Camera inputCamera;
        public Material lineMaterialOverride;

        [Header("Plane Rules")]
        public GridPulseOnlyController gridController;
        public bool runOnlyInXZPlane = true;

        [Header("Circle Shape")]
        [Min(16)] public int pointsCount = 128;
        [Min(0.01f)] public float startRadius = 0.35f;
        [Min(0.01f)] public float expansionSpeed = 7.5f;
        [Min(0f)] public float maxWaveAmplitude = 0.6f;
        [Min(0.1f)] public float circumferenceFrequency = 8f;
        public float phaseScrollSpeed = 8f;

        [Header("Harmonics")]
        [SerializeField] private HarmonicMode harmonicMode = HarmonicMode.PresetList;
        [SerializeField] private HarmonicPreset harmonicPreset = HarmonicPreset.SmoothSynthwave;
        public Vector2 randomHarmonic2StrengthRange = new Vector2(0.15f, 0.55f);
        public Vector2 randomHarmonic3StrengthRange = new Vector2(0.05f, 0.30f);
        public Vector2 randomPhaseOffsetRange = new Vector2(-3.14159f, 3.14159f);
        [SerializeField] private HarmonicTerm[] customHarmonics = new HarmonicTerm[]
        {
            new HarmonicTerm { harmonicNumber = 1, interHarmonicOffset = 0f, strength = 1f, phaseOffset = 0f },
            new HarmonicTerm { harmonicNumber = 2, interHarmonicOffset = 0f, strength = 0.24f, phaseOffset = 0.28f },
            new HarmonicTerm { harmonicNumber = 3, interHarmonicOffset = 0f, strength = 0.08f, phaseOffset = 0.72f }
        };

        [Header("Burst")]
        [Min(0.01f)] public float attackDuration = 0.12f;
        [Min(0.01f)] public float fadeDuration = 0.6f;
        public bool requireShiftForMouseClick = false;

        [Header("Projection")]
        [Min(0f)] public float planeSurfaceOffset = 0.02f;
        public Transform projectionPlaneTransform;
        public float projectionPlaneLocalDepth = 42f;

        [Header("Visual")]
        [ColorUsage(true, true)]
        public Color neonColor = new Color(1f, 0f, 1f, 1f);
        [Min(0f)] public float hdrIntensity = 5f;
        public bool forceTransparentMaterialForAlphaFade = true;

        private LineRenderer _line;
        private Camera _mainCamera;
        private Material _lineMaterial;

        private Vector3[] _positions;
        private Vector3 _center;
        private Vector3 _planeNormal;
        private Vector3 _axisA;
        private Vector3 _axisB;

        private float _currentAlpha;
        private float _currentAmplitude;
        private float _burstElapsed;
        private bool _isBurstActive;
        private Coroutine _burstRoutine;
        private float _activeHarmonic2Strength;
        private float _activeHarmonic3Strength;
        private float _activeHarmonic2PhaseOffset;
        private float _activeHarmonic3PhaseOffset;

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            TryResolveCamera();

            pointsCount = Mathf.Max(16, pointsCount);
            _positions = new Vector3[pointsCount + 1];

            _line.useWorldSpace = true;
            _line.loop = true;
            _line.positionCount = _positions.Length;
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
            pointsCount = Mathf.Max(16, pointsCount);
            startRadius = Mathf.Max(0.01f, startRadius);
            expansionSpeed = Mathf.Max(0.01f, expansionSpeed);
            maxWaveAmplitude = Mathf.Max(0f, maxWaveAmplitude);
            circumferenceFrequency = Mathf.Max(0.1f, circumferenceFrequency);
            attackDuration = Mathf.Max(0.01f, attackDuration);
            fadeDuration = Mathf.Max(0.01f, fadeDuration);
            hdrIntensity = Mathf.Max(0f, hdrIntensity);
            planeSurfaceOffset = Mathf.Max(0f, planeSurfaceOffset);
            NormalizeRange(ref randomHarmonic2StrengthRange, 0f);
            NormalizeRange(ref randomHarmonic3StrengthRange, 0f);
            NormalizeRange(ref randomPhaseOffsetRange, -100f);
            SanitizeCustomHarmonics();

            if (_positions == null || _positions.Length != pointsCount + 1)
            {
                _positions = new Vector3[pointsCount + 1];
            }

            if (_line != null)
            {
                _line.loop = true;
                _line.positionCount = _positions.Length;
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

            if (runOnlyInXZPlane && gridController != null && !gridController.IsXZPlaneActive)
            {
                ForceStopBurst();
                return;
            }

            if (TryGetPointerDownScreenPosition(out Vector2 screenPosition) && !IsPointerOverUi())
            {
                StartBurstAtScreenPosition(screenPosition);
            }

            if (_isBurstActive)
            {
                _burstElapsed += Time.deltaTime;
                RenderCircle();
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

        private void StartBurstAtScreenPosition(Vector2 screenPosition)
        {
            if (!TryBuildProjectionPlane(out Plane plane))
            {
                return;
            }

            if (!plane.Raycast(_mainCamera.ScreenPointToRay(screenPosition), out float enter))
            {
                return;
            }

            _center = _mainCamera.ScreenPointToRay(screenPosition).GetPoint(enter);

            BuildPlaneBasis(_planeNormal, out _axisA, out _axisB);

            _burstElapsed = 0f;
            _currentAlpha = 0f;
            _currentAmplitude = 0f;
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

        private void RenderCircle()
        {
            float baseRadius = startRadius + (_burstElapsed * expansionSpeed);
            float timePhase = Time.time * phaseScrollSpeed;
            float step = Mathf.PI * 2f / pointsCount;

            for (int i = 0; i < pointsCount; i++)
            {
                float ang = i * step;
                float theta = (ang * circumferenceFrequency) + timePhase;
                float wave = _currentAmplitude * SampleHarmonicWave(theta);
                float radius = Mathf.Max(0.01f, baseRadius + wave);

                Vector3 radialDir = (_axisA * Mathf.Cos(ang)) + (_axisB * Mathf.Sin(ang));
                _positions[i] = _center + (radialDir * radius);
            }

            _positions[pointsCount] = _positions[0];
            _line.SetPositions(_positions);
        }

        private IEnumerator BurstEnvelopeRoutine()
        {
            float a = 0f;
            while (a < attackDuration)
            {
                a += Time.deltaTime;
                float t = Mathf.Clamp01(a / attackDuration);
                float s = Mathf.SmoothStep(0f, 1f, t);

                _currentAlpha = s;
                _currentAmplitude = Mathf.Lerp(0f, maxWaveAmplitude, s);
                ApplyLineColor(_currentAlpha);
                yield return null;
            }

            float d = 0f;
            while (d < fadeDuration)
            {
                d += Time.deltaTime;
                float t = Mathf.Clamp01(d / fadeDuration);
                float s = Mathf.SmoothStep(0f, 1f, t);

                _currentAlpha = Mathf.Lerp(1f, 0f, s);
                _currentAmplitude = Mathf.Lerp(maxWaveAmplitude, 0f, s);
                ApplyLineColor(_currentAlpha);
                yield return null;
            }

            ForceStopBurst();
        }

        private float SampleHarmonicWave(float theta)
        {
            if (harmonicMode == HarmonicMode.PresetList && harmonicPreset == HarmonicPreset.Custom)
            {
                if (customHarmonics == null || customHarmonics.Length == 0)
                {
                    return Mathf.Sin(theta);
                }

                float sum = 0f;
                float norm = 0f;
                for (int i = 0; i < customHarmonics.Length; i++)
                {
                    float n = customHarmonics[i].harmonicNumber + customHarmonics[i].interHarmonicOffset;
                    if (Mathf.Abs(n) < 0.000001f)
                    {
                        continue;
                    }
                    float strength = customHarmonics[i].strength;
                    sum += Mathf.Sin((theta * n) + customHarmonics[i].phaseOffset) * strength;
                    norm += Mathf.Abs(strength);
                }

                if (norm < 0.0001f)
                {
                    return 0f;
                }

                return sum / norm;
            }

            float fundamental = Mathf.Sin(theta);
            float harmonic2 = Mathf.Sin((theta * 2f) + _activeHarmonic2PhaseOffset) * _activeHarmonic2Strength;
            float harmonic3 = Mathf.Sin((theta * 3f) + _activeHarmonic3PhaseOffset) * _activeHarmonic3Strength;
            float normalization = Mathf.Max(1f, 1f + _activeHarmonic2Strength + _activeHarmonic3Strength);
            return (fundamental + harmonic2 + harmonic3) / normalization;
        }

        private void ConfigureHarmonicsForBurst()
        {
            if (harmonicMode == HarmonicMode.RandomPerBurst)
            {
                _activeHarmonic2Strength = Random.Range(randomHarmonic2StrengthRange.x, randomHarmonic2StrengthRange.y);
                _activeHarmonic3Strength = Random.Range(randomHarmonic3StrengthRange.x, randomHarmonic3StrengthRange.y);
                _activeHarmonic2PhaseOffset = Random.Range(randomPhaseOffsetRange.x, randomPhaseOffsetRange.y);
                _activeHarmonic3PhaseOffset = Random.Range(randomPhaseOffsetRange.x, randomPhaseOffsetRange.y);
                return;
            }

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

                case HarmonicPreset.Custom:
                    _activeHarmonic2Strength = 0f;
                    _activeHarmonic3Strength = 0f;
                    _activeHarmonic2PhaseOffset = 0f;
                    _activeHarmonic3PhaseOffset = 0f;
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

        private void SanitizeCustomHarmonics()
        {
            if (customHarmonics == null)
            {
                customHarmonics = new HarmonicTerm[0];
                return;
            }

            for (int i = 0; i < customHarmonics.Length; i++)
            {
                if (customHarmonics[i].harmonicNumber < 0)
                {
                    customHarmonics[i].harmonicNumber = 0;
                }
            }
        }

        private void ApplyLineColor(float alpha)
        {
            Color c = neonColor * hdrIntensity;
            c.a = alpha;

            _line.startColor = c;
            _line.endColor = c;

            if (_lineMaterial == null)
            {
                return;
            }

            if (_lineMaterial.HasProperty("_BaseColor"))
            {
                _lineMaterial.SetColor("_BaseColor", c);
            }
            else if (_lineMaterial.HasProperty("_Color"))
            {
                _lineMaterial.SetColor("_Color", c);
            }

            if (_lineMaterial.HasProperty("_EmissionColor"))
            {
                Color e = neonColor * (hdrIntensity * Mathf.Clamp01(alpha));
                e.a = alpha;
                _lineMaterial.SetColor("_EmissionColor", e);
            }
        }

        private void ConfigureMaterialForAlphaFade()
        {
            if (!forceTransparentMaterialForAlphaFade || _lineMaterial == null)
            {
                return;
            }

            if (_lineMaterial.HasProperty("_Surface")) _lineMaterial.SetFloat("_Surface", 1f);
            if (_lineMaterial.HasProperty("_Blend")) _lineMaterial.SetFloat("_Blend", 0f);
            if (_lineMaterial.HasProperty("_ZWrite")) _lineMaterial.SetFloat("_ZWrite", 0f);
            if (_lineMaterial.HasProperty("_SrcBlend")) _lineMaterial.SetFloat("_SrcBlend", 5f);
            if (_lineMaterial.HasProperty("_DstBlend")) _lineMaterial.SetFloat("_DstBlend", 10f);
            _lineMaterial.renderQueue = 3000;
        }

        private void BuildPlaneBasis(Vector3 normal, out Vector3 axisA, out Vector3 axisB)
        {
            axisA = Vector3.ProjectOnPlane(_mainCamera.transform.right, normal);
            if (axisA.sqrMagnitude < 0.0001f)
            {
                axisA = Vector3.ProjectOnPlane(Vector3.right, normal);
            }
            axisA.Normalize();

            axisB = Vector3.Cross(normal, axisA).normalized;
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

        private bool TryResolveProjectionPlane()
        {
            if (projectionPlaneTransform != null)
            {
                return true;
            }

            if (gridController == null)
            {
                GameObject go = GameObject.Find("GridPulseRoot");
                if (go != null)
                {
                    gridController = go.GetComponent<GridPulseOnlyController>();
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

            return false;
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
                normal = transform.forward;
                point = transform.position;
            }

            if (normal.sqrMagnitude < 0.0001f)
            {
                plane = default;
                return false;
            }

            _planeNormal = normal.normalized;
            Vector3 toCamera = _mainCamera != null
                ? (_mainCamera.transform.position - point)
                : Vector3.zero;
            float side = Mathf.Sign(Vector3.Dot(_planeNormal, toCamera));
            if (Mathf.Approximately(side, 0f))
            {
                side = 1f;
            }

            point += _planeNormal * (planeSurfaceOffset * side);
            plane = new Plane(_planeNormal, point);
            return true;
        }

        private void ForceStopBurst()
        {
            _isBurstActive = false;
            _currentAmplitude = 0f;
            _currentAlpha = 0f;
            if (_burstRoutine != null)
            {
                StopCoroutine(_burstRoutine);
                _burstRoutine = null;
            }
            if (_line != null)
            {
                _line.enabled = false;
            }
        }
    }
}
