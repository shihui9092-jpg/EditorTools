using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

/// <summary>
/// 单个相机机位预设数据。
/// </summary>
[System.Serializable]
public class MinoCameraPreset
{
    [Tooltip("相机世界坐标")]
    [FormerlySerializedAs("Pos")]
    public Vector3 worldPosition;

    [Tooltip("轨道旋转角（x=俯仰，y=水平）")]
    [FormerlySerializedAs("RotateAngles")]
    public Vector3 eulerAngles;

    [Tooltip("相对焦点的垂直偏移")]
    [FormerlySerializedAs("height")]
    public float orbitHeight;

    [Tooltip("相对焦点的水平偏移")]
    [FormerlySerializedAs("offset")]
    public float orbitOffset;

    [Tooltip("轨道距离（滚轮缩放目标值）")]
    [FormerlySerializedAs("distance")]
    public float orbitDistance;
}

/// <summary>
/// 可命名、可绑定快捷键的机位槽位。
/// </summary>
[System.Serializable]
public class MinoCameraPresetSlot
{
    [Tooltip("机位显示名称")]
    public string presetName = "默认机位";

    [Tooltip("按下该键切换到本机位；None 表示不绑定快捷键")]
    public KeyCode activationKey = KeyCode.Alpha1;

    [Tooltip("镜头参数")]
    public MinoCameraPreset view = new MinoCameraPreset();
}

/// <summary>
/// 角色展示场景用的轨道相机控制器。
/// </summary>
public class MinoCameraController : MonoBehaviour
{
    #region 目标引用

    [Header("目标引用")]
    [Tooltip("相机环绕的中心点")]
    [SerializeField]
    [FormerlySerializedAs("targetFocus")]
    private Transform orbitFocus;

    [Tooltip("展示用的角色或模型根对象")]
    [SerializeField]
    [FormerlySerializedAs("targetObj")]
    private GameObject displayTarget;

    [Tooltip("主灯光 Transform（灯光旋转模式时使用）")]
    [SerializeField]
    [FormerlySerializedAs("mainLight")]
    private Transform mainLightTransform;

    #endregion

    #region 交互设置

    [Header("交互设置")]
    [Tooltip("开启后，左键拖拽改为绕 Y 轴旋转展示对象或主灯")]
    [SerializeField]
    [FormerlySerializedAs("EnableDragObject")]
    private bool enableDragRotateTarget;

    [Tooltip("与上一项配合：为 true 时拖拽旋转主灯，否则旋转展示对象")]
    [SerializeField]
    [FormerlySerializedAs("EnableRotateLight")]
    private bool enableDragRotateMainLight;

    #endregion

    #region 轨道相机

    [Header("轨道相机")]
    [Tooltip("相机相对焦点的高度偏移")]
    [SerializeField]
    [FormerlySerializedAs("height")]
    private float orbitHeight;

    [Tooltip("相机相对焦点的左右偏移")]
    [SerializeField]
    [FormerlySerializedAs("offset")]
    private float orbitOffset;

    [Tooltip("相机与焦点的轨道距离")]
    [SerializeField]
    [FormerlySerializedAs("distance")]
    private float orbitDistance = 3.5f;

    [Tooltip("滚轮缩放灵敏度")]
    [SerializeField, Range(0.1f, 4f)]
    [FormerlySerializedAs("ZoomWheelSpeed")]
    private float scrollZoomSpeed = 4f;

    [Tooltip("轨道距离下限")]
    [SerializeField]
    [FormerlySerializedAs("minDistance")]
    private float minOrbitDistance = 1f;

    [Tooltip("轨道距离上限")]
    [SerializeField]
    [FormerlySerializedAs("maxDistance")]
    private float maxOrbitDistance = 4f;

    [Tooltip("水平环绕速度")]
    [SerializeField]
    [FormerlySerializedAs("xSpeed")]
    private float orbitYawSpeed = 250f;

