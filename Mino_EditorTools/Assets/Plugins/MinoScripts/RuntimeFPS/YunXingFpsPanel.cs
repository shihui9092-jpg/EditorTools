using System;
using System.Text;
using UnityEngine;

/// <summary>
/// 运行模式帧率显示：屏幕空间 IMGUI 叠加 FPS / MS / MinMax / 1% Low 等。
/// </summary>
[DisallowMultipleComponent]
public sealed class YunXingFpsPanel : MonoBehaviour
{
    public const string HostObjectName = "__Mino_RuntimeFpsDisplay__";

    private const float SmoothFactor = 0.1f;
    private const float BaseTextWidth = 220f;
    private const float TextRefreshInterval = 0.05f;

    // FPS 文字分段色：绿 #00CB26、黄 #C2CB00、红 #FF0000
    private static readonly Color FpsColorGreen = new Color(0f, 203f / 255f, 38f / 255f);
    private static readonly Color FpsColorYellow = new Color(194f / 255f, 203f / 255f, 0f);
    private static readonly Color FpsColorRed = new Color(1f, 0f, 0f);

    private static YunXingFpsPanel _instance;
    private static YunXingFpsBuildConfig _cachedSettings;
    private static bool _runtimePanelVisible = true;

    private float _smoothedDeltaTime = 1f / 60f;
    private GUIStyle _labelStyle;
    private string _displayText = string.Empty;
    private float _nextTextRefreshTime;
    private FrameTimeSampler _frameTimeSampler;

    public static YunXingFpsBuildConfig Settings
    {
        get
        {
            if (_cachedSettings != null)
                return _cachedSettings;

            _cachedSettings = Resources.Load<YunXingFpsBuildConfig>(
                YunXingFpsBuildConfig.ResourceName);

            if (_cachedSettings == null)
                _cachedSettings = CreateFallbackSettings();

            return _cachedSettings;
        }
    }

    public static bool IsFeatureEnabled()
    {
        return Settings.enableDisplay;
    }

    public static bool IsShowInReleaseBuild()
    {
        return Settings.showInReleaseBuild;
    }

    public static void SetFeatureEnabled(bool enabled)
    {
        Settings.enableDisplay = enabled;
    }

    public static void SetShowInReleaseBuild(bool enabled)
    {
        Settings.showInReleaseBuild = enabled;
    }

