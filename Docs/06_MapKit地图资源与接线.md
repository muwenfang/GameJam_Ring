# 06 · MapKit 六张地图资源与代码对接

> 2026-11-25：上传目录 `GameJam_Ring_Upload/Assets` 里新增的地图部分（`Assets/MapKit`、`Assets/Screenshots` 及其 `.meta`）已放入现有工程。
> 本文说明三件事：**改了哪些现有代码**、**六张地图与现有编号怎么对应**、**你在 Unity 里需要拖哪些线**。
>
> **11-25 晚间后续**：六图根节点与碰撞地面现在可由菜单「钟声 / 地图 / 生成六张地图与碰撞地面」一次生成并自动连好 `Scenes[0..5]`，不必逐条手工拖。详见 **`Docs/09_按地图切换与碰撞地面.md`**。本文第六节的「纯手工接线」仍然有效，作为不点菜单时的备选。
> **11-27 后续**：背景几何已按 25×12 设计网格重做，并新增「按屏幕重新对齐地图与碰撞地面」菜单就地修复已生成的场景，见 **`Docs/15_地图屏幕对齐与碰撞地面层修正.md`**。

---

## 一、结论

- `MapKit` 是一套**独立资源包**（脚本在 `MapKitTest` 命名空间），与现有 `Ring.*` 代码零耦合；上传的 `Scripts/` 是旧版项目快照，上传的 `Scenes/MapDesign_钟声.unity` 是地图设计验证场景，**均未合入本工程**。
- 本轮**不改动 MapKit 一行代码、不改任何图片 / `.meta`、不改场景 YAML、不自动生成任何 UI 或 GameObject**。
- 现有代码从「占位接口」升级为「可对接真实地图」：新增纯 C# 身份映射表、机制状态反射桥接、两个**可选** HUD 标签字段。
- 全部接线靠 **Inspector 拖线**；一个槽位都不拖也能正常开局（沿用既有的优雅降级）。

---

## 二、改动清单（只改现有代码）

| 文件 | 改动 | 性质 |
|---|---|---|
| `Assets/Scripts/Maps/BattleMapId.cs` | 新增 `BattleMapInfo` 与 `BattleMaps.Infos` 六图映射表；`Info()` 查询；`ToZoneName` / `ToZoneFolder` / `ToAssetPath` / `ToConfigKey` / `ToMechanicScript` / `ToMechanicSummary` 六个扩展方法；`ToDisplayName` 由「地图 N」改为真实中文名 | 纯增量，零 `UnityEngine`，可离线回归 |
| `Assets/Scripts/Maps/MapScene.cs` | 新增可选 `zoneMechanic`（`MonoBehaviour`）槽位；`DisplayName` / `ZoneFolder` / `ZoneName` 只读属性；`StatusText()` 反射调用 MapKit 机制的同名方法 | 增量；`Enter/Exit` 仍只做 `SetActive`，不创建物体 |
| `Assets/Scripts/Maps/BattleMapPresenter.cs` | 新增 `CurrentScene` / `CurrentDisplayName` / `StatusText()`；`Show` 记录当前根节点、日志带上资源目录；`Hide` 清空 | 增量；六槽位与幂等逻辑不变 |
| `Assets/Scripts/Flow/GameFlow.cs` | 新增只读 `CurrentMapInfo`；更新 `fixedMap` 与 `CurrentMap` 的注释 / Tooltip | 增量；随机与 `MapChanged` 时序不变 |
| `Assets/Scripts/UI/GameUiController.cs` | 新增两个可选 `Text` 字段（`mapNameLabel` / `mapStatusLabel`）；`RefreshMapLabels` / `RefreshMapStatus` / `ClearMapLabels`；`Update` 里按 0.1s 节流刷状态 | 增量；**不创建、不自动查找任何 UI 物体** |
| `.check/belltest/Program.cs` | 旧断言「地图 1」改为「Z1 雨夜竹林」；新增 Section「地图：MapKit 资产映射」17 条断言 | 离线回归 |
| `Docs/05`、`Docs/00`、`Docs/03` | 更新「地图实体尚未制作」等过时表述 | 文档 |

**刻意没改的**：枚举成员名仍是 `Map01..Map06`、值仍是 0..5。它们是 Inspector 里已序列化的枚举值，改名会让场景 / 预制体里存下来的 `Fixed Map` 等字段产生歧义；真实身份全部走映射表，值不动即可随时扩充。

