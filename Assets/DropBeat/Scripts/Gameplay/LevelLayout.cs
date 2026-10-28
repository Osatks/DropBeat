using System.Collections.Generic;
using UnityEngine;

namespace DropBeat
{
    /// <summary>Runtime state of one note/ring.</summary>
    public sealed class PlayNote
    {
        public int index;
        public double time;
        public NoteKind kind;
        public Vector3 position;
        public float scale;
        public bool judged;
        public Grade grade;
        public double error;
        public double judgedAt;
        public RingView view;
    }

    /// <summary>
    /// Places rings and defines the ball's path. The ball's position is a pure function of song time:
    /// it leaves ring i exactly at note i and lands in ring i+1 exactly at note i+1, dropping in from
    /// above, so the moment of landing IS the beat. Nothing is simulated, so it can never drift.
    /// </summary>
    public sealed class LevelLayout
    {
        public readonly List<PlayNote> Notes = new List<PlayNote>(512);
        public Vector3 PadPosition { get; private set; }
        public double PadLeaveTime { get; private set; }
        public Vector3 ExitPosition { get; private set; }
        public double ExitTime { get; private set; }
        public float HalfWidth { get; private set; }

        private SongData _song;

        public void Build(SongData song, List<ChartNote> chart, float halfWidth, int seed)
        {
            _song = song;
            Notes.Clear();
            HalfWidth = Mathf.Max(1.5f, halfWidth);
            if (chart.Count == 0) return;

            var rng = new System.Random(seed);
            float spb = song.SecondsPerBeat;
            float lead = spb * Mathf.Max(1, Mathf.CeilToInt(0.55f / spb));
            PadLeaveTime = chart[0].time - lead;
            PadPosition = new Vector3(0f, 0f, 0f);

            float x = 0f;
            float y = 0f;
            float dir = rng.NextDouble() < 0.5 ? -1f : 1f;
            double prevTime = PadLeaveTime;
            for (int i = 0; i < chart.Count; i++)
            {
                ChartNote c = chart[i];
                float gap = (float)(c.time - prevTime);
                float rise = Mathf.Clamp(0.6f + gap * 1.7f, 0.85f, 3.4f);
                float dx = Mathf.Clamp(0.5f + gap * 2.6f, 0.8f, Mathf.Min(4.2f, HalfWidth));
                if (c.kind == NoteKind.Offbeat) dx *= 0.6f;
                if (c.kind == NoteKind.Downbeat && rng.NextDouble() < 0.4) dir = -dir;
                if (i == 0) dx *= 0.5f;

                x += dir * dx;
                if (x > HalfWidth)
                {
                    x = HalfWidth - (x - HalfWidth);
                    dir = -1f;
                }
                else if (x < -HalfWidth)
                {
                    x = -HalfWidth + (-HalfWidth - x);
                    dir = 1f;
                }
                y += rise;

                Notes.Add(new PlayNote
                {
                    index = i,
                    time = c.time,
                    kind = c.kind,
                    position = new Vector3(x, y, 0f),
                    scale = c.kind == NoteKind.Downbeat ? 1.15f : c.kind == NoteKind.Offbeat ? 0.8f : 1f
                });
                prevTime = c.time;
            }

            PlayNote last = Notes[Notes.Count - 1];
            ExitTime = last.time + Mathf.Max(1.2f, spb * 2f);
            ExitPosition = last.position + new Vector3(-Mathf.Sign(last.position.x + 0.001f) * 1.5f, 7f, 0f);
        }

        /// <summary>Ball position (and velocity) at heard song time t.</summary>
        public Vector3 BallAt(double t, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (Notes.Count == 0) return PadPosition;

            if (t < PadLeaveTime)
            {
                // Idle: hop on the start pad on every beat so the player feels the tempo first.
                int k = _song.BeatIndexAt(t);
                double b0 = _song.BeatTime(k);
                double b1 = _song.BeatTime(k + 1);
                float s = (float)((t - b0) / System.Math.Max(0.05, b1 - b0));
                const float hop = 0.45f;
                float dur = (float)(b1 - b0);
                velocity = new Vector3(0f, 4f * hop * (1f - 2f * s) / Mathf.Max(0.05f, dur), 0f);
                return PadPosition + new Vector3(0f, 4f * hop * s * (1f - s), 0f);
            }

            if (t >= ExitTime) return ExitPosition;

            // Find the flight segment containing t.
            int next = FindNextNote(t);
            Vector3 a, b;
            double ta, tb;
            if (next == 0)
            {
                a = PadPosition;
                ta = PadLeaveTime;
            }
            else
            {
                a = Notes[next - 1].position;
                ta = Notes[next - 1].time;
            }

            if (next >= Notes.Count)
            {
                b = ExitPosition;
                tb = ExitTime;
            }
            else
            {
                b = Notes[next].position;
                tb = Notes[next].time;
            }

            return Flight(a, b, ta, tb, t, out velocity);
        }

        /// <summary>Index of the first note with time &gt; t (Notes.Count if none).</summary>
        public int FindNextNote(double t)
        {
            int lo = 0, hi = Notes.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (Notes[mid].time > t) hi = mid;
                else lo = mid + 1;
            }
            return lo;
        }

        public static Vector3 Flight(Vector3 a, Vector3 b, double ta, double tb, double t, out Vector3 velocity)
        {
            float g = Mathf.Max(0.05f, (float)(tb - ta));
            float s = Mathf.Clamp01((float)((t - ta) / g));
            float dy = b.y - a.y;
            // Apex sits above the target ring, so the ball always comes down into it on the beat.
            float h = 0.5f * Mathf.Max(0f, dy) + Mathf.Clamp(0.3f + 0.9f * g, 0.35f, 2.2f);
            Vector3 p = Vector3.LerpUnclamped(a, b, s);
            p.y = a.y + dy * s + 4f * h * s * (1f - s);
            velocity = new Vector3((b.x - a.x) / g, (dy + 4f * h * (1f - 2f * s)) / g, (b.z - a.z) / g);
            return p;
        }
    }
}
