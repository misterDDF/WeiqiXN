public class UserInfoPopup : UIPageWithBinder<UserInfoPopupUI>
{
    public override string pageName => UIPage.GetPageName<UserInfoPopup>();

    protected override void OnLoaded()
    {
        base.OnLoaded();

        binder.btn_close.onClick.AddListener(OnClickBtnClose);
        if (binder.btn_edit_name != null) {
            binder.btn_edit_name.onClick.AddListener(OnClickBtnEditName);
        }
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        RefreshUserInfo();
        SetSaveTip(string.Empty);
    }

    public void OnClickBtnClose()
    {
        ClosePage();
    }

    public void OnClickBtnEditName()
    {
        ConfirmPopup.ShowInput(
            "修改姓名",
            "请输入新的本地用户名",
            User.Instance.compUserInfo.userName.value,
            SaveUserName,
            null,
            "保存",
            "取消"
        );
    }

    private void SaveUserName(string userName)
    {
        User.Instance.compUserInfo.Rename(userName);
        User.Instance.Save();
        RefreshUserInfo();
        SetSaveTip("已保存");
        Global.Instance.lanRoomService?.SyncLocalPlayerProfile();
    }

    private void RefreshUserInfo()
    {
        User.Instance.compUserInfo.EnsureValidUserInfo();
        binder.txt_user_id.text = $"ID: {User.Instance.compUserInfo.userId.value}";
        if (binder.txt_user_name != null) {
            binder.txt_user_name.text = User.Instance.compUserInfo.userName.value;
        }
        binder.txt_win_count.text = User.Instance.compUserInfo.winCount.value.ToString();
        binder.txt_lose_count.text = User.Instance.compUserInfo.loseCount.value.ToString();
    }

    private void SetSaveTip(string message)
    {
        if (binder.txt_save_tip != null) {
            binder.txt_save_tip.text = message ?? string.Empty;
        }
    }
}
