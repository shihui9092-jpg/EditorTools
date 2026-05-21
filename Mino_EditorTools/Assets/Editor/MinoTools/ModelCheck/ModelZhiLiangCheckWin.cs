using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 模型基础检测工具：
/// 1. 面数检测（Triangles）
/// 2. 重叠顶点检测（按 epsilon 近似）
/// 3. Mesh 节点碰撞挂载检测（用于排查个别网格误挂碰撞）
/// 4. 子网格/材质槽位检测
/// 5. SkinnedMesh 骨骼数、BlendShape 检测
/// 6. LOD 检测（缺失与降档趋势）
/// 7. Markdown 报告导出
/// </summary>
public class ModelZhiLiangCheckWin : EditorWindow
{
    private class MeshCheckRow
    {
        public GameObject gameObject;
        public string hierarchyPath;
        public string meshName;
        public int triangleCount;
        public int vertexCount;
        public int overlappedVertexCount;
        public bool hasColliderOnMeshNode;
        public int subMeshCount;
        public int materialSlotCount;
        public int boneCount;
        public int blendShapeCount;
        public int meshColliderTriangleCount;
        public bool hasLodGroup;
        public int lodLevel;
    }

    private class LODGroupIssueRow
    {
        public string hierarchyPath;
        public int[] lodTriangles;
        public string issue;
    }

    [SerializeField] private GameObject targetRoot;
    [SerializeField] private bool includeInactive = true;
    [SerializeField] private int triangleThreshold = 10000;
    [SerializeField] private float overlapEpsilon = 0.0001f;
    [SerializeField] private bool colliderCheckOnSelfOnly = true;
    [SerializeField] private int subMeshThreshold = 1;
    [SerializeField] private int materialSlotThreshold = 1;
    [SerializeField] private int skinnedBoneThreshold = 80;
    [SerializeField] private int blendShapeThreshold = 20;
    [SerializeField] private int meshColliderTriangleThreshold = 2000;
    [SerializeField] private bool requireLodForHighPolyMesh = true;
    [SerializeField] private int lodRequiredTriangleThreshold = 5000;

    private readonly List<MeshCheckRow> _rows = new List<MeshCheckRow>();
    private readonly List<MeshCheckRow> _triangleExceededRows = new List<MeshCheckRow>();
    private readonly List<MeshCheckRow> _overlapRows = new List<MeshCheckRow>();
    private readonly List<MeshCheckRow> _meshHasColliderRows = new List<MeshCheckRow>();
    private readonly List<MeshCheckRow> _subMeshExceededRows = new List<MeshCheckRow>();
    private readonly List<MeshCheckRow> _materialSlotExceededRows = new List<MeshCheckRow>();
    private readonly List<MeshCheckRow> _highBoneRows = new List<MeshCheckRow>();
    private readonly List<MeshCheckRow> _highBlendShapeRows = new List<MeshCheckRow>();
    private readonly List<MeshCheckRow> _complexMeshColliderRows = new List<MeshCheckRow>();
    private readonly List<MeshCheckRow> _missingLodRows = new List<MeshCheckRow>();
    private readonly List<LODGroupIssueRow> _lodIssueRows = new List<LODGroupIssueRow>();
    private Vector2 _scroll;

    private int _totalTriangles;
    private int _totalVertices;
    private int _totalOverlappedVertices;

    [MenuItem("Tools/MinoTools/模型检测/模型基础检测工具")]
    public static void Open()
    {
        ModelZhiLiangCheckWin window = GetWindow<ModelZhiLiangCheckWin>("模型基础检测");
        window.minSize = new Vector2(760f, 460f);
    }

    private void OnGUI()
    {
        DrawHeader();
        EditorGUILayout.Space(6);
        DrawConfig();
        EditorGUILayout.Space(8);
        DrawActionButtons();
        EditorGUILayout.Space(8);
        DrawSummary();
        EditorGUILayout.Space(6);
        DrawDetails();
    }

    private void DrawHeader()
    {
        EditorGUILayout.HelpBox(
            "对目标模型/Prefab/场景节点执行基础质量检测：\n" +
            "1) 面数检测  2) 重叠顶点检测  3) 碰撞体挂载检测",
            MessageType.Info);
    }

