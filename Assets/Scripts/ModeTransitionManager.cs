using System;
using UnityEngine;

namespace BeatDrop
{
    /// <summary>
    /// Multi-mode camera/grid state machine that matches the Beat Drop spec:
    /// Vertical, Horizontal, and Inward tunnel modes.
    ///
    /// - Uses Lerp/Slerp for smooth pose blending.
    /// - Optional plane->cylinder mesh morph for Inward mode.
    /// - Exposes launch direction for gameplay scripts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ModeTransitionManager : MonoBehaviour
    {
        public enum Mode
        {
            Vertical,
            Horizontal,
            Inward,
            Transition
        }

        [Serializable]
        private struct Pose
        {
            public Vector3 position;
            public Vector3 eulerRotation;
            public Vector3 scale;

            public Quaternion Rotation => Quaternion.Euler(eulerRotation);
        }

        [Header("Scene References")]
        [SerializeField] private Transform gameplayCamera;
        [SerializeField] private Transform backgroundGridRoot;
        [SerializeField] private Transform ballRoot;

        [Header("Transition")]
        [SerializeField] private Mode startMode = Mode.Vertical;
        [Min(0.01f)]
        [SerializeField] private float transitionDuration = 1.5f;
        [SerializeField] private bool useSmoothStep = true;

        [Header("Camera Poses")]
        [SerializeField] private Pose cameraVertical;
        [SerializeField] private Pose cameraHorizontal;
        [SerializeField] private Pose cameraInward;

        [Header("Grid Root Poses")]
        [SerializeField] private Pose gridVertical;
        [SerializeField] private Pose gridHorizontal;
        [SerializeField] private Pose gridInward;

        [Header("Optional Mesh Morph (Same Topology Required)")]
        [SerializeField] private MeshFilter morphMeshFilter;
        [SerializeField] private Mesh verticalPlaneMesh;
        [SerializeField] private Mesh inwardCylinderMesh;

        public Mode CurrentMode { get; private set; }
        public Mode TargetMode { get; private set; }
        public bool IsTransitioning => CurrentMode == Mode.Transition;
        public Mode ActiveStableMode => IsTransitioning ? TargetMode : CurrentMode;

        public event Action<Mode> OnModeEntered;

        /// <summary>
        /// Primary projectile launch axis by active mode.
        /// </summary>
        public Vector3 LaunchDirection
        {
            get
            {
                switch (ActiveStableMode)
                {
                    case Mode.Horizontal:
                        return Vector3.right;
                    case Mode.Inward:
                        return Vector3.forward;
                    default:
                        return Vector3.down;
                }
            }
        }

        private Mode _fromMode;
        private float _transitionElapsed;
        private Pose _cameraFrom;
        private Pose _cameraTo;
        private Pose _gridFrom;
        private Pose _gridTo;

        private Mesh _runtimeMorphMesh;
        private Vector3[] _planeVerts;
        private Vector3[] _cylinderVerts;
        private Vector3[] _morphedVerts;
        private bool _canMorphMesh;
        private float _currentMorphWeight;
        private float _targetMorphWeight;

        private void Awake()
        {
            PrepareMorphMesh();
            Mode initial = startMode == Mode.Transition ? Mode.Vertical : startMode;
            ApplyModeImmediate(initial);
        }

        private void Update()
        {
            if (!IsTransitioning)
            {
                return;
            }

            _transitionElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_transitionElapsed / transitionDuration);
            float eased = useSmoothStep ? (t * t * (3f - 2f * t)) : t;

            // Pose blending math:
            // P(t) = Lerp(P0, P1, t)
            // R(t) = Slerp(R0, R1, t)
            // S(t) = Lerp(S0, S1, t)
            LerpPose(gameplayCamera, _cameraFrom, _cameraTo, eased);
            LerpPose(backgroundGridRoot, _gridFrom, _gridTo, eased);

            if (_canMorphMesh)
            {
                _currentMorphWeight = Mathf.Lerp(_currentMorphWeight, _targetMorphWeight, eased);
                MorphGridMesh(_currentMorphWeight);
            }

            if (t >= 1f)
            {
                CurrentMode = TargetMode;
                OnModeEntered?.Invoke(CurrentMode);
            }
        }

        public void TransitionToVertical() => TransitionToMode(Mode.Vertical);
        public void TransitionToHorizontal() => TransitionToMode(Mode.Horizontal);
        public void TransitionToInward() => TransitionToMode(Mode.Inward);

