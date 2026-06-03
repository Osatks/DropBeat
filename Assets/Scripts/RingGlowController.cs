using UnityEngine;

[ExecuteAlways]
public class RingGlowController : MonoBehaviour
{
    [Header("Targets")]
    public Renderer innerGlowRenderer;
    public Renderer outerGlowRenderer;

    [Header("Glow Slider")]
    [ColorUsage(true, true)] public Color innerBaseGlowColor = new Color(0f, 1.0f, 1.1f, 1f);
    [ColorUsage(true, true)] public Color outerBaseGlowColor = new Color(0f, 1.0f, 1.1f, 1f);
    [Range(0f, 10f)] public float glowIntensity = 3.75f;

    MaterialPropertyBlock _mpb;

    void OnEnable()
    {
        AutoAssignIfNeeded();
        Apply();
    }

    void OnValidate()
    {
        AutoAssignIfNeeded();
        Apply();
    }

    void Update()
    {
        if (Application.isPlaying)
        {
            Apply();
        }
    }

    [ContextMenu("Auto Assign")] 
    public void AutoAssignIfNeeded()
    {
        if (innerGlowRenderer == null)
        {
            Transform t = transform.Find("InnerRing");
            if (t != null) innerGlowRenderer = t.GetComponent<Renderer>();
        }

        if (outerGlowRenderer == null)
        {
            Transform t = transform.Find("OuterStroke");
            if (t != null) outerGlowRenderer = t.GetComponent<Renderer>();
        }

    }

    [ContextMenu("Apply Glow")]
    public void Apply()
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        ApplyRendererGlow(innerGlowRenderer, innerBaseGlowColor * Mathf.Max(0f, glowIntensity));
        ApplyRendererGlow(outerGlowRenderer, outerBaseGlowColor * Mathf.Max(0f, glowIntensity));
    }

    void ApplyRendererGlow(Renderer target, Color hdrColor)
    {
        if (target == null) return;

        target.GetPropertyBlock(_mpb);

        _mpb.SetColor("_BaseColor", hdrColor);

        Material mat = target.sharedMaterial;
        if (mat != null && mat.HasProperty("_EmissionColor"))
        {
            _mpb.SetColor("_EmissionColor", hdrColor);
        }

        target.SetPropertyBlock(_mpb);
    }
}
