using UnityEngine;
using System;
using System.IO;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 通用资源凹槽：支持图片、材质、预制体。
/// </summary>
public class ZiYuanRenameSlot : MonoBehaviour
{
    [SerializeField]
    private AssetRenameConfig[] assetRenameConfigs = Array.Empty<AssetRenameConfig>();

    /// <summary>
    /// 资源重命名配置数组。
    /// </summary>
    public AssetRenameConfig[] AssetRenameConfigs
    {
        get => assetRenameConfigs;
        set => assetRenameConfigs = value;
    }

    /// <summary>
    /// 判断资源是否为允许类型：图片、材质、预制体。
    /// </summary>
    public static bool IsSupportedAsset(UnityEngine.Object assetObject)
    {
        if (assetObject == null)
            return false;

        if (assetObject is Texture2D || assetObject is Sprite || assetObject is Material)
            return true;

#if UNITY_EDITOR
        if (assetObject is GameObject prefabObject)
        {
            return PrefabUtility.IsPartOfPrefabAsset(prefabObject);
        }
#endif

        return false;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 按数组配置批量重命名资源文件。
    /// </summary>
    public bool TryRenameAllAssetFiles(out string 结果消息)
    {
        if (assetRenameConfigs == null || assetRenameConfigs.Length == 0)
        {
            结果消息 = "当前没有可处理的重命名配置。";
            return false;
        }

        int 成功数量 = 0;
        int 失败数量 = 0;
        string 首个成功消息 = string.Empty;

        for (int 索引 = 0; 索引 < assetRenameConfigs.Length; 索引++)
        {
            AssetRenameConfig 重命名配置 = assetRenameConfigs[索引];
            if (!TryRenameSingleAssetFile(重命名配置, out string 单项结果消息))
            {
                失败数量++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(首个成功消息))
            {
                首个成功消息 = 单项结果消息;
            }

            成功数量++;
        }

        if (成功数量 <= 0)
        {
            结果消息 = "未完成命名，请检查资源或自定义文件名。";
            return false;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.SetDirty(this);

        结果消息 = 成功数量 == 1
            ? $"命名成功。{首个成功消息}"
            : $"命名成功：{成功数量} 个，失败：{失败数量} 个。";
        return true;
    }

    /// <summary>
    /// 尝试按单个配置项重命名资源文件。
    /// </summary>
    private static bool TryRenameSingleAssetFile(AssetRenameConfig 重命名配置, out string 结果消息)
    {
        if (重命名配置 == null || 重命名配置.TargetAsset == null)
        {
            结果消息 = "配置项未指定资源。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(重命名配置.CustomAssetName))
        {
            结果消息 = "配置项未填写自定义文件名。";
            return false;
        }

        string 资源路径 = AssetDatabase.GetAssetPath(重命名配置.TargetAsset);
        if (string.IsNullOrWhiteSpace(资源路径) || !资源路径.StartsWith("Assets/"))
        {
            结果消息 = "当前资源不是项目内可重命名文件。";
            return false;
        }

        string 清洗后名称 = GetSafeFileName(重命名配置.CustomAssetName);
        string 重命名错误 = AssetDatabase.RenameAsset(资源路径, 清洗后名称);
        if (!string.IsNullOrEmpty(重命名错误))
        {
            结果消息 = $"重命名失败：{重命名错误}";
            return false;
        }

        重命名配置.CustomAssetName = 清洗后名称;
        结果消息 = $"已重命名为：{清洗后名称}";
        return true;
    }

    /// <summary>
    /// 清洗文件名中的非法字符。
    /// </summary>
    private static string GetSafeFileName(string 原始名称)
    {
        if (string.IsNullOrWhiteSpace(原始名称))
            return "NewAsset";

        string 安全名称 = 原始名称.Trim();
        char[] 非法字符数组 = Path.GetInvalidFileNameChars();
        for (int 索引 = 0; 索引 < 非法字符数组.Length; 索引++)
        {
            安全名称 = 安全名称.Replace(非法字符数组[索引], '_');
        }

        return string.IsNullOrWhiteSpace(安全名称) ? "NewAsset" : 安全名称;
    }

    private void OnValidate()
    {
        if (assetRenameConfigs == null || assetRenameConfigs.Length == 0)
            return;

        for (int 索引 = 0; 索引 < assetRenameConfigs.Length; 索引++)
        {
            AssetRenameConfig 重命名配置 = assetRenameConfigs[索引];
            if (重命名配置 == null || 重命名配置.TargetAsset == null)
                continue;

            if (IsSupportedAsset(重命名配置.TargetAsset))
                continue;

            重命名配置.TargetAsset = null;
        }

        EditorUtility.SetDirty(this);
    }
#endif

    [Serializable]
    public class AssetRenameConfig
    {
        [SerializeField]
        private UnityEngine.Object targetAsset;

        [SerializeField]
        private string customAssetName;

        /// <summary>
        /// 待重命名资源（支持图片、材质、预制体）。
        /// </summary>
        public UnityEngine.Object TargetAsset
        {
            get => targetAsset;
            set => targetAsset = value;
        }

        /// <summary>
        /// 目标文件名（不含扩展名）。
        /// </summary>
        public string CustomAssetName
        {
            get => customAssetName;
            set => customAssetName = value;
        }
    }
}