    public static bool ShouldCreateInPlayer()
    {
        if (!IsFeatureEnabled())
            return false;

        if (Debug.isDebugBuild)
            return true;

        return IsShowInReleaseBuild();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreateInPlayer()
    {
#if UNITY_EDITOR
        return;
#else
        if (ShouldCreateInPlayer())
            EnsureCreated();
#endif
    }

    public static void EnsureCreated()
    {
        if (!IsFeatureEnabled())
            return;

        if (_instance != null)
            return;

        _runtimePanelVisible = true;

        GameObject hostObject = new GameObject(HostObjectName);
        _instance = hostObject.AddComponent<YunXingFpsPanel>();

#if UNITY_EDITOR
        hostObject.hideFlags = HideFlags.HideAndDontSave;
#else
        hostObject.hideFlags = HideFlags.DontSave;
        DontDestroyOnLoad(hostObject);
#endif
    }

    public static void DestroyInstance()
    {
        if (_instance == null)
            return;

        GameObject hostObject = _instance.gameObject;
        _instance = null;

        if (hostObject == null)
            return;

#if UNITY_EDITOR
        UnityEngine.Object.DestroyImmediate(hostObject);
#else
        UnityEngine.Object.Destroy(hostObject);
#endif
    }

    public static void ReloadSettings()
    {
        _cachedSettings = null;
        if (_instance != null)
            _instance.RebuildSampler();
    }

    public static void ToggleRuntimePanelVisible()
    {
        _runtimePanelVisible = !_runtimePanelVisible;
    }

    private static YunXingFpsBuildConfig CreateFallbackSettings()
    {
        return ScriptableObject.CreateInstance<YunXingFpsBuildConfig>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        RebuildSampler();
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    private void RebuildSampler()
    {
        int capacity = Mathf.Max(64, Mathf.CeilToInt(Settings.sampleWindowSeconds * 144f));
        _frameTimeSampler = new FrameTimeSampler(capacity);
    }

    private void Update()
    {
        if (!IsFeatureEnabled())
            return;

        HandleHotkeyToggle();

        float deltaTime = Time.unscaledDeltaTime;
        if (deltaTime <= 0f)
            return;

        _smoothedDeltaTime += (deltaTime - _smoothedDeltaTime) * SmoothFactor;
        _frameTimeSampler?.AddSample(deltaTime);

        if (Time.unscaledTime >= _nextTextRefreshTime)
        {
            _displayText = BuildDisplayText();
            _nextTextRefreshTime = Time.unscaledTime + TextRefreshInterval;
        }
    }

    private void HandleHotkeyToggle()
    {
        if (!Settings.enableHotkeyToggle)
            return;

        if (!Input.GetKeyDown(Settings.toggleHotkey))
            return;

        ToggleRuntimePanelVisible();
    }

    private string BuildDisplayText()
    {
        YunXingFpsBuildConfig config = Settings;
        StringBuilder builder = new StringBuilder(128);

        float fps = 1f / Mathf.Max(_smoothedDeltaTime, 0.00001f);
        float milliseconds = _smoothedDeltaTime * 1000f;

        if (config.showFps)
            builder.AppendLine($"FPS  {fps:0.0}");

        if (config.showMs)
            builder.AppendLine($"MS   {milliseconds:0.0}");

        if (_frameTimeSampler != null &&
            _frameTimeSampler.TryGetStats(out float minFps, out float maxFps, out float onePercentLowFps))
        {
            if (config.showMinMaxFps)
                builder.AppendLine($"Min {minFps:0}  Max {maxFps:0}");

            if (config.show1PercentLowFps)
                builder.AppendLine($"1%L {onePercentLowFps:0.0}");
        }

        if (config.showTargetFrameRate)
        {
            int targetFps = Application.targetFrameRate;
            string targetText = targetFps <= 0 ? "∞" : targetFps.ToString();
            builder.AppendLine($"目标 {targetText}  VSync {QualitySettings.vSyncCount}");
        }

        builder.Append("[运行]");
        return builder.ToString().TrimEnd();
    }

    private void OnGUI()
    {
        if (!IsFeatureEnabled() || !_runtimePanelVisible)
            return;

        if (Event.current.type != EventType.Repaint)
            return;

        EnsureStyles();

        float layoutScale = GetLayoutScale();
        _labelStyle.fontSize = Mathf.RoundToInt(Settings.fontSize * layoutScale);

        float fps = 1f / Mathf.Max(_smoothedDeltaTime, 0.00001f);
        _labelStyle.normal.textColor = GetFpsColor(fps, Settings);
        _labelStyle.alignment = GetTextAlignment(Settings.anchor);

        MeasureTextContent(_displayText, layoutScale, out float textWidth, out float textHeight);
        Rect textRect = CalculateTextRect(textWidth, textHeight, layoutScale);
        GUI.Label(textRect, _displayText, _labelStyle);
    }

    /// <summary>
    /// 按实际文本与字号测量显示区域，避免裁切底部行（如「目标」「[运行]」）。
    /// </summary>
    private void MeasureTextContent(string text, float scale, out float textWidth, out float textHeight)
    {
        EnsureStyles();

        float layoutWidth = BaseTextWidth * scale;
        GUIContent content = new GUIContent(string.IsNullOrEmpty(text) ? " " : text);
        textHeight = _labelStyle.CalcHeight(content, layoutWidth);
        Vector2 textSize = _labelStyle.CalcSize(content);

        textWidth = Mathf.Max(layoutWidth, textSize.x + 4f);
        textHeight = Mathf.Max(textHeight, _labelStyle.CalcHeight(content, textWidth));
    }

    /// <summary>
    /// 综合手动 panelScale 与可选的参考屏高自适应，得到字号/边距/布局宽度所用系数。
    /// </summary>
    private static float GetLayoutScale()
    {
        YunXingFpsBuildConfig config = Settings;
        float scale = config.panelScale;

        if (!config.enableResolutionScale)
            return scale;

        float referenceHeight = Mathf.Max(config.referenceScreenHeight, 1f);
        float screenHeight = Screen.height > 0f ? Screen.height : referenceHeight;
        scale *= screenHeight / referenceHeight;
        return scale;
    }

    private Rect CalculateTextRect(float textWidth, float textHeight, float layoutScale)
    {
        YunXingFpsBuildConfig config = Settings;
        float margin = config.margin * layoutScale;

        Rect safe = config.useSafeArea
            ? Screen.safeArea
            : new Rect(0f, 0f, Screen.width, Screen.height);

        switch (config.anchor)
        {
            case YunXingFpsBuildConfig.PanelAnchor.TopRight:
                return new Rect(safe.xMax - textWidth - margin, safe.y + margin, textWidth, textHeight);
            case YunXingFpsBuildConfig.PanelAnchor.BottomLeft:
                return new Rect(safe.x + margin, safe.yMax - textHeight - margin, textWidth, textHeight);
            case YunXingFpsBuildConfig.PanelAnchor.BottomRight:
                return new Rect(safe.xMax - textWidth - margin, safe.yMax - textHeight - margin, textWidth, textHeight);
            default:
                return new Rect(safe.x + margin, safe.y + margin, textWidth, textHeight);
        }
    }

    private void EnsureStyles()
    {
        if (_labelStyle != null)
            return;

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft,
            wordWrap = false,
            clipping = TextClipping.Overflow,
            padding = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 0, 0, 0)
        };
    }

