using System;
using System.IO;
using Newtonsoft.Json.Linq;
using XNClient.Logger;

public static class OgsSessionStore
{
    private static string SaveFilePath => Path.Combine(GameSaveConfig.SaveRootPath, "OgsSession.json");

    public static bool TryLoad(OgsSession session)
    {
        if (session == null || !File.Exists(SaveFilePath)) {
            return false;
        }

        try {
            JObject json = JObject.Parse(File.ReadAllText(SaveFilePath));
            session.accessToken = json["accessToken"]?.ToString() ?? string.Empty;
            session.refreshToken = json["refreshToken"]?.ToString() ?? string.Empty;
            session.tokenType = json["tokenType"]?.ToString() ?? string.Empty;
            session.scope = json["scope"]?.ToString() ?? string.Empty;
            session.userId = json["userId"]?.ToString() ?? string.Empty;
            session.username = json["username"]?.ToString() ?? string.Empty;
            session.avatarUrl = json["avatarUrl"]?.ToString() ?? string.Empty;
            session.country = json["country"]?.ToString() ?? string.Empty;
            session.registeredAt = json["registeredAt"]?.ToString() ?? string.Empty;
            session.tags = json["tags"]?.ToString() ?? string.Empty;
            session.about = json["about"]?.ToString() ?? string.Empty;
            session.ratingOverall = json["ratingOverall"]?.ToString() ?? string.Empty;
            session.ranking = json["ranking"]?.ToString() ?? string.Empty;
            session.rating19 = json["rating19"]?.ToString() ?? string.Empty;
            session.rating13 = json["rating13"]?.ToString() ?? string.Empty;
            session.rating9 = json["rating9"]?.ToString() ?? string.Empty;

            long expiresAtUnix = json["expiresAtUnix"]?.ToObject<long>() ?? 0L;
            session.expiresAtUtc = expiresAtUnix > 0
                ? DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix).UtcDateTime
                : DateTime.MinValue;
            return session.HasAccessToken || session.CanRefresh;
        }
        catch (Exception ex) {
            XNLogger.LogError("Load OGS session failed.", ("filePath", SaveFilePath), ("err", ex.Message));
            session.Clear();
            return false;
        }
    }

    public static bool Save(OgsSession session)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        XNLogger.LogWarn("Save OGS session is skipped on WebGL platform.", ("filePath", SaveFilePath));
        return false;
#else
        if (session == null) {
            return false;
        }

        try {
            Directory.CreateDirectory(Path.GetDirectoryName(SaveFilePath));
            var json = new JObject
            {
                ["accessToken"] = session.accessToken ?? string.Empty,
                ["refreshToken"] = session.refreshToken ?? string.Empty,
                ["tokenType"] = session.tokenType ?? string.Empty,
                ["scope"] = session.scope ?? string.Empty,
                ["userId"] = session.userId ?? string.Empty,
                ["username"] = session.username ?? string.Empty,
                ["avatarUrl"] = session.avatarUrl ?? string.Empty,
                ["country"] = session.country ?? string.Empty,
                ["registeredAt"] = session.registeredAt ?? string.Empty,
                ["tags"] = session.tags ?? string.Empty,
                ["about"] = session.about ?? string.Empty,
                ["ratingOverall"] = session.ratingOverall ?? string.Empty,
                ["ranking"] = session.ranking ?? string.Empty,
                ["rating19"] = session.rating19 ?? string.Empty,
                ["rating13"] = session.rating13 ?? string.Empty,
                ["rating9"] = session.rating9 ?? string.Empty,
                ["expiresAtUnix"] = session.expiresAtUtc == DateTime.MinValue
                    ? 0L
                    : new DateTimeOffset(session.expiresAtUtc).ToUnixTimeSeconds(),
            };
            File.WriteAllText(SaveFilePath, json.ToString());
            return true;
        }
        catch (Exception ex) {
            XNLogger.LogError("Save OGS session failed.", ("filePath", SaveFilePath), ("err", ex.Message));
            return false;
        }
#endif
    }

    public static void Clear()
    {
        try {
            if (File.Exists(SaveFilePath)) {
                File.Delete(SaveFilePath);
            }
        }
        catch (Exception ex) {
            XNLogger.LogError("Clear OGS session failed.", ("filePath", SaveFilePath), ("err", ex.Message));
        }
    }
}
