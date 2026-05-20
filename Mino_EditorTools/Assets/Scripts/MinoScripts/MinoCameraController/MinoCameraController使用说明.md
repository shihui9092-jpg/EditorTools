# MinoCameraController 使用说明

## 1. 脚本定位

`MinoCameraController.cs` 是角色展示与预览场景用的轨道相机控制脚本，支持：

- 鼠标拖拽环绕与滚轮缩放；
- 拖拽物体旋转 / 灯光旋转切换；
- **可增删、可命名、可自定义快捷键**的机位预设（DOTween 平滑切换）；
- 运行模式下锁定镜头并保存到指定机位；
- 基于模型碰撞体的距离修正；
- 常用快捷功能（重置角色与灯光旋转等）。

---

## 2. 挂载与依赖

- 挂载位置：展示相机对象。
- 必填引用：
  - `orbitFocus`：相机环绕中心
  - `displayTarget`：展示角色对象
- 可选引用：
  - `mainLightTransform`：主灯光（灯光旋转模式）

依赖：

- 预设动画依赖 `DOTween`。
- UI 穿透判断依赖 `EventSystem`（无 EventSystem 时自动降级）。

---

## 3. Inspector 参数分组

### 3.1 目标引用

| 字段 | 说明 |
|------|------|
| `orbitFocus` | 环绕焦点 Transform |
| `displayTarget` | 展示模型根对象 |
| `mainLightTransform` | 主灯 Transform |

### 3.2 交互设置

| 字段 | 说明 |
|------|------|
| `enableDragRotateTarget` | 左键拖拽改为旋转目标（Inspector 勾选） |
| `enableDragRotateMainLight` | 拖拽时改转主灯而非模型（Inspector 勾选） |

### 3.3 轨道相机

| 字段 | 说明 |
|------|------|
| `orbitHeight` / `orbitOffset` / `orbitDistance` | 高度、侧向偏移、轨道距离 |
| `scrollZoomSpeed` | 滚轮缩放灵敏度 |
| `minOrbitDistance` / `maxOrbitDistance` | 距离上下限 |
| `orbitYawSpeed` / `orbitPitchSpeed` | 水平 / 俯仰拖拽速度 |
| `pitchMinLimit` / `pitchMaxLimit` | 俯仰角限制 |
| `targetYawRotateSpeed` | 拖拽旋转目标时的角速度 |

### 3.4 机位列表（`cameraPresetSlots`）

每个机位为 `MinoCameraPresetSlot`：

| 字段 | 说明 |
|------|------|
| `presetName` | 机位显示名称（如「正面全身」） |
| `activationKey` | 切换快捷键；`None` 表示不绑定键 |
| `view` | 镜头参数（`MinoCameraPreset`） |

- **新组件默认仅 1 个机位**：「默认机位」，快捷键 `Alpha1`。
- Inspector 列表底栏 **+** 添加机位、**-** 删除（至少保留 1 个）。
- Inspector 分三块折叠：**相机与轨道参数**、**机位列表**、**运行模式 · 机位录制**（折叠状态会记住）。
- 机位列表内每项布局：名称 / 快捷键 / 镜头参数（与原先一致）。
- 重复快捷键会在编辑器中输出 Warning。

`view` 内字段：

| 字段 | 说明 |
|------|------|
| `worldPosition` | 相机世界坐标 |
| `eulerAngles` | 俯仰 x / 水平 y |
| `orbitHeight` / `orbitOffset` / `orbitDistance` | 轨道三参数 |

---

## 4. 快捷键

### 全局

| 按键 | 作用 |
|------|------|
| `L` | 锁定 / 解锁相机（锁定后冻结轨道更新） |
| `↑ ↓ ← →` | 微调 `orbitHeight` / `orbitOffset` |
| `R` | 重置角色与灯光旋转 |

### 机位（按各槽位 `activationKey` 配置）

| 操作 | 按键 |
|------|------|
| 切换到某机位 | 该机位的 `activationKey`（**未按 Shift**） |
| 保存当前镜头到某机位 | **Shift +** 该机位的 `activationKey` |

示例：机位 A 设为 `Alpha1` → 按 `1` 切换，按 `Shift+1` 保存。

---

## 5. 预设机制说明

- 切换时 DOTween 0.5s 插值到 `view` 内参数，并 Kill 旧序列；
- `isPresetTransitioning` 为 true 时屏蔽手动操控；
- `activationKey == None` 的机位只能通过代码 / Inspector 应用，无键盘切换。