---

## 三、六图身份映射（`BattleMaps.Infos`）

顺序来自 `Assets/MapKit/Maps/01..06`、上传设计场景的 `Z1..Z6`、以及 `Assets/Screenshots` 的 `MapDesign_Z1..Z6_地图.png`，三者一致。

| `BattleMapId`（槽位下标） | 中文显示名 | 设计场景根节点名 | 资源目录 | `map_config.json` 键 | 机制脚本 | 机制说明 |
|---|---|---|---|---|---|---|
| `Map01`（0） | Z1 雨夜竹林 | `Z1_雨夜竹林` | `01_BambooNight` | `bamboo` | `BambooNightMechanic` | 雷雨：随机落雷，低概率 40 伤害 + 眩晕 1s |
| `Map02`（1） | Z2 蒸汽井盖 | `Z2_蒸汽井盖` | `02_Steam` | `steam` | `SteamMechanic` | 蒸汽：每 4~5s 约 20% 井口预警 1s 后喷发，5 伤害 |
| `Map03`（2） | Z3 竞技场 | `Z3_竞技场` | `03_Arena` | `arena` | `ArenaMechanic` | 竞技场：每 15s 双方各叠 15% 攻击，可叠加 |
| `Map04`（3） | Z4 精灵森林 | `Z4_精灵森林` | `04_ElfForest` | `forest` | `ForestMechanic` | 抓取藤：冒芽 → 成熟 → 缠住 → 凋亡 |
| `Map05`（4） | Z5 钟塔 | `Z5_钟塔` | `05_ClockTower` | `clock` | `ClockMechanic` | 钟声三选一：回溯 / 加速 ×1.15 / 减速 ×0.85 |
| `Map06`（5） | Z6 洞穴 | `Z6_洞窟` | `06_Cave` | `cave` | `CaveMechanic` | 落石：每 8s 落 3~10 颗，预警 1s，10 伤害 |

> 命名差异：截图用「Z6 洞穴」，设计场景根节点用「Z6_洞窟」。两者都保留在映射表里 —— `DisplayName` 用于 UI，`ZoneName` 用于按名对照场景物体。

`BattleMapPresenter.Scenes[i]` 与 `BattleMapId` 值一一对应；`GameFlow.CurrentMap`、`MapChanged` 事件、`Fixed Map` 调试字段全部沿用这个编号，无需改动。

---

## 四、MapKit 包内有什么（已入库，未改动）

| 路径 | 内容 |
|---|---|
| `MapKit/Maps/01..06` | 每图 `Backgrounds` / `Ground`（含 `*_Tile.asset`）/ `Sprites`；`04_ElfForest` 另有 `Animations/` 下 7 个 `.anim`（抓握藤与缠绕特效） |
| `MapKit/Scripts/MapKitConfig.cs` | 读 `Resources/MapKit/map_config.json` 的 `MapKitConfig.Load()`；`RangeF` / `RangeI` / `GridConfig` / 六套机制参数类 |
| `MapKit/Scripts/Mechanics/` | `ZoneMechanicBase` + 六个 `XXXMechanic`；纯视觉演示（日志前缀 `[机制验证]`），**不含角色 / 战斗耦合**，各自提供 `public string StatusText()` |
| `MapKit/Scripts/ForestFireflies.cs`、`ForestLeafSway.cs` | 精灵森林的萤火虫与树叶摆动；`UnityEditor` 引用已用 `#if UNITY_EDITOR` 隔离 |
| `MapKit/Resources/MapKit/map_config.json` | `grid` 25×12、`tileUnits` 1、`actorWidth/Height` 1×2；六套机制参数（见下节） |
| `MapKit/Materials`、`MapKit/Shaders` | `ForestWindSway.mat`、`WindSwayBackground.shader` |
| `MapKit/Editor/` | `MapKitImporter`（图片导入参数表）、`MapKitTools`（生成藤动画 / 地面 Tile / 重新导入）、`MapKitForestLeafSetup`、`MapKitWindEditorTicker` |
| `Screenshots/` | 6 张地图效果图 + 6 张机制验证图（竹林雷雨 / 雨丝落点 / 森林动态 / 藤蔓 / 钟面转动 / 洞穴落石） |

