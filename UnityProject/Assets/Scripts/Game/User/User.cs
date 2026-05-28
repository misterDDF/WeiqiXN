using System.Collections.Generic;
using System.IO;

public class User : SavableObj
{
    private static User _instance;
    public static User Instance
    {
        get
        {
            if (_instance == null) {
                _instance = new User();
            }
            return _instance;
        }
    }

    public List<UserComponentBase> compList = new List<UserComponentBase>();

    public UserComponentUserInfo compUserInfo;
    public UserComponentDuelSetupPreference compDuelSetupPreference;

    public void Init()
    {
        compUserInfo = new UserComponentUserInfo(this);
        compDuelSetupPreference = new UserComponentDuelSetupPreference(this);

        string saveFilePath = GameSaveConfig.UserSaveFilePath;
        if (File.Exists(saveFilePath)) {
            Global.Instance.gameSaveManager.LoadData(this, saveFilePath);
            compUserInfo.EnsureValidUserInfo();
        } else {
            compUserInfo.CreateNewUser();
            Global.Instance.gameSaveManager.SaveData(this, saveFilePath);
        }
    }

    public void Save()
    {
        Global.Instance.gameSaveManager.SaveData(this, GameSaveConfig.UserSaveFilePath);
    }

    public void Destroy()
    {
        foreach (var comp in compList) {
            comp.OnDestroy();
        }
        _instance = null;
    }
}