    private static TextAnchor GetTextAlignment(YunXingFpsBuildConfig.PanelAnchor anchor)
    {
        switch (anchor)
        {
            case YunXingFpsBuildConfig.PanelAnchor.TopRight:
                return TextAnchor.UpperRight;
            case YunXingFpsBuildConfig.PanelAnchor.BottomLeft:
                return TextAnchor.LowerLeft;
            case YunXingFpsBuildConfig.PanelAnchor.BottomRight:
                return TextAnchor.LowerRight;
            default:
                return TextAnchor.UpperLeft;
        }
    }

    private static Color GetFpsColor(float fps, YunXingFpsBuildConfig config)
    {
        if (fps >= config.greenFpsThreshold)
            return FpsColorGreen;

        if (fps >= config.yellowFpsThreshold)
            return FpsColorYellow;

        return FpsColorRed;
    }

    /// <summary>
    /// 环形缓冲，用于窗口期内 Min/Max/1% Low FPS。
    /// </summary>
    private sealed class FrameTimeSampler
    {
        private readonly float[] _samples;
        private int _writeIndex;
        private int _count;

        public FrameTimeSampler(int capacity)
        {
            _samples = new float[Mathf.Max(16, capacity)];
        }

        public void AddSample(float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            _samples[_writeIndex] = deltaTime;
            _writeIndex = (_writeIndex + 1) % _samples.Length;
            if (_count < _samples.Length)
                _count++;
        }

        public bool TryGetStats(out float minFps, out float maxFps, out float onePercentLowFps)
        {
            minFps = 0f;
            maxFps = 0f;
            onePercentLowFps = 0f;

            if (_count == 0)
                return false;

            float[] copy = new float[_count];
            int start = _count < _samples.Length ? 0 : _writeIndex;

            for (int i = 0; i < _count; i++)
            {
                int index = (start + i) % _samples.Length;
                copy[i] = _samples[index];
            }

            float maxDelta = copy[0];
            float minDelta = copy[0];
            for (int i = 1; i < copy.Length; i++)
            {
                if (copy[i] > maxDelta) maxDelta = copy[i];
                if (copy[i] < minDelta) minDelta = copy[i];
            }

            minFps = 1f / Mathf.Max(maxDelta, 0.00001f);
            maxFps = 1f / Mathf.Max(minDelta, 0.00001f);

            Array.Sort(copy);
            Array.Reverse(copy);

            int worstCount = Mathf.Max(1, Mathf.CeilToInt(copy.Length * 0.01f));
            float sum = 0f;
            for (int i = 0; i < worstCount; i++)
                sum += copy[i];

            float avgWorstDelta = sum / worstCount;
            onePercentLowFps = 1f / Mathf.Max(avgWorstDelta, 0.00001f);
            return true;
        }
    }
}
