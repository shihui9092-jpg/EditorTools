using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

[System.Serializable]
public class CameraPreset
{
    public Vector3 Pos;
    public Vector3 RotateAngles;
    public float height;
    public float offset;
    public float distance;
}

public class CameraController : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private Transform targetFocus;
    [SerializeField] private GameObject targetObj;
    [SerializeField] private Transform mainLight;
    [SerializeField] private Light rimLight;

    [Header("Interaction")]
    [SerializeField] private bool EnableDragObject = false;
    [SerializeField] private bool EnableRotateLight = false;

    [Header("Camera Orbit")]
    [SerializeField] private float height = 0.0f;
    [SerializeField] private float offset = 0.0f;
    [SerializeField] private float distance = 3.5f;
    [SerializeField, Range(0.1f, 4f)] private float ZoomWheelSpeed = 4.0f;

    [SerializeField] private float minDistance = 1f;
    [SerializeField] private float maxDistance = 4f;
    [SerializeField] private float xSpeed = 250.0f;
    [SerializeField] private float ySpeed = 120.0f;
    [SerializeField] private float yMinLimit = -10;
    [SerializeField] private float yMaxLimit = 60;
    [SerializeField] private float objRotateSpeed = 500.0f;

    [Header("Camera Presets")]
    [SerializeField] private List<CameraPreset> cameraPresets = new List<CameraPreset>();
    // 兼容旧场景数据：保留原 1~7 字段
    [SerializeField] private CameraPreset CameraPresets1;
    [SerializeField] private CameraPreset CameraPresets2;
    [SerializeField] private CameraPreset CameraPresets3;
    [SerializeField] private CameraPreset CameraPresets4;
    [SerializeField] private CameraPreset CameraPresets5;
    [SerializeField] private CameraPreset CameraPresets6;
    [SerializeField] private CameraPreset CameraPresets7;

    private float x = 0.0f;
    private float y = 0.0f;
    private float normalAngle = 0.0f;
    private float curDistance = 0f;
    private float curXSpeed = 0f;
    private float curYSpeed = 0f;
    private float reqXSpeed = 0f;
    private float reqYSpeed = 0f;
    private float curObjRotateSpeed = 0f;
    private float reqObjRotateSpeed = 0f;
    private bool draggingObject = false;
    private bool lastLMBState = false;
    private Collider[] surfaceColliders;
    private float boundsMaxSize = 20f;
    private bool isWet = false;
    private Quaternion charRotation;
    private Quaternion lightRotation;
    private Sequence activePresetSequence;

    [HideInInspector] public bool disableSteering = false;
    [HideInInspector] public bool isApplyingCameraPreset = false;

    private void Start()
    {
        Vector3 angles = transform.eulerAngles;
        x = angles.y;
        y = angles.x;

        if (targetObj != null && mainLight != null)
        {
            charRotation = targetObj.transform.rotation;
            lightRotation = mainLight.rotation;
        }

        if (rimLight == null)
        {
            GameObject rimLightObject = GameObject.Find("RimLight");
            if (rimLightObject != null)
            {
                rimLight = rimLightObject.GetComponent<Light>();
            }
        }

        Reset();
    }

    private void LateUpdate()
    {
        HandlePresetHotkeys();

        bool isOverUI = IsPointerOverUIElement();
        bool isMouseOverGameWindow = IsMouseOverGameWindow();

        if (isMouseOverGameWindow)
        {
            HandleRuntimeHotkeys();
        }

        if (ShouldBlockSteeringByCorner())
        {
            return;
        }

        if (CanHandleSteering(isMouseOverGameWindow, isOverUI))
        {
            HandleDragSteering();
        }

        UpdateDistanceBySurfaceCollision();
        ApplyCameraTransform();
    }

    public void DisableSteering(bool state)
    {
        disableSteering = state;
    }

    public void Reset()
    {
        lastLMBState = Input.GetMouseButton(0);
        disableSteering = false;

        curDistance = distance;
        curXSpeed = 0f;
        curYSpeed = 0f;
        reqXSpeed = 0f;
        reqYSpeed = 0f;
        curObjRotateSpeed = 0f;
        reqObjRotateSpeed = 0f;
        surfaceColliders = null;

        if (targetObj != null)
        {
            Renderer[] renderers = targetObj.GetComponentsInChildren<Renderer>();
            Bounds bounds = new Bounds();
            bool initedBounds = false;
            foreach (Renderer rend in renderers)
            {
                if (!initedBounds)
                {
                    initedBounds = true;
                    bounds = rend.bounds;
                }
                else
                {
                    bounds.Encapsulate(rend.bounds);
                }
            }

            Vector3 size = bounds.size;
            float dist = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            boundsMaxSize = dist;
            curDistance += boundsMaxSize * 1.2f;
            surfaceColliders = targetObj.GetComponentsInChildren<Collider>();
        }
    }

    public void SetNormalAngle(float angle)
    {
        normalAngle = angle;
    }

    public void set_normal_angle(float angle)
    {
        SetNormalAngle(angle);
    }

    private void HandlePresetHotkeys()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) ApplyCameraPreset(GetPresetByHotkeyIndex(0));
        if (Input.GetKeyDown(KeyCode.Alpha2)) ApplyCameraPreset(GetPresetByHotkeyIndex(1));
        if (Input.GetKeyDown(KeyCode.Alpha3)) ApplyCameraPreset(GetPresetByHotkeyIndex(2));
        if (Input.GetKeyDown(KeyCode.Alpha4)) ApplyCameraPreset(GetPresetByHotkeyIndex(3));
        if (Input.GetKeyDown(KeyCode.Alpha5)) ApplyCameraPreset(GetPresetByHotkeyIndex(4));
        if (Input.GetKeyDown(KeyCode.Alpha6)) ApplyCameraPreset(GetPresetByHotkeyIndex(5));
        if (Input.GetKeyDown(KeyCode.Alpha7)) ApplyCameraPreset(GetPresetByHotkeyIndex(6));
    }

    private void HandleRuntimeHotkeys()
    {
        if (Input.GetKey(KeyCode.UpArrow)) height += 0.005f;
        if (Input.GetKey(KeyCode.DownArrow)) height -= 0.005f;
        if (Input.GetKey(KeyCode.LeftArrow)) offset -= 0.005f;
        if (Input.GetKey(KeyCode.RightArrow)) offset += 0.005f;

        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetCharAndLighting();
        }

        if (Input.GetKeyDown(KeyCode.J))
        {
            EnableDragObject = !EnableDragObject;
        }

        if (Input.GetKeyDown(KeyCode.K))
        {
            EnableRotateLight = !EnableRotateLight;
        }

        if (Input.GetKeyDown(KeyCode.B) && rimLight != null)
        {
            rimLight.enabled = !rimLight.enabled;
        }

        if (Input.GetKeyDown(KeyCode.W))
        {
            isWet = !isWet;
            Shader.SetGlobalFloat("RainGlobal", isWet ? 1.0f : 0.0f);
        }
    }

    private bool IsMouseOverGameWindow()
    {
        Vector3 mousePos = Input.mousePosition;
        return !(mousePos.x < 0 || mousePos.y < 0 || mousePos.x > Screen.width || mousePos.y > Screen.height);
    }

    private bool ShouldBlockSteeringByCorner()
    {
        Vector3 mousePosition = Input.mousePosition;
        return mousePosition.x < Screen.width / 3f && mousePosition.y > (Screen.height - Screen.height / 3f);
    }

    private bool CanHandleSteering(bool isMouseOverGameWindow, bool isOverUI)
    {
        return targetObj != null && targetFocus != null && isMouseOverGameWindow && !isOverUI && !isApplyingCameraPreset;
    }

    private void HandleDragSteering()
    {
        UpdateDraggingState();

        if (draggingObject)
        {
            if (Input.GetMouseButton(0) && !disableSteering)
            {
                reqObjRotateSpeed += (Input.GetAxis("Mouse X") * objRotateSpeed * 0.02f - reqObjRotateSpeed) * Time.deltaTime * 10f;
            }
            else
            {
                reqObjRotateSpeed += (0f - reqObjRotateSpeed) * Time.deltaTime * 4f;
            }

            reqXSpeed += (0f - reqXSpeed) * Time.deltaTime * 4f;
            reqYSpeed += (0f - reqYSpeed) * Time.deltaTime * 4f;
        }
        else
        {
            if (Input.GetMouseButton(0) && !disableSteering)
            {
                reqXSpeed += (Input.GetAxis("Mouse X") * xSpeed * 0.02f - reqXSpeed) * Time.deltaTime * 10f;
                reqYSpeed += (Input.GetAxis("Mouse Y") * ySpeed * 0.02f - reqYSpeed) * Time.deltaTime * 10f;
            }
            else
            {
                reqXSpeed += (0f - reqXSpeed) * Time.deltaTime * 4f;
                reqYSpeed += (0f - reqYSpeed) * Time.deltaTime * 4f;
            }

            reqObjRotateSpeed += (0f - reqObjRotateSpeed) * Time.deltaTime * 4f;
            if (EnableDragObject)
            {
                reqObjRotateSpeed = 0f;
                curObjRotateSpeed = 0f;
            }
        }

        curObjRotateSpeed += (reqObjRotateSpeed - curObjRotateSpeed) * Time.deltaTime * 20f;
        if (EnableDragObject)
        {
            if (EnableRotateLight && mainLight != null)
            {
                mainLight.transform.Rotate(Vector3.up, -curObjRotateSpeed, Space.World);
            }
            else if (targetObj != null)
            {
                targetObj.transform.Rotate(Vector3.up, -curObjRotateSpeed, Space.World);
            }
        }

        curXSpeed += (reqXSpeed - curXSpeed) * Time.deltaTime * 20f;
        curYSpeed += (reqYSpeed - curYSpeed) * Time.deltaTime * 20f;
        x += curXSpeed;
        y -= curYSpeed;
        y = ClampAngle(y, yMinLimit + normalAngle, yMaxLimit + normalAngle);

        distance -= Input.GetAxis("Mouse ScrollWheel") * ZoomWheelSpeed;
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
    }

    private void UpdateDraggingState()
    {
        bool currentLMB = Input.GetMouseButton(0);
        if (!lastLMBState && currentLMB)
        {
            draggingObject = EnableDragObject;
        }
        else if (lastLMBState && !currentLMB)
        {
            draggingObject = false;
        }

        lastLMBState = currentLMB;
    }

    private void UpdateDistanceBySurfaceCollision()
    {
        if (targetFocus == null)
        {
            curDistance = distance;
            return;
        }

        if (surfaceColliders == null || surfaceColliders.Length == 0)
        {
            curDistance = distance;
            return;
        }

        Vector3 viewDir = Vector3.Normalize(targetFocus.position - transform.position);
        float requiredDistance = 0.01f;
        bool surfaceFound = false;

        foreach (Collider surfaceCollider in surfaceColliders)
        {
            if (surfaceCollider == null)
            {
                continue;
            }

            if (surfaceCollider.Raycast(new Ray(transform.position - viewDir * boundsMaxSize, viewDir), out RaycastHit hitInfo, Mathf.Infinity))
            {
                requiredDistance = Mathf.Max(Vector3.Distance(hitInfo.point, targetFocus.position) + distance, requiredDistance);
                surfaceFound = true;
            }
        }

        if (surfaceFound)
        {
            curDistance += (requiredDistance - curDistance) * Time.deltaTime * 4f;
        }
        else
        {
            curDistance = distance;
        }
    }

    private void ApplyCameraTransform()
    {
        if (targetFocus == null)
        {
            return;
        }

        Quaternion rotation = Quaternion.Euler(y, x, 0f);
        Vector3 position = rotation * new Vector3(offset, height, -curDistance) + targetFocus.position;
        transform.rotation = rotation;
        transform.position = position;
    }

    private void ApplyCameraPreset(CameraPreset preset)
    {
        if (preset == null)
        {
            return;
        }

        if (activePresetSequence != null && activePresetSequence.IsActive())
        {
            activePresetSequence.Kill();
        }

        Vector3 angles = preset.RotateAngles;
        isApplyingCameraPreset = true;

        activePresetSequence = DOTween.Sequence();
        activePresetSequence.Join(DOTween.To(() => transform.position, value => transform.position = value, preset.Pos, 0.5f));
        activePresetSequence.Join(DOTween.To(() => x, value => x = value, angles.y, 0.5f));
        activePresetSequence.Join(DOTween.To(() => y, value => y = value, angles.x, 0.5f));
        activePresetSequence.Join(DOTween.To(() => height, value => height = value, preset.height, 0.5f));
        activePresetSequence.Join(DOTween.To(() => offset, value => offset = value, preset.offset, 0.5f));
        activePresetSequence.Join(DOTween.To(() => distance, value => distance = value, preset.distance, 0.5f));
        activePresetSequence.OnKill(() => { isApplyingCameraPreset = false; });
        activePresetSequence.OnComplete(() => { isApplyingCameraPreset = false; });
    }

    private CameraPreset GetPresetByHotkeyIndex(int index)
    {
        if (cameraPresets != null && index >= 0 && index < cameraPresets.Count && cameraPresets[index] != null)
        {
            return cameraPresets[index];
        }

        switch (index)
        {
            case 0: return CameraPresets1;
            case 1: return CameraPresets2;
            case 2: return CameraPresets3;
            case 3: return CameraPresets4;
            case 4: return CameraPresets5;
            case 5: return CameraPresets6;
            case 6: return CameraPresets7;
            default: return null;
        }
    }

    private void ResetCharAndLighting()
    {
        if (targetObj != null)
        {
            targetObj.transform.rotation = charRotation;
        }

        if (mainLight != null)
        {
            mainLight.rotation = lightRotation;
        }
    }

    private static float ClampAngle(float angle, float min, float max)
    {
        if (angle < -360f) angle += 360f;
        if (angle > 360f) angle -= 360f;
        return Mathf.Clamp(angle, min, max);
    }

    public bool IsPointerOverUIElement()
    {
        return IsPointerOverUIElement(GetEventSystemRaycastResults());
    }

    private bool IsPointerOverUIElement(List<RaycastResult> eventSystemRaycastResults)
    {
        if (eventSystemRaycastResults == null)
        {
            return false;
        }

        for (int i = 0; i < eventSystemRaycastResults.Count; i++)
        {
            RaycastResult result = eventSystemRaycastResults[i];
            if (result.gameObject.layer == LayerMask.NameToLayer("UI"))
            {
                return true;
            }
        }
        return false;
    }

    private static List<RaycastResult> GetEventSystemRaycastResults()
    {
        if (EventSystem.current == null)
        {
            return null;
        }

        PointerEventData eventData = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };
        List<RaycastResult> raycastResults = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, raycastResults);
        return raycastResults;
    }
}