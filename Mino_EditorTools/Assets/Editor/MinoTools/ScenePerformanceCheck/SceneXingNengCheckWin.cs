using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 当前场景性能检查工具（独立，不依赖其他 MinoTools）：
/// 模型网格、场景引用贴图、实时光照（不含纯烘焙）、URP Volume 后处理等。
/// </summary>
public class SceneXingNengCheckWin : EditorWindow
{
    private enum ResultTab
    {
        Summary,
        Mesh,
        Texture,
        Light,
        PostProcess
    }

    #region Row Types

    private class MeshRow
    {
        public GameObject gameObject;
        public string hierarchyPath;
        public string meshName;
        public string rendererType;
        public int triangleCount;
        public int vertexCount;
        public int materialSlotCount;
        public int boneCount;
        public bool hasLodGroup;
        public bool exceedsTriangleThreshold;
    }

    private class TextureRow
    {
        public string assetPath;
        public string extension;
        public int width;
        public int height;
        public int importMaxSize;
        public long diskBytes;
        public int referenceCount;
        public bool overDimension;
        public bool overDiskSize;
    }

    private class LightRow
    {
        public GameObject gameObject;
        public string hierarchyPath;
        public LightType lightType;
        public LightmapBakeType bakeType;
        public MixedLightingMode mixedMode;
        public LightShadows shadows;
        public float range;
        public float intensity;
        public bool castsShadow;
        public string note;
    }

    private class VolumeRow
    {
        public GameObject gameObject;
        public string hierarchyPath;
        public bool isGlobal;
        public float priority;
        public float weight;
        public string profilePath;
        public List<string> activeComponents = new List<string>();
        public List<string> expensiveComponents = new List<string>();
    }

    #endregion

    #region Config

    [SerializeField] private bool includeInactive = true;
    [SerializeField] private int meshTriangleThreshold = 10000;
    [SerializeField] private int sceneTotalTriangleWarning = 300000;
    [SerializeField] private int maxTextureWidth = 2048;
    [SerializeField] private int maxTextureHeight = 2048;
    [SerializeField] private float maxTextureDiskMB = 2f;
    [SerializeField] private int maxRealtimeLights = 4;
    [SerializeField] private int maxShadowCastingLights = 1;
    [SerializeField] private int materialSlotWarning = 2;
    [SerializeField] private int skinnedBoneWarning = 80;

