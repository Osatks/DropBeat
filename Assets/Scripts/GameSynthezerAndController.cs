using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BeatDrop
{
    /// <summary>
    /// Main gameplay orchestrator:
    /// 1) ring target/judgement flow
    /// 2) beat-timed flight solve from current ball state
    /// 3) countdown, HUD, fail state, and reactive FX hooks
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class GameSynthezerAndController : MonoBehaviour
    {
        public event System.Action<int, int> OnRingAdvanced;

        [Header("References")]
        [SerializeField] private Rigidbody ballBody;
        [SerializeField] private TrailRenderer ballTrail;
        [SerializeField] private Transform ringChainRoot;
        [SerializeField] private Transform targetRingCenter;
        [SerializeField] private GridPulseOnlyController gridPulseController;
        [SerializeField] private BeatDropEffectManager effectManager;
        [SerializeField] private ALDLevelAsset aldLevelAsset;
        [SerializeField] private DspBeatSync beatSync;
        [SerializeField] private bool allowLocalRigidbodyFallback = false;

        [Header("Timing Calibration")]
        [SerializeField] private float aldRuntimeTimingOffsetSeconds = 0f;
        [SerializeField] private bool autoCreateEffectManager = true;

        [Header("Countdown + Start")]
        [SerializeField] private bool delayStartWithCountdown = true;
        [SerializeField] private float startDelaySeconds = 3f;
        [SerializeField] private bool applyInitialImpulseOnStart = true;
        [SerializeField] private float initialStartImpulse = 5.8f;
        [SerializeField] private bool autoUseBeatSyncStartDelayForPreSongTiming = true;
        [SerializeField] private float extraPreSongLeadSeconds = 0f;

        [Header("Ball Bounds")]
        [SerializeField] private Vector3 startPosition = new Vector3(0f, 1f, 0f);
        [SerializeField] private float centerSpring = 10f;
        [SerializeField] private float centerDamping = 4f;
        [SerializeField] private float maxHorizontalOffset = 2.2f;
        [SerializeField] private float maxVerticalSpeed = 24f;
        [SerializeField] private float minHeight = 0.4f;
        [SerializeField] private float maxHeight = 250f;
        [SerializeField] private float verticalBoundarySpring = 16f;

        [Header("Hit Region + Judge")]
        [SerializeField] private bool requireInputInsideRingRegion = false;
        [SerializeField] private bool applyFallbackImpulseOnMiss = true;
        [SerializeField] private float fallbackMissImpulse = 5.2f;
        [SerializeField] private float ringRegionHorizontalRadius = 1.35f;
        [SerializeField] private float ringRegionVerticalTolerance = 0.6f;
        [SerializeField] private float perfectDistance = 0.12f;
        [SerializeField] private float greatDistance = 0.32f;
        [SerializeField] private float fallbackPerfectBeatWindowSeconds = 0.08f;
        [SerializeField] private float fallbackGreatBeatWindowSeconds = 0.2f;
        [SerializeField] private float fallbackLateBeatWindowSeconds = 0.32f;
        [SerializeField] private float minimumRuntimePerfectWindowSeconds = 0.055f;
        [SerializeField] private float minimumRuntimeGreatWindowSeconds = 0.16f;
        [SerializeField] private float minimumRuntimeLateWindowSeconds = 0.28f;

        [Header("Beat Flight Planner")]
        [SerializeField] private BeatFlightConstraints flightConstraints = default;
        [SerializeField] private bool forceSetVelocity = true;
        [SerializeField] private bool guaranteeNextRingReach = true;
        [SerializeField] private float solvedVerticalSpeedCapPadding = 1.5f;
        [SerializeField] private float perfectAssistBlend = 1f;
        [SerializeField] private float greatAssistBlend = 0.86f;
        [SerializeField] private float lateAssistBlend = 0.7f;
        [SerializeField] private float minimumAssistBlend = 0.62f;
        [SerializeField] private bool logPlanner = true;

        [Header("Jerk Flight Assist")]
        [SerializeField] private bool useJerkAssistedFlight = true;
        [SerializeField] private Vector3 jerkTargetFinalVelocity = Vector3.zero;
        [SerializeField] private float maxJerkInitialAcceleration = 85f;
        [SerializeField] private float maxJerkMagnitude = 220f;
        [SerializeField] private float jerkArrivalSnapSeconds = 0f;

        [Header("Target Visual")]
        [ColorUsage(true, true)]
        [SerializeField] private Color targetRingColor = new Color(2.2f, 0.35f, 1.55f, 1f);
        [ColorUsage(true, true)]
        [SerializeField] private Color normalRingColor = new Color(0f, 1f, 1.1f, 1f);
        [SerializeField] private bool enableTargetTimingGradient = true;
        [ColorUsage(true, true)]
        [SerializeField] private Color timingGradientCyan = new Color(0f, 1f, 1.1f, 1f);
        [ColorUsage(true, true)]
        [SerializeField] private Color timingGradientPink = new Color(2.2f, 0.35f, 1.55f, 1f);
        [SerializeField] private float timingGradientPerfectBoost = 1.15f;

        [Header("Hit FX")]
        [SerializeField] private bool lightweightWarmupRingClear = true;
        [SerializeField] private bool pulseGridOnWarmupHit = false;
        [SerializeField] private bool disableGridPlaneToggleInput = true;
        [SerializeField] private bool pulseGridOnlyOnPerfect = true;
        [SerializeField] private bool pulseGridOnGreatHit = true;
        [SerializeField] private bool projectGridPulseLikeClick = true;
        [SerializeField] private float gridPulseBehindBallDistance = 18f;
        [SerializeField] private float gridWaveformSpacing = 1.25f;

        [Header("Autoplayer")]
        [SerializeField] private bool enableAutoplayer = false;
        [SerializeField] private bool autoplayerForcePerfectHits = false;
        [SerializeField] private float autoplayerHitLeadSeconds = 0f;
        [SerializeField] private float autoplayerLateToleranceSeconds = 0.055f;
        [SerializeField] private float autoplayerWarmupDelaySeconds = 0.15f;
        [SerializeField] private bool logAutoplayer = true;

        [Header("Fail State")]
        [SerializeField] private float missFallDistanceBelowTarget = 1.4f;
        [SerializeField] private bool logGameOver = true;

        [Header("HUD")]
        [SerializeField] private bool showNoteRingSyncHud = true;
        [SerializeField] private bool showHudOnlyDuringGameplay = true;
        [SerializeField] private int hudFontSize = 20;
        [SerializeField] private Vector2 hudMargin = new Vector2(16f, 16f);

        [Header("Trail Visual")]
        [SerializeField] private bool attachTrailToBallBody = true;
        [SerializeField] private bool clearTrailOnStartPose = true;
        [ColorUsage(true, true)]
        [SerializeField] private Color trailStartColor = new Color(0.15f, 11.5f, 16f, 1f);
        [ColorUsage(true, true)]
        [SerializeField] private Color trailEndColor = new Color(7.2f, 0.8f, 9.5f, 0.35f);
        [SerializeField] private float trailTime = 0.8f;
        [SerializeField] private float trailStartWidth = 0.34f;
        [SerializeField] private float trailEndWidth = 0.09f;
        [SerializeField] private float trailMinVertexDistance = 0.004f;
        [SerializeField] private int trailCornerVertices = 4;
        [SerializeField] private int trailCapVertices = 4;
        [SerializeField] private float trailPulseSpeed = 9.2f;
        [SerializeField] private float trailPulseAmount = 0.35f;
        [SerializeField] private float trailSpeedBoost = 0.08f;
        [SerializeField] private float trailMaxWidthMultiplier = 1.75f;
        [SerializeField] private float trailWidthSmoothing = 0.07f;

        private enum HitQuality : byte
        {
            Perfect = 0,
            Great = 1,
            Late = 2,
            Miss = 3
        }

        private readonly RingSequenceRuntime _rings = new RingSequenceRuntime();
        private readonly List<float> _selectedRingHitTimes = new List<float>(256);
        private readonly List<float> _timingCorrectedRingHitTimes = new List<float>(256);
        private Material _trailMaterial;

        private bool _gameStarted;
        private bool _gameOver;
        private bool _levelCompleted;
        private bool _startSequenceRunning;
        private float _startTimerRemaining;
        private float _gameplayStartTime;
        private bool _attemptedAutoBuilderResync;
        private string _lastJudgeMessage = string.Empty;
        private float _nextRingResolveRetryTime;
        private int _lastVisualTargetIndex = -1;
        private float _runtimeVerticalSpeedCap;
        private int _lastAutoplayerRingIndex = -1;
        private float _lastAutoplayerSongTime = float.NegativeInfinity;
        private float _trailWidthMultiplier = 1f;
        private float _trailWidthVelocity;
        private string _lastArrivalMessage = string.Empty;
        private bool _jerkFlightActive;
        private BeatJerkFlightPlan _activeJerkFlight;
        private Vector3 _jerkFlightStartPosition;
        private float _jerkFlightStartSongTime;
        private float _jerkFlightStartRealtime;

        private void Awake()
        {
            EnsureReferences();
            SetupBallRigidbody();
            SetupTrail();

            // Compatibility: old scene presets used very tight vertical tolerance.
            // If strict region gating is enabled with tiny tolerance, inputs can feel dead.
            if (requireInputInsideRingRegion && ringRegionVerticalTolerance <= 0.2f)
            {
                requireInputInsideRingRegion = false;
            }

            if (disableGridPlaneToggleInput && gridPulseController != null)
            {
                gridPulseController.SetPlaneToggleInputEnabled(false);
            }

            EnsureDefaultFlightConstraintsIfNeeded();
            flightConstraints.Clamp();
            EnsureJerkFinalVelocityFitsConstraints();
            maxVerticalSpeed = Mathf.Max(maxVerticalSpeed, Mathf.Min(36f, flightConstraints.maxInitialSpeed + 2f));
            _runtimeVerticalSpeedCap = maxVerticalSpeed;
        }

        private void Start()
        {
            ReinitializeLevelFromRingChain();
            TryAutoResyncWithLevelBuilderIfNeeded();

            _gameStarted = false;
            _startSequenceRunning = delayStartWithCountdown && startDelaySeconds > 0f;
            _startTimerRemaining = _startSequenceRunning ? startDelaySeconds : 0f;
            if (!_startSequenceRunning)
            {
                BeginGameplay();
            }
        }

        private void Update()
        {
            if (_startSequenceRunning)
            {
                _startTimerRemaining -= Time.deltaTime;
                if (_startTimerRemaining <= 0f)
                {
                    _startSequenceRunning = false;
                    BeginGameplay();
                }
            }

            if ((_rings.Count == 0 || targetRingCenter == null) && Time.time >= _nextRingResolveRetryTime)
            {
                ReinitializeLevelFromRingChain();
                TryAutoResyncWithLevelBuilderIfNeeded();
                _nextRingResolveRetryTime = Time.time + 0.4f;
            }

            if (!_gameStarted || _gameOver || _levelCompleted)
            {
                return;
            }

            if (TryRunAutoplayer())
            {
                UpdateTrailVisual();
                UpdateTargetRingTimingGradient();
                TryResolveMissFail();
                return;
            }

            if (IsHitPressedThisFrame())
            {
                HandleHitInput();
            }

            UpdateTrailVisual();
            UpdateTargetRingTimingGradient();
            TryResolveMissFail();
        }
    }
}
