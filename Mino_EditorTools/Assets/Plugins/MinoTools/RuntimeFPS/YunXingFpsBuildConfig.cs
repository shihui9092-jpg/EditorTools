using UnityEngine;

/// <summary>
/// 帧率显示打包配置（Resources 随包发布，Inspector 可调）。
/// </summary>
public class YunXingFpsBuildConfig : ScriptableObject
{
    public const string ResourceName = "YunXingFpsBuildConfig";

    public enum PanelAnchor
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    [Header("开关")]
    [Tooltip("总开关：关闭后 Play Mode 与打包 Player 均不显示")]
    public bool enableDisplay = true;

    [Tooltip("勾选后，非 Development 的 Release 包也会显示帧率")]
    public bool showInReleaseBuild;

    [Header("显示项")]
    public bool showFps = true;
    public bool showMs = true;
    public bool showMinMaxFps = true;
    public bool show1PercentLowFps = true;
    public bool showTargetFrameRate = true;

    [Header("布局")]
    public PanelAnchor anchor = PanelAnchor.TopRight;

    [Tooltip("相对安全区边缘的边距（像素）")]
    public float margin = 10f;

    [Range(10, 24)]
    public int fontSize = 14;

    [Tooltip("面板整体缩放（在分辨率自适应系数之上叠加；1.5 约为在默认基础上再放大约 0.5 倍）")]
    [Range(0.75f, 2.5f)]
    public float panelScale = 1.5f;

    [Tooltip("按当前屏幕高度相对参考高度缩放字号、边距与布局宽度，使多分辨率下视觉比例一致")]
    public bool enableResolutionScale = true;

    [Tooltip("参考屏幕高度（像素）；实际缩放系数 = 当前屏高 / 本值")]
    [Min(480f)]
    public float referenceScreenHeight = 1080f;

    [Tooltip("使用 Screen.safeArea，适配刘海屏/全面屏")]
    public bool useSafeArea = true;

    [Header("颜色阈值")]
    public float greenFpsThreshold = 55f;
    public float yellowFpsThreshold = 30f;

    [Header("统计")]
    [Tooltip("Min/Max/1% Low 统计窗口（秒）")]
    [Range(1f, 10f)]
    public float sampleWindowSeconds = 3f;

    [Header("热键")]
    [Tooltip("运行时按热键显示/隐藏面板（不影响总开关配置）")]
    public bool enableHotkeyToggle = true;

    public KeyCode toggleHotkey = KeyCode.F1;
}
