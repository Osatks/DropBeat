using UnityEngine;

[DisallowMultipleComponent]
public sealed class RingHitPulseSplitEffect : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RingGlowController glowController;
    [SerializeField] private Transform outerGroup;
    [SerializeField] private Transform innerGroup;

    [Header("Target Visual State")]
    [ColorUsage(true, true)]
    [SerializeField] private Color normalInnerColor = new Color(0f, 1.0f, 1.1f, 1f);
    [ColorUsage(true, true)]
    [SerializeField] private Color normalOuterColor = new Color(0f, 1.0f, 1.1f, 1f);
    [ColorUsage(true, true)]
    [SerializeField] private Color targetInnerColor = new Color(2.1f, 0.2f, 1.35f, 1f);
    [ColorUsage(true, true)]
    [SerializeField] private Color targetOuterColor = new Color(2.4f, 0.35f, 1.6f, 1f);
    [SerializeField] private float targetIntensityMultiplier = 1.2f;

    [Header("Split Motion")]
    [SerializeField] private float splitDistance = 0.32f;
    [SerializeField] private float splitTiltDegrees = 14f;
    [SerializeField] private float splitDuration = 0.12f;
    [SerializeField] private float returnDuration = 0.2f;

    [Header("Break & Fall")]
    [SerializeField] private float breakOutwardImpulse = 2.8f;
    [SerializeField] private float breakUpwardImpulse = 0.7f;
    [SerializeField] private float breakDepthImpulse = 0.45f;
    [SerializeField] private float breakTorqueImpulse = 1.8f;
    [SerializeField] private float brokenHalfLifetime = 6f;

    [Header("Glow Pulse")]
    [SerializeField] private float perfectGlowBoost = 1.65f;
    [SerializeField] private float greatGlowBoost = 1.35f;
    [SerializeField] private float lateGlowBoost = 1.15f;

    private Transform _cachedParent;
    private Renderer[] _coreRenderers = System.Array.Empty<Renderer>();
    private Vector3 _outerBaseLocalPos;
    private Vector3 _innerBaseLocalPos;
    private Quaternion _outerBaseLocalRot;
    private Quaternion _innerBaseLocalRot;
    private float _baseGlowIntensity = 3.75f;

    private float _elapsed;
    private float _qualityStrength;
    private float _glowBoost;
    private bool _isTargeted;
    private bool _isBroken;
    private bool _active;

    private void Awake()
    {
        AutoAssignReferences();
        CacheCoreRenderers();
        CacheBaseState();
        ApplyTargetVisual(false);
    }

    private void OnEnable()
    {
        AutoAssignReferences();
        CacheCoreRenderers();
        CacheBaseState();
        RestoreBaseState();
        ApplyTargetVisual(_isTargeted);
    }

    private void Update()
    {
        if (!_active || _isBroken)
        {
            return;
        }

        float dt = Application.isPlaying ? Time.deltaTime : 0.016f;
        _elapsed += dt;
        float totalDuration = Mathf.Max(0.01f, splitDuration + returnDuration);

        float split01;
        if (_elapsed <= splitDuration)
        {
            split01 = Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, splitDuration));
        }
        else
        {
            float tBack = (_elapsed - splitDuration) / Mathf.Max(0.01f, returnDuration);
            split01 = 1f - Mathf.Clamp01(tBack);
        }

        float easedSplit = split01 * split01 * (3f - 2f * split01);
        float splitAmount = splitDistance * _qualityStrength * easedSplit;
        float tiltAmount = splitTiltDegrees * _qualityStrength * easedSplit;

        if (outerGroup != null)
        {
            outerGroup.localPosition = _outerBaseLocalPos + (Vector3.right * splitAmount);
            outerGroup.localRotation = _outerBaseLocalRot * Quaternion.Euler(0f, 0f, tiltAmount);
        }

        if (innerGroup != null)
        {
            innerGroup.localPosition = _innerBaseLocalPos - (Vector3.right * splitAmount);
            innerGroup.localRotation = _innerBaseLocalRot * Quaternion.Euler(0f, 0f, -tiltAmount);
        }

        if (glowController != null)
        {
            float pulse01 = Mathf.Sin(Mathf.Clamp01(_elapsed / totalDuration) * Mathf.PI);
            float glow = _baseGlowIntensity * Mathf.Lerp(1f, _glowBoost, pulse01);
            glowController.glowIntensity = glow;
            glowController.Apply();
        }

        if (_elapsed >= totalDuration)
        {
            _active = false;
            RestoreBaseState();
            ApplyTargetVisual(_isTargeted);
        }
    }

    public void TriggerPulse(float qualityStrength01)
    {
        if (_isBroken)
        {
            return;
        }

        AutoAssignReferences();
        CacheBaseState();

        _qualityStrength = Mathf.Clamp01(qualityStrength01);
        _glowBoost = Mathf.Lerp(lateGlowBoost, perfectGlowBoost, _qualityStrength);
        if (_qualityStrength >= 0.66f)
        {
            _glowBoost = perfectGlowBoost;
        }
        else if (_qualityStrength >= 0.33f)
        {
            _glowBoost = greatGlowBoost;
        }
        else
        {
            _glowBoost = lateGlowBoost;
        }

        _elapsed = 0f;
        _active = true;
    }

    public void BreakAndFall(float qualityStrength01)
    {
        if (_isBroken)
        {
            return;
        }

        float q = Mathf.Clamp01(qualityStrength01);
        TriggerPulse(q);
        _isBroken = true;

        if (outerGroup != null)
        {
            outerGroup.localPosition = _outerBaseLocalPos + (Vector3.right * splitDistance * Mathf.Lerp(0.9f, 1.2f, q));
            outerGroup.localRotation = _outerBaseLocalRot * Quaternion.Euler(0f, 0f, splitTiltDegrees * Mathf.Lerp(0.8f, 1.15f, q));
            LaunchBrokenHalf(outerGroup, +1f, q);
        }

        if (innerGroup != null)
        {
            innerGroup.localPosition = _innerBaseLocalPos - (Vector3.right * splitDistance * Mathf.Lerp(0.9f, 1.2f, q));
            innerGroup.localRotation = _innerBaseLocalRot * Quaternion.Euler(0f, 0f, -splitTiltDegrees * Mathf.Lerp(0.8f, 1.15f, q));
            LaunchBrokenHalf(innerGroup, -1f, q);
        }

        DisableCoreRenderers();
        SetColliderEnabled(false);
        SetRotateEnabled(false);
        if (glowController != null)
        {
            glowController.enabled = false;
        }

        // The root is no longer needed after halves are detached.
        Destroy(gameObject, 0.05f);
    }

    public void SetTargeted(bool targeted)
    {
        if (_isBroken)
        {
            return;
        }

        _isTargeted = targeted;
        ApplyTargetVisual(targeted);
    }

    private void AutoAssignReferences()
    {
        if (_cachedParent == null)
        {
            _cachedParent = transform.parent;
        }

        if (glowController == null)
        {
            glowController = GetComponent<RingGlowController>();
        }

        if (outerGroup == null)
        {
            Transform found = transform.Find("OuterGroup");
            if (found != null)
            {
                outerGroup = found;
            }
        }

        if (innerGroup == null)
        {
            Transform found = transform.Find("InnerGroup");
            if (found != null)
            {
                innerGroup = found;
            }
        }
    }

    private void CacheCoreRenderers()
    {
        _coreRenderers = new[]
        {
            FindRenderer("OuterRing"),
            FindRenderer("InnerRing"),
            FindRenderer("OuterStroke")
        };
    }

    private Renderer FindRenderer(string childName)
    {
        Transform t = transform.Find(childName);
        if (t == null)
        {
            return null;
        }

        return t.GetComponent<Renderer>();
    }

    private void CacheBaseState()
    {
        if (outerGroup != null)
        {
            _outerBaseLocalPos = outerGroup.localPosition;
            _outerBaseLocalRot = outerGroup.localRotation;
        }

        if (innerGroup != null)
        {
            _innerBaseLocalPos = innerGroup.localPosition;
            _innerBaseLocalRot = innerGroup.localRotation;
        }

        if (glowController != null)
        {
            _baseGlowIntensity = glowController.glowIntensity;
        }
    }

    private void RestoreBaseState()
    {
        if (_isBroken)
        {
            return;
        }

        if (outerGroup != null)
        {
            outerGroup.localPosition = _outerBaseLocalPos;
            outerGroup.localRotation = _outerBaseLocalRot;
        }

        if (innerGroup != null)
        {
            innerGroup.localPosition = _innerBaseLocalPos;
            innerGroup.localRotation = _innerBaseLocalRot;
        }

        if (glowController != null)
        {
            glowController.glowIntensity = _baseGlowIntensity;
            glowController.Apply();
        }
    }

    private void ApplyTargetVisual(bool targeted)
    {
        if (glowController == null)
        {
            return;
        }

        glowController.innerBaseGlowColor = targeted ? targetInnerColor : normalInnerColor;
        glowController.outerBaseGlowColor = targeted ? targetOuterColor : normalOuterColor;
        glowController.glowIntensity = _baseGlowIntensity * (targeted ? Mathf.Max(1f, targetIntensityMultiplier) : 1f);
        glowController.Apply();
    }

    private void LaunchBrokenHalf(Transform half, float sideSign, float qualityStrength01)
    {
        if (half == null)
        {
            return;
        }

        if (_cachedParent != null)
        {
            half.SetParent(_cachedParent, true);
        }
        else
        {
            half.SetParent(null, true);
        }

        Rigidbody rb = half.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = half.gameObject.AddComponent<Rigidbody>();
        }

        rb.useGravity = true;
        rb.mass = 0.9f;
        rb.linearDamping = 0.08f;
        rb.angularDamping = 0.04f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        float qualityScale = Mathf.Lerp(0.85f, 1.2f, qualityStrength01);
        Vector3 push = (Vector3.right * sideSign * breakOutwardImpulse) +
                       (Vector3.up * breakUpwardImpulse) +
                       (Vector3.back * breakDepthImpulse);
        rb.AddForce(push * qualityScale, ForceMode.Impulse);
        rb.AddTorque(new Vector3(0f, 0f, -sideSign) * breakTorqueImpulse * qualityScale, ForceMode.Impulse);

        Destroy(half.gameObject, Mathf.Max(0.2f, brokenHalfLifetime));
    }

    private void DisableCoreRenderers()
    {
        for (int i = 0; i < _coreRenderers.Length; i++)
        {
            if (_coreRenderers[i] != null)
            {
                _coreRenderers[i].enabled = false;
            }
        }
    }

    private void SetColliderEnabled(bool enabled)
    {
        Collider[] colliders = GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = enabled;
        }
    }

    private void SetRotateEnabled(bool enabled)
    {
        RingAutoRotate rotate = GetComponent<RingAutoRotate>();
        if (rotate != null)
        {
            rotate.enabled = enabled;
        }
    }
}