---

## 五、机制参数摘要（读自 `map_config.json`）

| 键 | 主要参数 |
|---|---|
| `grid` | `cols=25, rows=12, tileUnits=1, actorWidth=1, actorHeight=2` |
| `bamboo` | `intervalSeconds 1.6~2.4`、`extraChance 0.22`、`extraCheckEvery 1.4`、`damageChance 0.02`、`damage 40`、`stunSeconds 1`、`darknessAlpha 0.99`、`flashSeconds 0.6` |
| `steam` | `batchInterval 4~5`、`ventRatio 0.2`、`warningSeconds 1`、`maxConsecutive 3`、`damage 5` |
| `arena` | `intervalSeconds 15`、`atkBonusPerStack 0.15`、`stackable true`、`onlyInsideArea true` |
| `forest` | `batchInterval 4~6`、`ratioOfEmpty 0.2`、`growSeconds 0.7`、`matureSeconds 2`、`witherSeconds 0.3`、`rootSeconds 0.8`、`tangleOncePerPlant true` |
| `clock` | `intervalSeconds 10`、`rewindSeconds 3`、`hasteMultiplier 1.15`、`slowMultiplier 0.85`、`speedDuration 4` |
| `cave` | `intervalSeconds 8`、`rockCount 3~10`、`warningSeconds 1`、`damage 10`、`rockSizeTiles 1` |

> 这些参数由 MapKit 机制脚本在 `Awake/Start` 里通过 `MapKitConfig.Load()` 自行读取，现有代码**不重复解析、不缓存、不覆盖**。改数值直接改 JSON。

### 竹林落雷闪烁节奏（2026-11-28 放慢）

Z1 的「频闪」由三处叠加而成，只调其中一个会留下其余两处继续刺眼：

| 来源 | 旧值 | 现值 | 说明 |
|---|---|---|---|
| 雷击间隔 `intervalSeconds` | `0.8~1.2s` | `1.6~2.4s` | 平均约 2 秒一道雷；两次雷之间的全黑约 1.0~1.8 秒（扣掉 0.6 秒闪光）。曾一度放到 `2.6~4.2`，因**黑屏等待过长**收回 |
| 追加雷 `extraChance` / `extraCheckEvery` | `0.5` / `1` | `0.22` / `1.4` | 连击概率与二次雷延迟同时放大，避免刚闪完又补一发 |
| 单次闪光 `flashSeconds` | `0.3s` | `0.6s` | 见下一行代码侧改动 |
| 闪电线存活 | 硬编码 `0.22s` | `flashSeconds × 0.78` | 否则第二次亮时闪电已消失，只剩黑幕闪白 |

代码侧：`BambooNightMechanic.FlashAndHide()` 原来把「亮 0.05s → 暗 0.04s → 亮 0.05s」写死，50ms/40ms 的白黑硬对切是最刺眼的频闪成分，而且 `flashSeconds` 只影响后面的淡出段。现在四个阶段（亮1 / 中间回落 / 亮2 / 淡回黑）全部按 `flashSeconds` 的比例分配（`0.20 / 0.17 / 0.20 / 0.43`），中间回落停在 `Lerp(亮, 黑, 0.55)` 而不是压到近全黑，所以只改 JSON 里的 `flashSeconds` 就能整体减速。黑幕「打亮」透明度提成常量 `CoverAlphaLit = 0.13f`，`Update()` 里驱动雨丝亮度也读同一常量。

> **黑屏时长 ≈ `intervalSeconds` − `flashSeconds`**（`ThunderLoop` 拍完一道雷就开始计时，闪光协程并行播 0.6 秒）。所以想「暗得短」有两个互不冲突的旋钮：
> - 压 `intervalSeconds`：现在是 `1.6~2.4`，再想短可到 `1.2~1.8`（会开始接近旧版频闪感）；
> - 提 `flashSeconds` 占比：闪光长则暗的相对短，但整体会更晃。
>
> 另一种「不那么黑」的手段是降 `darknessAlpha`（如 `0.99 → 0.95`，黑暗期能透出极淡轮廓）。这条会削弱「全黑视野、靠闪电照明」的设计意图，属玩法取舍，未擅自改动。伤害雷的 `damageChance 0.02` / `damage 40` 也未动。

---

