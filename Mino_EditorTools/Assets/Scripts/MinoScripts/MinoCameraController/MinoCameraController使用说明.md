# MinoCameraController 使用说明

## 1. 脚本定位

`MinoCameraController.cs` 是角色展示与预览场景用的轨道相机控制脚本，支持：

- 鼠标拖拽环绕与滚轮缩放；
- 拖拽物体旋转 / 灯光旋转切换；
- 1~7 相机预设平滑切换（DOTween）；
- 基于模型碰撞体的距离修正；
- 常用快捷功能（重置、RimLight、湿润效果）。

---

## 2. 挂载与依赖

- 挂载位置：展示相机对象。
- 必填引用：
  - `targetFocus`：相机环绕中心
  - `targetObj`：展示角色对象
- 可选引用：
  - `mainLight`：主灯光（灯光旋转模式）
  - `rimLight`：轮廓光（`B` 键切换）

依赖：

- 预设动画依赖 `DOTween`。
- UI 穿透判断依赖 `EventSystem`（无 EventSystem 时自动降级）。

---

## 3. Inspector 参数分组

### 3.1 Targets

- `targetFocus`
- `targetObj`
- `mainLight`
- `rimLight`

### 3.2 Interaction

- `EnableDragObject`：是否拖拽旋转对象
- `EnableRotateLight`：拖拽时改为旋转主灯光

### 3.3 Camera Orbit

- `height` / `offset` / `distance`
- `ZoomWheelSpeed`
- `minDistance` / `maxDistance`
- `xSpeed` / `ySpeed`
- `yMinLimit` / `yMaxLimit`
- `objRotateSpeed`

### 3.4 Camera Presets

- `cameraPresets`：新预设列表（优先）
- `CameraPresets1~7`：旧字段兼容（兜底）

---

## 4. 快捷键

- `1~7`：切换相机预设
- `↑ ↓ ← →`：微调 `height/offset`
- `R`：重置角色与灯光旋转
- `J`：切换 `EnableDragObject`
- `K`：切换 `EnableRotateLight`
- `B`：切换 `rimLight`
- `W`：切换全局湿润参数 `RainGlobal`

---

## 5. 预设机制说明

- 热键触发时优先读 `cameraPresets[index]`；
- 若列表缺失则回退到旧字段 `CameraPresets1~7`；
- 切换前会 `Kill` 旧 `DOTween Sequence`，避免叠加抖动；
- 通过 `OnKill/OnComplete` 统一恢复 `isApplyingCameraPreset`。

---

## 6. 输入保护与距离修正

- 鼠标必须在 Game 窗口内才处理相机输入。
- 当鼠标在 UI 层时，阻断相机拖拽控制。
- 基于 `targetObj` 子碰撞体进行距离修正，降低穿模风险。

---

## 7. 推荐使用流程

1. 绑定 `targetFocus` 与 `targetObj`；
2. 按项目需要绑定 `mainLight`/`rimLight`；
3. 先调 Orbit 参数，再配置预设；
4. 运行验证：
   - 预设切换是否平滑；
   - 拖拽旋转和缩放是否正常；
   - UI 区域是否正确阻断输入。

---

## 8. 常见问题

- **按 1~7 无反应**：检查预设是否配置、DOTween 是否可用。
- **拖拽无效**：检查 `targetFocus/targetObj`、`disableSteering`、UI 遮挡。
- **B 键无效果**：确认 `rimLight` 已绑定，或场景内存在名为 `RimLight` 的 Light。
- **预设切换异常**：检查是否有其他脚本同时写相机 Transform。
