using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 贴图大小检测工具：
/// 1. 检测贴图分辨率（宽高）
/// 2. 检测贴图磁盘大小（KB/MB）
/// 3. 支持主流图片格式过滤
/// </summary>
public class TexSizeCheckWin : EditorWindow
{
    private class TextureRow
    {
        public string assetPath;
        public string extension;
        public int width;
        public int height;
        public int maxTextureSize;
        public long diskBytes;
        public bool overDimension;
        public bool overDiskSize;
    }

    [SerializeField] private int maxWidthThreshold = 2048;
    [SerializeField] private int maxHeightThreshold = 2048;
    [SerializeField] private float maxDiskSizeMBThreshold = 2f;
    [SerializeField] private bool onlyShowWarningRows = false;
    [SerializeField] private string extensionFilter =
        ".png,.jpg,.jpeg,.tga,.psd,.psb,.tif,.tiff,.bmp,.gif,.exr,.hdr,.dds,.ktx,.ktx2,.webp,.iff";

    private readonly List<TextureRow> _rows = new List<TextureRow>();
    private readonly List<TextureRow> _warningRows = new List<TextureRow>();
    private readonly HashSet<string> _extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private Vector2 _scroll;
    private long _totalDiskBytes;

    [MenuItem("Tools/MinoTools/贴图检测/贴图大小检测工具")]
    public static void Open()
    {
        TexSizeCheckWin window = GetWindow<TexSizeCheckWin>("贴图大小检测");
        window.minSize = new Vector2(860f, 480f);
    }

    private void OnGUI()
    {
        DrawHeader();
        EditorGUILayout.Space(6);
        DrawConfig();
        EditorGUILayout.Space(8);
        DrawButtons();
        EditorGUILayout.Space(8);
        DrawSummary();
        EditorGUILayout.Space(6);
        DrawList();
    }

    private void DrawHeader()
    {
        EditorGUILayout.HelpBox(
            "检测项目中的贴图资源分辨率与磁盘体积。\n" +
            "支持主流格式过滤，可快速定位超大贴图。",
            MessageType.Info);
    }

    private void DrawConfig()
    {
        maxWidthThreshold = Mathf.Max(1, EditorGUILayout.IntField("宽度阈值", maxWidthThreshold));
        maxHeightThreshold = Mathf.Max(1, EditorGUILayout.IntField("高度阈值", maxHeightThreshold));
        maxDiskSizeMBThreshold = Mathf.Max(0.01f, EditorGUILayout.FloatField("磁盘大小阈值(MB)", maxDiskSizeMBThreshold));
        onlyShowWarningRows = EditorGUILayout.Toggle("仅显示超阈值项", onlyShowWarningRows);
        extensionFilter = EditorGUILayout.TextField("格式过滤（逗号分隔）", extensionFilter);
        EditorGUILayout.HelpBox(
            "示例：.png,.jpg,.jpeg,.tga,.psd,.psb,.tif,.tiff,.bmp,.gif,.exr,.hdr,.dds,.ktx,.ktx2,.webp",
            MessageType.None);
    }