## 六、Inspector 接线清单（你来做）

`Ring.*` 侧只解析引用、切显隐、读状态，**不 `new GameObject`、不 `Instantiate`、不改场景 YAML**。UI 仍全部由你在场景里手工搭好再拖进 Inspector。

唯一例外是 `MapKit` 自带的 `SteamMechanic`：它有一处**可选的运行时兜底生成**（`Build Manholes If Missing`，默认开）—— 场景里没搭够井盖格时才在 `Z2` 根节点下生成 25 个固定 1×1 的格子；关掉开关或手工搭好 `Manholes_25格` 容器后，代码一个物体都不会创建。详见第三节与 `Docs/21`。

1. **六张地图根节点**：在场景里为每个 Z 区建一个根 GameObject，用 `MapKit/Maps/0X` 下的背景 / 地面 Tile / Sprite / 动画搭层级。物体命名与层级可参考上传的 `MapDesign_钟声.unity`（`Z1_雨夜竹林` … `Z6_洞窟`、`BG_背景`、`Ground_地面25格`、`PROP_时针` / `PROP_分针` / `PROP_钟面`、`G_01..G_25`）。
2. **挂 `MapScene`**：每个根节点挂一个，设置 `Map Id` = `Map01..Map06`（按上表顺序）。建议根节点在场景里**存成隐藏** —— `Show` 只会激活选中的那张，初始全激活会六张叠在一起。
3. **挂机制组件**：把对应的 `XXXMechanic` 挂在同区根节点或其子物体上，并在 Inspector 里赋它需要的 Sprite / Transform 引用。注意两个脚本对层级名有依赖：
   - `SteamMechanic`：**井盖格来源已改**。优先读 `Manholes_25格` 容器（可手工搭 25 个子格）；没搭够时按 `Build Manholes If Missing` 运行时生成 25 个固定 1×1 的格子，并隐藏 `Ground_地面25格` 自身那张被拉成长条的贴图。`Ground_地面25格` 现在只用于定位与碰撞，**不再当作井盖格来源**（旧实现 `GetComponentsInChildren` 会把它自身算进去，导致只识别出 1 个井盖）。
   - `ClockMechanic` 找 `PROP_时针` / `PROP_分针`。
   机制组件挂在同一根节点下时，会随 `MapScene.Enter/Exit` 的 `SetActive` 自动启停，**不需要代码额外开关**。

   `SteamMechanic` 新增的 Inspector 字段（都有默认值，不拖也能跑）：`Manhole Count`（25）、`Manhole Width Cells`（1）、`Manhole Local Y`（0.5）、`Manhole Sorting Order`（-9）、`Manhole Container`、`Build Manholes If Missing`。
4. **`BattleMapPresenter`**：把六个根节点依次拖进 `Scenes[0..5]`（顺序 = 上表）。
5. **`MapScene` 的 `Zone Mechanic`（可选）**：把该图的主机制组件拖进去，`StatusText()` 才会被 HUD 读到；不拖不影响地图显隐与机制运行。
6. **`GameUiController` 的两个可选标签**：场景里手工搭两个 `Text`（uGUI）后拖进 `Map Name Label` / `Map Status Label`；不拖也能跑，只是不显示文字。
7. **`battleCanvas`**：沿用 `Docs/05` 的查找规则（`battleCanvas` 优先、兼容旧名 `BattleGround`），最稳妥是直接拖进 `Battle Canvas` 槽位。

---

## 七、运行时链路

```
点「开始游戏」
  → GameFlow.StartMatch()
      ① CurrentMap = BattleMaps.Pick(Random.Range(0, 6))
      ② Flow.StartMatch() → StateChanged(MainMenu → Loading)
           → GameUiController.ApplyState
               battleCanvas.SetActive(true)
               BattleMapPresenter.Show(CurrentMap)
                   其余五张 Exit()；选中根节点 Enter()（SetActive(true)）
                   根节点下的 XXXMechanic 随激活开始跑协程
               RefreshMapLabels(CurrentMap)
                   mapNameLabel  ← DisplayName + " · " + MechanicSummary
                   mapStatusLabel ← Presenter.StatusText()
                                    → MapScene.StatusText()
                                    → 反射调用 XXXMechanic.StatusText()
      ③ MapChanged → HandleMapChanged → Show（幂等）+ 刷新标签
  → Loading → Collect → Elimination：Update 每 0.1s 刷一次 mapStatusLabel
  → 结算 / 返回主菜单 → battleCanvas 隐藏 + Hide() + ClearMapLabels()
```

