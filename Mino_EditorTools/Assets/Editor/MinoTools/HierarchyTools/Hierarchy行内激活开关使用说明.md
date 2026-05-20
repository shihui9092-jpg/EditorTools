# Hierarchy 行内激活开关 使用说明

## 1. 工具定位

`HierarchyGameObjectToggle` 是一个 Unity 编辑器扩展脚本，在 **Hierarchy 窗口每一行右侧** 绘制激活状态开关，用于快速切换 `GameObject` 的 `activeSelf`（物体自身是否启用），无需打开 Inspector。

适用场景：

- 场景中物体较多，需要频繁开关显示或逻辑对象
- 对比「启用 / 禁用」效果时的快速迭代
- 与多选配合，批量切换一组物体的激活状态

---

## 2. 功能说明

| 功能 | 说明 |
|------|------|
| 行内开关 | Hierarchy 每行最右侧显示 Toggle，状态对应该物体的 `activeSelf` |
| 单击切换 | 在开关区域点击即可切换；不会误触发 Hierarchy 行选中 |
| 多选批量 | 若点击的物体在当前选中列表中，则对 **所有已选 GameObject** 统一设置激活状态 |
| 撤销支持 | 支持 `Ctrl+Z` / `Ctrl+Y` 撤销与重做 |
| Prefab 兼容 | 对 Prefab 实例会记录 Overrides，便于保存与版本管理 |
| 父级未激活提示 | 父物体未激活时，开关以 **灰色** 显示（仍可切换 `activeSelf`，便于父级恢复后生效） |
| 菜单总开关 | 可通过菜单启用或关闭本功能 |

---

## 3. 使用方法

### 3.1 启用条件

1. 脚本位于 `Assets/Editor/` 目录下（当前路径：`Assets/Editor/MinoTools/HierarchyTools/`）。
2. 回到 Unity，等待脚本编译完成。
3. 功能 **默认开启**，无需额外配置。

### 3.2 日常操作

1. 打开 **Hierarchy** 窗口。
2. 在任意 GameObject 行 **最右侧** 点击开关：
   - 勾选：该物体 `activeSelf = true`
   - 取消勾选：该物体 `activeSelf = false`
3. 需要批量操作时：
   - 在 Hierarchy 中 **多选** 多个物体
   - 点击其中任意一个的开关，即可对整批选中项统一切换
4. 误操作时使用 **Ctrl+Z** 撤销。

### 3.3 菜单开关

路径：**Tools → MinoTools → Hierarchy工具 → Hierarchy 行内激活开关**

- 菜单项带勾选表示功能已启用
- 再次点击可关闭行内开关绘制（不影响场景中物体已有状态）
- 设置保存在本机 `EditorPrefs`，重启编辑器后仍有效

---

## 4. 注意事项

### 4.1 `activeSelf` 与 `activeInHierarchy`

- 开关表示的是 **物体自身** 是否启用（`activeSelf`）。
- 若 **父物体被禁用**，子物体在场景中可能仍不可见，但子物体开关仍可能显示为「开启」。
- 此时开关会以灰色显示，表示「自身已启用，但受父级影响未在层级中生效」。

### 4.2 与 Inspector 的关系

Inspector 中物体名称旁的勾选框同样控制 `activeSelf`。本工具与其行为一致，只是操作入口在 Hierarchy。

### 4.3 Prefab 工作流

- 在场景中修改 Prefab **实例** 的激活状态会写入 **Overrides**。
- 保存场景或应用 Prefab 变更时，请按团队规范处理 Overrides。

### 4.4 性能说明

- 工具仅在 Hierarchy 重绘时绘制开关，并已过滤无关事件类型。
- 极大 Hierarchy（数千节点）下若感到卡顿，可通过菜单暂时关闭本功能。

---

## 5. 本次优化项摘要

相对初版脚本，当前版本主要改进：

1. **事件过滤**：仅处理 `Repaint` 与 `MouseDown`，减少无效回调开销。
2. **点击隔离**：开关区域点击后 `Use()` 事件，避免与 Hierarchy 行选中冲突。
3. **多选批量**：选中多个物体时，点击其中一个开关可批量切换。
4. **Undo 分组**：多物体操作登记到同一撤销组，一次 Ctrl+Z 可回退整批修改。
5. **Prefab Overrides**：实例修改时调用 `RecordPrefabInstancePropertyModifications`。
6. **场景脏标记**：变更后标记当前场景为脏，便于保存提示。
7. **父级未激活**：灰色显示开关，语义更清晰。
8. **生命周期**：域重载与退出编辑器时取消事件订阅，避免重复注册。
9. **菜单总开关**：可通过 `Tools/MinoTools` 菜单启用/关闭功能。
10. **布局常量**：开关宽度与边距提取为常量，便于后续调整。

---

## 6. 文件位置

| 文件 | 路径 |
|------|------|
| 脚本 | `Assets/Editor/MinoTools/HierarchyTools/HierarchyGameObjectToggle.cs` |
| 说明 | `Assets/Editor/MinoTools/HierarchyTools/Hierarchy行内激活开关使用说明.md` |

---

## 7. 常见问题

**Q：开关没有出现？**  
A：确认脚本已编译无报错；检查菜单 **Tools → MinoTools → Hierarchy工具 → Hierarchy 行内激活开关** 是否已勾选。

**Q：点了开关但场景里物体仍不显示？**  
A：检查父物体是否被禁用，或该物体是否仅有 `activeSelf` 为 true 但 `activeInHierarchy` 为 false。

**Q：多选时只想改一个物体？**  
A：先取消多选，仅选中目标物体后再点击开关；或从多选中排除该物体后再操作。

**Q：如何彻底移除功能？**  
A：删除或移出 `HierarchyGameObjectToggle.cs`（勿放在 `Assets/Editor` 下），或通过菜单关闭功能。