    private void DrawConfig()
    {
        targetRoot = (GameObject)EditorGUILayout.ObjectField("检测目标", targetRoot, typeof(GameObject), true);
        includeInactive = EditorGUILayout.Toggle("包含非激活子节点", includeInactive);
        triangleThreshold = Mathf.Max(0, EditorGUILayout.IntField("单网格面数阈值", triangleThreshold));
        overlapEpsilon = Mathf.Max(0.0000001f, EditorGUILayout.FloatField("重叠顶点容差(Epsilon)", overlapEpsilon));
        colliderCheckOnSelfOnly = EditorGUILayout.Toggle("碰撞挂载检测仅检查Mesh自身节点", colliderCheckOnSelfOnly);
        subMeshThreshold = Mathf.Max(1, EditorGUILayout.IntField("子网格数阈值", subMeshThreshold));
        materialSlotThreshold = Mathf.Max(1, EditorGUILayout.IntField("材质槽位阈值", materialSlotThreshold));
        skinnedBoneThreshold = Mathf.Max(0, EditorGUILayout.IntField("Skinned骨骼数阈值", skinnedBoneThreshold));
        blendShapeThreshold = Mathf.Max(0, EditorGUILayout.IntField("BlendShape阈值", blendShapeThreshold));
        meshColliderTriangleThreshold = Mathf.Max(0, EditorGUILayout.IntField("MeshCollider面数阈值", meshColliderTriangleThreshold));
        requireLodForHighPolyMesh = EditorGUILayout.Toggle("高面数Mesh必须有LOD", requireLodForHighPolyMesh);
        lodRequiredTriangleThreshold = Mathf.Max(0, EditorGUILayout.IntField("要求LOD的面数阈值", lodRequiredTriangleThreshold));
    }

    private void DrawActionButtons()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = targetRoot != null;
            if (GUILayout.Button("开始检测", GUILayout.Height(30)))
            {
                RunCheck();
            }

            GUI.enabled = _rows.Count > 0;
            if (GUILayout.Button("清空结果", GUILayout.Height(30)))
            {
                ClearResult();
            }

            GUI.enabled = true;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = _rows.Count > 0;
            if (GUILayout.Button("导出Markdown报告", GUILayout.Height(26)))
            {
                ExportMarkdownReport();
            }