    private static readonly HashSet<string> ExpensivePostFxTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Bloom",
        "DepthOfField",
        "MotionBlur",
        "ScreenSpaceAmbientOcclusion",
        "Vignette",
        "ChromaticAberration",
        "LensDistortion",
        "FilmGrain",
        "PaniniProjection",
        "ColorAdjustments",
        "Tonemapping",
        "LiftGammaGain",
        "SplitToning",
        "ShadowsMidtonesHighlights",
        "WhiteBalance",
        "ColorCurves",
        "ChannelMixer",
        "ColorLookup",
        "LensFlare",
        "ScreenSpaceLensFlare"
    };

    private static readonly HashSet<string> HighCostPostFxTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Bloom",
        "DepthOfField",
        "MotionBlur",
        "ScreenSpaceAmbientOcclusion",
        "ScreenSpaceLensFlare"
    };

    #endregion

    #region State

    private ResultTab _tab = ResultTab.Summary;
    private Vector2 _scroll;
    private bool _hasResult;
    private string _sceneName = string.Empty;
    private string _scenePath = string.Empty;

    private readonly List<MeshRow> _meshRows = new List<MeshRow>();
    private readonly List<MeshRow> _meshWarnings = new List<MeshRow>();
    private readonly List<TextureRow> _textureRows = new List<TextureRow>();
    private readonly List<TextureRow> _textureWarnings = new List<TextureRow>();
    private readonly List<LightRow> _realtimeLightRows = new List<LightRow>();
    private readonly List<LightRow> _lightWarnings = new List<LightRow>();
    private readonly List<VolumeRow> _volumeRows = new List<VolumeRow>();
    private readonly List<VolumeRow> _volumeWarnings = new List<VolumeRow>();

    private int _totalTriangles;
    private int _totalVertices;
    private int _meshRendererCount;
    private int _skinnedMeshCount;
    private int _lodGroupCount;
    private int _uniqueMaterialCount;
    private int _totalMaterialSlots;
    private long _textureTotalDiskBytes;
    private int _shadowCastingLightCount;
    private int _expensivePostFxCount;
    private readonly HashSet<LODGroup> _lodGroups = new HashSet<LODGroup>();

    #endregion

    [MenuItem("Tools/MinoTools/场景性能检查/场景性能检查工具")]
    public static void Open()
    {
        SceneXingNengCheckWin window = GetWindow<SceneXingNengCheckWin>("场景性能检查");
        window.minSize = new Vector2(820f, 520f);
    }

    private void OnGUI()
    {
        DrawHeader();
        EditorGUILayout.Space(6);
        DrawConfig();
        EditorGUILayout.Space(8);
        DrawButtons();
        EditorGUILayout.Space(8);

        if (!_hasResult)
        {
            return;
        }

        DrawTabBar();
        EditorGUILayout.Space(4);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        switch (_tab)
        {
            case ResultTab.Summary:
                DrawSummaryTab();
                break;
            case ResultTab.Mesh:
                DrawMeshTab();
                break;
            case ResultTab.Texture:
                DrawTextureTab();
                break;
            case ResultTab.Light:
                DrawLightTab();
                break;
            case ResultTab.PostProcess:
                DrawPostProcessTab();
                break;
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        EditorGUILayout.HelpBox(
            "扫描当前已打开场景的性能相关项：模型面数、场景材质引用的贴图、实时光照（不含纯烘焙灯）、URP Volume 后处理。\n" +
            "不调用其他 MinoTools，结果可导出 Markdown。",
            MessageType.Info);
    }

    private void DrawConfig()
    {
        EditorGUILayout.LabelField("扫描范围", EditorStyles.boldLabel);
        includeInactive = EditorGUILayout.Toggle("包含非激活对象", includeInactive);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("阈值", EditorStyles.boldLabel);
        meshTriangleThreshold = Mathf.Max(0, EditorGUILayout.IntField("单网格面数阈值", meshTriangleThreshold));
        sceneTotalTriangleWarning = Mathf.Max(0, EditorGUILayout.IntField("场景总面数警戒", sceneTotalTriangleWarning));
        materialSlotWarning = Mathf.Max(1, EditorGUILayout.IntField("材质槽位警戒", materialSlotWarning));
        skinnedBoneWarning = Mathf.Max(0, EditorGUILayout.IntField("Skinned骨骼数警戒", skinnedBoneWarning));
        maxTextureWidth = Mathf.Max(1, EditorGUILayout.IntField("贴图宽度阈值", maxTextureWidth));
        maxTextureHeight = Mathf.Max(1, EditorGUILayout.IntField("贴图高度阈值", maxTextureHeight));
        maxTextureDiskMB = Mathf.Max(0.01f, EditorGUILayout.FloatField("贴图磁盘阈值(MB)", maxTextureDiskMB));
        maxRealtimeLights = Mathf.Max(0, EditorGUILayout.IntField("实时光数量警戒", maxRealtimeLights));
        maxShadowCastingLights = Mathf.Max(0, EditorGUILayout.IntField("投影灯光数量警戒", maxShadowCastingLights));
    }

    private void DrawButtons()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("扫描当前场景", GUILayout.Height(30)))
            {
                RunScan();
            }

            GUI.enabled = _hasResult;
            if (GUILayout.Button("清空结果", GUILayout.Height(30)))
            {
                ClearResult();
            }

            if (GUILayout.Button("导出 Markdown 报告", GUILayout.Height(30)))
            {
                ExportMarkdownReport();
            }

            GUI.enabled = true;
        }
    }

    private void DrawTabBar()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            _tab = (ResultTab)GUILayout.Toolbar((int)_tab, new[]
            {
                $"汇总 ({GetSummaryIssueCount()})",
                $"模型 ({_meshWarnings.Count})",
                $"贴图 ({_textureWarnings.Count})",
                $"实时光 ({GetLightIssueCount()})",
                $"后处理 ({_volumeWarnings.Count})"
            });
        }
    }

    private int GetSummaryIssueCount()
    {
        int count = _meshWarnings.Count + _textureWarnings.Count + GetLightIssueCount() + _volumeWarnings.Count;
        if (_totalTriangles > sceneTotalTriangleWarning)
        {
            count++;
        }

        return count;
    }

    private int GetLightIssueCount()
    {
        int count = 0;
        if (_realtimeLightRows.Count > maxRealtimeLights)
        {
            count++;
        }

        if (_shadowCastingLightCount > maxShadowCastingLights)
        {
            count++;
        }

        return count;
    }

    private void DrawSummaryTab()
    {
        EditorGUILayout.LabelField("场景信息", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"场景：{_sceneName}");
        EditorGUILayout.LabelField($"路径：{_scenePath}");

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("模型汇总", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"MeshRenderer：{_meshRendererCount}  |  SkinnedMeshRenderer：{_skinnedMeshCount}  |  LODGroup：{_lodGroupCount}");
        EditorGUILayout.LabelField($"网格条目：{_meshRows.Count}  |  总三角面：{_totalTriangles}  |  总顶点：{_totalVertices}");
        EditorGUILayout.LabelField($"唯一材质数：{_uniqueMaterialCount}  |  材质槽合计：{_totalMaterialSlots}");
        DrawWarningLine(_totalTriangles > sceneTotalTriangleWarning,
            $"场景总面数超过警戒（>{sceneTotalTriangleWarning}）");

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("贴图汇总（场景材质引用）", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"贴图数：{_textureRows.Count}  |  总体积：{FormatSize(_textureTotalDiskBytes)}  |  超阈值：{_textureWarnings.Count}");

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("实时光照（不含纯烘焙）", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"实时光数量：{_realtimeLightRows.Count}  |  开启阴影：{_shadowCastingLightCount}");
        DrawWarningLine(_realtimeLightRows.Count > maxRealtimeLights,
            $"实时光数量超过警戒（>{maxRealtimeLights}）");
        DrawWarningLine(_shadowCastingLightCount > maxShadowCastingLights,
            $"投影灯光超过警戒（>{maxShadowCastingLights}）");

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("后处理 Volume", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Volume 数量：{_volumeRows.Count}  |  含高开销项：{_expensivePostFxCount}  |  异常 Volume：{_volumeWarnings.Count}");

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("快速结论", EditorStyles.boldLabel);
        if (GetSummaryIssueCount() == 0)
        {
            EditorGUILayout.HelpBox("未发现超过阈值的项。", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox($"共发现 {GetSummaryIssueCount()} 类需关注项，请切换上方分页查看明细。", MessageType.Warning);
        }
    }

    private static void DrawWarningLine(bool condition, string message)
    {
        if (condition)
        {
            EditorGUILayout.LabelField("⚠ " + message, EditorStyles.boldLabel);
        }
    }

    private void DrawMeshTab()
    {
        EditorGUILayout.LabelField($"全部网格（{_meshRows.Count}）", EditorStyles.boldLabel);
        DrawMeshRowList(_meshRows, false);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField($"超阈值 / 缺 LOD（{_meshWarnings.Count}）", EditorStyles.boldLabel);
        DrawMeshRowList(_meshWarnings, true);
    }

    private void DrawMeshRowList(List<MeshRow> rows, bool warningOnly)
    {
        if (rows.Count == 0)
        {
            EditorGUILayout.LabelField("无");
            return;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            MeshRow row = rows[i];
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField($"{i + 1}. {row.hierarchyPath}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"类型: {row.rendererType} | Mesh: {row.meshName} | Tri: {row.triangleCount} | Vert: {row.vertexCount}");
                EditorGUILayout.LabelField(
                    $"材质槽: {row.materialSlotCount} | 骨骼: {row.boneCount} | LODGroup: {row.hasLodGroup}");
                if (warningOnly && row.exceedsTriangleThreshold)
                {
                    EditorGUILayout.LabelField("⚠ 单网格面数超阈值", EditorStyles.miniLabel);
                }

                DrawPingButton(row.gameObject);
            }
        }
    }

    private void DrawTextureTab()
    {
        EditorGUILayout.LabelField($"场景引用贴图（{_textureRows.Count}）", EditorStyles.boldLabel);
        if (_textureRows.Count == 0)
        {
            EditorGUILayout.LabelField("无");
            return;
        }

        for (int i = 0; i < _textureRows.Count; i++)
        {
            TextureRow row = _textureRows[i];
            using (new EditorGUILayout.VerticalScope("box"))
            {
                string flag = row.overDimension || row.overDiskSize ? " ⚠" : string.Empty;
                EditorGUILayout.LabelField($"{i + 1}. {row.assetPath}{flag}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"分辨率: {row.width}x{row.height} | ImportMax: {row.importMaxSize} | 磁盘: {FormatSize(row.diskBytes)} | 引用次数: {row.referenceCount}");
                if (GUILayout.Button("定位资源", GUILayout.Width(90f)))
                {
                    UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(row.assetPath);
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
            }
        }
    }

    private void DrawLightTab()
    {
        if (GetLightIssueCount() > 0)
        {
            EditorGUILayout.HelpBox(
                $"实时光 {_realtimeLightRows.Count}（警戒>{maxRealtimeLights}），投影光 {_shadowCastingLightCount}（警戒>{maxShadowCastingLights}）",
                MessageType.Warning);
        }

        EditorGUILayout.LabelField($"实时光照清单（{_realtimeLightRows.Count}，已排除纯烘焙）", EditorStyles.boldLabel);
        if (_realtimeLightRows.Count == 0)
        {
            EditorGUILayout.LabelField("无");
            return;
        }

        for (int i = 0; i < _realtimeLightRows.Count; i++)
        {
            LightRow row = _realtimeLightRows[i];
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField($"{i + 1}. {row.hierarchyPath}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"类型: {row.lightType} | Mode: {row.bakeType} | Mixed: {row.mixedMode} | 阴影: {row.shadows}");
                EditorGUILayout.LabelField($"强度: {row.intensity:F2} | 范围: {row.range:F1} | {row.note}");
                DrawPingButton(row.gameObject);
            }
        }
    }

    private void DrawPostProcessTab()
    {
        EditorGUILayout.LabelField($"Volume 列表（{_volumeRows.Count}）", EditorStyles.boldLabel);
        if (_volumeRows.Count == 0)
        {
            EditorGUILayout.LabelField("无");
            return;
        }

        for (int i = 0; i < _volumeRows.Count; i++)
        {
            VolumeRow row = _volumeRows[i];
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField($"{i + 1}. {row.hierarchyPath}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"Global: {row.isGlobal} | Priority: {row.priority} | Weight: {row.weight:F2}");
                EditorGUILayout.LabelField($"Profile: {row.profilePath}");
                EditorGUILayout.LabelField($"启用组件: {string.Join(", ", row.activeComponents)}");
                if (row.expensiveComponents.Count > 0)
                {
                    EditorGUILayout.LabelField($"⚠ 高开销: {string.Join(", ", row.expensiveComponents)}", EditorStyles.boldLabel);
                }

                DrawPingButton(row.gameObject);
            }
        }
    }

    private static void DrawPingButton(GameObject go)
    {
        if (go != null && GUILayout.Button("定位对象", GUILayout.Width(90f)))
        {
            Selection.activeObject = go;
            EditorGUIUtility.PingObject(go);
        }
    }

    private void RunScan()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("提示", "请先打开并加载一个场景。", "确定");
            return;
        }

        ClearResult();
        _sceneName = scene.name;
        _scenePath = scene.path;

        GameObject[] roots = scene.GetRootGameObjects();
        HashSet<Material> uniqueMaterials = new HashSet<Material>();
        Dictionary<string, TextureRow> textureMap = new Dictionary<string, TextureRow>(StringComparer.OrdinalIgnoreCase);
        float textureDiskThreshold = maxTextureDiskMB * 1024f * 1024f;
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;

        for (int r = 0; r < roots.Length; r++)
        {
            ScanNodeRecursive(roots[r].transform, roots[r].transform, uniqueMaterials, textureMap, projectRoot, textureDiskThreshold);
        }

        FinalizeMeshWarnings();
        FinalizeTextureRows(textureMap);
        FinalizeLightWarnings();
        FinalizeVolumeWarnings();

        _lodGroupCount = _lodGroups.Count;
        _uniqueMaterialCount = uniqueMaterials.Count;
        _hasResult = true;

        EditorUtility.DisplayDialog(
            "扫描完成",
            $"场景：{_sceneName}\n" +
            $"总面数：{_totalTriangles}\n" +
            $"实时光：{_realtimeLightRows.Count}\n" +
            $"贴图：{_textureRows.Count}\n" +
            $"Volume：{_volumeRows.Count}\n" +
            $"需关注项：{GetSummaryIssueCount()}",
            "确定");
    }

    private void ScanNodeRecursive(
        Transform node,
        Transform sceneRoot,
        HashSet<Material> uniqueMaterials,
        Dictionary<string, TextureRow> textureMap,
        string projectRoot,
        float textureDiskThreshold)
    {
        if (node == null)
        {
            return;
        }

        if (!includeInactive && !node.gameObject.activeInHierarchy)
        {
            return;
        }

        GameObject go = node.gameObject;
        string path = BuildHierarchyPath(go.transform, sceneRoot);

        LODGroup lodGroup = go.GetComponent<LODGroup>();
        if (lodGroup != null)
        {
            _lodGroups.Add(lodGroup);
        }

        ScanMeshRenderer(go, path);
        ScanSkinnedMesh(go, path);
        ScanLights(go, path);
        ScanVolume(go, path);

        CollectMaterialsFromRenderer(go, uniqueMaterials, textureMap, projectRoot, textureDiskThreshold);

        int childCount = node.childCount;
        for (int i = 0; i < childCount; i++)
        {
            ScanNodeRecursive(node.GetChild(i), sceneRoot, uniqueMaterials, textureMap, projectRoot, textureDiskThreshold);
        }
    }

    private void ScanMeshRenderer(GameObject go, string path)
    {
        MeshFilter filter = go.GetComponent<MeshFilter>();
        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        if (filter == null || renderer == null || filter.sharedMesh == null)
        {
            return;
        }

        _meshRendererCount++;
        AddMeshRow(go, path, filter.sharedMesh, "MeshRenderer", renderer.sharedMaterials, false, 0);
    }

    private void ScanSkinnedMesh(GameObject go, string path)
    {
        SkinnedMeshRenderer smr = go.GetComponent<SkinnedMeshRenderer>();
        if (smr == null || smr.sharedMesh == null)
        {
            return;
        }

        _skinnedMeshCount++;
        AddMeshRow(go, path, smr.sharedMesh, "SkinnedMeshRenderer", smr.sharedMaterials, true, smr.bones != null ? smr.bones.Length : 0);
    }

    private void AddMeshRow(
        GameObject go,
        string path,
        Mesh mesh,
        string rendererType,
        Material[] materials,
        bool isSkinned,
        int boneCount)
    {
        int tri = mesh.triangles != null ? mesh.triangles.Length / 3 : 0;
        int vert = mesh.vertexCount;
        _totalTriangles += tri;
        _totalVertices += vert;

        int slotCount = materials != null ? materials.Length : 0;
        _totalMaterialSlots += slotCount;

        bool hasLod = go.GetComponentInParent<LODGroup>(true) != null;

        bool exceeds = tri > meshTriangleThreshold;
        bool slotWarn = slotCount > materialSlotWarning;
        bool boneWarn = isSkinned && boneCount > skinnedBoneWarning;
        bool missingLodWarn = tri > meshTriangleThreshold / 2 && !hasLod;

        MeshRow row = new MeshRow
        {
            gameObject = go,
            hierarchyPath = path,
            meshName = mesh.name,
            rendererType = rendererType,
            triangleCount = tri,
            vertexCount = vert,
            materialSlotCount = slotCount,
            boneCount = boneCount,
            hasLodGroup = hasLod,
            exceedsTriangleThreshold = exceeds
        };
        _meshRows.Add(row);

        if (exceeds || slotWarn || boneWarn || missingLodWarn)
        {
            _meshWarnings.Add(row);
        }
    }

    private void FinalizeMeshWarnings()
    {
        _meshRows.Sort((a, b) => b.triangleCount.CompareTo(a.triangleCount));
        _meshWarnings.Sort((a, b) => b.triangleCount.CompareTo(a.triangleCount));
    }

    private void CollectMaterialsFromRenderer(
        GameObject go,
        HashSet<Material> uniqueMaterials,
        Dictionary<string, TextureRow> textureMap,
        string projectRoot,
        float textureDiskThreshold)
    {
        Renderer[] renderers = go.GetComponents<Renderer>();
        for (int r = 0; r < renderers.Length; r++)
        {
            Material[] mats = renderers[r].sharedMaterials;
            if (mats == null)
            {
                continue;
            }

            for (int m = 0; m < mats.Length; m++)
            {
                CollectTexturesFromMaterial(mats[m], uniqueMaterials, textureMap, projectRoot, textureDiskThreshold);
            }
        }

        CollectUiTextures(go, textureMap, projectRoot, textureDiskThreshold);
    }

    private static void CollectUiTextures(
        GameObject go,
        Dictionary<string, TextureRow> textureMap,
        string projectRoot,
        float textureDiskThreshold)
    {
        UnityEngine.UI.Graphic graphic = go.GetComponent<UnityEngine.UI.Graphic>();
        if (graphic != null && graphic.mainTexture != null)
        {
            RegisterTexture(graphic.mainTexture, textureMap, projectRoot, textureDiskThreshold);
        }
    }

    private static void CollectTexturesFromMaterial(
        Material mat,
        HashSet<Material> uniqueMaterials,
        Dictionary<string, TextureRow> textureMap,
        string projectRoot,
        float textureDiskThreshold)
    {
        if (mat == null)
        {
            return;
        }

        uniqueMaterials.Add(mat);
        if (mat.shader == null)
        {
            return;
        }

        Shader shader = mat.shader;
        int count = ShaderUtil.GetPropertyCount(shader);
        for (int i = 0; i < count; i++)
        {
            if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
            {
                continue;
            }

            string propName = ShaderUtil.GetPropertyName(shader, i);
            Texture tex = mat.GetTexture(propName);
            RegisterTexture(tex, textureMap, projectRoot, textureDiskThreshold);
        }
    }

    private static void RegisterTexture(
        Texture tex,
        Dictionary<string, TextureRow> textureMap,
        string projectRoot,
        float textureDiskThreshold)
    {
        if (tex == null)
        {
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(tex);
        if (string.IsNullOrEmpty(assetPath))
        {
            return;
        }

        if (textureMap.TryGetValue(assetPath, out TextureRow existing))
        {
            existing.referenceCount++;
            return;
        }

        Texture2D texture2D = tex as Texture2D;
        int width = texture2D != null ? texture2D.width : tex.width;
        int height = texture2D != null ? texture2D.height : tex.height;

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        int maxSize = importer != null ? importer.maxTextureSize : 0;

        long bytes = 0;
        if (!string.IsNullOrEmpty(projectRoot))
        {
            string fullPath = Path.Combine(projectRoot, assetPath);
            if (File.Exists(fullPath))
            {
                bytes = new FileInfo(fullPath).Length;
            }
        }

        TextureRow row = new TextureRow
        {
            assetPath = assetPath,
            extension = Path.GetExtension(assetPath).ToLowerInvariant(),
            width = width,
            height = height,
            importMaxSize = maxSize,
            diskBytes = bytes,
            referenceCount = 1,
            overDimension = false,
            overDiskSize = false
        };
        textureMap.Add(assetPath, row);
    }

    private void FinalizeTextureRows(Dictionary<string, TextureRow> textureMap)
    {
        float textureDiskThreshold = maxTextureDiskMB * 1024f * 1024f;
        _textureRows.Clear();
        _textureWarnings.Clear();
        _textureTotalDiskBytes = 0;

        foreach (KeyValuePair<string, TextureRow> pair in textureMap)
        {
            TextureRow row = pair.Value;
            row.overDimension = row.width > maxTextureWidth || row.height > maxTextureHeight;
            row.overDiskSize = row.diskBytes > textureDiskThreshold;
            _textureRows.Add(row);
            _textureTotalDiskBytes += row.diskBytes;
            if (row.overDimension || row.overDiskSize)
            {
                _textureWarnings.Add(row);
            }
        }

        _textureRows.Sort((a, b) => b.diskBytes.CompareTo(a.diskBytes));
        _textureWarnings.Sort((a, b) => b.diskBytes.CompareTo(a.diskBytes));
    }

    private void ScanLights(GameObject go, string path)
    {
        Light light = go.GetComponent<Light>();
        if (light == null)
        {
            return;
        }

        if (!IsRealtimeLight(light))
        {
            return;
        }

        bool castsShadow = light.shadows != LightShadows.None;
        if (castsShadow)
        {
            _shadowCastingLightCount++;
        }

        LightRow row = new LightRow
        {
            gameObject = go,
            hierarchyPath = path,
            lightType = light.type,
            bakeType = light.lightmapBakeType,
            mixedMode = light.bakingOutput.mixedLightingMode,
            shadows = light.shadows,
            range = light.range,
            intensity = light.intensity,
            castsShadow = castsShadow,
            note = BuildLightNote(light)
        };
        _realtimeLightRows.Add(row);
    }

    private static bool IsRealtimeLight(Light light)
    {
        if (light == null)
        {
            return false;
        }

        return light.lightmapBakeType != LightmapBakeType.Baked;
    }

    private static string BuildLightNote(Light light)
    {
        if (light.lightmapBakeType == LightmapBakeType.Realtime)
        {
            return "完全实时光";
        }

        if (light.lightmapBakeType == LightmapBakeType.Mixed)
        {
            return "混合光（含实时直射/阴影贡献）";
        }

        return string.Empty;
    }

    private void FinalizeLightWarnings()
    {
        _lightWarnings.Clear();
        for (int i = 0; i < _realtimeLightRows.Count; i++)
        {
            LightRow row = _realtimeLightRows[i];
            if (row.castsShadow)
            {
                _lightWarnings.Add(row);
            }
        }

        if (_realtimeLightRows.Count <= maxRealtimeLights && _shadowCastingLightCount <= maxShadowCastingLights)
        {
            _lightWarnings.Clear();
        }
    }

    private void ScanVolume(GameObject go, string path)
    {
        Volume volume = go.GetComponent<Volume>();
        if (volume == null)
        {
            return;
        }

        VolumeProfile profile = volume.sharedProfile != null ? volume.sharedProfile : volume.profile;
        VolumeRow row = new VolumeRow
        {
            gameObject = go,
            hierarchyPath = path,
            isGlobal = volume.isGlobal,
            priority = volume.priority,
            weight = volume.weight,
            profilePath = profile != null ? AssetDatabase.GetAssetPath(profile) : "(无 Profile)"
        };

        if (profile != null)
        {
            CollectVolumeComponents(profile, row);
        }

        _volumeRows.Add(row);
    }

    private static void CollectVolumeComponents(VolumeProfile profile, VolumeRow row)
    {
        SerializedObject so = new SerializedObject(profile);
        SerializedProperty components = so.FindProperty("components");
        if (components == null || !components.isArray)
        {
            return;
        }

        for (int i = 0; i < components.arraySize; i++)
        {
            SerializedProperty element = components.GetArrayElementAtIndex(i);
            VolumeComponent comp = element.objectReferenceValue as VolumeComponent;
            if (comp == null || !comp.active)
            {
                continue;
            }

            string typeName = comp.GetType().Name;
            row.activeComponents.Add(typeName);

            if (HighCostPostFxTypes.Contains(typeName))
            {
                row.expensiveComponents.Add(typeName);
            }
            else if (ExpensivePostFxTypes.Contains(typeName))
            {
                row.expensiveComponents.Add(typeName);
            }
        }
    }

    private void FinalizeVolumeWarnings()
    {
        _expensivePostFxCount = 0;
        for (int i = 0; i < _volumeRows.Count; i++)
        {
            VolumeRow row = _volumeRows[i];
            _expensivePostFxCount += row.expensiveComponents.Count;
            if (row.expensiveComponents.Count > 0 || row.weight > 0.01f && row.activeComponents.Count > 6)
            {
                _volumeWarnings.Add(row);
            }
        }
    }

    private void ClearResult()
    {
        _hasResult = false;
        _sceneName = string.Empty;
        _scenePath = string.Empty;
        _meshRows.Clear();
        _meshWarnings.Clear();
        _textureRows.Clear();
        _textureWarnings.Clear();
        _realtimeLightRows.Clear();
        _lightWarnings.Clear();
        _volumeRows.Clear();
        _volumeWarnings.Clear();

        _totalTriangles = 0;
        _totalVertices = 0;
        _meshRendererCount = 0;
        _skinnedMeshCount = 0;
        _lodGroupCount = 0;
        _uniqueMaterialCount = 0;
        _totalMaterialSlots = 0;
        _textureTotalDiskBytes = 0;
        _shadowCastingLightCount = 0;
        _expensivePostFxCount = 0;
        _lodGroups.Clear();
    }

    private static string BuildHierarchyPath(Transform node, Transform sceneRoot)
    {
        if (node == null)
        {
            return string.Empty;
        }

        if (node == sceneRoot)
        {
            return node.name;
        }

        Transform current = node;
        string path = current.name;
        while (current.parent != null && current.parent != sceneRoot)
        {
            current = current.parent;
            path = current.name + "/" + path;
        }

        return sceneRoot.name + "/" + path;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes + " B";
        }

        if (bytes < 1024 * 1024)
        {
            return (bytes / 1024f).ToString("F1") + " KB";
        }

        return (bytes / (1024f * 1024f)).ToString("F2") + " MB";
    }

    private void ExportMarkdownReport()
    {
        if (!_hasResult)
        {
            EditorUtility.DisplayDialog("提示", "请先扫描当前场景。", "确定");
            return;
        }

        string defaultName = $"ScenePerf_{_sceneName}_{DateTime.Now:yyyyMMdd_HHmmss}.md";
        string outputPath = EditorUtility.SaveFilePanel("导出场景性能报告", Application.dataPath, defaultName, "md");
        if (string.IsNullOrEmpty(outputPath))
        {
            return;
        }

        StringBuilder sb = new StringBuilder(8192);
        sb.AppendLine("# 场景性能检查报告");
        sb.AppendLine();
        sb.AppendLine($"- 场景：`{_sceneName}`");
        sb.AppendLine($"- 路径：`{_scenePath}`");
        sb.AppendLine($"- 时间：`{DateTime.Now:yyyy-MM-dd HH:mm:ss}`");
        sb.AppendLine();

        sb.AppendLine("## 汇总");
        sb.AppendLine();
        sb.AppendLine($"- 总三角面：{_totalTriangles}（警戒 {sceneTotalTriangleWarning}）");
        sb.AppendLine($"- 总顶点：{_totalVertices}");
        sb.AppendLine($"- MeshRenderer / SkinnedMesh：{_meshRendererCount} / {_skinnedMeshCount}");
        sb.AppendLine($"- 唯一材质 / 材质槽合计：{_uniqueMaterialCount} / {_totalMaterialSlots}");
        sb.AppendLine($"- 场景贴图数 / 总体积：{_textureRows.Count} / {FormatSize(_textureTotalDiskBytes)}");
        sb.AppendLine($"- 实时光数量 / 投影光：{_realtimeLightRows.Count} / {_shadowCastingLightCount}");
        sb.AppendLine($"- Volume 数 / 高开销后处理项：{_volumeRows.Count} / {_expensivePostFxCount}");
        sb.AppendLine();

        AppendMeshSection(sb);
        AppendTextureSection(sb);
        AppendLightSection(sb);
        AppendVolumeSection(sb);

        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        EditorUtility.RevealInFinder(outputPath);
    }

    private void AppendMeshSection(StringBuilder sb)
    {
        sb.AppendLine("## 模型（超阈值 / 警戒）");
        sb.AppendLine();
        if (_meshWarnings.Count == 0 && _totalTriangles <= sceneTotalTriangleWarning)
        {
            sb.AppendLine("- 无");
        }
        else
        {
            if (_totalTriangles > sceneTotalTriangleWarning)
            {
                sb.AppendLine($"- ⚠ 场景总面数 {_totalTriangles} 超过警戒 {sceneTotalTriangleWarning}");
            }

            for (int i = 0; i < _meshWarnings.Count; i++)
            {
                MeshRow row = _meshWarnings[i];
                sb.AppendLine(
                    $"- `{row.hierarchyPath}` | {row.rendererType} | Tri={row.triangleCount} | MatSlots={row.materialSlotCount} | Bones={row.boneCount} | LOD={row.hasLodGroup}");
            }
        }

        sb.AppendLine();
    }

    private void AppendTextureSection(StringBuilder sb)
    {
        sb.AppendLine("## 贴图（超阈值）");
        sb.AppendLine();
        if (_textureWarnings.Count == 0)
        {
            sb.AppendLine("- 无");
        }
        else
        {
            for (int i = 0; i < _textureWarnings.Count; i++)
            {
                TextureRow row = _textureWarnings[i];
                sb.AppendLine(
                    $"- `{row.assetPath}` | {row.width}x{row.height} | {FormatSize(row.diskBytes)} | 引用={row.referenceCount}");
            }
        }

        sb.AppendLine();
    }

    private void AppendLightSection(StringBuilder sb)
    {
        sb.AppendLine("## 实时光照（不含纯烘焙）");
        sb.AppendLine();
        if (_realtimeLightRows.Count == 0)
        {
            sb.AppendLine("- 无");
        }
        else
        {
            for (int i = 0; i < _realtimeLightRows.Count; i++)
            {
                LightRow row = _realtimeLightRows[i];
                sb.AppendLine(
                    $"- `{row.hierarchyPath}` | {row.lightType} | {row.bakeType} | Shadow={row.shadows} | Intensity={row.intensity:F2} | {row.note}");
            }
        }

        sb.AppendLine();
    }

    private void AppendVolumeSection(StringBuilder sb)
    {
        sb.AppendLine("## 后处理 Volume");
        sb.AppendLine();
        if (_volumeRows.Count == 0)
        {
            sb.AppendLine("- 无");
        }
        else
        {
            for (int i = 0; i < _volumeRows.Count; i++)
            {
                VolumeRow row = _volumeRows[i];
                string expensive = row.expensiveComponents.Count > 0
                    ? string.Join(",", row.expensiveComponents)
                    : "无";
                sb.AppendLine(
                    $"- `{row.hierarchyPath}` | Global={row.isGlobal} | Weight={row.weight:F2} | Profile=`{row.profilePath}` | 高开销=[{expensive}]");
                sb.AppendLine($"  - 启用: {string.Join(", ", row.activeComponents)}");
            }
        }
    }
}