    [Tooltip("俯仰旋转速度")]
    [SerializeField]
    [FormerlySerializedAs("ySpeed")]
    private float orbitPitchSpeed = 120f;

    [Tooltip("俯仰角下限")]
    [SerializeField]
    [FormerlySerializedAs("yMinLimit")]
    private float pitchMinLimit = -10f;

    [Tooltip("俯仰角上限")]
    [SerializeField]
    [FormerlySerializedAs("yMaxLimit")]
    private float pitchMaxLimit = 60f;

    [Tooltip("拖拽旋转展示对象/主灯时的角速度")]
    [SerializeField]
    [FormerlySerializedAs("objRotateSpeed")]
    private float targetYawRotateSpeed = 500f;

    #endregion

    #region 机位预设

    [Header("机位预设")]
    [Tooltip("机位槽位列表，可自定义名称与快捷键")]
    [SerializeField]
    private List<MinoCameraPresetSlot> cameraPresetSlots = new List<MinoCameraPresetSlot>();

#if UNITY_EDITOR
    [FormerlySerializedAs("cameraPresetList")]
    [SerializeField, HideInInspector]
    private List<MinoCameraPreset> legacyCameraPresetList;

    [FormerlySerializedAs("CameraPresets1")] [SerializeField, HideInInspector] private MinoCameraPreset legacyPreset1;
    [FormerlySerializedAs("CameraPresets2")] [SerializeField, HideInInspector] private MinoCameraPreset legacyPreset2;
    [FormerlySerializedAs("CameraPresets3")] [SerializeField, HideInInspector] private MinoCameraPreset legacyPreset3;
    [FormerlySerializedAs("CameraPresets4")] [SerializeField, HideInInspector] private MinoCameraPreset legacyPreset4;
    [FormerlySerializedAs("CameraPresets5")] [SerializeField, HideInInspector] private MinoCameraPreset legacyPreset5;
    [FormerlySerializedAs("CameraPresets6")] [SerializeField, HideInInspector] private MinoCameraPreset legacyPreset6;
    [FormerlySerializedAs("CameraPresets7")] [SerializeField, HideInInspector] private MinoCameraPreset legacyPreset7;
#endif

    #endregion

    #region 运行模式机位录制

    [Header("运行模式机位录制")]
    [Tooltip("为 true 时冻结当前镜头，不再响应拖拽/轨道更新，便于保存机位")]
    [SerializeField]
    private bool isCameraLocked;

    [Tooltip("运行模式下保存机位时使用的槽位下标")]
    [SerializeField]
    private int capturePresetIndex;

    #endregion

    #region 运行时状态

    // 当前轨道欧拉角（y=水平，x=俯仰）
    private float orbitYaw;
    private float orbitPitch;

    // 外部可叠加的俯仰基准偏移
    private float pitchAngleOffset;

    private float smoothedOrbitDistance;
    private float currentYawSpeed;
    private float currentPitchSpeed;
    private float requestedYawSpeed;
    private float requestedPitchSpeed;
    private float currentTargetYawSpeed;
    private float requestedTargetYawSpeed;
    private bool isDraggingTarget;
    private bool wasLeftMousePressed;
    private Collider[] surfaceColliders;
    private float targetBoundsMaxSize = 20f;
    private Quaternion initialTargetRotation;
    private Quaternion initialMainLightRotation;
    private Sequence activePresetTween;

    // UI 射线检测缓存，避免每帧分配 List 与重复查询 Layer
    private static int cachedUiLayer = -1;
    private readonly List<RaycastResult> uiRaycastResultsCache = new List<RaycastResult>();

    /// <summary>为 true 时禁止鼠标操控相机与对象。</summary>
    [HideInInspector]
    [FormerlySerializedAs("disableSteering")]
    public bool disableInput;

    /// <summary>为 true 时表示正在播放机位过渡动画。</summary>
    [HideInInspector]
    [FormerlySerializedAs("isApplyingCameraPreset")]
    public bool isPresetTransitioning;

    #endregion

