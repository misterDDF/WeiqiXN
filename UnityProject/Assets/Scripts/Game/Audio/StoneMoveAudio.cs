using UnityEngine;

public sealed class StoneMoveAudio
{
    private float pendingPlaceTime = -1f;
    private float pendingCaptureTime = -1f;
    private int pendingCaptureCount;

    public void SchedulePlace()
    {
        pendingPlaceTime = Time.unscaledTime + ChessStoneView.PlacementDropSeconds;
    }

    public void ScheduleCapture(int captureCount)
    {
        if (captureCount <= 0) {
            return;
        }

        pendingCaptureCount = captureCount;
        pendingCaptureTime = Time.unscaledTime + ChessStoneView.PlacementDropSeconds;
    }

    public void Update()
    {
        float now = Time.unscaledTime;
        if (pendingCaptureTime >= 0f && now >= pendingCaptureTime) {
            pendingCaptureTime = -1f;
            GameAudio.PlayStoneCapture(pendingCaptureCount);
        }

        if (pendingPlaceTime >= 0f && now >= pendingPlaceTime) {
            pendingPlaceTime = -1f;
            GameAudio.PlayStonePlace();
        }
    }

    public void Cancel()
    {
        pendingPlaceTime = -1f;
        pendingCaptureTime = -1f;
        pendingCaptureCount = 0;
    }
}
