using System;
using System.IO;
using McpUnity.Utils;
using UnityEngine;
using UnityEditor;

namespace McpUnity.Unity
{
    /// <summary>
    /// Handles persistence of MCP Unity settings
    /// </summary>
    [Serializable]
    public class McpUnitySettings
    {
        // Constants
        public const string ServerVersion = "1.2.0";
        public const string PackageName = "com.gamelovers.mcp-unity";
        public const int RequestTimeoutMinimum = 10;
        
        // Paths
        private const string DefaultSettingsPath = "Packages/com.gamelovers.mcp-unity/McpUnitySettings.json";
        private const string LocalSettingsPath = "Packages/com.gamelovers.mcp-unity/McpUnitySettings.local.json";
        
        private static McpUnitySettings _instance;

        [Tooltip("Port number for MCP server")]
        public int Port = 8090;
        
        [Tooltip("Timeout in seconds for tool request")]
        public int RequestTimeoutSeconds = RequestTimeoutMinimum;
        
        [Tooltip("Whether to automatically start the MCP server when Unity opens")]
        public bool AutoStartServer = true;
        
        [Tooltip("Whether to show info logs in the Unity console")]
        public bool EnableInfoLogs = true;

        [Tooltip("Optional: Full path to the npm executable (e.g., /Users/user/.asdf/shims/npm or C:\\path\\to\\npm.cmd). If not set, 'npm' from the system PATH will be used.")]
        public string NpmExecutablePath = string.Empty;
        
        [Tooltip("Allow connections from remote MCP bridges. When disabled, only localhost connections are allowed (default).")]
        public bool AllowRemoteConnections = false;

        /// <summary>
        /// Singleton instance of settings
        /// </summary>
        public static McpUnitySettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new McpUnitySettings();
                }
                return _instance;
            }
        }

        /// <summary>
        /// Private constructor for singleton
        /// </summary>
        private McpUnitySettings() 
        { 
            LoadSettings();
        }

        /// <summary>
        /// Load settings from disk
        /// </summary>
        public void LoadSettings()
        {
            try
            {
                if (File.Exists(DefaultSettingsPath))
                {
                    string json = File.ReadAllText(DefaultSettingsPath);
                    JsonUtility.FromJsonOverwrite(json, this);
                }
                else
                {
                    // Create default settings file on the first time initialization
                    SaveSettings(DefaultSettingsPath);
                }

                if (File.Exists(LocalSettingsPath))
                {
                    string json = File.ReadAllText(LocalSettingsPath);
                    JsonUtility.FromJsonOverwrite(json, this);
                }
            }
            catch (Exception ex)
            {
                // Can't use LoggerService here as it depends on settings
                Debug.LogError($"[MCP Unity] Failed to load settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Save settings to disk
        /// </summary>
        /// <remarks>
        /// WARNING: This file is also read by the MCP server. Changes here will require updates to it. See mcpUnity.ts
        /// </remarks>
        public void SaveSettings()
        {
            SaveSettings(LocalSettingsPath);
        }

        private void SaveSettings(string settingsPath)
        {
            try
            {
                string json = JsonUtility.ToJson(this, true);
                File.WriteAllText(settingsPath, json);
            }
            catch (Exception ex)
            {
                // Can't use LoggerService here as it might create circular dependency
                Debug.LogError($"[MCP Unity] Failed to save settings: {ex.Message}");
            }
        }
    }
}
