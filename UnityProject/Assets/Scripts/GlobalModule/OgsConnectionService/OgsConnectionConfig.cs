using System;
using XNClient.Logger;

public static class OgsConnectionConfig
{
    private const string FallbackClientId = "fdvTJ1wMm3v5psveVoE5BD6b0RYykAX8uLwWLQIP";
    private const string FallbackApiBaseUrl = "https://online-go.com";
    private const string FallbackAuthorizationPath = "/oauth2/authorize/";
    private const string FallbackTokenPath = "/oauth2/token/";
    private const string FallbackMePath = "/api/v1/me/";
    private const string FallbackMePathWithoutTrailingSlash = "/api/v1/me";
    private const string FallbackUiConfigPath = "/api/v1/ui/config";
    private const string FallbackRedirectUri = "http://127.0.0.1:8765/ogs/callback";
    private const string FallbackWebSocketUrl = "wss://online-go.com/ws";
    private const string FallbackScope = "read write";
    private const int FallbackRequestTimeoutMilliseconds = 15000;
    private const int FallbackWebSocketSmokeReceiveMilliseconds = 3000;
    private const int FallbackGameStateSmokeReceiveMilliseconds = 10000;
    private const int FallbackActiveBotsReceiveMilliseconds = 10000;
    private const int FallbackBotGameStateReceiveMilliseconds = 20000;
    private const int FallbackDefaultBotGameBoardSize = 9;
    private const string FallbackDefaultBotGameRules = "japanese";
    private const string FallbackDefaultBotGameName = "Friendly Match";

    public static string DefaultClientId => GetString("clientId", FallbackClientId);
    public static string DefaultApiBaseUrl => GetString("apiBaseUrl", FallbackApiBaseUrl).TrimEnd('/');
    public static string AuthorizationPath => GetString("authorizationPath", FallbackAuthorizationPath);
    public static string TokenPath => GetString("tokenPath", FallbackTokenPath);
    public static string MePath => GetString("mePath", FallbackMePath);
    public static string MePathWithoutTrailingSlash => GetString("mePathWithoutTrailingSlash", FallbackMePathWithoutTrailingSlash);
    public static string UiConfigPath => GetString("uiConfigPath", FallbackUiConfigPath);
    public static string DefaultRedirectUri => GetString("redirectUri", FallbackRedirectUri);
    public static string DefaultWebSocketUrl => GetString("webSocketUrl", FallbackWebSocketUrl);
    public static string DefaultScope => GetString("scope", FallbackScope);
    public static int RequestTimeoutMilliseconds => GetInt("requestTimeoutMilliseconds", FallbackRequestTimeoutMilliseconds);
    public static int WebSocketSmokeReceiveMilliseconds => GetInt("webSocketSmokeReceiveMilliseconds", FallbackWebSocketSmokeReceiveMilliseconds);
    public static int GameStateSmokeReceiveMilliseconds => GetInt("gameStateSmokeReceiveMilliseconds", FallbackGameStateSmokeReceiveMilliseconds);
    public static int ActiveBotsReceiveMilliseconds => GetInt("activeBotsReceiveMilliseconds", FallbackActiveBotsReceiveMilliseconds);
    public static int BotGameStateReceiveMilliseconds => GetInt("botGameStateReceiveMilliseconds", FallbackBotGameStateReceiveMilliseconds);
    public static int DefaultBotGameBoardSize => GetInt("defaultBotGameBoardSize", FallbackDefaultBotGameBoardSize);
    public static string DefaultBotGameRules => GetString("defaultBotGameRules", FallbackDefaultBotGameRules);
    public static string DefaultBotGameName => GetString("defaultBotGameName", FallbackDefaultBotGameName);

    private static string GetString(string key, string fallbackValue)
    {
        string value = GetRawValue(key);
        if (!string.IsNullOrWhiteSpace(value)) {
            return value.Trim();
        }

        XNLogger.LogWarn("OGS config string value invalid, fallback will be used.", ("key", key), ("fallback", fallbackValue));
        return fallbackValue;
    }

    private static int GetInt(string key, int fallbackValue)
    {
        string value = GetRawValue(key);
        if (int.TryParse(value, out int result)) {
            return result;
        }

        XNLogger.LogWarn(
            "OGS config int value invalid, fallback will be used.",
            ("key", key),
            ("value", value ?? string.Empty),
            ("fallback", fallbackValue.ToString()));
        return fallbackValue;
    }

    private static string GetRawValue(string key)
    {
        try {
            OgsConfigDataType data = OgsConfigDataType.GetConfigData(key);
            return data?.value;
        }
        catch (Exception e) {
            XNLogger.LogWarn("Read OGS config failed.", ("key", key), ("error", e.Message));
            return null;
        }
    }
}
