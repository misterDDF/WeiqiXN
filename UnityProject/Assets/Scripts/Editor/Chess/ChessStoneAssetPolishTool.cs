#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 正式棋子美术配置：生成双凸透镜形网格与接触阴影面片，配置 StoneGloss / StoneContactShadow 材质并写回棋子 prefab。
// 落点预览材质由 ChessStonePreviewAssetPolishTool 从这里的正式材质复制，调整外观后需要再执行一次预览配置。
public static class ChessStoneAssetPolishTool
{
    private const string ShaderPath = "Assets/Models/Chess/Shaders/StoneGloss.shader";
    private const string ContactShadowShaderPath = "Assets/Models/Chess/Shaders/StoneContactShadow.shader";
    private const string DetailMapPath = "Assets/Models/Chess/Textures/StoneDetail.png";
    private const string MeshFolder = "Assets/Models/Chess/Meshes";
    private const string LensMeshPath = MeshFolder + "/GoStoneLens.asset";
    private const string ContactShadowMeshPath = MeshFolder + "/StoneContactShadowQuad.asset";
    private const string BlackMaterialPath = "Assets/Models/Chess/Materials/ChessBlack.mat";
    private const string WhiteMaterialPath = "Assets/Models/Chess/Materials/ChessWhite.mat";
    private const string ContactShadowMaterialPath = "Assets/Models/Chess/Materials/ChessStoneContactShadow.mat";
    private const string BlackPrefabPath = "Assets/Models/Chess/ChessBlack.prefab";
    private const string WhitePrefabPath = "Assets/Models/Chess/ChessWhite.prefab";
    private const string ModelNodePath = "VisualOffset/Model";
    public const string ContactShadowNodePath = "VisualOffset/ContactShadow";

    // 棋子世界尺寸：直径 3.9、厚 1.6。Model 节点保留该缩放与 y=0.8 的抬高（ChessStoneView 的落子动画以此为静止姿态），网格按单位尺寸生成。
    private static readonly Vector3 StoneSize = new Vector3(3.9f, 1.6f, 3.9f);
    private const float StoneEdgeFilletRadius = 0.25f;
    private const int LensRadialSegments = 40;
    private const int LensTopCapSegments = 6;
    private const int LensFilletSegments = 5;
    // 俯视相机看不到下半片，只保留投影轮廓所需的精度。
    private const int LensBottomCapSegments = 2;
    // 网格旋转对称，整圈随机偏航只改变白子条纹方向。
    private const float StoneMaxYawDegrees = 180f;

    // 接触阴影面片略高于盘面、低于坐标与形势标记；半边长需容纳子边遮蔽，以及主光仰角不低于 45° 时的投影偏移。
    private const float ContactShadowHeight = 0.02f;
    private const float ContactShadowHalfSize = 3.4f;
    // 木面上的阴影偏暖、偏饱和，而不是中性灰。
    private static readonly Color ContactShadowColor = new Color(0.3f, 0.22f, 0.15f, 1f);
    private const float ContactShadowContactStrength = 0.5f;
    private const float ContactShadowContactWidth = 0.9f;
    private const float ContactShadowCastStrength = 0.4f;
    private const float ContactShadowCastHeight = 0.9f;
    private const float ContactShadowCastSoftness = 0.7f;

    private sealed class StoneLook
    {
        public Color baseColor;
        public Color reflectionTint;
        public float smoothness;
        public float softboxStrength;
        // 面光源轮廓半宽、半高、边缘模糊宽度（均为正切值），外圈光晕强度。
        public Vector4 softboxShape;
        public float reflectionStrength;
        public float wrap;
        public Vector4 detailStrength;
    }

