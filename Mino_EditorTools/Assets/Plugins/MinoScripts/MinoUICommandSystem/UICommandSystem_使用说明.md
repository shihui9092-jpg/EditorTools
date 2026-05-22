# UICommandSystem 使用说明

## 1. 文件结构

本次已将两个脚本统一整理到同一目录：

- `Assets/Scripts/UICommandSystem/UICommandCenter.cs`
- `Assets/Scripts/UICommandSystem/UIButtonCommandBinder.cs`

---

## 2. 系统作用

这套系统用于把 UI 按钮点击统一转换成“命令 ID（commandId）”，再由命令中心执行对应逻辑。

核心思路：

- 按钮层只负责“发命令”（`UIButtonCommandBinder`）
- 命令中心负责“查命令并执行”（`UICommandCenter`）

这样可以让按钮与具体业务逻辑解耦，减少每个按钮都单独写监听方法的重复工作。

---

## 3. 组件职责说明

## 3.1 `UIButtonCommandBinder`

挂在某个 `Button` 物体上，负责：

- 在启用时注册按钮点击事件
- 在禁用时移除按钮点击事件
- 点击时调用 `UICommandCenter.Execute(commandId)`

关键字段：

- `commandCenter`：目标命令中心引用
- `commandId`：该按钮触发的命令字符串

---

## 3.2 `UICommandCenter`

挂在某个全局物体（如 `UICanvas` / `UIRoot`）上，负责：

- 在 Inspector 维护 `commandId -> UnityEvent` 列表
- `Awake` 时构建字典映射，提升运行时查询效率
- 对外提供 `Execute(string commandId)` 执行入口

并附带常见动作方法（可直接在 UnityEvent 里绑定）：

- `SetObjectActive(GameObject target)`
- `SetObjectInactive(GameObject target)`
- `ToggleObjectActive(GameObject target)`
- `SetTimeScale(float value)`
- `LoadSceneByName(string sceneName)`
- `ReloadActiveScene()`
- `QuitGame()`

---

## 4. 从零配置流程（推荐步骤）

## 4.1 创建命令中心

1. 在场景中创建空物体（例如命名 `UICommandCenter`）
2. 挂载 `UICommandCenter` 脚本
3. 在 `命令配置(commandEntries)` 中新增命令条目

每个条目包含：

- `commandId`：命令唯一标识（如 `OpenSettings`）
- `onExecute`：该命令触发时要执行的 UnityEvent

---

## 4.2 给按钮绑定命令

1. 选中按钮物体（带 `Button` 组件）
2. 挂载 `UIButtonCommandBinder`
3. 在 `commandCenter` 拖入上一步的命令中心对象
4. 在 `commandId` 填入目标命令（必须和命令中心条目一致）

---

## 4.3 运行验证

1. 进入 Play Mode
2. 点击按钮
3. 验证对应行为是否触发（例如显示面板、切场景、暂停等）
4. 若无效，先看 Console 是否有以下警告：
   - 未绑定命令中心
   - commandId 为空
   - 未找到命令
   - 重复 commandId

---

## 5. 常见用法示例

## 5.1 打开设置面板

- `commandId`：`OpenSettings`
- `onExecute`：绑定 `SetObjectActive(settingsPanel)`

## 5.2 关闭设置面板

- `commandId`：`CloseSettings`
- `onExecute`：绑定 `SetObjectInactive(settingsPanel)`

## 5.3 切换暂停状态

- `commandId`：`TogglePause`
- `onExecute`：可绑定自定义暂停方法，或通过 `SetTimeScale(0/1)` 实现基础暂停

## 5.4 返回主菜单场景

- `commandId`：`BackToMain`
- `onExecute`：绑定 `LoadSceneByName("MainMenu")`

---

## 6. 命名与配置规范建议

为降低配置错误，建议团队统一以下规范：

- `commandId` 使用英文驼峰或大驼峰（例如 `OpenShop`、`ReloadLevel`）
- 同一场景内保持唯一，不允许重复
- 按钮和命令 ID 语义一致（方便检索）
- 命令中心集中放在 UI 根节点，避免多处分散

---

## 7. 排错清单（遇到按钮无响应时）

按顺序检查：

1. 按钮是否真的有 `Button` 组件且 `interactable=true`
2. `UIButtonCommandBinder.commandCenter` 是否已绑定
3. `UIButtonCommandBinder.commandId` 是否为空或拼写错误
4. `UICommandCenter.commandEntries` 是否存在同名 `commandId`
5. 目标 `UnityEvent` 是否绑定了对象和方法
6. 场景跳转命令对应场景是否已加入 Build Settings
7. 是否存在重复命令 ID（后续同名项会被忽略）

---

## 8. 当前行为说明（重要）

以下是当前脚本的既有行为，便于你后续判断是否需要改动：

- `UICommandCenter` 仅在 `Awake()` 自动构建一次命令字典
- 重复 `commandId` 时：只保留先出现的条目，后续同名项忽略并告警
- 空 `commandId` 或找不到命令时：仅输出警告，不抛异常

---

## 9. 适用场景与边界

适用：

- 中小规模 UI 按钮分发
- 快速搭建菜单、弹窗、场景切换入口
- 希望通过 Inspector 配置动作而不是大量手写按钮回调

边界：

- 若命令参数复杂、强类型要求高、流程链路很长，后续可能需要更强的命令资产化方案

---

## 10. 维护建议

- 每次新增按钮时，优先复用已有命令 ID，避免同义新命令泛滥
- 每个功能模块改动后，做一次按钮点击回归
- 定期清理 `commandEntries` 中不再使用的命令项

