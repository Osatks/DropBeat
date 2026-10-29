using System;
using UnityEngine;

namespace DropBeat
{
    /// <summary>Materials created by the scene builder (DropBeat/Neon and DropBeat/NeonAdditive).</summary>
    [Serializable]
    public sealed class VisualAssets
    {
        public Material neon;
        public Material additive;
        public Material softAdditive;
        public Material backdrop;
        public Material ring;
        public Material ring3d;

        public static readonly int ColorId = Shader.PropertyToID("_Color");
        public static readonly int RimId = Shader.PropertyToID("_RimColor");

        [ColorUsage(false, true)] public Color perfect = new Color(3.2f, 2.5f, 1.0f, 1f);
        [ColorUsage(false, true)] public Color great = new Color(0.4f, 2.4f, 3.2f, 1f);
        [ColorUsage(false, true)] public Color good = new Color(0.6f, 2.8f, 0.8f, 1f);
        [ColorUsage(false, true)] public Color miss = new Color(2.8f, 0.15f, 0.25f, 1f);
        [ColorUsage(false, true)] public Color offbeat = new Color(2.6f, 0.35f, 1.9f, 1f);

        public Color GradeColor(Grade g)
        {
            switch (g)
            {
                case Grade.Perfect: return perfect;
                case Grade.Great: return great;
                case Grade.Good: return good;
                default: return miss;
            }
        }
    }
}
