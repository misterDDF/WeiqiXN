using UnityEngine;

public class UserComponentUserInfo : UserComponentBase
{
    public const string DefaultUserName = "人类";

    public SavableField<string> userId = SavableFieldFactory.CreateStringField(string.Empty);
    public SavableField<string> userName = SavableFieldFactory.CreateStringField(DefaultUserName);
    public SavableField<int> winCount = SavableFieldFactory.CreateIntField(0);
    public SavableField<int> loseCount = SavableFieldFactory.CreateIntField(0);

    public UserComponentUserInfo(User owner) : base(owner)
    {

    }

    public void CreateNewUser()
    {
        // 当前只作为本地存档标识使用，不作为跨设备唯一账号。
        userId.value = Random.Range(100000, 999999).ToString();
        userName.value = DefaultUserName;
        winCount.value = 0;
        loseCount.value = 0;
    }

    public void EnsureValidUserInfo()
    {
        if (string.IsNullOrWhiteSpace(userId.value)) {
            userId.value = Random.Range(100000, 999999).ToString();
        }

        if (string.IsNullOrWhiteSpace(userName.value)) {
            userName.value = DefaultUserName;
        }

        if (winCount.value < 0) {
            winCount.value = 0;
        }

        if (loseCount.value < 0) {
            loseCount.value = 0;
        }
    }

    public void Rename(string newUserName)
    {
        userName.value = string.IsNullOrWhiteSpace(newUserName)
            ? DefaultUserName
            : newUserName.Trim();
    }

    public UserProfileData BuildProfileData()
    {
        EnsureValidUserInfo();
        return new UserProfileData(userName.value);
    }

    public void RecordDuelResult(bool isWin)
    {
        if (isWin) {
            winCount.value += 1;
        } else {
            loseCount.value += 1;
        }
    }
}
