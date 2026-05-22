using System;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 可挂载在场景物体上的本体材质批量生成组件。
/// </summary>
public class PiLiangMatMaker : MonoBehaviour
{
    private const string 默认总文件夹名 = "材质批量生成";

    [SerializeField]
    private MaterialGenerateConfig[] materialGenerateConfigs = Array.Empty<MaterialGenerateConfig>();

    [SerializeField]
    private string totalFolderName;

    /// <summary>
    /// 每个材质的生成配置（材质球与文件夹名一一绑定）。
    /// </summary>
    public MaterialGenerateConfig[] MaterialGenerateConfigs
    {
        get => materialGenerateConfigs;
        set => materialGenerateConfigs = value;
    }

    public void GenerateMaterialFromCurrent()
    {
#if UNITY_EDITOR
        if (materialGenerateConfigs == null || materialGenerateConfigs.Length == 0)
            return;

        string 时间戳 = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        bool 已生成至少一个材质 = false;

        for (int 索引 = 0; 索引 < materialGenerateConfigs.Length; 索引++)
        {
            MaterialGenerateConfig 材质生成配置 = materialGenerateConfigs[索引];
            if (材质生成配置 == null || 材质生成配置.BodyMaterial == null)
                continue;

            GenerateSingleMaterial(材质生成配置, 时间戳);
            已生成至少一个材质 = true;
        }

        if (!已生成至少一个材质)
            return;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.SetDirty(this);
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// 按单个配置项生成材质副本。
    /// </summary>
    private void GenerateSingleMaterial(MaterialGenerateConfig 材质生成配置, string 时间戳)
    {
        Material 源材质球 = 材质生成配置.BodyMaterial;
        string 总文件夹名称来源 = string.IsNullOrWhiteSpace(totalFolderName) ? 默认总文件夹名 : totalFolderName;
        string 总文件夹安全名 = GetSafeName(总文件夹名称来源);
        string 目标根目录 = EnsureSubFolder("Assets", 总文件夹安全名);

        string 文件夹名称来源 = string.IsNullOrWhiteSpace(材质生成配置.GeneratedFolderName)
            ? 源材质球.name
            : 材质生成配置.GeneratedFolderName;
        string 安全文件夹名 = GetSafeName(文件夹名称来源);
        string 材质目标目录 = EnsureSubFolder(目标根目录, 安全文件夹名);

        string 生成材质名 = $"{源材质球.name}_{时间戳}";
        string 生成材质路径 = AssetDatabase.GenerateUniqueAssetPath($"{材质目标目录}/{生成材质名}.mat");

        Material 生成材质球 = new Material(源材质球)
        {
            name = Path.GetFileNameWithoutExtension(生成材质路径)
        };

        AssetDatabase.CreateAsset(生成材质球, 生成材质路径);
    }

    /// <summary>
    /// 在父目录下确保指定子目录存在，并返回完整目录路径。
    /// </summary>
    private static string EnsureSubFolder(string 父目录路径, string 子目录名)
    {
        string 子目录完整路径 = $"{父目录路径}/{子目录名}";
        if (!AssetDatabase.IsValidFolder(子目录完整路径))
        {
            AssetDatabase.CreateFolder(父目录路径, 子目录名);
        }

        return 子目录完整路径;
    }
#endif

    /// <summary>
    /// 过滤非法文件名字符，确保文件夹与材质命名可用。
    /// </summary>
    private static string GetSafeName(string 原始名称)
    {
        if (string.IsNullOrWhiteSpace(原始名称))
            return "Material";

        string 安全名称 = 原始名称;
        char[] 非法字符数组 = Path.GetInvalidFileNameChars();
        for (int 索引 = 0; 索引 < 非法字符数组.Length; 索引++)
        {
            安全名称 = 安全名称.Replace(非法字符数组[索引], '_');
        }

        return string.IsNullOrWhiteSpace(安全名称) ? "Material" : 安全名称;
    }

    [Serializable]
    public class MaterialGenerateConfig
    {
        [SerializeField]
        private Material bodyMaterial;

        [SerializeField]
        private string generatedFolderName;

        /// <summary>
        /// 本体材质球。
        /// </summary>
        public Material BodyMaterial
        {
            get => bodyMaterial;
            set => bodyMaterial = value;
        }

        /// <summary>
        /// 当前材质球对应的生成文件夹名。
        /// </summary>
        public string GeneratedFolderName
        {
            get => generatedFolderName;
            set => generatedFolderName = value;
        }
    }
}
