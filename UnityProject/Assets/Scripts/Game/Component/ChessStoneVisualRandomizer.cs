using UnityEngine;

public class ChessStoneVisualRandomizer : MonoBehaviour
{
    [SerializeField] private Transform visualOffsetRoot;
    [SerializeField] private float maxPositionOffset = 0.28f;
    [SerializeField] private float positionOffsetPower = 2f;
    [SerializeField] private float maxYawDegrees = 8f;

    private void Awake()
    {
        EnsureVisualOffsetRoot();
    }

    private void OnEnable()
    {
        ApplyRandomOffset();
    }

    private void ApplyRandomOffset()
    {
        EnsureVisualOffsetRoot();
        if (visualOffsetRoot == null) {
            return;
        }

        Vector2 offset = CreateWeightedOffset();
        float yaw = Random.Range(-maxYawDegrees, maxYawDegrees);

        visualOffsetRoot.localPosition = new Vector3(offset.x, 0f, offset.y);
        visualOffsetRoot.localRotation = Quaternion.Euler(0f, yaw, 0f);
        visualOffsetRoot.localScale = Vector3.one;
    }

    private Vector2 CreateWeightedOffset()
    {
        float safeMaxOffset = Mathf.Max(maxPositionOffset, 0f);
        if (safeMaxOffset <= 0f) {
            return Vector2.zero;
        }

        float safePower = Mathf.Max(positionOffsetPower, 0.01f);
        float radius = Mathf.Pow(Random.value, safePower) * safeMaxOffset;
        float angle = Random.Range(0f, Mathf.PI * 2f);
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    private void EnsureVisualOffsetRoot()
    {
        if (visualOffsetRoot != null) {
            return;
        }

        Transform found = transform.Find("VisualOffset");
        if (found != null) {
            visualOffsetRoot = found;
        }
    }
}