### 旧数据迁移

打开场景或 Prefab 时 `OnValidate` 自动：

1. 旧 `cameraPresetList` → 转为命名槽位（`机位1`…，键 `1`~`9` / `F1`…）；
2. 旧 `CameraPresets1~7` 字段 → 同上；
3. 无机位数据 → 创建 1 个「默认机位」。

保存资源后即完成升级。

---

## 6. 代码接口（对外）

| 方法 / 字段 | 说明 |
|-------------|------|
| `PresetSlotCount` | 当前机位数量 |
| `GetPresetSlot(int)` | 获取槽位 |
| `AddPresetSlot(string name, KeyCode key)` | 添加机位（`key` 为 `None` 时自动分配未占用键） |
| `RemovePresetSlot(int)` | 删除机位（至少保留 1 个） |
| `CaptureCurrentViewToPreset(int)` | 保存当前镜头到指定槽位 |
| `CaptureCurrentViewToSelectedPreset()` | 保存到 `capturePresetIndex` |
| `SetCameraLocked(bool)` | 锁定 / 解锁 |
| `SetInputEnabled(bool)` / `DisableSteering(bool)` | 禁止 / 允许操控 |
| `ResetCameraState()` / `Reset()` | 重置轨道与碰撞缓存 |

---

## 7. 运行模式录制机位

1. Play 模式下调到满意镜头；
2. **锁定相机**（`L` 或 Inspector 按钮）；
3. 在「保存目标机位」下拉框选择机位，或点 **快速保存** 按钮；
4. 也可 **Shift + 该机位快捷键** 保存；
5. **快速保存**按钮：第一次点击某机位 → 锁定相机；再次点击同一按钮 → 保存到该机位（按钮会显示 `·保存` 提示）；
6. 退出 Play 前 **保存场景 / Prefab**。

---

## 8. 参数导出 / 导入（编辑与运行模式）

Inspector 顶部两个按钮：

| 按钮 | 作用 |
|------|------|
| **导出参数配置** | 将当前挂载对象参数写入 `CameraParameterProfiles/` 下的 `.minocamera.json` |
| **导入参数配置** | 打开上述目录，选择配置文件并覆盖到当前对象 |

导出内容：

- `MinoCameraController` 全部可序列化字段（含机位列表、轨道参数、录制选项等）；
- 同 GameObject 上 **Camera** 组件的全部可序列化字段；
- 相机 **Transform** 本地 `position` / `rotation`（欧拉角）/ `scale`；
- 当前 **运行时轨道状态**：`orbitYaw`、`orbitPitch`、`smoothedOrbitDistance`（拖拽/滚轮后的即时镜头，不仅限于机位列表）；
- GameObject 的 **Tag**、**Layer**；
- `orbitFocus` / `displayTarget` / `mainLightTransform` 使用 **GlobalObjectId** 单独保存。

说明：

- 同场景导入时引用通常可自动恢复；**跨场景**若对象不存在会提示并在 Inspector 中手动重绑。
- 运行模式下导入同样生效，退出 Play 前请保存场景或 Prefab。
- **旧版** `.minocamera.json`（无 Transform / 运行时轨道字段）仍可导入，行为与升级前一致；仅有 Transform 无轨道角时，会用 Transform 欧拉角初始化 yaw/pitch。

导出路径（固定）：

`Assets/Scripts/MinoScripts/MinoCameraController/CameraParameterProfiles/`

- 子文件夹名 **CameraParameterProfiles** 表示「相机参数配置档案」。
- 文件名格式：`场景名_对象名_yyyyMMdd_HHmmss.minocamera.json`，避免覆盖历史导出。
- 首次导出时若文件夹不存在会自动创建。

---

## 9. 推荐使用流程

1. 绑定 `orbitFocus`、`displayTarget`；
2. 在机位列表中添加所需机位，设置名称与快捷键；
3. Play 中逐个调整并 Shift+快捷键 录制，或手动填写 `view`；
4. 验证切换、保存、UI 遮挡是否正常。

---

## 10. 常见问题

- **按键无反应**：检查 `activationKey` 是否为 `None`、是否与其他机位重复、DOTween 是否可用。
- **保存后切机位不对**：确认已保存场景；`view` 与当前轨道参数一致。
- **无法删除机位**：至少需保留 1 个机位。
- **拖拽无效**：检查焦点与目标、`disableInput`、UI 遮挡。
