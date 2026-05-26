using TMPro;
using UnityEngine;

public class LoadingPage : UIPageWithBinder<LoadingPageUI>
{
    private static LoadingPage activePage;
    private static LoadingProgressData currentProgress = new LoadingProgressData(MessageText.Get("loading_default"), string.Empty, 0f);
    private static float progressRangeStart;
    private static float progressRangeEnd = 1f;

    public override string pageName => UIPage.GetPageName<LoadingPage>();

    public static bool hasActivePage => activePage != null;

    public static void SetProgress(string statusText, string detailText, float progress)
    {
        currentProgress = new LoadingProgressData(statusText, detailText, ResolveDisplayProgress(progress));
        activePage?.RefreshProgress();
    }

    public static void SetProgressRange(float start, float end)
    {
        progressRangeStart = Mathf.Clamp01(start);
        progressRangeEnd = Mathf.Clamp01(end);
        if (progressRangeEnd < progressRangeStart) {
            progressRangeEnd = progressRangeStart;
        }
    }

    public static void ResetProgressRange()
    {
        progressRangeStart = 0f;
        progressRangeEnd = 1f;
    }

    protected override void OnLoaded()
    {
        base.OnLoaded();

        activePage = this;
        RefreshProgress();
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        activePage = this;
        RefreshProgress();
    }

    protected override void OnClose()
    {
        if (activePage == this) {
            activePage = null;
        }

        ResetProgressRange();
        base.OnClose();
    }

    private void RefreshProgress()
    {
        if (binder == null) {
            return;
        }

        SetText(binder.txt_loading, currentProgress.statusText);
        SetText(binder.txt_detail, currentProgress.detailText);
        SetText(binder.txt_percent, $"{Mathf.RoundToInt(currentProgress.progress * 100f)}%");

        if (binder.img_progress_fill != null) {
            binder.img_progress_fill.fillAmount = currentProgress.progress;
        }
    }

    private static void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null) {
            text.text = value ?? string.Empty;
        }
    }

    private static float ResolveDisplayProgress(float progress)
    {
        return Mathf.Lerp(progressRangeStart, progressRangeEnd, Mathf.Clamp01(progress));
    }

    private struct LoadingProgressData
    {
        public readonly string statusText;
        public readonly string detailText;
        public readonly float progress;

        public LoadingProgressData(string statusText, string detailText, float progress)
        {
            this.statusText = statusText ?? string.Empty;
            this.detailText = detailText ?? string.Empty;
            this.progress = progress;
        }
    }
}
