# 14 · TopCanvas「键位调整」按钮 → 游戏内改键面板

> 2026-11-28 更新：游戏内改键面板已经落地（`Assets/Scripts/UI/KeyBindingPanel.cs`，UI 全部由代码搭），
> 点「键位调整」直接打开它；`Assets/Editor/InputRebindWindow.cs` 保留给策划改「默认键位」用。
> 2026-11-27 首版只做了按钮绑定点，本轮把面板本体补上了。

---

## 一、结论

- `TopCanvas → SettingPanel` 下名为 `Input` 的按钮（文字标签「键位调整」）由 `GameUiController` 在 `Start()` 时按名字自动解析并绑定回调；
  也提供 public 的 `OpenKeyBindingsFromUi()` 供 Inspector 手工拖 `Button.OnClick`（**二选一**，同时绑会一次点击开关两次）。
- 点击后**打开游戏内改键面板**（不是编辑器窗口）。面板第一次点击时在 `TopCanvas` 下现搭，之后复用；
  再点一次「键位调整」或面板里的「关闭」/ `Esc` 收起。
- 改键是给玩家的：改完立刻生效，并按 `InputSettings.persistToPlayerPrefs` 写 `PlayerPrefs`。

---

## 二、按钮与场景（改动前的核查结论，仍然有效）

| 项 | 实测 |
|---|---|
| 按钮位置 | `SampleScene.unity`：`TopCanvas`(GO `666675877`) → `SettingPanel`(GO `1224053333`) → `Input`(GO `1962090762`) |
| 按钮组件 | `Button` 组件 `1962090764`；子文字物体名为小写 `input`（GO `1193005592`），显示文本「键位调整」 |
| OnClick | 场景里是空持久调用（`m_Target: {fileID: 0}`），实际绑定由代码在运行时完成 |
| 为什么不能放进 MainMenu | 按钮挂在 `TopCanvas` 下，不在 `MainMenu` Canvas 子树里；`FindInputButton()` 的查找顺序因此是 **设置面板子级 → 主菜单子级 → 全场景**，名字大小写不敏感 |

---

## 三、游戏内面板：`Assets/Scripts/UI/KeyBindingPanel.cs`

**全部 UI 由代码搭建**（复用 `UiBuilder`），不依赖场景里的预制体、不改 `.unity`。

| 元素 | 说明 |
|---|---|
| 压暗层 | 全屏半透明 `Dim`，`raycastTarget = true`，面板开着时点不到下面的按钮 |
| 面板 | 1000×960 居中，深色底（`UiBuilder.PanelColor`） |
| 标题 / 提示 | 「键位调整」+「点一下键位，再按下你想用的键。Esc 取消。改完立刻生效，并自动保存。」 |
| 列表 | 11 行 = `InputActions.All`，每行「动作名 / 主键按钮 / 备用键按钮」 |
| 状态行 | 改键成功（绿）/ 冲突与错误（红）/ 普通提示（灰）、捕捉中的「按下新键…」 |
| 底部 | 「恢复默认」（`GameInput.ResetBindings()`）、「关闭」 |

**交互**

1. 点某一行的键位按钮 → 进入捕捉态，那一格显示「按下新键…」，状态行提示正在改哪个动作的哪一格。
2. 直接按键（`InputKeyCatalog.All` 逐个 `Input.GetKeyDown`，鼠标键走 `Input.GetMouseButtonDown(0/1/2)`）→ 调 `GameInput.TryRebind`。
   - 成功：「「平A」主键已改为「K」，已保存。」（`persistToPlayerPrefs` 关着时提示不落盘）
   - 冲突：「「K」已经被「跳跃」占用了，换一个键。」—— 面板不会替你抢键，必须先改掉占用者。
   - 按下的是当前键：提示「本来就是 K」，不改。
3. `Esc` 在捕捉态 = 取消这次改键；不在捕捉态 = 关闭面板。因此**Esc 不能被绑给别的动作**（它固定当取消/关闭用）。
4. 「恢复默认」：`GameInput.ResetBindings()` → 重建文档默认表 + 清 `PlayerPrefs` 里的键位存档。

**暂停**：面板打开时存下当前 `Time.timeScale` 再设 0，关闭时还原。
与设置面板的暂停可以嵌套：设置里暂停（存 1 → 0）后打开改键（存 0 → 0），先关哪个都不会把游戏卡在 0。