    private void Start()
    {
        Vector3 angles = transform.eulerAngles;
        orbitYaw = angles.y;
        orbitPitch = angles.x;

        if (displayTarget != null && mainLightTransform != null)
        {
            initialTargetRotation = displayTarget.transform.rotation;
            initialMainLightRotation = mainLightTransform.rotation;
        }

        EnsureDefaultPresetSlots();
        ClampCapturePresetIndex();
        ResetCameraState();
    }

    private void OnDestroy()
    {
        activePresetTween?.Kill();
        activePresetTween = null;
        isPresetTransitioning = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        TryMigrateAllLegacyPresets();
        EnsureDefaultPresetSlots();
        ClampCapturePresetIndex();
        ValidateDuplicateActivationKeys();
    }
#endif

    private void LateUpdate()
    {
        bool isPointerOverUi = IsPointerOverUIElement();
        bool isMouseOverGameView = IsMouseOverGameView();

        if (isMouseOverGameView)
        {
            HandleRuntimeHotkeys();
        }

        if (isCameraLocked)
        {
            return;
        }

        HandlePresetHotkeys();

        if (ShouldBlockInputInTopLeftCorner())
        {
            return;
        }

        if (CanHandleInput(isMouseOverGameView, isPointerOverUi))
        {
            HandleDragInput();
        }

        UpdateSmoothedDistanceByCollision();
        ApplyOrbitTransform();
    }

    /// <summary>运行模式下是否已锁定相机（冻结当前镜头）。</summary>
    public bool IsCameraLocked => isCameraLocked;

    /// <summary>当前机位槽位数量。</summary>
    public int PresetSlotCount => cameraPresetSlots != null ? cameraPresetSlots.Count : 0;

    /// <summary>获取/设置运行模式保存机位时使用的槽位下标。</summary>
    public int CapturePresetIndex
    {
        get => capturePresetIndex;
        set
        {
            capturePresetIndex = value;
            ClampCapturePresetIndex();
        }
    }

    /// <summary>获取指定槽位（越界返回 null）。</summary>
    public MinoCameraPresetSlot GetPresetSlot(int index)
    {
        if (cameraPresetSlots == null || index < 0 || index >= cameraPresetSlots.Count)
        {
            return null;
        }

        return cameraPresetSlots[index];
    }

    /// <summary>添加新机位槽位。</summary>
    public MinoCameraPresetSlot AddPresetSlot(string name, KeyCode activationKey = KeyCode.None)
    {
        EnsurePresetSlotsInitialized();
        if (activationKey == KeyCode.None)
        {
            activationKey = FindUnusedActivationKey();
        }

        MinoCameraPresetSlot slot = new MinoCameraPresetSlot
        {
            presetName = string.IsNullOrWhiteSpace(name) ? $"机位{cameraPresetSlots.Count + 1}" : name,
            activationKey = activationKey,
            view = new MinoCameraPreset()
        };
        cameraPresetSlots.Add(slot);
        ClampCapturePresetIndex();

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        return slot;
    }

    /// <summary>删除机位槽位（至少保留 1 个）。</summary>
    public bool RemovePresetSlot(int index)
    {
        if (cameraPresetSlots == null || cameraPresetSlots.Count <= 1)
        {
            return false;
        }

        if (index < 0 || index >= cameraPresetSlots.Count)
        {
            return false;
        }

        cameraPresetSlots.RemoveAt(index);
        ClampCapturePresetIndex();

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        return true;
    }

    /// <summary>锁定或解锁相机。锁定后停止轨道更新与输入操控。</summary>
    public void SetCameraLocked(bool locked)
    {
        isCameraLocked = locked;

        if (locked)
        {
            if (activePresetTween != null && activePresetTween.IsActive())
            {
                activePresetTween.Kill();
            }

            isPresetTransitioning = false;
            disableInput = true;
            ClearMotionSpeeds();
        }
        else
        {
            disableInput = false;
        }
    }

