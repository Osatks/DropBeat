using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace DropBeat
{
    /// <summary>
    /// Test bot for the timing pipeline. Injects real Space key events into the Input System with
    /// timestamps exactly <see cref="pressOffsetMs"/> after each note is heard, then checks that the
    /// game judged each tap with that same error. Proves taps are measured from their event time
    /// (independent of frame rate) and converted to song time without bias.
    /// Add it at runtime during a non-autoplay song; results are logged and kept in <see cref="Report"/>.
    /// </summary>
    public sealed class TimingProbe : MonoBehaviour
    {
        public float pressOffsetMs;
        public float jitterMs;
        public int maxNotes = 40;

        private GameController _game;
        private int _next = -1;
        private int _pressed;
        private bool _keyDown;
        private double _releaseAt;
        private readonly List<int> _pressedNotes = new List<int>();
        private readonly List<double> _intendedMs = new List<double>();
        private System.Random _rng;

        public string Report { get; private set; } = "running";

        private void Start()
        {
            _game = FindAnyObjectByType<GameController>();
            _rng = new System.Random(11);
        }

        private void Update()
        {
            if (_game == null || _game.CurrentState != GameController.State.Playing || Keyboard.current == null) return;
            var notes = _game.Layout.Notes;
            if (_next < 0) _next = _game.Layout.FindNextNote(_game.Clock.SongTime + 0.3);

            double now = Time.realtimeSinceStartupAsDouble;
            double toInputTime = InputState.currentTime - now;

            if (_keyDown && now >= _releaseAt)
            {
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(), now + toInputTime);
                _keyDown = false;
            }

            if (_pressed >= maxNotes || _next >= notes.Count)
            {
                if (_pressed > 0 && !_keyDown && Report == "running") Summarize();
                return;
            }

            double jitter = jitterMs > 0f ? (_rng.NextDouble() * 2.0 - 1.0) * jitterMs * 0.001 : 0.0;
            double target = _game.Clock.RealtimeAtSongTime(notes[_next].time + pressOffsetMs * 0.001 + jitter);
            if (!_keyDown && now >= target)
            {
                // Event stamped at the exact intended moment, even though we only get to queue it this frame.
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.Space), target + toInputTime);
                _keyDown = true;
                _releaseAt = now + 0.03;
                _pressedNotes.Add(_next);
                _intendedMs.Add(pressOffsetMs + jitter * 1000.0);
                _pressed++;
                _next++;
            }
        }

        private void Summarize()
        {
            var notes = _game.Layout.Notes;
            double sum = 0, sumSq = 0, maxAbs = 0;
            int n = 0, perfect = 0;
            for (int k = 0; k < _pressedNotes.Count; k++)
            {
                PlayNote note = notes[_pressedNotes[k]];
                if (!note.judged || double.IsNaN(note.error)) continue;
                double dev = note.error * 1000.0 - _intendedMs[k];
                sum += dev;
                sumSq += dev * dev;
                maxAbs = System.Math.Max(maxAbs, System.Math.Abs(dev));
                if (note.grade == Grade.Perfect) perfect++;
                n++;
            }

            double mean = n > 0 ? sum / n : 0;
            double rms = n > 0 ? System.Math.Sqrt(sumSq / n) : 0;
            Report = "TimingProbe offset " + pressOffsetMs.ToString("+0;-0;0", CultureInfo.InvariantCulture) + "ms jitter " + jitterMs + "ms: " +
                     n + "/" + _pressedNotes.Count + " taps judged, measured-minus-intended mean " + mean.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) +
                     "ms rms " + rms.ToString("F2", CultureInfo.InvariantCulture) + "ms max " + maxAbs.ToString("F2", CultureInfo.InvariantCulture) +
                     "ms, perfect " + perfect + ", score combo " + _game.Score.Combo + ", fps " + (1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)).ToString("F0");
            Debug.Log("[DropBeat] " + Report);
        }
    }
}
