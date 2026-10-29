using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace DropBeat
{
    /// <summary>
    /// Test bot for the full input/judging pipeline. Injects real keyboard events into the Input System
    /// with timestamps exactly <see cref="pressOffsetMs"/> after each note is heard: Space for taps
    /// (held through hold rails and released on the end beat), D + J together for dual rings, arrow keys
    /// for swipes. Then checks the game measured each press with exactly the intended error.
    /// Add it at runtime during a non-autoplay song; results go to the log and <see cref="Report"/>.
    /// </summary>
    public sealed class TimingProbe : MonoBehaviour
    {
        public float pressOffsetMs;
        public int maxNotes = 40;

        private GameController _game;
        private int _next = -1;
        private int _pressed;
        private double _releaseAt = -1;
        private readonly List<PlayNote> _pressedNotes = new List<PlayNote>();

        public string Report { get; private set; } = "running";

        private void Start()
        {
            _game = FindAnyObjectByType<GameController>();
        }

        private void Update()
        {
            if (_game == null || _game.CurrentState != GameController.State.Playing || Keyboard.current == null) return;
            var notes = _game.Layout.Notes;
            if (_next < 0) _next = _game.Layout.FindNextNote(_game.Clock.SongTime + 0.3);

            double now = Time.realtimeSinceStartupAsDouble;
            double toInput = InputState.currentTime - now;

            if (_releaseAt > 0 && now >= _releaseAt)
            {
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(), _releaseAt + toInput);
                _releaseAt = -1;
            }

            if (_pressed >= maxNotes || _next >= notes.Count)
            {
                if (_pressed > 0 && _releaseAt < 0 && Report == "running" && AllSettled()) Summarize();
                return;
            }

            PlayNote n = notes[_next];
            if (n.judged) { _next++; return; }
            double target = _game.Clock.RealtimeAtSongTime(n.time + pressOffsetMs * 0.001);
            if (now < target || _releaseAt > 0) return;

            Key[] keys;
            if (n.type == NoteType.Swipe)
            {
                keys = new[] { n.swipe == SwipeDir.Left ? Key.LeftArrow : n.swipe == SwipeDir.Right ? Key.RightArrow : Key.UpArrow };
            }
            else if (n.partner != null)
            {
                keys = new[] { Key.D, Key.J };
                _pressedNotes.Add(n.partner);
            }
            else
            {
                keys = new[] { Key.Space };
            }

            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys), target + toInput);
            _pressedNotes.Add(n);
            _pressed++;
            _releaseAt = n.type == NoteType.Hold ? _game.Clock.RealtimeAtSongTime(n.endTime) : target + 0.03;
            _next++;
            if (n.partner != null && _next < notes.Count && notes[_next] == n.partner) _next++;
        }

        private bool AllSettled()
        {
            foreach (var n in _pressedNotes)
            {
                if (!n.judged) return false;
                if (n.type == NoteType.Hold && !n.tailJudged) return false;
            }
            return true;
        }

        private void Summarize()
        {
            double sum = 0, sumSq = 0, maxAbs = 0;
            int n = 0, perfect = 0, holds = 0, holdOk = 0, swipes = 0, swipeOk = 0, duals = 0, dualOk = 0;
            foreach (PlayNote note in _pressedNotes)
            {
                if (note.type == NoteType.Hold)
                {
                    holds++;
                    if (note.tailJudged && note.tailGrade != Grade.Miss) holdOk++;
                }
                if (note.type == NoteType.Swipe)
                {
                    swipes++;
                    if (note.grade != Grade.Miss) swipeOk++;
                }
                if (note.type == NoteType.Dual)
                {
                    duals++;
                    if (note.grade != Grade.Miss) dualOk++;
                }
                if (!note.judged || double.IsNaN(note.error)) continue;
                double dev = note.error * 1000.0 - pressOffsetMs;
                sum += dev;
                sumSq += dev * dev;
                maxAbs = System.Math.Max(maxAbs, System.Math.Abs(dev));
                if (note.grade == Grade.Perfect) perfect++;
                n++;
            }

            double mean = n > 0 ? sum / n : 0;
            double rms = n > 0 ? System.Math.Sqrt(sumSq / n) : 0;
            Report = "TimingProbe offset " + pressOffsetMs.ToString("+0;-0;0", CultureInfo.InvariantCulture) + "ms: " +
                     n + "/" + _pressedNotes.Count + " timed, error vs intended mean " + mean.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) +
                     "ms rms " + rms.ToString("F2", CultureInfo.InvariantCulture) + "ms max " + maxAbs.ToString("F2", CultureInfo.InvariantCulture) +
                     "ms, perfect " + perfect + " | holds " + holdOk + "/" + holds + " swipes " + swipeOk + "/" + swipes + " dual halves " + dualOk + "/" + duals +
                     " | misses " + _game.Score.Miss + ", fps " + (1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)).ToString("F0");
            Debug.Log("[DropBeat] " + Report);
        }
    }
}
