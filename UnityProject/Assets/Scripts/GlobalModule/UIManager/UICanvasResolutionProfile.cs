using UnityEngine;
using UnityEngine.UI;

public static class UICanvasResolutionProfile
{
    private static readonly Vector2 PcReferenceResolution = new Vector2(1600f, 900f);
    private static readonly Vector2 MobilePortraitReferenceResolution = new Vector2(720f, 1280f);

    public static Vector2 RuntimeReferenceResolution
    {
        get
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            return MobilePortraitReferenceResolution;
#else
            return PcReferenceResolution;
#endif
        }
    }

    public static void ApplyRuntimeResolution(CanvasScaler canvasScaler)
    {
        if (canvasScaler == null) {
            return;
        }

        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = RuntimeReferenceResolution;
    }
}