**反射桥接的理由**：`Ring.Maps` 不引用 `MapKitTest` 程序集，MapKit 机制脚本也不必实现任何 Ring 接口，双向零耦合。没拖组件 / 方法不存在 / 调用抛异常，`StatusText()` 一律返回空串，UI 显示「机制：待命」，绝不影响开局。

---

## 八、降级行为

| 情况 | 行为 |
|---|---|
| 没挂 `BattleMapPresenter` | 每局仍随机地图，`[流程] 本局地图：Z1 雨夜竹林` 照常打印；界面不切地图，启动打一条警告 |
| 选中槽位为空（`Scenes[i] == null`） | 其余五张 `Exit()`，本局不显示地图，每局打一条明确警告（含槽位下标与真实地图名） |
| 没拖 `Zone Mechanic` | 机制照常运行，只是 `StatusText()` 返回空串，HUD 显示「机制：待命」 |
| 没拖两个 `Text` 标签 | `RefreshMapLabels` / `RefreshMapStatus` / `ClearMapLabels` 都是空操作 |
| 非法编号 | `Pick` / `ClampToValid` 折回合法范围；`Info()` 返回 null；扩展方法返回空串或「未知地图」 |

`BattleMapPresenter.Show()` 仍是**幂等**的：同一张地图不会反复触发 `Enter()`；回主菜单 `Hide()` 后下一局重新 `Show` 才会再次进入。

---

## 九、验证

### 离线校验命令（在 `APPDATA` 等环境变量正常的 PowerShell / CMD 里执行）

```powershell
# 本机 Git Bash / PowerShell 缺 APPDATA、ProgramData 时，NuGet restore 会报
# "Value cannot be null. (Parameter 'path1')"，加 --no-restore 即可正常编译。
& 'C:\Program Files\dotnet\dotnet.exe' build 'D:\unity\GameJam_Ring\.check\check.csproj' --no-restore -v q --nologo
& 'C:\Program Files\dotnet\dotnet.exe' build 'D:\unity\GameJam_Ring\.check\belltest\belltest.csproj' --no-restore -v q --nologo
& 'C:\Program Files\dotnet\dotnet.exe' 'D:\unity\GameJam_Ring\.check\belltest\bin\Debug\net10.0\belltest.dll'
```

### 本次结果（如实记录）

- **改动前基线**（11-23，同一工程）：全工程编译 0 警告 / 0 错误；离线回归 **441 条通过 / 0 失败**。
- **11-25 晚间：Unity Console 报出一串 CS0234 / CS0246（`Ring.Maps` 命名空间不存在）。根因不在代码，在 `.meta`**：`Assets/Scripts/` 下 4 个 `.meta` 的 `guid` 是 33 / 34 位非法长度（合法值必须是 **32 位十六进制**）——
  - `Scripts/Maps.meta`、`Scripts/Maps/BattleMapId.cs.meta`、`BattleMapPresenter.cs.meta`、`MapScene.cs.meta`
  - Unity 日志原文：`The .meta file ... does not have a valid GUID and its corresponding Asset file will be ignored.`
  - 后果：`Maps/` 整个目录连同 3 个脚本被 Unity 忽略 → `Assembly-CSharp.csproj` 不含它们 → `Ring.Maps` 不存在 → `GameFlow.cs` / `GameUiController.cs` 的 17 条引用全部连带报错。**这些脚本此前从未真正进入过 Unity 编译**，所以离线 `.check` 工程（按文件系统 glob 收源码、不看 `.meta`）一直是绿的，掩盖了问题。
  - 修复：把 4 个 guid 截为合法 32 位（保留前缀、最小变更），全项目 **288 个 `.meta` 校验：0 非法 / 0 重复**。这 4 个 guid 在任意 `.unity` / `.prefab` / `.asset` 中均 **0 引用**，故改 guid 不会破坏任何 Inspector 连线。
