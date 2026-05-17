# Submesh 拆分工具使用说明

## 1. 工具作用

该工具用于将一个 `SkinnedMeshRenderer` 的多材质网格（多个 Submesh）拆分为多个独立网格，并在原对象下创建对应子节点。

拆分后每个子节点：

- 挂载一个新的 `SkinnedMeshRenderer`；
- 只保留对应 Submesh 的几何数据；
- 只保留对应的单个材质；
- 生成并保存一个独立 `.asset` Mesh 文件。

---

## 2. 菜单入口

- `Tools/MinoTools/模型处理/Submesh拆分`

---

## 3. 适用场景

- 一个角色/模型使用多个材质球，希望按材质拆分独立网格；
- 需要把原网格的 Submesh 作为独立资产输出，便于后续工具链处理；
- 需要在不手工进 DCC 的情况下做快速拆分验证。

---

## 4. 使用前提

目标对象必须满足：

- 已选中 `GameObject`；
- 该对象上存在 `SkinnedMeshRenderer`；
- `sharedMesh` 不为空；
- `sharedMesh.subMeshCount >= 2`（至少有 2 个 Submesh 才有拆分意义）。

---

## 5. 操作步骤

1. 在 Hierarchy 中选中一个或多个目标对象。
2. 点击菜单：`Tools/MinoTools/模型处理/Submesh拆分`。
3. 工具会逐个处理选中对象，并在 Console 输出结果。
4. 处理完成后可在对象层级下看到新建子节点：  
   - 命名规则：`原对象名_sub_索引`

---

## 6. 输出内容说明

### 6.1 层级输出

- 在原对象下创建多个子对象（每个 Submesh 一个）。
- 子对象使用本地坐标原点（`SetParent(..., false)` 保持局部对齐）。

### 6.2 资源输出

- 每个拆分网格都会保存为 `.asset` 文件；
- 保存目录：原始 Mesh 资产所在目录；
- 文件名：`原对象名_sub_索引.asset`；
- 若重名，自动生成唯一文件名（已做防冲突处理）。

### 6.3 渲染输出

- 子对象 `SkinnedMeshRenderer` 会复制原渲染器序列化属性；
- 仅保留当前 Submesh 对应材质（单材质数组）。

---

## 7. 脚本主要处理逻辑（简述）

- 入口方法：`SplitSelectedSubmeshes()`
  - 遍历 `Selection.gameObjects`；
  - 空选择会直接给出警告并返回。

- 单对象处理：`ProcessGameObject(GameObject go)`
  - 校验 `SkinnedMeshRenderer` 与 `sharedMesh`；
  - 通过 `GetAllSubMeshAsIsolatedMeshes` 拿到每个 Submesh 的独立 Mesh；
  - 为每个 Submesh 新建子对象、创建资源、绑定材质与网格。

- Submesh 提取：`MeshExtension.GetSubmesh`
  - 重建顶点索引映射；
  - 拷贝 UV / Normal / Tangent / Color / BoneWeight / BindPose；
  - 生成独立 Mesh 返回。

---

## 8. 常见问题与排查

### Q1：执行后提示 `No GameObject selected.`

- 原因：没有选中任何对象。
- 处理：先在 Hierarchy/Project 中选中目标对象再执行。

### Q2：提示 `MeshRenderer null for 'xxx'!`

- 原因：对象上没有 `SkinnedMeshRenderer`。
- 处理：确认是蒙皮网格对象；如果是 `MeshRenderer + MeshFilter`，此工具当前不处理该类型。

### Q3：提示 `Mesh null for 'xxx'!`

- 原因：`SkinnedMeshRenderer.sharedMesh` 为空。
- 处理：检查模型导入是否正确，确认 Renderer 上绑定了 Mesh。

### Q4：提示 `Only 1 submeshes ...`

- 原因：网格只有 1 个 Submesh。
- 处理：这类网格无需拆分，或先在建模工具中按材质拆分。

### Q5：重复执行后资产重名怎么办？

- 当前已启用 `GenerateUniqueAssetPath` 自动避重；
- 会在同目录生成带序号的唯一文件名。

---

## 9. 注意事项

- 该工具会在场景中创建子对象，并在项目内创建新的 Mesh 资产，请注意版本管理提交范围。
- 工具不主动清理旧的拆分结果；重复执行会继续生成新对象/新资产。
- 如果要批量重跑，建议先清理旧结果再执行，避免层级和资源冗余。

---

## 10. 建议工作流（团队实践）

1. 先复制目标预制体到测试场景。
2. 执行拆分并验证：
   - 子对象数量与 `subMeshCount` 一致；
   - 每个子对象材质是否正确；
   - 蒙皮动画下是否表现正常。
3. 验证通过后再应用到正式资源或批处理流程。
