using UnityEngine;

public class RingAutoRotate : MonoBehaviour
{
    [Header("Self Rotation")]
    public Vector3 axis = Vector3.forward;
    public float degreesPerSecond = 32f;
    public bool unscaledTime = false;

    [Header("Parent Controls Child Group Speeds")]
    public bool controlChildGroupsFromParent = false;
    public string outerGroupChildName = "OuterGroup";
    public float outerGroupDegreesPerSecond = 20f;
    public string innerGroupChildName = "InnerGroup";
    public float innerGroupDegreesPerSecond = -11f;

    Transform _outerGroup;
    Transform _innerGroup;

    void OnEnable()
    {
        CacheChildGroups();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        CacheChildGroups();
    }
#endif

    void Update()
    {
        float dt = unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        RotateTarget(transform, axis, degreesPerSecond, dt);

        if (!controlChildGroupsFromParent)
            return;

        if (_outerGroup == null || _innerGroup == null)
            CacheChildGroups();

        RotateTarget(_outerGroup, axis, outerGroupDegreesPerSecond, dt);
        RotateTarget(_innerGroup, axis, innerGroupDegreesPerSecond, dt);
    }

    void CacheChildGroups()
    {
        _outerGroup = string.IsNullOrWhiteSpace(outerGroupChildName) ? null : transform.Find(outerGroupChildName);
        _innerGroup = string.IsNullOrWhiteSpace(innerGroupChildName) ? null : transform.Find(innerGroupChildName);
    }

    static void RotateTarget(Transform target, Vector3 rotateAxis, float speed, float dt)
    {
        if (target == null)
            return;

        if (Mathf.Approximately(speed, 0f))
            return;

        if (rotateAxis.sqrMagnitude < 0.000001f)
            return;

        target.Rotate(rotateAxis.normalized, speed * dt, Space.Self);
    }
}
