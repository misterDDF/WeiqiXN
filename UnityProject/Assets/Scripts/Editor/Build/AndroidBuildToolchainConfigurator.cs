using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class AndroidBuildToolchainConfigurator
{
    private const string AndroidExternalToolsSettingsTypeName = "UnityEditor.Android.AndroidExternalToolsSettings";
    private const string TemurinJdk11Path = @"C:\Program Files\Eclipse Adoptium\jdk-11.0.31.11-hotspot";
    private const string LocalAndroidSdkRoot = @"C:\Users\78447\AppData\Local\Android\Sdk";

    [MenuItem(CustomEditorMenuPaths.Build + "/Fix Android External Tools")]
    public static void ConfigureForUnityEmbeddedTools()
    {
        string androidPlayerRoot = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer");
        string sdkRoot = ResolveSdkRoot(androidPlayerRoot);
        string ndkRoot = Path.Combine(androidPlayerRoot, "NDK");
        string jdkRoot = ResolveJdkRoot();

        ValidateDirectory(sdkRoot, "Android SDK");
        ValidateDirectory(ndkRoot, "Android NDK");
        ValidateDirectory(jdkRoot, "Android JDK");
        ValidateFile(Path.Combine(jdkRoot, "bin", "java.exe"), "Android JDK java.exe");

        Type settingsType = FindAndroidExternalToolsSettingsType();
        SetStringProperty(settingsType, "SdkPath", sdkRoot);
        SetStringProperty(settingsType, "NdkPath", ndkRoot);
        SetStringProperty(settingsType, "JdkPath", jdkRoot);
        SetStringProperty(settingsType, "sdkRootPath", sdkRoot);
        SetStringProperty(settingsType, "ndkRootPath", ndkRoot);
        SetStringProperty(settingsType, "jdkRootPath", jdkRoot);

        Debug.Log($"Android external tools configured. SDK: {sdkRoot}, NDK: {ndkRoot}, JDK: {jdkRoot}");
    }

    private static string ResolveSdkRoot(string embeddedAndroidPlayerRoot)
    {
        if (Directory.Exists(LocalAndroidSdkRoot)) {
            return LocalAndroidSdkRoot;
        }

        string embeddedSdkRoot = Path.Combine(embeddedAndroidPlayerRoot, "SDK");
        if (Directory.Exists(embeddedSdkRoot)) {
            return embeddedSdkRoot;
        }

        throw new DirectoryNotFoundException("No usable Android SDK was found.");
    }

    private static string ResolveJdkRoot()
    {
        if (Directory.Exists(TemurinJdk11Path)) {
            return TemurinJdk11Path;
        }

        string registryPath = ReadJavaHomeFromRegistry();
        if (!string.IsNullOrEmpty(registryPath) && Directory.Exists(registryPath)) {
            return registryPath;
        }

        string embeddedJdkRoot = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer", "OpenJDK");
        if (Directory.Exists(embeddedJdkRoot)) {
            return embeddedJdkRoot;
        }

        throw new DirectoryNotFoundException("No usable Android JDK was found. Install JDK 11 or fix the configured path.");
    }

    private static string ReadJavaHomeFromRegistry()
    {
        return string.Empty;
    }

    private static Type FindAndroidExternalToolsSettingsType()
    {
        Type settingsType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(AndroidExternalToolsSettingsTypeName, false))
            .FirstOrDefault(type => type != null);
        if (settingsType != null) {
            return settingsType;
        }

        string assemblyPath = Path.Combine(
            EditorApplication.applicationContentsPath,
            "PlaybackEngines",
            "AndroidPlayer",
            "UnityEditor.Android.Extensions.dll");
        if (File.Exists(assemblyPath)) {
            Assembly.LoadFrom(assemblyPath);
            settingsType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(AndroidExternalToolsSettingsTypeName, false))
                .FirstOrDefault(type => type != null);
        }

        if (settingsType == null) {
            throw new InvalidOperationException("Unity Android external tools settings API was not found. Check Android Build Support installation.");
        }

        return settingsType;
    }

    private static void SetStringProperty(Type settingsType, string propertyName, string value)
    {
        PropertyInfo property = settingsType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (property == null || !property.CanWrite) {
            return;
        }

        property.SetValue(null, value);
    }

    private static void ValidateDirectory(string path, string label)
    {
        if (!Directory.Exists(path)) {
            throw new DirectoryNotFoundException($"{label} directory not found: {path}");
        }
    }

    private static void ValidateFile(string path, string label)
    {
        if (!File.Exists(path)) {
            throw new FileNotFoundException($"{label} not found.", path);
        }
    }
}
