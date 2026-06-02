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
