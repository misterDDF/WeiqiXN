using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class EditorStateProbeTool
{
    public const string WriteMenuPath = CustomEditorMenuPaths.Root + "/Editor/Write Editor State";
    public const string LegacyMenuPath = CustomEditorMenuPaths.Root + "/Editor/Print Editor State";
    public const string RelativeProbePath = "Temp/WeiqiXN/editor_state_probe.json";
    private const string SequenceKey = "WeiqiXN.EditorStateProbe.Sequence";

    [MenuItem(WriteMenuPath)]
    [MenuItem(LegacyMenuPath)]
    public static void WriteEditorState()
    {
        Scene activeScene = EditorSceneManager.GetActiveScene();
        int sequence = EditorPrefs.GetInt(SequenceKey, 0) + 1;
        EditorPrefs.SetInt(SequenceKey, sequence);

        string probePath = GetProbePath();
        Directory.CreateDirectory(Path.GetDirectoryName(probePath));
        File.WriteAllText(probePath, BuildJson(activeScene, sequence), Encoding.UTF8);
    }

    private static string GetProbePath()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", RelativeProbePath));
    }

    private static string BuildJson(Scene activeScene, int sequence)
    {
        return "{\n" +
            $"  \"sequence\": {sequence},\n" +
            $"  \"timeUtc\": \"{JsonEscape(DateTime.UtcNow.ToString("O"))}\",\n" +
            $"  \"isPlaying\": {JsonBool(EditorApplication.isPlaying)},\n" +
            $"  \"isPlayingOrWillChangePlaymode\": {JsonBool(EditorApplication.isPlayingOrWillChangePlaymode)},\n" +
            $"  \"isPaused\": {JsonBool(EditorApplication.isPaused)},\n" +
            $"  \"isCompiling\": {JsonBool(EditorApplication.isCompiling)},\n" +
            $"  \"isUpdating\": {JsonBool(EditorApplication.isUpdating)},\n" +
            $"  \"activeSceneName\": \"{JsonEscape(activeScene.name)}\",\n" +
            $"  \"activeScenePath\": \"{JsonEscape(activeScene.path)}\",\n" +
            $"  \"activeSceneDirty\": {JsonBool(activeScene.isDirty)}\n" +
            "}\n";
    }

    private static string JsonBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string JsonEscape(string value)
    {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }
}
