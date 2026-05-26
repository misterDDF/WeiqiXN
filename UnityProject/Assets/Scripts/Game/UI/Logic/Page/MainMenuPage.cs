using UnityEngine;

public class MainMenuPage : UIPageWithBinder<MainMenuPageUI>
{
    public override string pageName => UIPage.GetPageName<MainMenuPage>();
    private StateRoot layoutStateRoot;
    private bool hasAppliedLayoutState;
    private bool lastPortraitLayout;

    protected override void OnLoaded()
    {
        base.OnLoaded();

        layoutStateRoot = gameObject.GetComponent<StateRoot>();
        ApplyCurrentLayoutState(true);

        binder.btn_new_game.onClick.AddListener(OnClickBtnNewGame);
        if (binder.btn_ai_game != null) {
            binder.btn_ai_game.onClick.AddListener(OnClickBtnAiGame);
        }
        if (binder.btn_lan_game != null) {
            binder.btn_lan_game.onClick.AddListener(OnClickBtnLanGame);
        }
        binder.btn_exit.onClick.AddListener(OnClickBtnExit);
        binder.btn_user_info.onClick.AddListener(OnClickBtnUserInfo);
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        ApplyCurrentLayoutState(false);
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        ApplyCurrentLayoutState(false);
    }

    private void ApplyCurrentLayoutState(bool force)
    {
        if (layoutStateRoot == null) {
            return;
        }

        bool isPortrait = UIUtils.IsPortrait(rectTransform);
        if (!force && hasAppliedLayoutState && isPortrait == lastPortraitLayout) {
            return;
        }

        layoutStateRoot.SetState(isPortrait ? "Portrait" : "Landscape", force);
        hasAppliedLayoutState = true;
        lastPortraitLayout = isPortrait;
    }

    public void OnClickBtnNewGame()
    {
        DuelSetupPopup.Open(false);
    }

    public void OnClickBtnAiGame()
    {
        DuelSetupPopup.Open(true);
    }

    public void OnClickBtnLanGame()
    {
        LanRoomPopup.OpenPopup();
    }

    public void OnClickBtnExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        UnityEngine.Application.Quit();
#endif
    }

    public void OnClickBtnUserInfo()
    {
        Global.Instance.uiManager.ShowPage<UserInfoPopup>();
    }
}
