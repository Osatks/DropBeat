using UnityEngine;

namespace DropBeat
{
    /// <summary>One shared particle system used for every hit spark burst.</summary>
    public sealed class HitBurst : MonoBehaviour
    {
        private ParticleSystem _ps;

        public static HitBurst Create(Transform parent, Material softAdditive)
        {
            var go = new GameObject("HitBurst");
            go.transform.SetParent(parent, false);
            var burst = go.AddComponent<HitBurst>();
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.gravityModifier = 0.7f;
            main.maxParticles = 1500;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = softAdditive;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            burst._ps = ps;
            return burst;
        }

        public void Emit(Vector3 position, Color color, int count)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = position,
                applyShapeToPosition = true,
                startColor = ToLdr(color)
            };
            _ps.Emit(p, count);
        }

        private static Color ToLdr(Color hdr)
        {
            float m = Mathf.Max(1f, Mathf.Max(hdr.r, Mathf.Max(hdr.g, hdr.b)));
            return new Color(hdr.r / m, hdr.g / m, hdr.b / m, 1f);
        }
    }
}
