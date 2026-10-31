using System;
using System.Collections.Generic;
using UnityEngine;

namespace DropBeat
{
    public enum Grade : byte
    {
        Perfect = 0,
        Great = 1,
        Good = 2,
        Miss = 3
    }

    /// <summary>Timing windows (half-widths, seconds) around each note.</summary>
    public readonly struct JudgeWindows
    {
        public readonly double perfect;
        public readonly double great;
        public readonly double good;
        /// <summary>A tap this early still "uses up" the note as a miss instead of being ignored.</summary>
        public readonly double earlyMiss;

        public JudgeWindows(double scale)
        {
            perfect = 0.050 * scale;
            great = 0.100 * scale;
            good = 0.150 * scale;
            earlyMiss = 0.190 * scale;
        }

        public static JudgeWindows For(Difficulty d)
        {
            return new JudgeWindows(d == Difficulty.Easy ? 1.25 : d == Difficulty.Hard ? 0.9 : 1.0);
        }

        public Grade Grade(double error)
        {
            double a = Math.Abs(error);
            if (a <= perfect) return DropBeat.Grade.Perfect;
            if (a <= great) return DropBeat.Grade.Great;
            if (a <= good) return DropBeat.Grade.Good;
            return DropBeat.Grade.Miss;
        }
    }

    /// <summary>Score, combo, health and timing statistics for one run.</summary>
    public sealed class ScoreState
    {
        public const int PerfectPoints = 300;
        public const int GreatPoints = 200;
        public const int GoodPoints = 100;

        public int Score { get; private set; }
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }
        public int Perfect { get; private set; }
        public int Great { get; private set; }
        public int Good { get; private set; }
        public int Miss { get; private set; }
        public float Health { get; private set; } = 1f;
        public int TotalNotes { get; private set; }
        public int Judged => Perfect + Great + Good + Miss;

        /// <summary>Signed tap errors (tap - note) in seconds, for hits only.</summary>
        public readonly List<float> Errors = new List<float>(512);

        private readonly float _missDamage;
        private readonly int _maxScore;

        public ScoreState(int totalNotes, Difficulty difficulty)
        {
            TotalNotes = totalNotes;
            _missDamage = difficulty == Difficulty.Easy ? 0.06f : difficulty == Difficulty.Hard ? 0.12f : 0.08f;
            int max = 0;
            for (int i = 0; i < totalNotes; i++) max += Mathf.RoundToInt(PerfectPoints * Multiplier(i));
            _maxScore = Mathf.Max(1, max);
        }

        public static float Multiplier(int combo) => 1f + Mathf.Min(combo / 10, 10) * 0.1f;

        public float ComboMultiplier => Multiplier(Combo);

        /// <summary>Weighted accuracy over judged notes, 0..1.</summary>
        public float Accuracy
        {
            get
            {
                int n = Judged;
                if (n == 0) return 1f;
                return (Perfect + Great * 0.7f + Good * 0.4f) / n;
            }
        }

        /// <summary>Score as a fraction of the best possible score for the whole chart.</summary>
        public float ScoreFraction => Mathf.Clamp01(Score / (float)_maxScore);

        public bool FullCombo => Miss == 0 && Judged == TotalNotes;

        /// <param name="multiplier">2 during overdrive.</param>
        /// <param name="countError">False for hold tails (no tap timing to record).</param>
        public int Register(Grade grade, double error, int multiplier = 1, bool countError = true)
        {
            int points = 0;
            switch (grade)
            {
                case Grade.Perfect:
                    Perfect++;
                    points = PerfectPoints;
                    Health = Mathf.Min(1f, Health + 0.03f);
                    break;
                case Grade.Great:
                    Great++;
                    points = GreatPoints;
                    Health = Mathf.Min(1f, Health + 0.02f);
                    break;
                case Grade.Good:
                    Good++;
                    points = GoodPoints;
                    Health = Mathf.Min(1f, Health + 0.008f);
                    break;
                default:
                    Miss++;
                    Combo = 0;
                    Health = Mathf.Max(0f, Health - _missDamage);
                    return 0;
            }

            points = Mathf.RoundToInt(points * ComboMultiplier) * Mathf.Max(1, multiplier);
            Score += points;
            Combo++;
            MaxCombo = Mathf.Max(MaxCombo, Combo);
            if (countError) Errors.Add((float)error);
            return points;
        }

        /// <summary>Bonus points (section clears, the song's twist); counted in the score.</summary>
        public void AddBonus(int points)
        {
            points = Mathf.Max(0, points);
            Score += points;
            Bonus += points;
        }

        public int Bonus { get; private set; }

        /// <summary>Every judgement in song order: x = note time, y = grade (results song strip).</summary>
        public readonly List<Vector2> Marks = new List<Vector2>(512);

        public void Mark(double t, Grade g) => Marks.Add(new Vector2((float)t, (float)g));

        /// <summary>Accuracy needed for the next rank letter (1 when already at S).</summary>
        public static float NextRankThreshold(float acc) => acc < 0.7f ? 0.7f : acc < 0.8f ? 0.8f : acc < 0.9f ? 0.9f : acc < 0.95f ? 0.95f : 1f;

        /// <summary>0..1 progress through the current rank band.</summary>
        public static float BandProgress(float acc)
        {
            float lo = acc < 0.7f ? 0f : acc < 0.8f ? 0.7f : acc < 0.9f ? 0.8f : acc < 0.95f ? 0.9f : 0.95f;
            float hi = NextRankThreshold(acc);
            return hi <= lo ? 1f : Mathf.Clamp01((acc - lo) / (hi - lo));
        }

        public string Rank(bool failed)
        {
            if (failed) return "F";
            float a = Accuracy;
            if (Perfect == TotalNotes && TotalNotes > 0) return "SS";
            if (a >= 0.95f) return "S";
            if (a >= 0.90f) return "A";
            if (a >= 0.80f) return "B";
            if (a >= 0.70f) return "C";
            return "D";
        }

        /// <summary>Mean and standard deviation of the tap error in ms (positive = late).</summary>
        public void ErrorStats(out float meanMs, out float stdMs, out float medianMs)
        {
            meanMs = stdMs = medianMs = 0f;
            int n = Errors.Count;
            if (n == 0) return;
            double s = 0;
            for (int i = 0; i < n; i++) s += Errors[i];
            double mean = s / n;
            double v = 0;
            for (int i = 0; i < n; i++) v += (Errors[i] - mean) * (Errors[i] - mean);
            meanMs = (float)(mean * 1000.0);
            stdMs = (float)(Math.Sqrt(v / n) * 1000.0);
            var sorted = new List<float>(Errors);
            sorted.Sort();
            medianMs = sorted[n / 2] * 1000f;
        }
    }
}
