using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public static class OgsConnectionSmokeTool
{
    private const string ClientIdKey = "WeiqiXN.Ogs.ClientId";
    private const string RedirectUriKey = "WeiqiXN.Ogs.RedirectUri";
    private const string ScopeKey = "WeiqiXN.Ogs.Scope";
    private const string CodeVerifierKey = "WeiqiXN.Ogs.CodeVerifier";
    private const string AuthorizationStateKey = "WeiqiXN.Ogs.AuthorizationState";
    private const string AuthorizationCodeKey = "WeiqiXN.Ogs.AuthorizationCode";
    private const string WebSocketUrlKey = "WeiqiXN.Ogs.WebSocketUrl";
    private const string GameIdKey = "WeiqiXN.Ogs.GameId";

    [MenuItem(CustomEditorMenuPaths.Root + "/OGS/1. Generate Authorization URL")]
    public static void GenerateAuthorizationUrl()
    {
        try {
            OgsConnectionService service = CreateService();
            string clientId = GetClientId();
            string redirectUri = GetRedirectUri();
            string scope = GetScope();
            if (string.IsNullOrWhiteSpace(clientId)) {
                EditorUtility.DisplayDialog("OGS", "Set the OGS client id first.", "OK");
                return;
            }

            OgsAuthorizationRequest request = service.CreateAuthorizationRequest(clientId, redirectUri, scope);
            EditorPrefs.SetString(CodeVerifierKey, request.codeVerifier);
            EditorPrefs.SetString(AuthorizationStateKey, request.state);
            EditorGUIUtility.systemCopyBuffer = request.authorizationUrl;
            Debug.Log(
                "OGS authorization URL copied to clipboard.\n" +
                $"url: {request.authorizationUrl}\n" +
                $"state: {request.state}\n" +
                "After browser authorization, paste the returned code with the OGS editor menu.");
            EditorUtility.DisplayDialog("OGS", "Authorization URL copied to clipboard. After browser authorization, paste the returned code with the OGS editor menu and run the login smoke.", "OK");
        }
        catch (Exception ex) {
            Debug.LogError($"OGS authorization URL generation failed: {ex}");
            EditorUtility.DisplayDialog("OGS", $"Generate authorization URL failed: {ex.Message}", "OK");
        }
    }

    [MenuItem(CustomEditorMenuPaths.Root + "/OGS/2. Browser Login Smoke")]
    public static async void BrowserLoginSmoke()
    {
        await RunAsync("OGS browser login smoke", async () => {
            string clientId = GetClientId();
            if (string.IsNullOrWhiteSpace(clientId)) {
                return new OgsConnectionResult(false, "OGS client id is empty.");
            }

            string redirectUri = GetRedirectUri();
            if (!redirectUri.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase) &&
                !redirectUri.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase)) {
                return new OgsConnectionResult(false, "Editor browser login smoke requires a localhost redirect URI.");
            }

            OgsConnectionService service = CreateService();
            OgsConnectionResult result = await service.LoginWithBrowserCallbackAsync(
                clientId,
                redirectUri,
                GetScope());
            LogResult("OGS browser login smoke", result, service.Session);
            return result;
        });
    }

    [MenuItem(CustomEditorMenuPaths.Root + "/OGS/3. Login With Saved Code Smoke")]
    public static async void LoginWithAuthorizationCode()
    {
        await RunAsync("OGS login smoke", async () => {
            OgsConnectionService service = CreateService();
            OgsConnectionResult result = await service.LoginWithAuthorizationCodeAsync(
                GetClientId(),
                EditorPrefs.GetString(AuthorizationCodeKey, string.Empty),
                EditorPrefs.GetString(CodeVerifierKey, string.Empty),
                GetRedirectUri());
            LogResult("OGS login smoke", result, service.Session);
            return result;
        });
    }

    [MenuItem(CustomEditorMenuPaths.Root + "/OGS/4. Current User Smoke")]
    public static async void RefreshCurrentUser()
    {
        await RunAsync("OGS current user smoke", async () => {
            OgsConnectionService service = CreateService();
            OgsConnectionResult result = await service.RefreshCurrentUserAsync();
            LogResult("OGS current user smoke", result, service.Session);
            return result;
        });
    }

    [MenuItem(CustomEditorMenuPaths.Root + "/OGS/5. Realtime Auth Smoke")]
    public static async void RealtimeAuthSmoke()
    {
        try {
            OgsConnectionService service = CreateService();
            OgsRealtimeSmokeResult result = await service.TestRealtimeAuthenticationAsync(GetWebSocketUrl());
            Debug.Log(
                $"OGS realtime auth smoke: {(result.success ? "success" : "failed")}\n" +
                $"message: {result.message}\n" +
                $"firstMessage: {TrimForLog(result.firstMessage)}");
            EditorUtility.DisplayDialog("OGS", $"OGS realtime auth smoke: {(result.success ? "success" : "failed")}\n{result.message}", "OK");
        }
        catch (Exception ex) {
            Debug.LogError($"OGS realtime auth smoke failed: {ex}");
            EditorUtility.DisplayDialog("OGS", $"OGS realtime auth smoke failed: {ex.Message}", "OK");
        }
    }

    [MenuItem(CustomEditorMenuPaths.Root + "/OGS/6. Game State Readonly Smoke")]
    public static async void GameStateReadonlySmoke()
    {
        try {
            int gameId = GetGameId();
            if (gameId <= 0) {
                EditorUtility.DisplayDialog("OGS", "Set a positive OGS game id first.", "OK");
                return;
            }

            OgsConnectionService service = CreateService();
            OgsGameStateSmokeResult result = await service.TestReadonlyGameStateAsync(gameId, GetWebSocketUrl());
            Debug.Log(
                $"OGS game state readonly smoke: {(result.success ? "success" : "failed")}\n" +
                $"message: {result.message}\n" +
                $"gameId: {result.gameId}\n" +
                $"board: {result.boardWidth}x{result.boardHeight}\n" +
                $"moveCount: {result.moveCount}\n" +
                $"black: {result.blackPlayer}\n" +
                $"white: {result.whitePlayer}\n" +
                $"phase: {result.phase}\n" +
                $"rawMessage: {TrimForLog(result.rawMessage)}");
            EditorUtility.DisplayDialog("OGS", $"OGS game state readonly smoke: {(result.success ? "success" : "failed")}\n{result.message}", "OK");
        }
        catch (Exception ex) {
            Debug.LogError($"OGS game state readonly smoke failed: {ex}");
            EditorUtility.DisplayDialog("OGS", $"OGS game state readonly smoke failed: {ex.Message}", "OK");
        }
    }

    [MenuItem(CustomEditorMenuPaths.Root + "/OGS/7. Start 9x9 Bot Game Smoke")]
    public static async void StartBotGameSmoke()
    {
        try {
            OgsConnectionService service = CreateService();
            OgsBotGameStartResult result = await service.StartDefaultBotGameAsync(GetWebSocketUrl());
            OgsGameStateSmokeResult gameState = result.gameState;
            Debug.Log(
                $"OGS bot game smoke: {(result.success ? "success" : "failed")}\n" +
                $"message: {result.message}\n" +
                $"gameId: {result.gameId}\n" +
                $"challengeId: {result.challengeId}\n" +
                $"botId: {result.botId}\n" +
                $"botName: {result.botName}\n" +
                $"board: {gameState?.boardWidth ?? 0}x{gameState?.boardHeight ?? 0}\n" +
                $"moveCount: {gameState?.moveCount ?? 0}\n" +
                $"rawResponse: {TrimForLog(result.rawResponse)}");
            EditorUtility.DisplayDialog("OGS", $"OGS bot game smoke: {(result.success ? "success" : "failed")}\n{result.message}", "OK");
        }
        catch (Exception ex) {
            Debug.LogError($"OGS bot game smoke failed: {ex}");
            EditorUtility.DisplayDialog("OGS", $"OGS bot game smoke failed: {ex.Message}", "OK");
        }
    }

    [MenuItem(CustomEditorMenuPaths.Root + "/OGS/8. Logout And Clear Session")]
    public static void Logout()
    {
        OgsConnectionService service = CreateService();
        service.Logout();
        EditorUtility.DisplayDialog("OGS", "OGS session cleared.", "OK");
    }

    [MenuItem(CustomEditorMenuPaths.Root + "/OGS/Config/Open Settings")]
    public static void OpenSettings()
    {
        OgsConnectionSettingsWindow.Open();
    }

    private static OgsConnectionService CreateService()
    {
        return Global._instance?.ogsConnectionService ?? new OgsConnectionService();
    }

    private static async Task RunAsync(string title, Func<Task<OgsConnectionResult>> action)
    {
        try {
            OgsConnectionResult result = await action();
            EditorUtility.DisplayDialog("OGS", $"{title}: {(result.success ? "success" : "failed")}\n{result.message}", "OK");
        }
        catch (Exception ex) {
            Debug.LogError($"{title} failed: {ex}");
            EditorUtility.DisplayDialog("OGS", $"{title} failed: {ex.Message}", "OK");
        }
    }

    private static void LogResult(string title, OgsConnectionResult result, OgsSession session)
    {
        Debug.Log(
            $"{title}: {(result.success ? "success" : "failed")}\n" +
            $"message: {result.message}\n" +
            $"userId: {session.userId}\n" +
            $"username: {session.username}\n" +
            $"expiresAtUtc: {session.expiresAtUtc:O}\n" +
            $"hasRefreshToken: {session.CanRefresh}");
    }

    private static string GetClientId()
    {
        return EditorPrefs.GetString(ClientIdKey, string.Empty);
    }

    private static string GetRedirectUri()
    {
        return EditorPrefs.GetString(RedirectUriKey, OgsConnectionConfig.DefaultRedirectUri);
    }

    private static string GetScope()
    {
        return EditorPrefs.GetString(ScopeKey, OgsConnectionConfig.DefaultScope);
    }

    private static string GetWebSocketUrl()
    {
        return EditorPrefs.GetString(WebSocketUrlKey, OgsConnectionConfig.DefaultWebSocketUrl);
    }

    private static int GetGameId()
    {
        return EditorPrefs.GetInt(GameIdKey, 0);
    }

    private static string TrimForLog(string value)
    {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }
        return value.Length <= 300 ? value : value.Substring(0, 300);
    }

    private sealed class OgsConnectionSettingsWindow : EditorWindow
    {
        private string clientId;
        private string redirectUri;
        private string scope;
        private string authorizationCode;
        private string websocketUrl;
        private int gameId;

        public static void Open()
        {
            OgsConnectionSettingsWindow window = GetWindow<OgsConnectionSettingsWindow>("OGS Settings");
            window.minSize = new Vector2(560, 160);
            window.Load();
            window.Show();
        }

        private void OnGUI()
        {
            if (clientId == null) {
                Load();
            }

            clientId = EditorGUILayout.TextField("Client Id", clientId);
            redirectUri = EditorGUILayout.TextField("Redirect Uri", redirectUri);
            scope = EditorGUILayout.TextField("Scope", scope);
            authorizationCode = EditorGUILayout.TextField("Authorization Code", authorizationCode);
            websocketUrl = EditorGUILayout.TextField("WebSocket Url", websocketUrl);
            gameId = EditorGUILayout.IntField("Game Id", gameId);
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save")) {
                Save();
            }
            if (GUILayout.Button("Copy Saved Verifier")) {
                EditorGUIUtility.systemCopyBuffer = EditorPrefs.GetString(CodeVerifierKey, string.Empty);
                Debug.Log("OGS saved PKCE verifier copied to clipboard.");
            }
            GUILayout.EndHorizontal();
        }

        private void Load()
        {
            clientId = GetClientId();
            redirectUri = GetRedirectUri();
            scope = GetScope();
            authorizationCode = EditorPrefs.GetString(AuthorizationCodeKey, string.Empty);
            websocketUrl = GetWebSocketUrl();
            gameId = GetGameId();
        }

        private void Save()
        {
            EditorPrefs.SetString(ClientIdKey, clientId?.Trim() ?? string.Empty);
            EditorPrefs.SetString(RedirectUriKey, string.IsNullOrWhiteSpace(redirectUri) ? OgsConnectionConfig.DefaultRedirectUri : redirectUri.Trim());
            EditorPrefs.SetString(ScopeKey, string.IsNullOrWhiteSpace(scope) ? OgsConnectionConfig.DefaultScope : scope.Trim());
            EditorPrefs.SetString(AuthorizationCodeKey, authorizationCode?.Trim() ?? string.Empty);
            EditorPrefs.SetString(WebSocketUrlKey, string.IsNullOrWhiteSpace(websocketUrl) ? OgsConnectionConfig.DefaultWebSocketUrl : websocketUrl.Trim());
            EditorPrefs.SetInt(GameIdKey, Math.Max(0, gameId));
            Debug.Log("OGS editor smoke settings saved.");
        }
    }
}
