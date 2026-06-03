using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace BeatDrop
{
    /// <summary>
    /// Playable rhythm loop controller:
    /// - Schedules song with DspBeatSync.
    /// - Spawns notes from chart using lead time.
    /// - Moves notes by closed-form DSP position solve each frame.
    /// - Judges tap input in deterministic time windows.
    /// </summary>
    public sealed class BeatDropGameController : MonoBehaviour
    {
        public event Action<int, HitGrade, LaneId> OnNoteJudged;

        [Header("References")]
        [SerializeField] private DspBeatSync beatSync;
        [SerializeField] private NoteChart noteChart;
        [SerializeField] private RhythmNotePool notePool;

        [Header("Lane Anchors")]
        [SerializeField] private Transform leftLaneSpawn;
        [SerializeField] private Transform rightLaneSpawn;
        [SerializeField] private Transform leftLaneHit;
        [SerializeField] private Transform rightLaneHit;

        [Header("Timing")]
        [Min(0.05f)]
        [SerializeField] private float noteTravelTimeSeconds = 1.2f;
        [Min(0.01f)]
        [SerializeField] private float despawnLateSeconds = 0.2f;

        [Header("Hit Windows")]
        [Min(0.001f)]
        [SerializeField] private float perfectWindowSeconds = 0.035f;
        [Min(0.001f)]
        [SerializeField] private float goodWindowSeconds = 0.08f;

        [Header("Input")]
        [SerializeField] private KeyCode leftKey = KeyCode.A;
        [SerializeField] private KeyCode rightKey = KeyCode.L;

        private readonly List<RhythmNote> _activeNotes = new List<RhythmNote>(256);
        private int _nextSpawnIndex;
        private int _score;
        private int _combo;

        public int Score => _score;
        public int Combo => _combo;

        private double SongTime
        {
            get
            {
                return AudioSettings.dspTime - _songStartDsp;
            }
        }

        private void Start()
        {
            if (!ValidateRefs())
            {
                enabled = false;
                return;
            }

            notePool.Initialize();
            ResetRunState();
            beatSync.StartClock();
            _songStartDsp = beatSync.SongStartDspTime;
        }

        private void Update()
        {
            double now = SongTime;
            SpawnDueNotes(now);
            UpdateActiveNotes(now);
            ProcessInput(now);
        }

        private bool ValidateRefs()
        {
            return beatSync != null && noteChart != null && notePool != null &&
                   leftLaneSpawn != null && rightLaneSpawn != null &&
                   leftLaneHit != null && rightLaneHit != null;
        }

        private void ResetRunState()
        {
            _nextSpawnIndex = 0;
            _score = 0;
            _combo = 0;
            for (int i = _activeNotes.Count - 1; i >= 0; i--)
            {
                notePool.Return(_activeNotes[i]);
            }
            _activeNotes.Clear();
        }

        private void SpawnDueNotes(double now)
        {
            double spawnThreshold = now + noteTravelTimeSeconds;
            int total = noteChart.Count;

            while (_nextSpawnIndex < total)
            {
                NoteData noteData = noteChart[_nextSpawnIndex];
                if (noteData.hitTimeSeconds > spawnThreshold)
                {
                    break;
                }

                Vector3 spawnPos = noteData.lane == LaneId.Left ? leftLaneSpawn.position : rightLaneSpawn.position;
                RhythmNote note = notePool.Rent();
                note.Activate(noteData.lane, noteData.hitTimeSeconds, spawnPos);
                _activeNotes.Add(note);

                _nextSpawnIndex++;
            }
        }

        private void UpdateActiveNotes(double now)
        {
            Vector3 leftSpawn = leftLaneSpawn.position;
            Vector3 rightSpawn = rightLaneSpawn.position;
            Vector3 leftHit = leftLaneHit.position;
            Vector3 rightHit = rightLaneHit.position;

            for (int i = _activeNotes.Count - 1; i >= 0; i--)
            {
                RhythmNote note = _activeNotes[i];
                double timeUntilHit = note.HitTime - now;

                // Normalized travel solve:
                // t=0 at spawn moment, t=1 at hit moment.
                float t = 1f - (float)(timeUntilHit / noteTravelTimeSeconds);
                t = Mathf.Clamp01(t);

                Vector3 from = note.Lane == LaneId.Left ? leftSpawn : rightSpawn;
                Vector3 to = note.Lane == LaneId.Left ? leftHit : rightHit;
                note.SetWorldPosition(Vector3.Lerp(from, to, t));

                if (timeUntilHit < -despawnLateSeconds)
                {
                    RegisterMiss();
                    OnNoteJudged?.Invoke(0, HitGrade.Miss, note.Lane);
                    notePool.Return(note);
                    _activeNotes.RemoveAt(i);
                }
            }
        }

        private void ProcessInput(double now)
        {
#if ENABLE_INPUT_SYSTEM
            ProcessInputSystem(now);
#else
            ProcessLegacyInput(now);
#endif
        }

        private void ProcessLegacyInput(double now)
        {
            if (Input.GetKeyDown(leftKey))
            {
                TryHitLane(LaneId.Left, now);
            }

            if (Input.GetKeyDown(rightKey))
            {
                TryHitLane(LaneId.Right, now);
            }

            if (Input.touchCount <= 0)
            {
                return;
            }

            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase != UnityEngine.TouchPhase.Began)
                {
                    continue;
                }

                LaneId lane = touch.position.x < (Screen.width * 0.5f) ? LaneId.Left : LaneId.Right;
                TryHitLane(lane, now);
            }
        }

