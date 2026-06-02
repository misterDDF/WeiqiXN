using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class EditorStateProbeTool
{
    public const string MenuPath = CustomEditorMenuPaths.Root + "/Editor/Print Editor State";
    private const string LogPrefix = "[EditorStateProbe]";

    [MenuItem(MenuPath)]
    public static void PrintEditorState()
    {
        Scene activeScene = EditorSceneManager.GetActiveScene();
        Debug.Log(
            $"{LogPrefix} " +
            $"isPlaying={EditorApplication.isPlaying} " +
            $"isPlayingOrWillChangePlaymode={EditorApplication.isPlayingOrWillChangePlaymode} " +
            $"isPaused={EditorApplication.isPaused} " +
            $"isCompiling={EditorApplication.isCompiling} " +
            $"isUpdating={EditorApplication.isUpdating} " +
            $"activeSceneName=\"{Escape(activeScene.name)}\" " +
            $"activeScenePath=\"{Escape(activeScene.path)}\" " +
            $"activeSceneDirty={activeScene.isDirty} " +
            $"timeUtc=\"{DateTime.UtcNow:O}\"");
    }

    private static string Escape(string value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