    /// <summary>将当前镜头参数写入指定机位槽位。</summary>
    public bool CaptureCurrentViewToPreset(int presetIndex)
    {
        MinoCameraPresetSlot slot = GetPresetSlot(presetIndex);
        if (slot == null)
        {
            return false;
        }

        if (slot.view == null)
        {
            slot.view = new MinoCameraPreset();
        }

        ApplyCurrentViewToPreset(slot.view);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        return true;
    }

    /// <summary>将当前镜头参数写入 <see cref="capturePresetIndex"/> 指定的机位。</summary>
    public bool CaptureCurrentViewToSelectedPreset()
    {
        return CaptureCurrentViewToPreset(capturePresetIndex);
    }

    /// <summary>生成当前镜头参数快照（不写入列表）。</summary>
    public MinoCameraPreset CreatePresetFromCurrentView()
    {
        MinoCameraPreset preset = new MinoCameraPreset();
        ApplyCurrentViewToPreset(preset);
        return preset;
    }

    private void ApplyCurrentViewToPreset(MinoCameraPreset preset)
    {
        preset.worldPosition = transform.position;
        preset.eulerAngles = new Vector3(orbitPitch, orbitYaw, 0f);
        preset.orbitHeight = orbitHeight;
        preset.orbitOffset = orbitOffset;
        preset.orbitDistance = orbitDistance;
    }

    private void EnsurePresetSlotsInitialized()
    {
        if (cameraPresetSlots == null)
        {
            cameraPresetSlots = new List<MinoCameraPresetSlot>();
        }
    }

    private void EnsureDefaultPresetSlots()
    {
        EnsurePresetSlotsInitialized();
        if (cameraPresetSlots.Count > 0)
        {
            return;
        }

        cameraPresetSlots.Add(CreateDefaultPresetSlot());
    }

    private static MinoCameraPresetSlot CreateDefaultPresetSlot()
    {
        return new MinoCameraPresetSlot
        {
            presetName = "默认机位",
            activationKey = KeyCode.Alpha1,
            view = new MinoCameraPreset()
        };
    }

    private void ClampCapturePresetIndex()
    {
        if (PresetSlotCount == 0)
        {
            capturePresetIndex = 0;
            return;
        }

        capturePresetIndex = Mathf.Clamp(capturePresetIndex, 0, PresetSlotCount - 1);
    }

    private static KeyCode GetSuggestedKeyForSlotIndex(int index)
    {
        if (index >= 0 && index <= 8)
        {
            return KeyCode.Alpha1 + index;
        }

        if (index >= 9 && index <= 18)
        {
            return KeyCode.F1 + (index - 9);
        }

        return KeyCode.None;
    }

    private KeyCode FindUnusedActivationKey()
    {
        for (int i = 0; i < 32; i++)
        {
            KeyCode key = GetSuggestedKeyForSlotIndex(i);
            if (key == KeyCode.None)
            {
                break;
            }

            if (!IsActivationKeyUsed(key))
            {
                return key;
            }
        }

        return KeyCode.None;
    }