            GUI.enabled = true;
        }

        if (targetRoot == null)
        {
            EditorGUILayout.HelpBox("请先在「检测目标」中指定模型对象或 Prefab。", MessageType.Warning);
        }
    }

    private void DrawSummary()
    {
        if (_rows.Count == 0)
        {
            return;
        }

        EditorGUILayout.LabelField("检测汇总", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"网格节点数：{_rows.Count}");
        EditorGUILayout.LabelField($"总面数：{_totalTriangles}");
        EditorGUILayout.LabelField($"总顶点数：{_totalVertices}");
        EditorGUILayout.LabelField($"总重叠顶点数：{_totalOverlappedVertices}");
        EditorGUILayout.LabelField($"面数超阈值节点：{_triangleExceededRows.Count}");
        EditorGUILayout.LabelField($"存在重叠顶点节点：{_overlapRows.Count}");
        EditorGUILayout.LabelField($"检测到挂载碰撞的Mesh节点：{_meshHasColliderRows.Count}");
        EditorGUILayout.LabelField($"子网格超阈值节点：{_subMeshExceededRows.Count}");
        EditorGUILayout.LabelField($"材质槽位超阈值节点：{_materialSlotExceededRows.Count}");
        EditorGUILayout.LabelField($"骨骼数超阈值节点：{_highBoneRows.Count}");
        EditorGUILayout.LabelField($"BlendShape超阈值节点：{_highBlendShapeRows.Count}");
        EditorGUILayout.LabelField($"MeshCollider复杂度超阈值节点：{_complexMeshColliderRows.Count}");
        EditorGUILayout.LabelField($"高面数但缺LOD节点：{_missingLodRows.Count}");
        EditorGUILayout.LabelField($"LOD降档异常组：{_lodIssueRows.Count}");
    }

    private void DrawDetails()
    {
        if (_rows.Count == 0)
        {
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawSection("面数超阈值节点", _triangleExceededRows, true, false, false);
        DrawSection("存在重叠顶点节点", _overlapRows, false, true, false);
        DrawSection("检测到挂载碰撞的Mesh节点", _meshHasColliderRows, false, false, true);
        DrawSectionSimple("子网格超阈值节点", _subMeshExceededRows, row => $"SubMesh: {row.subMeshCount}");
        DrawSectionSimple("材质槽位超阈值节点", _materialSlotExceededRows, row => $"Materials: {row.materialSlotCount}");
        DrawSectionSimple("骨骼数超阈值节点", _highBoneRows, row => $"Bones: {row.boneCount}");
        DrawSectionSimple("BlendShape超阈值节点", _highBlendShapeRows, row => $"BlendShape: {row.blendShapeCount}");
        DrawSectionSimple("MeshCollider复杂度超阈值节点", _complexMeshColliderRows, row => $"MeshCollider Triangles: {row.meshColliderTriangleCount}");
        DrawSectionSimple("高面数但缺LOD节点", _missingLodRows, row => $"Triangles: {row.triangleCount}");
        DrawLodIssueSection();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("全部网格明细", EditorStyles.boldLabel);
        for (int i = 0; i < _rows.Count; i++)
        {
            MeshCheckRow row = _rows[i];
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField($"{i + 1}. {row.hierarchyPath}");
                EditorGUILayout.LabelField($"Mesh: {row.meshName}");
                EditorGUILayout.LabelField($"Triangles: {row.triangleCount}  Vertices: {row.vertexCount}");
                EditorGUILayout.LabelField($"Overlapped Vertices: {row.overlappedVertexCount}  Collider On Mesh Node: {row.hasColliderOnMeshNode}");
                EditorGUILayout.LabelField(
                    $"SubMesh: {row.subMeshCount}  Materials: {row.materialSlotCount}  Bones: {row.boneCount}  BlendShape: {row.blendShapeCount}");
                EditorGUILayout.LabelField(
                    $"MeshCollider Triangles: {row.meshColliderTriangleCount}  Has LOD: {row.hasLodGroup}  LOD Level: {row.lodLevel}");
                if (row.gameObject != null && GUILayout.Button("定位对象", GUILayout.Width(90f)))
                {
                    Selection.activeObject = row.gameObject;
                    EditorGUIUtility.PingObject(row.gameObject);
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawSection(string title, List<MeshCheckRow> rows, bool showTriangle, bool showOverlap, bool showCollider)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField($"{title}（{rows.Count}）", EditorStyles.boldLabel);

        if (rows.Count == 0)
        {
            EditorGUILayout.LabelField("无");
            return;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            MeshCheckRow row = rows[i];
            using (new EditorGUILayout.HorizontalScope())
            {
                string suffix = string.Empty;
                if (showTriangle)
                {
                    suffix = $" | Triangles: {row.triangleCount}";
                }
                else if (showOverlap)
                {
                    suffix = $" | Overlap: {row.overlappedVertexCount}";
                }
                else if (showCollider)
                {
                    suffix = " | Collider: Mounted";
                }

                EditorGUILayout.LabelField($"{i + 1}. {row.hierarchyPath}{suffix}");
                if (row.gameObject != null && GUILayout.Button("定位", GUILayout.Width(60f)))
                {
                    Selection.activeObject = row.gameObject;
                    EditorGUIUtility.PingObject(row.gameObject);
                }
            }
        }
    }

    private void DrawSectionSimple(string title, List<MeshCheckRow> rows, Func<MeshCheckRow, string> suffixSelector)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField($"{title}（{rows.Count}）", EditorStyles.boldLabel);

        if (rows.Count == 0)
        {
            EditorGUILayout.LabelField("无");
            return;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            MeshCheckRow row = rows[i];
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{i + 1}. {row.hierarchyPath} | {suffixSelector(row)}");
                if (row.gameObject != null && GUILayout.Button("定位", GUILayout.Width(60f)))
                {
                    Selection.activeObject = row.gameObject;
                    EditorGUIUtility.PingObject(row.gameObject);
                }
            }
        }
    }

    private void DrawLodIssueSection()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField($"LOD降档异常组（{_lodIssueRows.Count}）", EditorStyles.boldLabel);
        if (_lodIssueRows.Count == 0)
        {
            EditorGUILayout.LabelField("无");
            return;
        }

        for (int i = 0; i < _lodIssueRows.Count; i++)
        {
            LODGroupIssueRow row = _lodIssueRows[i];
            string triangles = string.Join(" -> ", row.lodTriangles);
            EditorGUILayout.LabelField($"{i + 1}. {row.hierarchyPath} | LOD Triangles: {triangles} | {row.issue}");
        }
    }

    private void RunCheck()
    {
        ClearResult();

        Dictionary<Renderer, (bool hasLod, int lodLevel)> lodMap = BuildRendererLodMap(targetRoot, includeInactive);
        List<GameObject> meshNodes = CollectMeshNodes(targetRoot, includeInactive);
        for (int i = 0; i < meshNodes.Count; i++)
        {
            GameObject go = meshNodes[i];
            Mesh mesh = GetSharedMesh(go);
            if (mesh == null)
            {
                continue;
            }

            MeshCheckRow row = new MeshCheckRow
            {
                gameObject = go,
                hierarchyPath = BuildHierarchyPath(go, targetRoot.transform),
                meshName = mesh.name,
                triangleCount = mesh.triangles != null ? mesh.triangles.Length / 3 : 0,
                vertexCount = mesh.vertexCount,
                overlappedVertexCount = CountOverlappedVertices(mesh, overlapEpsilon),
                hasColliderOnMeshNode = HasColliderOnMeshNode(go, colliderCheckOnSelfOnly),
                subMeshCount = mesh.subMeshCount,
                materialSlotCount = GetMaterialSlotCount(go),
                boneCount = GetBoneCount(go),
                blendShapeCount = mesh.blendShapeCount,
                meshColliderTriangleCount = GetMeshColliderTriangleCount(go, colliderCheckOnSelfOnly)
            };
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null && lodMap.TryGetValue(renderer, out (bool hasLod, int lodLevel) lodInfo))
            {
                row.hasLodGroup = lodInfo.hasLod;
                row.lodLevel = lodInfo.lodLevel;
            }
            else
            {
                row.hasLodGroup = false;
                row.lodLevel = -1;
            }

            _rows.Add(row);
            _totalTriangles += row.triangleCount;
            _totalVertices += row.vertexCount;
            _totalOverlappedVertices += row.overlappedVertexCount;

            if (row.triangleCount > triangleThreshold)
            {
                _triangleExceededRows.Add(row);
            }

            if (row.overlappedVertexCount > 0)
            {
                _overlapRows.Add(row);
            }

            if (row.hasColliderOnMeshNode)
            {
                _meshHasColliderRows.Add(row);
            }

            if (row.subMeshCount > subMeshThreshold)
            {
                _subMeshExceededRows.Add(row);
            }

            if (row.materialSlotCount > materialSlotThreshold)
            {
                _materialSlotExceededRows.Add(row);
            }

            if (row.boneCount > skinnedBoneThreshold)
            {
                _highBoneRows.Add(row);
            }

            if (row.blendShapeCount > blendShapeThreshold)
            {
                _highBlendShapeRows.Add(row);
            }

            if (row.meshColliderTriangleCount > meshColliderTriangleThreshold)
            {
                _complexMeshColliderRows.Add(row);
            }

            if (requireLodForHighPolyMesh && row.triangleCount >= lodRequiredTriangleThreshold && !row.hasLodGroup)
            {
                _missingLodRows.Add(row);
            }
        }

        AnalyzeLodGroups(targetRoot, includeInactive);

        if (_rows.Count == 0)
        {
            EditorUtility.DisplayDialog("检测完成", "未检测到 MeshFilter 或 SkinnedMeshRenderer 对应网格。", "确定");
        }
        else
        {
            EditorUtility.DisplayDialog(
                "检测完成",
                $"网格节点：{_rows.Count}\n" +
                $"总面数：{_totalTriangles}\n" +
                $"重叠顶点总数：{_totalOverlappedVertices}\n" +
                $"检测到挂载碰撞的Mesh节点：{_meshHasColliderRows.Count}\n" +
                $"LOD降档异常组：{_lodIssueRows.Count}",
                "确定");
        }
    }

    private void ClearResult()
    {
        _rows.Clear();
        _triangleExceededRows.Clear();
        _overlapRows.Clear();
        _meshHasColliderRows.Clear();
        _subMeshExceededRows.Clear();
        _materialSlotExceededRows.Clear();
        _highBoneRows.Clear();
        _highBlendShapeRows.Clear();
        _complexMeshColliderRows.Clear();
        _missingLodRows.Clear();
        _lodIssueRows.Clear();
        _totalTriangles = 0;
        _totalVertices = 0;
        _totalOverlappedVertices = 0;
    }

    private List<GameObject> CollectMeshNodes(GameObject root, bool includeInactiveNode)
    {
        List<GameObject> nodes = new List<GameObject>();
        if (root == null)
        {
            return nodes;
        }

        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(includeInactiveNode);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter != null && filter.sharedMesh != null)
            {
                nodes.Add(filter.gameObject);
            }
        }

        SkinnedMeshRenderer[] skinnedRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactiveNode);
        for (int i = 0; i < skinnedRenderers.Length; i++)
        {
            SkinnedMeshRenderer skinned = skinnedRenderers[i];
            if (skinned == null || skinned.sharedMesh == null)
            {
                continue;
            }

            if (!nodes.Contains(skinned.gameObject))
            {
                nodes.Add(skinned.gameObject);
            }
        }

        return nodes;
    }

    private Mesh GetSharedMesh(GameObject go)
    {
        if (go == null)
        {
            return null;
        }

        MeshFilter filter = go.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            return filter.sharedMesh;
        }

        SkinnedMeshRenderer skinned = go.GetComponent<SkinnedMeshRenderer>();
        if (skinned != null && skinned.sharedMesh != null)
        {
            return skinned.sharedMesh;
        }

        return null;
    }

    private int GetMaterialSlotCount(GameObject go)
    {
        if (go == null)
        {
            return 0;
        }

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer == null || renderer.sharedMaterials == null)
        {
            return 0;
        }

        return renderer.sharedMaterials.Length;
    }

    private int GetBoneCount(GameObject go)
    {
        if (go == null)
        {
            return 0;
        }

        SkinnedMeshRenderer skinned = go.GetComponent<SkinnedMeshRenderer>();
        if (skinned == null || skinned.bones == null)
        {
            return 0;
        }

        return skinned.bones.Length;
    }

    private bool HasColliderOnMeshNode(GameObject go, bool selfOnly)
    {
        if (go == null)
        {
            return false;
        }

        if (selfOnly)
        {
            return go.GetComponent<Collider>() != null;
        }

        Transform current = go.transform;
        while (current != null)
        {
            Collider[] colliders = current.GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    return true;
                }
            }

            current = current.parent;
        }

        return false;
    }

    private int GetMeshColliderTriangleCount(GameObject go, bool selfOnly)
    {
        if (go == null)
        {
            return 0;
        }

        int maxTriangles = 0;
        if (selfOnly)
        {
            MeshCollider[] colliders = go.GetComponents<MeshCollider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Mesh mesh = colliders[i] != null ? colliders[i].sharedMesh : null;
                int tris = mesh != null && mesh.triangles != null ? mesh.triangles.Length / 3 : 0;
                maxTriangles = Mathf.Max(maxTriangles, tris);
            }

            return maxTriangles;
        }

        Transform current = go.transform;
        while (current != null)
        {
            MeshCollider[] colliders = current.GetComponents<MeshCollider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Mesh mesh = colliders[i] != null ? colliders[i].sharedMesh : null;
                int tris = mesh != null && mesh.triangles != null ? mesh.triangles.Length / 3 : 0;
                maxTriangles = Mathf.Max(maxTriangles, tris);
            }

            current = current.parent;
        }

        return maxTriangles;
    }

    private Dictionary<Renderer, (bool hasLod, int lodLevel)> BuildRendererLodMap(GameObject root, bool includeInactiveNode)
    {
        Dictionary<Renderer, (bool hasLod, int lodLevel)> map = new Dictionary<Renderer, (bool hasLod, int lodLevel)>();
        if (root == null)
        {
            return map;
        }

        LODGroup[] groups = root.GetComponentsInChildren<LODGroup>(includeInactiveNode);
        for (int i = 0; i < groups.Length; i++)
        {
            LOD[] lods = groups[i].GetLODs();
            for (int lodIndex = 0; lodIndex < lods.Length; lodIndex++)
            {
                Renderer[] renderers = lods[lodIndex].renderers;
                for (int r = 0; r < renderers.Length; r++)
                {
                    Renderer renderer = renderers[r];
                    if (renderer == null)
                    {
                        continue;
                    }

                    map[renderer] = (true, lodIndex);
                }
            }
        }

        return map;
    }

    private void AnalyzeLodGroups(GameObject root, bool includeInactiveNode)
    {
        if (root == null)
        {
            return;
        }

        LODGroup[] groups = root.GetComponentsInChildren<LODGroup>(includeInactiveNode);
        for (int i = 0; i < groups.Length; i++)
        {
            LODGroup group = groups[i];
            LOD[] lods = group.GetLODs();
            if (lods == null || lods.Length <= 1)
            {
                _lodIssueRows.Add(new LODGroupIssueRow
                {
                    hierarchyPath = BuildHierarchyPath(group.gameObject, root.transform),
                    lodTriangles = Array.Empty<int>(),
                    issue = "LOD层级数量不足（<=1）"
                });
                continue;
            }

            int[] lodTriangles = new int[lods.Length];
            for (int lodIndex = 0; lodIndex < lods.Length; lodIndex++)
            {
                lodTriangles[lodIndex] = SumTrianglesOnRenderers(lods[lodIndex].renderers);
            }

            bool isDescending = true;
            for (int lodIndex = 1; lodIndex < lodTriangles.Length; lodIndex++)
            {
                if (lodTriangles[lodIndex] >= lodTriangles[lodIndex - 1])
                {
                    isDescending = false;
                    break;
                }
            }

            if (!isDescending)
            {
                _lodIssueRows.Add(new LODGroupIssueRow
                {
                    hierarchyPath = BuildHierarchyPath(group.gameObject, root.transform),
                    lodTriangles = lodTriangles,
                    issue = "LOD面数未严格递减"
                });
            }
        }
    }

    private int SumTrianglesOnRenderers(Renderer[] renderers)
    {
        if (renderers == null)
        {
            return 0;
        }

        int sum = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            Mesh mesh = null;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null)
            {
                mesh = filter.sharedMesh;
            }
            else
            {
                SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                if (skinned != null)
                {
                    mesh = skinned.sharedMesh;
                }
            }

            if (mesh != null && mesh.triangles != null)
            {
                sum += mesh.triangles.Length / 3;
            }
        }

        return sum;
    }

    private int CountOverlappedVertices(Mesh mesh, float epsilon)
    {
        if (mesh == null || mesh.vertexCount == 0)
        {
            return 0;
        }

        Vector3[] vertices = mesh.vertices;
        Dictionary<Vector3Int, int> countMap = new Dictionary<Vector3Int, int>(vertices.Length);
        int overlap = 0;
        float inv = 1f / Mathf.Max(0.0000001f, epsilon);

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i];
            Vector3Int key = new Vector3Int(
                Mathf.RoundToInt(v.x * inv),
                Mathf.RoundToInt(v.y * inv),
                Mathf.RoundToInt(v.z * inv));

            if (countMap.TryGetValue(key, out int existing))
            {
                countMap[key] = existing + 1;
                overlap++;
            }
            else
            {
                countMap.Add(key, 1);
            }
        }

        return overlap;
    }

    private string BuildHierarchyPath(GameObject node, Transform rootTransform)
    {
        if (node == null)
        {
            return string.Empty;
        }

        Transform current = node.transform;
        string path = current.name;
        while (current.parent != null && current.parent != rootTransform)
        {
            current = current.parent;
            path = current.name + "/" + path;
        }

        if (rootTransform != null)
        {
            path = rootTransform.name + "/" + path;
        }

        return path;
    }

    private void ExportMarkdownReport()
    {
        string defaultName = $"ModelCheckReport_{DateTime.Now:yyyyMMdd_HHmmss}.md";
        string outputPath = EditorUtility.SaveFilePanel("导出模型检测报告", Application.dataPath, defaultName, "md");
        if (string.IsNullOrEmpty(outputPath))
        {
            return;
        }

        StringBuilder sb = new StringBuilder(4096);
        sb.AppendLine("# 模型检测报告");
        sb.AppendLine();
        sb.AppendLine($"- 目标：`{(targetRoot != null ? targetRoot.name : "未设置")}`");
        sb.AppendLine($"- 时间：`{DateTime.Now:yyyy-MM-dd HH:mm:ss}`");
        sb.AppendLine();
        sb.AppendLine("## 汇总");
        sb.AppendLine();
        sb.AppendLine($"- 网格节点数：{_rows.Count}");
        sb.AppendLine($"- 总面数：{_totalTriangles}");
        sb.AppendLine($"- 总顶点数：{_totalVertices}");
        sb.AppendLine($"- 总重叠顶点数：{_totalOverlappedVertices}");
        sb.AppendLine($"- 面数超阈值：{_triangleExceededRows.Count}");
        sb.AppendLine($"- 重叠顶点命中：{_overlapRows.Count}");
        sb.AppendLine($"- Mesh节点挂载碰撞命中：{_meshHasColliderRows.Count}");
        sb.AppendLine($"- 子网格超阈值：{_subMeshExceededRows.Count}");
        sb.AppendLine($"- 材质槽位超阈值：{_materialSlotExceededRows.Count}");
        sb.AppendLine($"- 骨骼数超阈值：{_highBoneRows.Count}");
        sb.AppendLine($"- BlendShape超阈值：{_highBlendShapeRows.Count}");
        sb.AppendLine($"- MeshCollider复杂度超阈值：{_complexMeshColliderRows.Count}");
        sb.AppendLine($"- 高面数缺LOD：{_missingLodRows.Count}");
        sb.AppendLine($"- LOD降档异常组：{_lodIssueRows.Count}");
        sb.AppendLine();

        AppendMeshList(sb, "面数超阈值节点", _triangleExceededRows, row => $"Triangles={row.triangleCount}");
        AppendMeshList(sb, "存在重叠顶点节点", _overlapRows, row => $"Overlap={row.overlappedVertexCount}");
        AppendMeshList(sb, "检测到挂载碰撞的Mesh节点", _meshHasColliderRows, row => "Collider=Mounted");
        AppendMeshList(sb, "子网格超阈值节点", _subMeshExceededRows, row => $"SubMesh={row.subMeshCount}");
        AppendMeshList(sb, "材质槽位超阈值节点", _materialSlotExceededRows, row => $"Materials={row.materialSlotCount}");
        AppendMeshList(sb, "骨骼数超阈值节点", _highBoneRows, row => $"Bones={row.boneCount}");
        AppendMeshList(sb, "BlendShape超阈值节点", _highBlendShapeRows, row => $"BlendShape={row.blendShapeCount}");
        AppendMeshList(sb, "MeshCollider复杂度超阈值节点", _complexMeshColliderRows, row => $"MeshColliderTriangles={row.meshColliderTriangleCount}");
        AppendMeshList(sb, "高面数但缺LOD节点", _missingLodRows, row => $"Triangles={row.triangleCount}");

        sb.AppendLine("## LOD降档异常组");
        sb.AppendLine();
        if (_lodIssueRows.Count == 0)
        {
            sb.AppendLine("- 无");
        }
        else
        {
            for (int i = 0; i < _lodIssueRows.Count; i++)
            {
                LODGroupIssueRow row = _lodIssueRows[i];
                string triangles = row.lodTriangles != null && row.lodTriangles.Length > 0
                    ? string.Join(" -> ", row.lodTriangles)
                    : "无有效LOD面数";
                sb.AppendLine($"- `{row.hierarchyPath}` | LOD={triangles} | {row.issue}");
            }
        }

        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        EditorUtility.RevealInFinder(outputPath);
    }

    private void AppendMeshList(StringBuilder sb, string title, List<MeshCheckRow> rows, Func<MeshCheckRow, string> suffixSelector)
    {
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        if (rows.Count == 0)
        {
            sb.AppendLine("- 无");
            sb.AppendLine();
            return;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            MeshCheckRow row = rows[i];
            sb.AppendLine($"- `{row.hierarchyPath}` | {suffixSelector(row)}");
        }

        sb.AppendLine();
    }
}
