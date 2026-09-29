using UnityEditor;
using UnityEngine;

public class EditorColorScope : System.IDisposable
{
    private Color originColor;

    public EditorColorScope(Color newColor)
    {
        originColor = GUI.color;
        GUI.color = newColor;
    }

    public void Dispose()
    {
        GUI.color = originColor;
    }
}

public static class EditorUtils
{
    // 换 shader 后材质仍保留旧属性（例如旧的纹理引用），会把无用资源带进包体，这里清掉当前 shader 不存在的属性。
    public static void RemoveUnusedMaterialProperties(Material material)
    {
        SerializedObject serializedMaterial = new SerializedObject(material);
        SerializedProperty savedProperties = serializedMaterial.FindProperty("m_SavedProperties");
        foreach (string arrayName in new[] { "m_TexEnvs", "m_Ints", "m_Floats", "m_Colors" }) {
            SerializedProperty array = savedProperties.FindPropertyRelative(arrayName);
            if (array == null) {
                continue;
            }

            for (int i = array.arraySize - 1; i >= 0; i--) {
                string propertyName = array.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue;
                if (!material.HasProperty(propertyName)) {
                    array.DeleteArrayElementAtIndex(i);
                }
            }
        }

        serializedMaterial.ApplyModifiedPropertiesWithoutUndo();
    }

    // 编辑态离屏截图：相机临时渲染到 sRGB RenderTexture 并写出 PNG，结束后恢复相机目标。
    public static void RenderCameraToPng(Camera camera, int width, int height, int msaaSamples, string path)
    {
        RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        target.antiAliasing = msaaSamples;
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        Texture2D readback = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readback.Apply();
            System.IO.File.WriteAllBytes(path, readback.EncodeToPNG());
        }
        finally {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            Object.DestroyImmediate(readback);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }
}