    private void DrawButtons()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("开始扫描", GUILayout.Height(30)))
            {
                RunScan();
            }

            GUI.enabled = _rows.Count > 0;
            if (GUILayout.Button("清空结果", GUILayout.Height(30)))
            {
                ClearRows();
            }

            GUI.enabled = _rows.Count > 0;
            if (GUILayout.Button("导出Markdown报告", GUILayout.Height(30)))
            {
                ExportMarkdownReport();
            }

            GUI.enabled = true;
        }
    }

    private void DrawSummary()
    {
        if (_rows.Count == 0)
        {
            return;
        }

        EditorGUILayout.LabelField("扫描汇总", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"贴图总数：{_rows.Count}");
        EditorGUILayout.LabelField($"超阈值项：{_warningRows.Count}");
        EditorGUILayout.LabelField($"总磁盘体积：{FormatSize(_totalDiskBytes)}");
    }

    private void DrawList()
    {
        if (_rows.Count == 0)
        {
            return;
        }

        List<TextureRow> source = onlyShowWarningRows ? _warningRows : _rows;

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        for (int i = 0; i < source.Count; i++)
        {
            TextureRow row = source[i];
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField($"{i + 1}. {row.assetPath}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"格式: {row.extension} | 分辨率: {row.width}x{row.height} | Import MaxSize: {row.maxTextureSize} | 文件大小: {FormatSize(row.diskBytes)}");
                EditorGUILayout.LabelField(
                    $"分辨率超阈值: {row.overDimension} | 体积超阈值: {row.overDiskSize}");
                if (GUILayout.Button("定位资源", GUILayout.Width(90f)))
                {
                    UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(row.assetPath);
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void RunScan()
    {
        ClearRows();
        BuildExtensions();
        if (_extensions.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "格式过滤为空或无效，请先填写至少一个图片后缀。", "确定");
            return;
        }

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
        string[] allPaths = AssetDatabase.GetAllAssetPaths();
        float sizeThresholdBytes = maxDiskSizeMBThreshold * 1024f * 1024f;

        for (int i = 0; i < allPaths.Length; i++)
        {
            string assetPath = allPaths[i];
            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string ext = Path.GetExtension(assetPath);
            if (string.IsNullOrEmpty(ext) || !_extensions.Contains(ext))
            {
                continue;
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
            {
                continue;
            }

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            int maxSize = importer != null ? importer.maxTextureSize : 0;

            string fullPath = Path.Combine(projectRoot, assetPath);
            long bytes = 0;
            if (File.Exists(fullPath))
            {
                bytes = new FileInfo(fullPath).Length;
            }

            TextureRow row = new TextureRow
            {
                assetPath = assetPath,
                extension = ext.ToLowerInvariant(),
                width = texture.width,
                height = texture.height,
                maxTextureSize = maxSize,
                diskBytes = bytes
            };

            row.overDimension = row.width > maxWidthThreshold || row.height > maxHeightThreshold;
            row.overDiskSize = row.diskBytes > sizeThresholdBytes;

            _rows.Add(row);
            _totalDiskBytes += row.diskBytes;
            if (row.overDimension || row.overDiskSize)
            {
                _warningRows.Add(row);
            }
        }

        _rows.Sort((a, b) => b.diskBytes.CompareTo(a.diskBytes));
        _warningRows.Sort((a, b) => b.diskBytes.CompareTo(a.diskBytes));

        EditorUtility.DisplayDialog(
            "扫描完成",
            $"贴图总数：{_rows.Count}\n" +
            $"超阈值项：{_warningRows.Count}\n" +
            $"总磁盘体积：{FormatSize(_totalDiskBytes)}",
            "确定");
    }

    private void BuildExtensions()
    {
        _extensions.Clear();
        if (string.IsNullOrWhiteSpace(extensionFilter))
        {
            return;
        }

        string[] tokens = extensionFilter.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            string ext = tokens[i].Trim();
            if (string.IsNullOrWhiteSpace(ext))
            {
                continue;
            }

            if (!ext.StartsWith("."))
            {
                ext = "." + ext;
            }

            _extensions.Add(ext.ToLowerInvariant());
        }
    }

    private void ClearRows()
    {
        _rows.Clear();
        _warningRows.Clear();
        _totalDiskBytes = 0;
    }

    private string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double kb = bytes / 1024d;
        if (kb < 1024)
        {
            return $"{kb:F2} KB";
        }

        double mb = kb / 1024d;
        if (mb < 1024)
        {
            return $"{mb:F2} MB";
        }

        double gb = mb / 1024d;
        return $"{gb:F2} GB";
    }

    private void ExportMarkdownReport()
    {
        string defaultName = $"TextureSizeReport_{DateTime.Now:yyyyMMdd_HHmmss}.md";
        string outputPath = EditorUtility.SaveFilePanel("导出贴图大小报告", Application.dataPath, defaultName, "md");
        if (string.IsNullOrEmpty(outputPath))
        {
            return;
        }

        using (StreamWriter writer = new StreamWriter(outputPath, false))
        {
            writer.WriteLine("# 贴图大小检测报告");
            writer.WriteLine();
            writer.WriteLine($"- 扫描时间：`{DateTime.Now:yyyy-MM-dd HH:mm:ss}`");
            writer.WriteLine($"- 宽度阈值：`{maxWidthThreshold}`");
            writer.WriteLine($"- 高度阈值：`{maxHeightThreshold}`");
            writer.WriteLine($"- 体积阈值：`{maxDiskSizeMBThreshold:F2} MB`");
            writer.WriteLine($"- 检测总数：`{_rows.Count}`");
            writer.WriteLine($"- 超阈值数：`{_warningRows.Count}`");
            writer.WriteLine($"- 总磁盘体积：`{FormatSize(_totalDiskBytes)}`");
            writer.WriteLine();
            writer.WriteLine("## 超阈值列表");
            writer.WriteLine();

            if (_warningRows.Count == 0)
            {
                writer.WriteLine("- 无");
            }
            else
            {
                for (int i = 0; i < _warningRows.Count; i++)
                {
                    TextureRow row = _warningRows[i];
                    writer.WriteLine(
                        $"- `{row.assetPath}` | 格式={row.extension} | 分辨率={row.width}x{row.height} | MaxSize={row.maxTextureSize} | 文件={FormatSize(row.diskBytes)} | 尺寸超限={row.overDimension} | 体积超限={row.overDiskSize}");
                }
            }
        }

        EditorUtility.RevealInFinder(outputPath);
    }
}