    [MenuItem(CustomEditorMenuPaths.ChessBoard + "/应用棋子美术配置")]
    public static void Polish()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null) {
            Debug.LogError($"Chess stone shader not found: {ShaderPath}");
            return;
        }

        Shader contactShadowShader = AssetDatabase.LoadAssetAtPath<Shader>(ContactShadowShaderPath);
        if (contactShadowShader == null) {
            Debug.LogError($"Chess stone contact shadow shader not found: {ContactShadowShaderPath}");
            return;
        }

        Texture2D detailMap = AssetDatabase.LoadAssetAtPath<Texture2D>(DetailMapPath);
        if (detailMap == null) {
            Debug.LogError($"Chess stone detail map not found: {DetailMapPath}");
            return;
        }

        // 黑子：那智黑石，近黑固有色、细颗粒；左上方映出一块边缘柔和的顶灯反光，外圈渐隐，其余部分保持暗色，边缘带出棋盘暖色。
        // 面光源在凸面上会缩成一小块：反光面积小、核心亮才像光的倒影，大而灰的一片读起来像污渍。
        ConfigureMaterial(BlackMaterialPath, shader, detailMap, new StoneLook
        {
            baseColor = new Color(0.07f, 0.068f, 0.066f, 1f),
            reflectionTint = new Color(0.95f, 0.95f, 0.93f, 1f),
            smoothness = 0.72f,
            softboxStrength = 12f,
            softboxShape = new Vector4(0.16f, 0.12f, 0.06f, 0.1f),
            reflectionStrength = 0.5f,
            wrap = 0f,
            detailStrength = new Vector4(0f, 0.3f, 0f, 0f),
        });

        // 白子：蛤碁石，微暖的白、近看才能分辨的细条纹与云状色差、轻微透光；明暗保留体积感，亮面上只留一小块淡光泽。
        ConfigureMaterial(WhiteMaterialPath, shader, detailMap, new StoneLook
        {
            baseColor = new Color(0.88f, 0.875f, 0.855f, 1f),
            reflectionTint = new Color(1f, 0.98f, 0.95f, 1f),
            smoothness = 0.7f,
            softboxStrength = 5f,
            softboxShape = new Vector4(0.16f, 0.12f, 0.06f, 0.08f),
            reflectionStrength = 0.4f,
            wrap = 0.3f,
            detailStrength = new Vector4(0.035f, 0.02f, 0.04f, 0f),
        });

        Material contactShadowMaterial = ConfigureContactShadowMaterial(contactShadowShader);
        Mesh lensMesh = CreateOrUpdateMesh(LensMeshPath, BuildLensMesh);
        Mesh contactShadowMesh = CreateOrUpdateMesh(ContactShadowMeshPath, BuildContactShadowMesh);
        ConfigurePrefab(BlackPrefabPath, BlackMaterialPath, lensMesh, contactShadowMesh, contactShadowMaterial);
        ConfigurePrefab(WhitePrefabPath, WhiteMaterialPath, lensMesh, contactShadowMesh, contactShadowMaterial);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Chess stone assets polished.");
    }

    private static void ConfigureMaterial(string materialPath, Shader shader, Texture2D detailMap, StoneLook look)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) {
            Debug.LogError($"Chess stone material not found: {materialPath}");
            return;
        }

        material.shader = shader;
        material.SetColor("_BaseColor", look.baseColor);
        material.SetColor("_HighlightColor", look.reflectionTint);
        material.SetFloat("_Smoothness", look.smoothness);
        material.SetFloat("_SoftboxStrength", look.softboxStrength);
        material.SetVector("_SoftboxShape", look.softboxShape);
        material.SetFloat("_ReflectionStrength", look.reflectionStrength);
        material.SetFloat("_Wrap", look.wrap);
        material.SetTexture("_DetailMap", detailMap);
        material.SetVector("_DetailStrength", look.detailStrength);
        material.SetFloat("_AmbientStrength", 1f);
        EditorUtils.RemoveUnusedMaterialProperties(material);
        EditorUtility.SetDirty(material);
    }

    private static Material ConfigureContactShadowMaterial(Shader shader)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(ContactShadowMaterialPath);
        if (material == null) {
            material = new Material(shader) { name = "ChessStoneContactShadow" };
            AssetDatabase.CreateAsset(material, ContactShadowMaterialPath);
        }

        material.shader = shader;
        material.SetColor("_ShadowColor", ContactShadowColor);
        material.SetFloat("_StoneRadius", StoneSize.x * 0.5f);
        material.SetFloat("_ContactStrength", ContactShadowContactStrength);
        material.SetFloat("_ContactWidth", ContactShadowContactWidth);
        material.SetFloat("_CastStrength", ContactShadowCastStrength);
        material.SetFloat("_CastHeight", ContactShadowCastHeight);
        material.SetFloat("_CastSoftness", ContactShadowCastSoftness);
        EditorUtils.RemoveUnusedMaterialProperties(material);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void ConfigurePrefab(string prefabPath, string materialPath, Mesh lensMesh, Mesh contactShadowMesh, Material contactShadowMaterial)
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        if (prefabRoot == null) {
            Debug.LogError($"Chess stone prefab not found: {prefabPath}");
            return;
        }

        try {
            Transform model = prefabRoot.transform.Find(ModelNodePath);
            if (model == null) {
                Debug.LogError($"Chess stone model node not found: {prefabPath}/{ModelNodePath}");
                return;
            }

            model.localPosition = new Vector3(0f, StoneSize.y * 0.5f, 0f);
            model.localRotation = Quaternion.identity;
            model.localScale = StoneSize;

            MeshFilter meshFilter = model.GetComponent<MeshFilter>();
            if (meshFilter != null) {
                meshFilter.sharedMesh = lensMesh;
            }

            // 棋子不投射实时阴影，落在盘面上的阴影由接触阴影面片表现。
            MeshRenderer renderer = model.GetComponent<MeshRenderer>();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (renderer != null && material != null) {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
            }

            ConfigureContactShadow(model.parent, contactShadowMesh, contactShadowMaterial);

            ChessStoneVisualRandomizer randomizer = prefabRoot.GetComponent<ChessStoneVisualRandomizer>();
            if (randomizer != null) {
                SerializedObject serializedRandomizer = new SerializedObject(randomizer);
                serializedRandomizer.FindProperty("maxYawDegrees").floatValue = StoneMaxYawDegrees;
                serializedRandomizer.ApplyModifiedPropertiesWithoutUndo();
            }

            // 落子动画改由 ChessStoneView 代码驱动；根节点旧 Animator 在每次重新激活时都会重播，需移除。
            Animator animator = prefabRoot.GetComponent<Animator>();
            if (animator != null) {
                UnityEngine.Object.DestroyImmediate(animator);
            }

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
        }
        finally {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    // 接触阴影挂在 VisualOffset 下：跟随随机偏移，不随 Model 倾斜；下落时由 ChessStoneView 按高度偏移投影。
    // 必须排在 Model 之后：运行时用 GetComponentInChildren<Renderer> 从预览棋子 prefab 取材质，需要先命中 Model。
    private static void ConfigureContactShadow(Transform visualOffset, Mesh mesh, Material material)
    {
        string nodeName = ContactShadowNodePath.Substring(ContactShadowNodePath.LastIndexOf('/') + 1);
        Transform shadow = visualOffset.Find(nodeName);
        if (shadow == null) {
            shadow = new GameObject(nodeName).transform;
            shadow.SetParent(visualOffset, false);
        }

        shadow.SetAsLastSibling();
        shadow.localPosition = new Vector3(0f, ContactShadowHeight, 0f);
        shadow.localRotation = Quaternion.identity;
        shadow.localScale = Vector3.one;

        GameObject shadowGO = shadow.gameObject;
        MeshFilter meshFilter = shadowGO.GetComponent<MeshFilter>();
        if (meshFilter == null) {
            meshFilter = shadowGO.AddComponent<MeshFilter>();
        }
        meshFilter.sharedMesh = mesh;

        MeshRenderer renderer = shadowGO.GetComponent<MeshRenderer>();
        if (renderer == null) {
            renderer = shadowGO.AddComponent<MeshRenderer>();
        }
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

        if (shadowGO.GetComponent<ChessStoneContactShadow>() == null) {
            shadowGO.AddComponent<ChessStoneContactShadow>();
        }
    }

    // 更新已有网格资产而不是重建，保持 GUID 与 prefab 引用不变。
    private static Mesh CreateOrUpdateMesh(string meshPath, Action<Mesh> build)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        bool created = mesh == null;
        if (created) {
            mesh = new Mesh();
        }

        build(mesh);
        if (created) {
            if (!AssetDatabase.IsValidFolder(MeshFolder)) {
                AssetDatabase.CreateFolder("Assets/Models/Chess", "Meshes");
            }
            AssetDatabase.CreateAsset(mesh, meshPath);
        } else {
            EditorUtility.SetDirty(mesh);
        }
        return mesh;
    }

    // 朝上的正方形面片，按世界尺寸生成，中心即棋子中心。
    private static void BuildContactShadowMesh(Mesh mesh)
    {
        float halfSize = ContactShadowHalfSize;
        mesh.Clear();
        mesh.name = "StoneContactShadowQuad";
        mesh.SetVertices(new List<Vector3>
        {
            new Vector3(-halfSize, 0f, -halfSize),
            new Vector3(-halfSize, 0f, halfSize),
            new Vector3(halfSize, 0f, halfSize),
            new Vector3(halfSize, 0f, -halfSize),
        });
        mesh.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
        mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        mesh.RecalculateBounds();
        mesh.UploadMeshData(false);
    }

    // 旋转体网格：剖面自上极点到下极点，每点为 (半径, 高度, 法线半径分量, 法线高度分量)，单位为世界尺寸。
    // 顶点除以 StoneSize 存为单位尺寸；法线按 StoneSize 缩放后归一化，使经过 Model 非等比缩放后仍是真实剖面法线。
    private static void BuildLensMesh(Mesh mesh)
    {
        List<Vector4> profile = BuildLensProfile();
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        AddLensVertex(profile[0], 0f, vertices, normals, uvs);
        for (int ring = 1; ring < profile.Count - 1; ring++) {
            for (int i = 0; i < LensRadialSegments; i++) {
                AddLensVertex(profile[ring], Mathf.PI * 2f * i / LensRadialSegments, vertices, normals, uvs);
            }
        }
        int bottomPole = vertices.Count;
        AddLensVertex(profile[profile.Count - 1], 0f, vertices, normals, uvs);

        int ringCount = profile.Count - 2;
        for (int i = 0; i < LensRadialSegments; i++) {
            int next = (i + 1) % LensRadialSegments;
            triangles.Add(0);
            triangles.Add(1 + next);
            triangles.Add(1 + i);
        }
        for (int ring = 0; ring < ringCount - 1; ring++) {
            int upper = 1 + ring * LensRadialSegments;
            int lower = upper + LensRadialSegments;
            for (int i = 0; i < LensRadialSegments; i++) {
                int next = (i + 1) % LensRadialSegments;
                triangles.Add(upper + i);
                triangles.Add(upper + next);
                triangles.Add(lower + next);
                triangles.Add(upper + i);
                triangles.Add(lower + next);
                triangles.Add(lower + i);
            }
        }
        int lastRing = 1 + (ringCount - 1) * LensRadialSegments;
        for (int i = 0; i < LensRadialSegments; i++) {
            int next = (i + 1) % LensRadialSegments;
            triangles.Add(lastRing + i);
            triangles.Add(lastRing + next);
            triangles.Add(bottomPole);
        }

        mesh.Clear();
        mesh.name = "GoStoneLens";
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.UploadMeshData(false);
    }

    private static List<Vector4> BuildLensProfile()
    {
        List<Vector4> profile = new List<Vector4>();
        AppendHalfProfile(profile, LensTopCapSegments);

        List<Vector4> bottom = new List<Vector4>();
        AppendHalfProfile(bottom, LensBottomCapSegments);
        for (int i = bottom.Count - 2; i >= 0; i--) {
            Vector4 point = bottom[i];
            profile.Add(new Vector4(point.x, -point.y, point.z, -point.w));
        }
        return profile;
    }

    // 上半剖面：球冠从极点到切点，再接边缘圆角到赤道；球冠半径由球冠与圆角相切且顶点高度为半厚解出。
    private static void AppendHalfProfile(List<Vector4> profile, int capSegments)
    {
        float radius = StoneSize.x * 0.5f;
        float halfHeight = StoneSize.y * 0.5f;
        float fillet = StoneEdgeFilletRadius;
        float filletCenterX = radius - fillet;
        float capRadius = (filletCenterX * filletCenterX + halfHeight * halfHeight - fillet * fillet) / (2f * (halfHeight - fillet));
        float capCenterY = halfHeight - capRadius;
        float tangentAngle = Mathf.Atan2(filletCenterX, -capCenterY);

        for (int i = 0; i <= capSegments; i++) {
            float angle = tangentAngle * i / capSegments;
            Vector2 normal = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
            profile.Add(new Vector4(capRadius * normal.x, capCenterY + capRadius * normal.y, normal.x, normal.y));
        }
        for (int i = 1; i <= LensFilletSegments; i++) {
            float angle = Mathf.Lerp(tangentAngle, Mathf.PI * 0.5f, (float)i / LensFilletSegments);
            Vector2 normal = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
            profile.Add(new Vector4(filletCenterX + fillet * normal.x, fillet * normal.y, normal.x, normal.y));
        }
    }

    private static void AddLensVertex(Vector4 profilePoint, float angle, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs)
    {
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);
        Vector3 position = new Vector3(
            profilePoint.x * cos / StoneSize.x,
            profilePoint.y / StoneSize.y,
            profilePoint.x * sin / StoneSize.z);
        Vector3 normal = new Vector3(
            profilePoint.z * cos * StoneSize.x,
            profilePoint.w * StoneSize.y,
            profilePoint.z * sin * StoneSize.z).normalized;
        vertices.Add(position);
        normals.Add(normal);
        uvs.Add(new Vector2(position.x + 0.5f, position.z + 0.5f));
    }
}
#endif
