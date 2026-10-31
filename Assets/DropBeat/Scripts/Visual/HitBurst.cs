using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// Shared particle systems for hit bursts: bright HDR spark streaks thrown off the ring's edge, and
    /// softer embers that drift and fade.
    /// </summary>
    public sealed class HitBurst : MonoBehaviour
    {
        private ParticleSystem _ps;
        private ParticleSystem _sparks;

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
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
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
            var emberMpb = new MaterialPropertyBlock();
            emberMpb.SetColor(VisualAssets.ColorId, new Color(1.6f, 1.6f, 1.6f, 1f));
            renderer.SetPropertyBlock(emberMpb);

            burst._ps = ps;
            burst._sparks = CreateSparks(go.transform, softAdditive);
            return burst;
        }

        private static ParticleSystem CreateSparks(Transform parent, Material softAdditive)
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.36f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 16f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.gravityModifier = 0.25f;
            main.maxParticles = 600;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.enabled = false;

            // Thrown outward from the ring's rim.
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.8f;
            shape.radiusThickness = 0f;

            // Drag: speed halves about every 115 ms, so the streaks snap out and hang.
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100f;
            limit.dampen = 0f;
            limit.drag = 6f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = softAdditive;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.035f;
            renderer.lengthScale = 1f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // Own HDR brightness so the sparks bloom; the shared material stays untouched.
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(VisualAssets.ColorId, new Color(3.5f, 3.5f, 3.5f, 1f));
            mpb.SetFloat("_SoftPower", 3f);
            renderer.SetPropertyBlock(mpb);
            return ps;
        }

        /// <summary>Sparks off the rim of a ring of <paramref name="radius"/> facing <paramref name="facing"/>.</summary>
        public void EmitSparks(Vector3 position, Quaternion facing, float radius, Color color, int count)
        {
            if (count <= 0 || _sparks == null) return;
            var shape = _sparks.shape;
            shape.radius = Mathf.Max(0.05f, radius);
            shape.rotation = facing.eulerAngles;
            var p = new ParticleSystem.EmitParams
            {
                position = position,
                applyShapeToPosition = true,
                startColor = ToLdr(color)
            };
            _sparks.Emit(p, count);
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
