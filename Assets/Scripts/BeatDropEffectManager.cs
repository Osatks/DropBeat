using UnityEngine;

namespace BeatDrop
{
    /// <summary>
    /// Central place for gameplay-reactive visual effects.
    /// Keeps the gameplay controller from owning grid/sine-wave implementation details.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BeatDropEffectManager : MonoBehaviour
    {
        private const string ManagerName = "EffectManager";
        private const string SineWaveChildName = "SineWaveEffect";

        [Header("References")]
        [SerializeField] private GridPulseOnlyController gridPulseController;
        [SerializeField] private SineWaveBurstController sineWaveEffect;
        [SerializeField] private Camera targetCamera;

        [Header("Auto Setup")]
        [SerializeField] private bool autoCreateSineWaveChild = true;
        [SerializeField] private bool autoFindReferences = true;

        [Header("Perfect Hit Wave")]
        [SerializeField] private bool triggerGridPulse = true;
        [SerializeField] private bool triggerGridWaveform = true;
        [SerializeField] private bool triggerSineWaveLine = true;
        [SerializeField] private bool projectLikeScreenClick = true;
        [SerializeField] private float fallbackBehindBallDistance = 18f;
        [SerializeField] private float waveformLaneSpacing = 1.25f;

        private void Awake()
        {
            ResolveReferences();
            EnsureSineWaveChild();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            fallbackBehindBallDistance = Mathf.Max(0f, fallbackBehindBallDistance);
            waveformLaneSpacing = Mathf.Max(0.05f, waveformLaneSpacing);

            if (!Application.isPlaying)
            {
                ResolveReferences();
            }
        }
#endif

        public static BeatDropEffectManager FindOrCreate()
        {
            BeatDropEffectManager manager = Object.FindAnyObjectByType<BeatDropEffectManager>();
            if (manager != null)
            {
                manager.ResolveReferences();
                manager.EnsureSineWaveChild();
                return manager;
            }

            GameObject existing = GameObject.Find(ManagerName);
            if (existing == null)
            {
                existing = new GameObject(ManagerName);
            }

            manager = existing.GetComponent<BeatDropEffectManager>();
            if (manager == null)
            {
                manager = existing.AddComponent<BeatDropEffectManager>();
            }

            manager.ResolveReferences();
            manager.EnsureSineWaveChild();
            return manager;
        }

        [ContextMenu("Ensure Sine Wave Child")]
        public void EnsureSineWaveChildNow()
        {
            ResolveReferences();
            EnsureSineWaveChild();
        }

        public void TriggerPerfectHit(Vector3 ballWorldPosition, float quality01)
        {
            ResolveReferences();
            EnsureSineWaveChild();

            Vector3 effectPoint = ResolveEffectPoint(ballWorldPosition);

            if (triggerGridPulse && gridPulseController != null)
            {
                gridPulseController.TriggerPulseAtWorldPoint(effectPoint);
            }

            if (triggerGridWaveform && gridPulseController != null)
            {
                gridPulseController.TriggerWaveformAtWorldPoint(effectPoint, waveformLaneSpacing);
            }

            if (triggerSineWaveLine && sineWaveEffect != null)
            {
                sineWaveEffect.TriggerBurstAtWorldPoint(effectPoint);
            }
        }

        public void Configure(
            GridPulseOnlyController grid,
            Camera camera,
            bool projectLikeClick,
            float behindBallDistance,
            float waveformSpacing)
        {
            if (grid != null)
            {
                gridPulseController = grid;
            }

            if (camera != null)
            {
                targetCamera = camera;
            }

            projectLikeScreenClick = projectLikeClick;
            fallbackBehindBallDistance = Mathf.Max(0f, behindBallDistance);
            waveformLaneSpacing = Mathf.Max(0.05f, waveformSpacing);
            ResolveReferences();
            EnsureSineWaveChild();
        }

        private Vector3 ResolveEffectPoint(Vector3 ballWorldPosition)
        {
            if (projectLikeScreenClick && gridPulseController != null &&
                gridPulseController.TryProjectWorldPointToGrid(ballWorldPosition, out Vector3 projected))
            {
                return projected;
            }

            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            Vector3 backDirection = cam != null ? cam.transform.forward : Vector3.forward;
            return ballWorldPosition + (backDirection.normalized * Mathf.Max(0f, fallbackBehindBallDistance));
        }

        private void ResolveReferences()
        {
            if (!autoFindReferences)
            {
                return;
            }

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (gridPulseController == null)
            {
                gridPulseController = Object.FindAnyObjectByType<GridPulseOnlyController>();
            }

            if (sineWaveEffect == null)
            {
                sineWaveEffect = GetComponentInChildren<SineWaveBurstController>(true);
            }
        }

        private void EnsureSineWaveChild()
        {
            if (!autoCreateSineWaveChild)
            {
                return;
            }

            if (sineWaveEffect != null)
            {
                ConfigureSineWave(sineWaveEffect);
                return;
            }

            Transform child = transform.Find(SineWaveChildName);
            GameObject sineObject = child != null ? child.gameObject : new GameObject(SineWaveChildName);
            if (child == null)
            {
                sineObject.transform.SetParent(transform, false);
            }

            LineRenderer line = sineObject.GetComponent<LineRenderer>();
            if (line == null)
            {
                line = sineObject.AddComponent<LineRenderer>();
            }

            ConfigureLineRenderer(line);
            sineWaveEffect = sineObject.GetComponent<SineWaveBurstController>();
            if (sineWaveEffect == null)
            {
                sineWaveEffect = sineObject.AddComponent<SineWaveBurstController>();
            }

            ConfigureSineWave(sineWaveEffect);
        }

        private void ConfigureSineWave(SineWaveBurstController sine)
        {
            if (sine == null)
            {
                return;
            }

            sine.inputCamera = targetCamera != null ? targetCamera : Camera.main;
            sine.gridController = gridPulseController;
            sine.autoFindGridPlane = true;
            sine.requireShiftForMouseClick = true;
            sine.maxAmplitude = Mathf.Max(sine.maxAmplitude, 1.15f);
            sine.hdrIntensity = Mathf.Max(sine.hdrIntensity, 5f);
        }

        private static void ConfigureLineRenderer(LineRenderer line)
        {
            if (line == null)
            {
                return;
            }

            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.startWidth = 0.08f;
            line.endWidth = 0.08f;

            if (line.sharedMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader != null)
            {
                Material material = new Material(shader)
                {
                    name = "Runtime_EffectManager_SineWaveLine"
                };
                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", new Color(1f, 0f, 1f, 1f));
                }
                else if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", new Color(1f, 0f, 1f, 1f));
                }
                line.sharedMaterial = material;
            }
        }
    }
}
