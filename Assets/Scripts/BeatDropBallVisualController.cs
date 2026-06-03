using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BeatDrop
{
    /// <summary>
    /// Physics-first 3D ball controller.
    ///
    /// - Drives a Rigidbody toward beat-derived target positions (spring + damping in FixedUpdate).
    /// - Applies launch impulses on tap/click/space and optional beat micro-impulses.
    /// - Supports Vertical / Horizontal / Inward modes from ModeTransitionManager.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BeatDropBallVisualController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private DspBeatSync beatSync;
        [SerializeField] private ModeTransitionManager modeManager;
        [SerializeField] private Transform ballVisual;
        [SerializeField] private Rigidbody ballBody;
        [SerializeField] private Transform lowerRing;
        [SerializeField] private Transform upperRing;
        [SerializeField] private Renderer ballRenderer;

        [Header("Mode Centers (Local)")]
        [SerializeField] private Vector3 verticalCenter = new Vector3(0f, 0.95f, 0f);
        [SerializeField] private Vector3 horizontalCenter = new Vector3(0f, 0.95f, 0f);
        [SerializeField] private Vector3 inwardCenter = new Vector3(0f, 0.55f, 0f);

        [Header("Mode Ranges")]
        [SerializeField] private float verticalTravel = 2.9f;
        [SerializeField] private float horizontalTravel = 4.8f;
        [SerializeField] private float inwardDepthTravel = 1.5f;

        [Header("Physics Follow")]
        [SerializeField] private float followSpring = 58f;
        [SerializeField] private float followDamper = 15f;
        [SerializeField] private float maxFollowAccel = 110f;

        [Header("Launch Impulses")]
        [SerializeField] private float tapLaunchImpulse = 5.6f;
        [SerializeField] private bool beatMicroImpulse = true;
        [SerializeField] private float beatImpulse = 0.75f;

        [Header("Visual Pulse")]
        [SerializeField] private Vector3 baseScale = Vector3.one;
        [SerializeField] private float scaleMin = 0.88f;
        [SerializeField] private float scaleMax = 1.16f;
        [SerializeField] private float ringBeatScaleBoost = 0.2f;
        [SerializeField] private float ringSpinSpeed = 85f;
        [SerializeField] private Color baseEmission = new Color(0f, 0f, 0f, 1f);
        [SerializeField] private float beatEmissionBoost = 0f;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock _mpb;
        private float _beatImpact;
        private Vector3 _targetWorld;

        private bool _queuedLaunch;
        private Vector3 _queuedLaunchDir;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();

            if (ballVisual == null)
            {
                ballVisual = transform;
            }

            if (ballRenderer == null && ballVisual != null)
            {
                ballRenderer = ballVisual.GetComponent<Renderer>();
            }

            if (ballBody == null && ballVisual != null)
            {
                ballBody = ballVisual.GetComponent<Rigidbody>();
            }

            if (ballBody == null && ballVisual != null)
            {
                ballBody = ballVisual.gameObject.AddComponent<Rigidbody>();
            }

            if (ballVisual != null && ballVisual.GetComponent<Collider>() == null)
            {
                ballVisual.gameObject.AddComponent<SphereCollider>();
            }

            if (ballBody != null)
            {
                ballBody.useGravity = false;
                ballBody.linearDamping = 1.1f;
                ballBody.angularDamping = 0.9f;
                ballBody.interpolation = RigidbodyInterpolation.Interpolate;
                ballBody.collisionDetectionMode = CollisionDetectionMode.Continuous;
                ballBody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
            }
        }

        private void OnEnable()
        {
            if (beatSync != null)
            {
                beatSync.OnBeatPulse += OnBeatPulse;
            }
        }

        private void OnDisable()
        {
            if (beatSync != null)
            {
                beatSync.OnBeatPulse -= OnBeatPulse;
            }
        }

        private void Update()
        {
            if (_mpb == null)
            {
                _mpb = new MaterialPropertyBlock();
            }

            float phase = beatSync != null ? beatSync.NormalizedBeatPhase : 0f;
            float tri = 1f - Mathf.Abs(phase * 2f - 1f);

            Vector3 localTarget = ComputeLocalTarget(phase);
            _targetWorld = transform.TransformPoint(localTarget);

            HandleInput();

            float scalePulse = Mathf.Lerp(scaleMin, scaleMax, tri);
            if (modeManager != null && modeManager.ActiveStableMode == ModeTransitionManager.Mode.Inward)
            {
                scalePulse *= 1.1f;
            }
            if (ballVisual != null)
            {
                ballVisual.localScale = baseScale * scalePulse;
            }

            float ringScale = 1f + (tri * ringBeatScaleBoost) + (_beatImpact * 0.09f);
            if (lowerRing != null)
            {
                lowerRing.localScale = Vector3.one * ringScale;
                lowerRing.Rotate(Vector3.forward, ringSpinSpeed * Time.deltaTime, Space.Self);
            }
            if (upperRing != null)
            {
                upperRing.localScale = Vector3.one * ringScale;
                upperRing.Rotate(Vector3.forward, -ringSpinSpeed * 0.75f * Time.deltaTime, Space.Self);
            }

            if (ballRenderer != null)
            {
                float emissionBoost = 1f + tri + (_beatImpact * beatEmissionBoost);
                ballRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissionColor, baseEmission * emissionBoost);
                ballRenderer.SetPropertyBlock(_mpb);
            }

            _beatImpact = Mathf.MoveTowards(_beatImpact, 0f, Time.deltaTime * 3.6f);
        }

        private void FixedUpdate()
        {
            if (ballBody == null)
            {
                return;
            }

            Vector3 error = _targetWorld - ballBody.position;
            Vector3 accel = (error * followSpring) - (ballBody.linearVelocity * followDamper);
            float mag = accel.magnitude;
            if (mag > maxFollowAccel && mag > 0.0001f)
            {
                accel = accel / mag * maxFollowAccel;
            }

            ballBody.AddForce(accel, ForceMode.Acceleration);

            if (_queuedLaunch)
            {
                ballBody.AddForce(_queuedLaunchDir * tapLaunchImpulse, ForceMode.Impulse);
                _queuedLaunch = false;
            }
        }

        private Vector3 ComputeLocalTarget(float phase)
        {
            ModeTransitionManager.Mode mode = modeManager != null
                ? modeManager.ActiveStableMode
                : ModeTransitionManager.Mode.Vertical;

            Vector3 pos;
            if (mode == ModeTransitionManager.Mode.Horizontal)
            {
                pos = horizontalCenter;
                pos.x += Mathf.Lerp(-horizontalTravel * 0.5f, horizontalTravel * 0.5f, phase);
            }
            else if (mode == ModeTransitionManager.Mode.Inward)
            {
                pos = inwardCenter;
                pos.z += Mathf.Lerp(-inwardDepthTravel * 0.5f, inwardDepthTravel * 0.5f, phase);
            }
            else
            {
                pos = verticalCenter;
                pos.y += Mathf.Lerp(verticalTravel * 0.5f, -verticalTravel * 0.5f, phase);
            }

            return pos;
        }

        private void HandleInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                QueueLaunch();
            }
            if (Touchscreen.current != null)
            {
                var touches = Touchscreen.current.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    if (touches[i].press.wasPressedThisFrame)
                    {
                        QueueLaunch();
                        break;
                    }
                }
            }
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                QueueLaunch();
            }
#else
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
            {
                QueueLaunch();
            }
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    if (Input.GetTouch(i).phase == UnityEngine.TouchPhase.Began)
                    {
                        QueueLaunch();
                        break;
                    }
                }
            }
#endif
        }

        private void QueueLaunch()
        {
            Vector3 dir = modeManager != null ? modeManager.LaunchDirection : Vector3.down;
            _queuedLaunch = true;
            _queuedLaunchDir = dir.normalized;
        }

        private void OnBeatPulse(float _)
        {
            _beatImpact = 1f;

            if (!beatMicroImpulse || ballBody == null)
            {
                return;
            }

            Vector3 dir = modeManager != null ? modeManager.LaunchDirection : Vector3.down;
            ballBody.AddForce(dir.normalized * beatImpulse, ForceMode.Impulse);
        }
    }
}