    private bool IsActivationKeyUsed(KeyCode key)
    {
        if (cameraPresetSlots == null || key == KeyCode.None)
        {
            return false;
        }

        for (int i = 0; i < cameraPresetSlots.Count; i++)
        {
            if (cameraPresetSlots[i] != null && cameraPresetSlots[i].activationKey == key)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsShiftHeld()
    {
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    }

    private void ClearMotionSpeeds()
    {
        currentYawSpeed = 0f;
        currentPitchSpeed = 0f;
        requestedYawSpeed = 0f;
        requestedPitchSpeed = 0f;
        currentTargetYawSpeed = 0f;
        requestedTargetYawSpeed = 0f;
    }

    /// <summary>设置是否允许鼠标操控。</summary>
    public void SetInputEnabled(bool enabled)
    {
        disableInput = !enabled;
    }

    /// <summary>兼容旧接口：禁用或启用操控。</summary>
    public void DisableSteering(bool disabled)
    {
        disableInput = disabled;
    }

    /// <summary>重置轨道距离、碰撞缓存与速度状态。</summary>
    public void ResetCameraState()
    {
        wasLeftMousePressed = Input.GetMouseButton(0);
        disableInput = false;

        smoothedOrbitDistance = orbitDistance;
        currentYawSpeed = 0f;
        currentPitchSpeed = 0f;
        requestedYawSpeed = 0f;
        requestedPitchSpeed = 0f;
        currentTargetYawSpeed = 0f;
        requestedTargetYawSpeed = 0f;
        surfaceColliders = null;

        if (displayTarget == null)
        {
            return;
        }

        Renderer[] renderers = displayTarget.GetComponentsInChildren<Renderer>();
        Bounds bounds = new Bounds();
        bool hasBounds = false;
        foreach (Renderer renderer in renderers)
        {
            if (!hasBounds)
            {
                hasBounds = true;
                bounds = renderer.bounds;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        Vector3 size = bounds.size;
        float maxAxis = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        targetBoundsMaxSize = maxAxis;
        smoothedOrbitDistance += targetBoundsMaxSize * 1.2f;
        surfaceColliders = displayTarget.GetComponentsInChildren<Collider>();
    }

    /// <summary>兼容旧接口。</summary>
    public void Reset()
    {
        ResetCameraState();
    }

    /// <summary>设置俯仰限制的基准偏移角。</summary>
    public void SetPitchAngleOffset(float angle)
    {
        pitchAngleOffset = angle;
    }

    /// <summary>兼容旧接口。</summary>
    public void SetNormalAngle(float angle)
    {
        SetPitchAngleOffset(angle);
    }

    /// <summary>兼容旧接口。</summary>
    public void set_normal_angle(float angle)
    {
        SetPitchAngleOffset(angle);
    }

    private void HandlePresetHotkeys()
    {
        if (cameraPresetSlots == null || IsShiftHeld())
        {
            return;
        }

        for (int i = 0; i < cameraPresetSlots.Count; i++)
        {
            MinoCameraPresetSlot slot = cameraPresetSlots[i];
            if (slot == null || slot.activationKey == KeyCode.None)
            {
                continue;
            }

            if (Input.GetKeyDown(slot.activationKey))
            {
                if (slot.view != null)
                {
                    ApplyCameraPreset(slot.view);
                }

                break;
            }
        }
    }

    private void HandleRuntimeHotkeys()
    {
        if (Input.GetKey(KeyCode.UpArrow))
        {
            orbitHeight += 0.005f;
        }

        if (Input.GetKey(KeyCode.DownArrow))
        {
            orbitHeight -= 0.005f;
        }

        if (Input.GetKey(KeyCode.LeftArrow))
        {
            orbitOffset -= 0.005f;
        }

        if (Input.GetKey(KeyCode.RightArrow))
        {
            orbitOffset += 0.005f;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetTargetAndLightRotation();
        }

        if (Input.GetKeyDown(KeyCode.L))
        {
            SetCameraLocked(!isCameraLocked);
        }

        if (!IsShiftHeld() || cameraPresetSlots == null)
        {
            return;
        }

        for (int i = 0; i < cameraPresetSlots.Count; i++)
        {
            MinoCameraPresetSlot slot = cameraPresetSlots[i];
            if (slot == null || slot.activationKey == KeyCode.None)
            {
                continue;
            }

            if (Input.GetKeyDown(slot.activationKey))
            {
                CaptureCurrentViewToPreset(i);
                capturePresetIndex = i;
                break;
            }
        }
    }

    private static bool IsMouseOverGameView()
    {
        Vector3 mousePos = Input.mousePosition;
        return mousePos.x >= 0 && mousePos.y >= 0 && mousePos.x <= Screen.width && mousePos.y <= Screen.height;
    }

    /// <summary>屏蔽左上角区域输入，避免与叠加 UI 冲突。</summary>
    private static bool ShouldBlockInputInTopLeftCorner()
    {
        Vector3 mousePosition = Input.mousePosition;
        return mousePosition.x < Screen.width / 3f && mousePosition.y > Screen.height * 2f / 3f;
    }

    private bool CanHandleInput(bool isMouseOverGameView, bool isPointerOverUi)
    {
        return displayTarget != null
            && orbitFocus != null
            && isMouseOverGameView
            && !isPointerOverUi
            && !isPresetTransitioning;
    }

    private void HandleDragInput()
    {
        UpdateDragModeOnMouseDown();

        if (isDraggingTarget)
        {
            if (Input.GetMouseButton(0) && !disableInput)
            {
                requestedTargetYawSpeed += (Input.GetAxis("Mouse X") * targetYawRotateSpeed * 0.02f - requestedTargetYawSpeed) * Time.deltaTime * 10f;
            }
            else
            {
                requestedTargetYawSpeed += (0f - requestedTargetYawSpeed) * Time.deltaTime * 4f;
            }

            requestedYawSpeed += (0f - requestedYawSpeed) * Time.deltaTime * 4f;
            requestedPitchSpeed += (0f - requestedPitchSpeed) * Time.deltaTime * 4f;
        }
        else
        {
            if (Input.GetMouseButton(0) && !disableInput)
            {
                requestedYawSpeed += (Input.GetAxis("Mouse X") * orbitYawSpeed * 0.02f - requestedYawSpeed) * Time.deltaTime * 10f;
                requestedPitchSpeed += (Input.GetAxis("Mouse Y") * orbitPitchSpeed * 0.02f - requestedPitchSpeed) * Time.deltaTime * 10f;
            }
            else
            {
                requestedYawSpeed += (0f - requestedYawSpeed) * Time.deltaTime * 4f;
                requestedPitchSpeed += (0f - requestedPitchSpeed) * Time.deltaTime * 4f;
            }

            requestedTargetYawSpeed += (0f - requestedTargetYawSpeed) * Time.deltaTime * 4f;
            if (enableDragRotateTarget)
            {
                requestedTargetYawSpeed = 0f;
                currentTargetYawSpeed = 0f;
            }
        }

        currentTargetYawSpeed += (requestedTargetYawSpeed - currentTargetYawSpeed) * Time.deltaTime * 20f;
        if (enableDragRotateTarget)
        {
            if (enableDragRotateMainLight && mainLightTransform != null)
            {
                mainLightTransform.Rotate(Vector3.up, -currentTargetYawSpeed, Space.World);
            }
            else if (displayTarget != null)
            {
                displayTarget.transform.Rotate(Vector3.up, -currentTargetYawSpeed, Space.World);
            }
        }

        currentYawSpeed += (requestedYawSpeed - currentYawSpeed) * Time.deltaTime * 20f;
        currentPitchSpeed += (requestedPitchSpeed - currentPitchSpeed) * Time.deltaTime * 20f;
        orbitYaw += currentYawSpeed;
        orbitPitch -= currentPitchSpeed;
        orbitPitch = ClampAngle(orbitPitch, pitchMinLimit + pitchAngleOffset, pitchMaxLimit + pitchAngleOffset);

        orbitDistance -= Input.GetAxis("Mouse ScrollWheel") * scrollZoomSpeed;
        orbitDistance = Mathf.Clamp(orbitDistance, minOrbitDistance, maxOrbitDistance);
    }

    private void UpdateDragModeOnMouseDown()
    {
        bool isLeftMousePressed = Input.GetMouseButton(0);
        if (!wasLeftMousePressed && isLeftMousePressed)
        {
            isDraggingTarget = enableDragRotateTarget;
        }
        else if (wasLeftMousePressed && !isLeftMousePressed)
        {
            isDraggingTarget = false;
        }

        wasLeftMousePressed = isLeftMousePressed;
    }

    private void UpdateSmoothedDistanceByCollision()
    {
        if (orbitFocus == null)
        {
            smoothedOrbitDistance = orbitDistance;
            return;
        }

        if (surfaceColliders == null || surfaceColliders.Length == 0)
        {
            smoothedOrbitDistance = orbitDistance;
            return;
        }

        Vector3 toFocus = orbitFocus.position - transform.position;
        if (toFocus.sqrMagnitude < 1e-8f)
        {
            // 相机与焦点重合时无法构造有效射线，跳过碰撞修正避免 Ray 方向非归一化断言
            smoothedOrbitDistance = orbitDistance;
            return;
        }

        Vector3 viewDirection = toFocus.normalized;
        float requiredDistance = 0.01f;
        bool hitSurface = false;

        foreach (Collider collider in surfaceColliders)
        {
            if (collider == null)
            {
                continue;
            }

            Ray ray = new Ray(transform.position - viewDirection * targetBoundsMaxSize, viewDirection);
            if (collider.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
            {
                requiredDistance = Mathf.Max(Vector3.Distance(hit.point, orbitFocus.position) + orbitDistance, requiredDistance);
                hitSurface = true;
            }
        }

        if (hitSurface)
        {
            smoothedOrbitDistance += (requiredDistance - smoothedOrbitDistance) * Time.deltaTime * 4f;
        }
        else
        {
            smoothedOrbitDistance = orbitDistance;
        }
    }

    private void ApplyOrbitTransform()
    {
        if (orbitFocus == null)
        {
            return;
        }

        Quaternion rotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
        Vector3 localOffset = new Vector3(orbitOffset, orbitHeight, -smoothedOrbitDistance);
        Vector3 worldPosition = rotation * localOffset + orbitFocus.position;
        transform.rotation = rotation;
        transform.position = worldPosition;
    }

    private void ApplyCameraPreset(MinoCameraPreset preset)
    {
        if (preset == null)
        {
            return;
        }

        if (activePresetTween != null && activePresetTween.IsActive())
        {
            activePresetTween.Kill();
        }

        Vector3 angles = preset.eulerAngles;
        isPresetTransitioning = true;

        activePresetTween = DOTween.Sequence();
        activePresetTween.Join(DOTween.To(() => transform.position, value => transform.position = value, preset.worldPosition, 0.5f));
        activePresetTween.Join(DOTween.To(() => orbitYaw, value => orbitYaw = value, angles.y, 0.5f));
        activePresetTween.Join(DOTween.To(() => orbitPitch, value => orbitPitch = value, angles.x, 0.5f));
        activePresetTween.Join(DOTween.To(() => orbitHeight, value => orbitHeight = value, preset.orbitHeight, 0.5f));
        activePresetTween.Join(DOTween.To(() => orbitOffset, value => orbitOffset = value, preset.orbitOffset, 0.5f));
        activePresetTween.Join(DOTween.To(() => orbitDistance, value => orbitDistance = value, preset.orbitDistance, 0.5f));
        activePresetTween.OnKill(() => { isPresetTransitioning = false; });
        activePresetTween.OnComplete(() => { isPresetTransitioning = false; });
    }

#if UNITY_EDITOR
    private void TryMigrateAllLegacyPresets()
    {
        if (cameraPresetSlots != null && cameraPresetSlots.Count > 0)
        {
            return;
        }

        bool migrated = false;

        if (legacyCameraPresetList != null && legacyCameraPresetList.Count > 0)
        {
            EnsurePresetSlotsInitialized();
            for (int i = 0; i < legacyCameraPresetList.Count; i++)
            {
                MinoCameraPreset legacyView = legacyCameraPresetList[i];
                if (legacyView == null)
                {
                    continue;
                }

                cameraPresetSlots.Add(CreateSlotFromLegacyView(legacyView, i));
                migrated = true;
            }

            legacyCameraPresetList = null;
        }

        MinoCameraPreset[] legacyPresets =
        {
            legacyPreset1, legacyPreset2, legacyPreset3, legacyPreset4,
            legacyPreset5, legacyPreset6, legacyPreset7
        };

        bool hasLegacyFields = false;
        for (int i = 0; i < legacyPresets.Length; i++)
        {
            if (legacyPresets[i] != null)
            {
                hasLegacyFields = true;
                break;
            }
        }

        if (hasLegacyFields)
        {
            EnsurePresetSlotsInitialized();
            for (int i = 0; i < legacyPresets.Length; i++)
            {
                if (legacyPresets[i] == null)
                {
                    continue;
                }

                while (cameraPresetSlots.Count <= i)
                {
                    cameraPresetSlots.Add(null);
                }

                if (cameraPresetSlots[i] == null)
                {
                    cameraPresetSlots[i] = CreateSlotFromLegacyView(legacyPresets[i], i);
                    migrated = true;
                }
            }

            legacyPreset1 = null;
            legacyPreset2 = null;
            legacyPreset3 = null;
            legacyPreset4 = null;
            legacyPreset5 = null;
            legacyPreset6 = null;
            legacyPreset7 = null;
        }

        if (migrated)
        {
            ClampCapturePresetIndex();
            UnityEditor.EditorUtility.SetDirty(this);
        }
    }

    private static MinoCameraPresetSlot CreateSlotFromLegacyView(MinoCameraPreset view, int index)
    {
        return new MinoCameraPresetSlot
        {
            presetName = $"机位{index + 1}",
            activationKey = GetSuggestedKeyForSlotIndex(index),
            view = view
        };
    }

    private void ValidateDuplicateActivationKeys()
    {
        if (cameraPresetSlots == null)
        {
            return;
        }

        for (int i = 0; i < cameraPresetSlots.Count; i++)
        {
            MinoCameraPresetSlot slotA = cameraPresetSlots[i];
            if (slotA == null || slotA.activationKey == KeyCode.None)
            {
                continue;
            }

            for (int j = i + 1; j < cameraPresetSlots.Count; j++)
            {
                MinoCameraPresetSlot slotB = cameraPresetSlots[j];
                if (slotB == null || slotB.activationKey == KeyCode.None)
                {
                    continue;
                }

                if (slotA.activationKey == slotB.activationKey)
                {
                    Debug.LogWarning(
                        $"[MinoCameraController] 机位「{slotA.presetName}」与「{slotB.presetName}」使用了相同快捷键 {slotA.activationKey}。",
                        this);
                }
            }
        }
    }
#endif

    private void ResetTargetAndLightRotation()
    {
        if (displayTarget != null)
        {
            displayTarget.transform.rotation = initialTargetRotation;
        }

        if (mainLightTransform != null)
        {
            mainLightTransform.rotation = initialMainLightRotation;
        }
    }

    private static float ClampAngle(float angle, float min, float max)
    {
        if (angle < -360f)
        {
            angle += 360f;
        }

        if (angle > 360f)
        {
            angle -= 360f;
        }

        return Mathf.Clamp(angle, min, max);
    }

    public bool IsPointerOverUIElement()
    {
        return IsPointerOverUIElement(GetEventSystemRaycastResults());
    }

    private static int GetUiLayer()
    {
        if (cachedUiLayer < 0)
        {
            cachedUiLayer = LayerMask.NameToLayer("UI");
        }

        return cachedUiLayer;
    }

    private static bool IsPointerOverUIElement(List<RaycastResult> raycastResults)
    {
        if (raycastResults == null || raycastResults.Count == 0)
        {
            return false;
        }

        int uiLayer = GetUiLayer();
        for (int i = 0; i < raycastResults.Count; i++)
        {
            if (raycastResults[i].gameObject.layer == uiLayer)
            {
                return true;
            }
        }

        return false;
    }

    private List<RaycastResult> GetEventSystemRaycastResults()
    {
        if (EventSystem.current == null)
        {
            return null;
        }

        PointerEventData eventData = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };
        uiRaycastResultsCache.Clear();
        EventSystem.current.RaycastAll(eventData, uiRaycastResultsCache);
        return uiRaycastResultsCache;
    }
}
