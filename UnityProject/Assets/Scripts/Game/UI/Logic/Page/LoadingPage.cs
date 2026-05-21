using TMPro;
using UnityEngine;

public class LoadingPage : UIPageWithBinder<LoadingPageUI>
{
    private static LoadingPage activePage;
    private static LoadingProgressData currentProgress = new LoadingProgressData("加载中...", string.Empty, 0f);

    public override string pageName => UIPage.GetPageName<LoadingPage>();

    public static bool hasActivePage => activePage != null;

    public static void SetProgress(string statusText, string detailText, float progress)
    {
        currentProgress = new LoadingProgressData(statusText, detailText, Mathf.Clamp01(progress));
        activePage?.RefreshProgress();
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
