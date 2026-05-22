using System;
using Newtonsoft.Json;
using XNClient.Logger;

public class UserProfileData
{
    public string name;

    public UserProfileData()
    {
    }

    public UserProfileData(string name)
    {
        this.name = name;
    }

    public static UserProfileData CreateFallback(string fallbackName)
    {
        return new UserProfileData(string.IsNullOrWhiteSpace(fallbackName) ? UserComponentUserInfo.DefaultUserName : fallbackName.Trim());
    }

    public void Normalize(string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(name)) {
            name = string.IsNullOrWhiteSpace(fallbackName) ? UserComponentUserInfo.DefaultUserName : fallbackName.Trim();
        } else {
            name = name.Trim();
        }
    }

    public string ToJson()
    {
        Normalize(UserComponentUserInfo.DefaultUserName);
        return JsonConvert.SerializeObject(this);
    }

    public static UserProfileData FromJson(string json, string fallbackName)
    {
        if (string.IsNullOrEmpty(json)) {
            return CreateFallback(fallbackName);
        }

        try {
            UserProfileData profile = JsonConvert.DeserializeObject<UserProfileData>(json);
            if (profile == null) {
                return CreateFallback(fallbackName);
            }

            profile.Normalize(fallbackName);
            return profile;
        }
        catch (Exception e) {
            XNLogger.LogWarn("Parse user profile data failed.", ("error", e.Message));
            return CreateFallback(fallbackName);
        }
    }
}
