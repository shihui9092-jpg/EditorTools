using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 编辑器：菜单开关、Play Mode 生命周期、打包配置资源（Resources）维护。
/// </summary>
[InitializeOnLoad]
public static class YunXingFpsBootstrap
{
    private const string MenuPathEnabled = "Tools/MinoTools/性能工具/运行模式帧率显示";
    private const string MenuPathReleaseBuild = "Tools/MinoTools/性能工具/发布包也显示帧率";

    private const string SettingsAssetPath =
        "Assets/MinoTools/Runtime/Resources/YunXingFpsBuildConfig.asset";

    static YunXingFpsBootstrap()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        EditorApplication.delayCall += EnsureSettingsAssetExists;
    }

    [MenuItem(MenuPathEnabled)]
    private static void ToggleFeatureEnabled()
    {
        YunXingFpsBuildConfig settings = GetOrCreateSettings();
        settings.enableDisplay = !settings.enableDisplay;
        SaveSettings(settings);
        YunXingFpsPanel.ReloadSettings();

        if (!EditorApplication.isPlaying)
            return;

        if (settings.enableDisplay)
            YunXingFpsPanel.EnsureCreated();
        else
            YunXingFpsPanel.DestroyInstance();
    }

    [MenuItem(MenuPathEnabled, true)]
    private static bool ToggleFeatureEnabledValidate()
    {
        Menu.SetChecked(MenuPathEnabled, YunXingFpsPanel.IsFeatureEnabled());
        return true;
    }

    [MenuItem(MenuPathReleaseBuild)]
    private static void ToggleShowInReleaseBuild()
    {
        YunXingFpsBuildConfig settings = GetOrCreateSettings();
        settings.showInReleaseBuild = !settings.showInReleaseBuild;
        SaveSettings(settings);
        YunXingFpsPanel.ReloadSettings();
    }

    [MenuItem(MenuPathReleaseBuild, true)]
    private static bool ToggleShowInReleaseBuildValidate()
    {
        Menu.SetChecked(MenuPathReleaseBuild, YunXingFpsPanel.IsShowInReleaseBuild());
        return true;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            YunXingFpsPanel.ReloadSettings();
            if (YunXingFpsPanel.IsFeatureEnabled())
                YunXingFpsPanel.EnsureCreated();
            return;
        }

        if (state == PlayModeStateChange.ExitingPlayMode)
            YunXingFpsPanel.DestroyInstance();
    }

    private static void EnsureSettingsAssetExists()
    {
        if (AssetDatabase.LoadAssetAtPath<YunXingFpsBuildConfig>(SettingsAssetPath) != null)
            return;

        string directoryPath = Path.GetDirectoryName(SettingsAssetPath);
        if (!string.IsNullOrEmpty(directoryPath) && !AssetDatabase.IsValidFolder(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
            AssetDatabase.Refresh();
        }

        YunXingFpsBuildConfig settings = ScriptableObject.CreateInstance<YunXingFpsBuildConfig>();
        settings.enableDisplay = true;
        settings.showInReleaseBuild = false;

        AssetDatabase.CreateAsset(settings, SettingsAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static YunXingFpsBuildConfig GetOrCreateSettings()
    {
        EnsureSettingsAssetExists();
        return AssetDatabase.LoadAssetAtPath<YunXingFpsBuildConfig>(SettingsAssetPath);
    }

    private static void SaveSettings(YunXingFpsBuildConfig settings)
    {
        if (settings == null)
            return;

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
    }
}
