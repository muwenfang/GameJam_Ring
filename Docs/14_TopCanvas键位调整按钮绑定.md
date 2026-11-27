# 14 · TopCanvas「键位调整」按钮绑定

> 2026-11-27。代码说明按工程约定使用 Markdown；一般说明文档才生成 docx。
> UI 控件仍由用户在 Unity 编辑器手工搭建，本轮**没有新建任何 UI 物体、没有改 `.unity` 场景文件**。

---

## 一、结论

`TopCanvas → SettingPanel` 下名为 `Input` 的按钮（文字标签为「键位调整」）已接入 `GameUiController`：
- 组件在 `Start()` 时按名字自动解析该按钮引用，并运行时绑定点击回调；
- 同时提供 public 的 `OpenKeyBindingsFromUi()`，可在 Inspector 的 `Button.OnClick` 下拉里手工拖绑；
- 点击后打开键位调整界面：优先切换用户手搭的游戏内面板，否则编辑器下打开菜单「钟声/输入映射/改键面板」。

**Unity 编辑器当前处于打开状态**，直接改 `SampleScene.unity` 会被 Unity 回写覆盖，因此本轮采用「代码按名自动解析」的接线方式：不需要拖线、不需要改场景，刷新脚本后即生效。

---

## 二、核查结论（改动前）

| 项 | 实测 |
|---|---|
| 按钮位置 | `SampleScene.unity`：`TopCanvas`(GO `666675877`) → `SettingPanel`(GO `1224053333`) → `Input`(GO `1962090762`) |
| 按钮组件 | `Button` 组件 `1962090764`；子文字物体名为小写 `input`（GO `1193005592`），显示文本「键位调整」 |
| OnClick | `m_OnClick.m_PersistentCalls.m_Calls` 只有一条空项（`m_Target: {fileID: 0}`、`m_MethodName` 为空） |
| 代码侧 | `GameUiController.ResolveMenuReferences()` 原来只在 `mainMenuCanvas`（名为 `MainMenu` 的 Canvas）子树里按名找按钮；该按钮挂在另一个 Canvas（`TopCanvas`）下，**按名找不到**；且源码里没有任何 `inputButton` 字段或 `HandleInputClicked` 方法 |
| 关联功能 | 改键能力已存在：运行时 `GameInput.TryRebind` + `InputSettings`，编辑器 `InputRebindWindow`（菜单「钟声/输入映射/改键面板」）；但**游戏内改键界面未落地**（见 `output/未落地板块报告_2026-11-26.docx` 8.4），代码侧也没有绑定点 |

---

## 三、改动清单（`Assets/Scripts/UI/GameUiController.cs`，单文件）

1. 常量：新增 `InputButtonName = "Input"`、`KeyBindingPanelName = "KeyBindingPanel"`。
2. 新增两个可选 Inspector 槽位：
   - `inputButton`（`Button`）：留空按名字自动找；
   - `keyBindingPanel`（`GameObject`）：用户手搭的游戏内键位面板，留空按名字自动找。
3. `ResolveMenuReferences()`：新增 `FindInputButton(root, settingsPanel)` 解析按钮；`FindGameObjectAnywhere()` 解析面板。查找顺序为**设置面板子级 → 主菜单子级 → 全场景**，名字大小写不敏感（兼容按钮物体 `Input` 与文字子物体 `input`）。
4. `BindMenuButtons()`：新增 `Bind(inputButton, "键位调整", HandleInputClicked)`，与其它主菜单按钮同构（先 `RemoveListener` 再 `AddListener`）。
5. 新增点击处理与 Inspector 包装：
   - `HandleInputClicked()` / `OpenKeyBindings()`（private）：`keyBindingPanel` 非空则切换显隐；否则 `#if UNITY_EDITOR` 下 `EditorApplication.ExecuteMenuItem("钟声/输入映射/改键面板")`，打包版打印提示；
   - `public void OpenKeyBindingsFromUi()`：void 无参包装，专供 `Button.OnClick` 下拉拖绑（UnityEvent 只列返回 void 的无参 public 方法，private 的 `HandleInputClicked` 选不到）。
6. 新增静态辅助：`FindInputButton` / `FindButtonNamed` / `FindGameObjectAnywhere`；全场景查找用 `FindObjectsByType<T>(FindObjectsInactive.Include)`，避免引入新的过时 API 警告。

**未改动**：`SampleScene.unity`、任何 prefab、任何其它脚本、`Assembly-CSharp.csproj`。

---

## 四、绑定链路与注意

```
Start() → ResolveMenuReferences()
  ├─ settingsPanel（场景里已拖好，GO 1224053333）
  ├─ inputButton ← FindInputButton()  // SettingPanel 子级里名为 Input 的 Button
  ├─ keyBindingPanel ← 可选
  └─ BindMenuButtons() → inputButton.onClick.AddListener(HandleInputClicked)
                                        ↓ 点击
                                   OpenKeyBindings()
        ┌──────────────────────────┴──────────────────────────┐
   面板已接（拖入或按名找到）                        面板为空
   切换面板显隐                              UNITY_EDITOR：打开改键窗口
                                            打包版：打印提示
```

**两种绑定方式二选一**：代码运行时自动绑定，或在 Inspector 的 OnClick 里绑 `OpenKeyBindingsFromUi()`。若同时绑，一次点击会调用 `OpenKeyBindings()` 两次，面板会「打开后立刻关闭」（退出按钮的 `ExitGameFromUi` 因 Quit 幂等无副作用，本按钮不幂等，务必避免重复绑定）。

---

## 五、验证

| 项目 | 命令 / 结果 |
|---|---|
| 全量离线编译（真实 Unity 6000.6.11f1 程序集，glob 收录 `Assets/**/*.cs`） | `dotnet build .check/check.csproj --no-restore -p:GenerateDependencyFile=false -t:Rebuild` → **0 错误、26 个既有警告**（无新增警告） |
| 场景结构 | 离线解析 `SampleScene.unity`：`SettingPanel` 五个子物体为 `SettingImage / AudioSlider / CloseSetting / FullScreen / Input`，`Input` 带 `Button` 组件；`GameUiController.settingsPanel` 已指向 `1224053333`，因此按名解析可命中 |

> 离线编译验证不了 MonoBehaviour 生命周期、焦点切换与 `ExecuteMenuItem` 的编辑器行为，需回 Unity 实测。

---

## 六、待用户在 Unity 里做

1. 切回 Unity 等待脚本刷新（当前编辑器开着，改 `.unity` 会被覆盖，所以没有改场景）。
2. Play，打开设置面板点「键位调整」：编辑器下应弹出「输入映射」改键窗口；Console 出现 `[界面] 点击键位调整。`
3. 可选：`GameUiController` 的 `Input Button` 槽里手工拖入该按钮（固定引用，不依赖按名查找）；`Key Binding Panel` 槽留空即可。
4. 以后搭好**游戏内键位调整面板**后，把它拖进 `Key Binding Panel`（或命名 `KeyBindingPanel`），按钮即改为切换该面板显隐。面板里的逐行改键、`GameInput.TryRebind`、恢复默认、保存仍属未落地板块，需要时再接。

---

## 七、边界

- 游戏内改键界面（逐行按键捕获 / 冲突提示 / 恢复默认 / 落盘）**未实现**，本轮只补按钮绑定点与入口，不虚报。
- 编辑器改键窗口是 `InputRebindWindow`（Editor 程序集），打包版不可用；打包版在没有游戏内面板时只打印提示。
- 未生成任何 UI 控件、未自动挂组件到 Canvas，沿用工程约定。