#if ENABLE_INPUT_SYSTEM
        private void ProcessInputSystem(double now)
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (WasKeyPressedThisFrame(kb, leftKey))
                {
                    TryHitLane(LaneId.Left, now);
                }

                if (WasKeyPressedThisFrame(kb, rightKey))
                {
                    TryHitLane(LaneId.Right, now);
                }
            }

            Touchscreen ts = Touchscreen.current;
            if (ts == null)
            {
                return;
            }

            var touches = ts.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                if (!touch.press.wasPressedThisFrame)
                {
                    continue;
                }

                Vector2 pos = touch.position.ReadValue();
                LaneId lane = pos.x < (Screen.width * 0.5f) ? LaneId.Left : LaneId.Right;
                TryHitLane(lane, now);
            }
        }

        private static bool WasKeyPressedThisFrame(Keyboard kb, KeyCode keyCode)
        {
            Key key = ConvertKeyCode(keyCode);
            if (key == Key.None)
            {
                return false;
            }

            KeyControl control = kb[key];
            return control != null && control.wasPressedThisFrame;
        }

        private static Key ConvertKeyCode(KeyCode code)
        {
            switch (code)
            {
                case KeyCode.A: return Key.A;
                case KeyCode.B: return Key.B;
                case KeyCode.C: return Key.C;
                case KeyCode.D: return Key.D;
                case KeyCode.E: return Key.E;
                case KeyCode.F: return Key.F;
                case KeyCode.G: return Key.G;
                case KeyCode.H: return Key.H;
                case KeyCode.I: return Key.I;
                case KeyCode.J: return Key.J;
                case KeyCode.K: return Key.K;
                case KeyCode.L: return Key.L;
                case KeyCode.M: return Key.M;
                case KeyCode.N: return Key.N;
                case KeyCode.O: return Key.O;
                case KeyCode.P: return Key.P;
                case KeyCode.Q: return Key.Q;
                case KeyCode.R: return Key.R;
                case KeyCode.S: return Key.S;
                case KeyCode.T: return Key.T;
                case KeyCode.U: return Key.U;
                case KeyCode.V: return Key.V;
                case KeyCode.W: return Key.W;
                case KeyCode.X: return Key.X;
                case KeyCode.Y: return Key.Y;
                case KeyCode.Z: return Key.Z;
                default: return Key.None;
            }
        }
#endif

        private void TryHitLane(LaneId lane, double now)
        {
            int bestIndex = -1;
            double bestAbsError = double.MaxValue;

            for (int i = 0; i < _activeNotes.Count; i++)
            {
                RhythmNote note = _activeNotes[i];
                if (note.Lane != lane)
                {
                    continue;
                }

                double err = now - note.HitTime;
                double absErr = Math.Abs(err);

                // Only consider notes within max judge window.
                if (absErr > goodWindowSeconds)
                {
                    continue;
                }

                if (absErr < bestAbsError)
                {
                    bestAbsError = absErr;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                RegisterMiss();
                OnNoteJudged?.Invoke(0, HitGrade.Miss, lane);
                return;
            }

            RhythmNote target = _activeNotes[bestIndex];
            double hitError = now - target.HitTime;
            HitGrade grade = HitJudge.Evaluate(hitError, perfectWindowSeconds, goodWindowSeconds);

            if (grade == HitGrade.Miss)
            {
                RegisterMiss();
                OnNoteJudged?.Invoke(0, HitGrade.Miss, lane);
                return;
            }

            int scoreDelta;
            if (grade == HitGrade.Perfect)
            {
                scoreDelta = 100;
                _score += scoreDelta;
                _combo += 1;
            }
            else
            {
                scoreDelta = 60;
                _score += scoreDelta;
                _combo += 1;
            }

            OnNoteJudged?.Invoke(scoreDelta, grade, lane);
            notePool.Return(target);
            _activeNotes.RemoveAt(bestIndex);
        }

        private void RegisterMiss()
        {
            _combo = 0;
        }

        private double _songStartDsp;
    }
}
