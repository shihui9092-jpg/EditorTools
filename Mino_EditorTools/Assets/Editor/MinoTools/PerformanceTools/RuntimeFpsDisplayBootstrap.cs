using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 编辑器：菜单开关、Play Mode 生命周期、打包配置资源（Resources）维护。
/// </summary>
[InitializeOnLoad]
public static class RuntimeFpsDisplayBootstrap
{
    private const string MenuPathEnabled = "Tools/MinoTools/性能工具/运行模式帧率显示";
    private const string MenuPathReleaseBuild = "Tools/MinoTools/性能工具/发布包也显示帧率";

    private const string SettingsAssetPath =
        "Assets/MinoTools/Runtime/Resources/MinoToolsRuntimeFpsBuildSettings.asset";

    static RuntimeFpsDisplayBootstrap()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        EditorApplication.delayCall += EnsureSettingsAssetExists;
    }

    [MenuItem(MenuPathEnabled)]
    private static void ToggleFeatureEnabled()
    {
        MinoToolsRuntimeFpsBuildSettings settings = GetOrCreateSettings();
        settings.enableDisplay = !settings.enableDisplay;
        SaveSettings(settings);
        RuntimeFpsDisplay.ReloadSettings();

        if (!EditorApplication.isPlaying)
            return;

        if (settings.enableDisplay)
            RuntimeFpsDisplay.EnsureCreated();
        else
            RuntimeFpsDisplay.DestroyInstance();
    }

    [MenuItem(MenuPathEnabled, true)]
    private static bool ToggleFeatureEnabledValidate()
    {
        Menu.SetChecked(MenuPathEnabled, RuntimeFpsDisplay.IsFeatureEnabled());
        return true;
    }

    [MenuItem(MenuPathReleaseBuild)]
    private static void ToggleShowInReleaseBuild()
    {
        MinoToolsRuntimeFpsBuildSettings settings = GetOrCreateSettings();
        settings.showInReleaseBuild = !settings.showInReleaseBuild;
        SaveSettings(settings);
        RuntimeFpsDisplay.ReloadSettings();
    }

    [MenuItem(MenuPathReleaseBuild, true)]
    private static bool ToggleShowInReleaseBuildValidate()
    {
        Menu.SetChecked(MenuPathReleaseBuild, RuntimeFpsDisplay.IsShowInReleaseBuild());
        return true;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            RuntimeFpsDisplay.ReloadSettings();
            if (RuntimeFpsDisplay.IsFeatureEnabled())
                RuntimeFpsDisplay.EnsureCreated();
            return;
        }

        if (state == PlayModeStateChange.ExitingPlayMode)
            RuntimeFpsDisplay.DestroyInstance();
    }

    private static void EnsureSettingsAssetExists()
    {
        if (AssetDatabase.LoadAssetAtPath<MinoToolsRuntimeFpsBuildSettings>(SettingsAssetPath) != null)
            return;

        string directoryPath = Path.GetDirectoryName(SettingsAssetPath);
        if (!string.IsNullOrEmpty(directoryPath) && !AssetDatabase.IsValidFolder(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
            AssetDatabase.Refresh();
        }

        MinoToolsRuntimeFpsBuildSettings settings = ScriptableObject.CreateInstance<MinoToolsRuntimeFpsBuildSettings>();
        settings.enableDisplay = true;
        settings.showInReleaseBuild = false;

        AssetDatabase.CreateAsset(settings, SettingsAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static MinoToolsRuntimeFpsBuildSettings GetOrCreateSettings()
    {
        EnsureSettingsAssetExists();
        return AssetDatabase.LoadAssetAtPath<MinoToolsRuntimeFpsBuildSettings>(SettingsAssetPath);
    }

    private static void SaveSettings(MinoToolsRuntimeFpsBuildSettings settings)
    {
        if (settings == null)
            return;

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
    }
}
