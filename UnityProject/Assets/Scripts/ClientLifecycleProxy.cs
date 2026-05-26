using UnityEngine;

public sealed class ClientLifecycleProxy : MonoBehaviour
{
    private void OnApplicationFocus(bool hasFocus)
    {
        Global.OnApplicationFocusChanged(hasFocus);
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        Global.OnApplicationPauseChanged(pauseStatus);
    }
}