- **真实编译验证已通过**：绕开卡住的 NuGet/MSBuild，直接调用 Unity 自带 Roslyn（`Editor/Data/DotNetSdkRoslyn/csc.dll`）+ Bee 生成的完整 `Assembly-CSharp.rsp`（含全部 Unity 程序集引用与 define），补回被漏编的 3 个 Maps 脚本 → **0 错误 / 0 警告**；产物 DLL 中可检出 `BattleMapInfo` / `BattleMapPresenter` / `BattleMapId` 类型，确认脚本真正参与编译。命令与临时产物在 `.check/_verify/`，验证后已清理。
- 静态核对同样通过：跨文件引用（`BattleMapInfo`、`BattleMaps.Info`、六个扩展方法、`StatusText` 链路、三个 UI 刷新方法）、`using`、命名空间、扩展方法可见性、序列化兼容性（枚举名与值未动）逐项一致。
- 回归断言已补齐：新增 Section「地图：MapKit 资产映射」共 **17 条**断言，并把旧的「地图 1」显示名断言改为「Z1 雨夜竹林」。
- **离线回归已成功重跑（本次修订，井盖 + 四向移动）**：`--no-restore` 可绕开本机 NuGet `Value cannot be null. (Parameter 'path1')`；全工程编译 **0 错误**，belltest **587 条通过 / 0 失败**，其中新增「矩形场地」9 条断言全部通过（W/S/A/D 四向位移、四角裁剪、不穿墙、圆形求解器不变）。此前「离线回归 DLL 未能重跑」的记录已被本次结果覆盖。详见 `Docs/21`。
- **必须回 Unity 实测**：切回 Unity 让它重新导入 4 个 `.meta` 并编译（会自动触发），确认 Console 0 错误；Play 后确认 `battleCanvas` 显示、六区按 `CurrentMap` 单选激活、机制状态文本随波次变化。离线校验无法覆盖 MonoBehaviour 生命周期、Inspector 序列化连线与 Canvas 实际渲染。

> 经验：Unity 的 `.meta` guid 必须是 32 位十六进制。手写 / 复制 `.meta` 时长度多一位就会让整个目录被静默忽略，且报错表现为「命名空间不存在」而非「meta 损坏」，极易误判为代码问题。排查入口是 `Editor.log` 里的 `does not have a valid GUID` 与 Unity 生成的 `Assembly-CSharp.csproj` 编译清单。

---

## 十、明确没做的（边界）

- **未把机制效果接入战斗**：伤害、眩晕、加速 / 减速、攻击加成仍只作用于 MapKit 自己的视觉演示（脚本注释明确「纯地图演示不含角色」），不影响 `CharacterInstance`、钟声规则与胜负判定。若要把它们接进角色数值，需要另定一套「地图机制 → 战斗系统」的事件契约，属于独立改动。
- **未合并上传的 `MapDesign_钟声.unity`**：它把六个 Z 区放在同一张「六区域对决地图」里做设计验证；本工程仍按 `Docs/05` 的层次 —— `BattleMapId` 是「这一局选哪张地图」，每图内部的 6 个区域锚点是 `MapScene.RegionAnchors`。两者层次不同，未强行统一。
- **未自动生成任何 UI 物体**，未改场景 YAML，未改 Screenshots 任何图片 / `.meta`。**本次修订（井盖只剩一个）**：修改了 `MapKit/Scripts/Mechanics/SteamMechanic.cs`，并在 `Z2` 根节点下提供**可选**的运行时井盖兜底生成（`Build Manholes If Missing`，默认开、可关）；手工搭好 `Manholes_25格` 容器后代码不创建任何物体。详见第三节与 `Docs/21`。其余 MapKit 脚本、图片、`.meta` 未改；唯一的 `.meta` 改动仍是 11-25 修复 `Assets/Scripts/Maps/` 下 4 个非法 guid（见第九节）。
- **状态校正（2026-11-26）**：`RegionAnchors` 已不再是预留数组——场景已建 `ArenaAnchor_Map01..06` 并填入 `MatchSpawner.Region Anchors[0..5]`；`BellResult.Swaps` 也已由 `GameFlow.RebuildArenas` 消费（见 `Docs/10` 第四节）。地图骨架（六 `MapScene` 根节点 + 碰撞地面）已由菜单生成到场景，机制脚本挂载仍需手工。
- 没有转场动画、没有按地图切 BGM / 相机——**仍未做**；机制效果接入战斗数值仍未做（见本节第一条）。
