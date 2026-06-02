public static class OgsConnectionConfig
{
    public const string DefaultApiBaseUrl = "https://online-go.com";
    public const string AuthorizationPath = "/oauth2/authorize/";
    public const string TokenPath = "/oauth2/token/";
    public const string MePath = "/api/v1/me/";
    public const string MePathWithoutTrailingSlash = "/api/v1/me";
    public const string UiConfigPath = "/api/v1/ui/config";
    public const string DefaultRedirectUri = "http://127.0.0.1:8765/ogs/callback";
    public const string DefaultWebSocketUrl = "wss://online-go.com/ws";
    public const string DefaultScope = "read";
    public const int RequestTimeoutMilliseconds = 15000;
    public const int WebSocketSmokeReceiveMilliseconds = 3000;
    public const int GameStateSmokeReceiveMilliseconds = 10000;
}
