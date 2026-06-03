using System;
using UnityEngine;

namespace BeatDrop
{
    public enum HitGrade : byte
    {
        Miss = 0,
        Good = 1,
        Perfect = 2
    }

    /// <summary>
    /// Stateless DSP-time hit window evaluation.
    /// </summary>
    public static class HitJudge
    {
        public static HitGrade Evaluate(double hitErrorSeconds, double perfectWindow, double goodWindow)
        {
            double abs = Math.Abs(hitErrorSeconds);
            if (abs <= perfectWindow) return HitGrade.Perfect;
            if (abs <= goodWindow) return HitGrade.Good;
            return HitGrade.Miss;
        }
    }
}