**输入屏蔽**：面板打开时还会 `GameInput.PushGameplaySuspension()`（关闭时 `Pop`），`GameInput.Poll()` 在屏蔽期间直接返回、意图清空——否则玩家按 `W` 或 `1` 去改键时，角色会同时往前走、顺手放个技能。已经在按的键会补发一次 `Released`，不会让角色卡在「一直按住」。

---

## 四、`GameUiController` 的接线改动

1. 字段 `keyBindingPanel` 的类型由 `GameObject` 改成 `KeyBindingPanel`（留空时运行时自动搭，不再需要手搭后拖入）。
2. `ResolveMenuReferences()`：`if (keyBindingPanel == null) keyBindingPanel = FindObjectOfType<KeyBindingPanel>(true);`
3. `OpenKeyBindings()`：先 `EnsureKeyBindingPanel()`（按类型找 → 找不到就在 `TopCanvas` 下 `KeyBindingPanel.Create()`），再「开着就关，关着就开」。
4. `Update()`：先 `SyncSettingsState()`（把 `settingsOpen` 跟 `SettingPanel.activeSelf` 对齐——场景里「设置 / x」按钮自带持久绑定直接切 `SetActive`，绕过了 `ShowSettings/HideSettings`，不对齐的话 Esc 会误判）；再在面板开着时先 `keyBindingPanel.TickInput()`；它吃掉了 `Esc` 就不再走 `HandleSettingsHotkey()`，避免「关改键面板的同时把设置也打开」。
5. `ShowMenu()` / `HideSettings()` 里调 `CloseKeyBindings()`：开局、回主菜单、收设置时不会留下一个暂停中的面板。
6. `BindMenuButtons()` 顺带把场景里的 `Setting` / `CloseSetting` 也接上（`HandleSettingsClicked` / `HideSettings`），与场景自带的持久绑定叠加执行、结果一致；这样打开设置时一定会暂停、关掉时一定清暂停。

**未改动**：`SampleScene.unity`、任何 prefab、`InputSettings.asset`。

---

## 五、验证

| 项目 | 结果 |
|---|---|
| 离线编译 `.check/check.csproj`（默认配置） | **0 错误**，30 个既有警告（`FindObjectOfType` 过时等，均非本轮新增） |
| 离线编译 `.check/check.csproj -p:DefineConstants=UNITY_EDITOR` | **0 错误**，32 个既有警告 |

> 离线编译验证不了生命周期、uGUI 点击与 `Input.GetKeyDown` 的实际响应，需回 Unity Play 一次。

---

## 六、在 Unity 里怎么验

1. 切回 Unity 等脚本刷新（本轮没有改 `.unity`，不会被回写覆盖）。
2. Play → 点「设置」→ 点「键位调整」（`TopCanvas/SettingPanel/Input`）：应弹出游戏内面板，Console 出现 `[界面] 点击键位调整。` 与 `[界面] 运行时搭好了键位调整面板（挂在 TopCanvas 下）。`
3. 点「平A」的主键 → 按 `K`：状态行变绿提示已保存，格子显示 `K`；按 `K` 应该能平A。
4. 再点「跳跃」主键按 `K`：应提示被「平A」占用，按键不变。
5. 点「恢复默认」：全部回到文档默认键位。
6. `Esc` 先取消捕捉，再 `Esc` 关面板；关闭后 `Time.timeScale` 回到 1（可在 Inspector 里盯一下）。
7. 面板开着时按 `W` / `1`：角色不该移动、也不该放技能（`GameInput.Poll` 被屏蔽）；关掉面板后恢复正常。

---

## 七、边界

- 只做键盘 + 三键鼠标的**单键绑定**：没有手柄、没有组合键、没有轴与死区（`InputKey` 枚举就没有这些成员）。
- `Esc` 固定当「取消 / 关闭」，不能被绑给其它动作。
- 冲突不自动换键：占用的键必须先由玩家改掉，避免「悄悄把别人的键抢走」。
- 编辑器窗口 `InputRebindWindow`（菜单「钟声/输入映射/改键面板」）没删：它改的是 `InputSettings.asset` 里的**默认键位**（策划配置），和玩家在游戏里的个人键位是两条路，共用同一张表的格式。
- 面板是运行时创建的物体，**不进场景存档**；Play 结束后自动消失。