        public void TransitionToMode(Mode mode)
        {
            if (mode == Mode.Transition)
            {
                return;
            }

            if (IsTransitioning && TargetMode == mode)
            {
                return;
            }

            if (!IsTransitioning && CurrentMode == mode)
            {
                return;
            }

            _transitionElapsed = 0f;
            _cameraFrom = CapturePose(gameplayCamera);
            _gridFrom = CapturePose(backgroundGridRoot);

            _cameraTo = GetCameraPose(mode);
            _gridTo = GetGridPose(mode);
            _targetMorphWeight = mode == Mode.Inward ? 1f : 0f;

            _fromMode = ActiveStableMode;
            TargetMode = mode;
            CurrentMode = Mode.Transition;
        }

        public void CycleNextMode()
        {
            Mode next = ActiveStableMode == Mode.Vertical
                ? Mode.Horizontal
                : (ActiveStableMode == Mode.Horizontal ? Mode.Inward : Mode.Vertical);
            TransitionToMode(next);
        }

        private Pose GetCameraPose(Mode mode)
        {
            switch (mode)
            {
                case Mode.Horizontal:
                    return cameraHorizontal;
                case Mode.Inward:
                    return cameraInward;
                default:
                    return cameraVertical;
            }
        }

        private Pose GetGridPose(Mode mode)
        {
            switch (mode)
            {
                case Mode.Horizontal:
                    return gridHorizontal;
                case Mode.Inward:
                    return gridInward;
                default:
                    return gridVertical;
            }
        }

        private void ApplyModeImmediate(Mode mode)
        {
            CurrentMode = mode;
            TargetMode = mode;
            _fromMode = mode;

            ApplyPose(gameplayCamera, GetCameraPose(mode));
            ApplyPose(backgroundGridRoot, GetGridPose(mode));

            _currentMorphWeight = mode == Mode.Inward ? 1f : 0f;
            if (_canMorphMesh)
            {
                MorphGridMesh(_currentMorphWeight);
            }

            OnModeEntered?.Invoke(mode);
        }

        private static Pose CapturePose(Transform tr)
        {
            return new Pose
            {
                position = tr.position,
                eulerRotation = tr.rotation.eulerAngles,
                scale = tr.localScale
            };
        }

        private static void ApplyPose(Transform tr, Pose pose)
        {
            if (tr == null)
            {
                return;
            }

            tr.position = pose.position;
            tr.rotation = pose.Rotation;
            tr.localScale = pose.scale;
        }

        private static void LerpPose(Transform tr, Pose from, Pose to, float t)
        {
            if (tr == null)
            {
                return;
            }

            tr.position = Vector3.Lerp(from.position, to.position, t);
            tr.rotation = Quaternion.Slerp(from.Rotation, to.Rotation, t);
            tr.localScale = Vector3.Lerp(from.scale, to.scale, t);
        }

        private void PrepareMorphMesh()
        {
            _canMorphMesh = false;

            if (morphMeshFilter == null || verticalPlaneMesh == null || inwardCylinderMesh == null)
            {
                return;
            }

            if (verticalPlaneMesh.vertexCount != inwardCylinderMesh.vertexCount)
            {
                Debug.LogWarning("ModeTransitionManager: Mesh morph disabled. Vertex counts do not match.", this);
                return;
            }

            _planeVerts = verticalPlaneMesh.vertices;
            _cylinderVerts = inwardCylinderMesh.vertices;
            _morphedVerts = new Vector3[_planeVerts.Length];

            _runtimeMorphMesh = Instantiate(verticalPlaneMesh);
            _runtimeMorphMesh.name = verticalPlaneMesh.name + "_RuntimeMorph";
            morphMeshFilter.sharedMesh = _runtimeMorphMesh;
            _canMorphMesh = true;
        }

        private void MorphGridMesh(float weight)
        {
            int count = _morphedVerts.Length;
            for (int i = 0; i < count; i++)
            {
                _morphedVerts[i] = Vector3.LerpUnclamped(_planeVerts[i], _cylinderVerts[i], weight);
            }

            _runtimeMorphMesh.vertices = _morphedVerts;
            _runtimeMorphMesh.RecalculateBounds();
        }

        private void OnDrawGizmosSelected()
        {
            if (ballRoot == null)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            Vector3 start = ballRoot.position;
            Vector3 end = start + LaunchDirection.normalized * 2f;
            Gizmos.DrawLine(start, end);
            Gizmos.DrawSphere(end, 0.05f);
        }
    }
}
