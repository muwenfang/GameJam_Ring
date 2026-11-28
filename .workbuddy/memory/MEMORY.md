# 项目长期约定（GameJam_Ring）

## UI 与场景
- **UI 优先由用户在 Unity 里手工搭，再拖进 Inspector**；代码侧尽量不自动生成 UI 物体。只有兜底路径才允许运行时搭（并保留开关）。
- **渲染层级（Sorting Order）固定口径**：`battleCanvas` / `MainMenu` = 0、`HudCanvas` = 90、`RingCanvas`（运行时 HUD/结算）= 100、`TopCanvas` = **1000（常驻置顶）**。
- **`TopCanvas` 是顶层界面容器**：常驻 `SetActive(true)`、Sorting Order 恒为 1000，**永不隐藏、永不还原层级**；其下的设置 / 结算 / 改键 / 提示等界面各自开合。新增顶层界面直接挂到它下面，不要再去抬 `mainMenuCanvas.sortingOrder`。
- **HUD 血条下方是「五个技能槽」**：固定 5 格，顺序 = `AbilitySlots.All`（特质→普攻→本我→自我→超我），每格显示该槽装的人设名，空格显示灰色槽位名；数据源是 `CharacterInstance.Loadout`。槽数不要做成可配（`maxTraitSlots` 已删）。手搭时格子可单行（1 个 Text「特质 剑客」）或两行（Caption + Label），不足 5 个由运行时补建。
- **界面切换 = 单场景 + 整块 Canvas 显隐**，不换 Scene。`HideMenu()` 直接关整个主菜单 Canvas（不要只关直接子物体，快照漏项会让全屏背景残留）。
- **遮挡排查口径**：地图与角色是 `BattleMaps` 下的世界空间 `SpriteRenderer`（order 约 -20/-10/-4），由 Main Camera 渲染；所有 UI Canvas 都是 Screen Space - Overlay，**永远画在相机之上**。出现「主菜单正常、开局后战场看不到」时，先找「未隐藏的不透明全屏 Overlay Image」，而不是查流程/生成日志。

## 地图层
- **地图根节点 `Z1..Z6` 代表「25×12 设计坐标系 → 世界」的变换**：`localScale = s`（16:9 下 0.833）、原点在背景左下角。`BG_背景` 与 `Ground_地面25格` 在局部空间按设计单位摆放（BG 中心 (12.5,6)、Ground 中心 (12.5,0.5)、碰撞体 25×1）。运行时 `MapScene.Enter()` 会重算并覆盖，**不要靠拖根节点微调构图**。
- 唯一几何实现是 `MapStageBuilder.TryApply`（`Assets/Scripts/Maps/MapStage.cs`）；`MapScreenFitter` 只是 Inspector 外壳。格宽 `CellSize = 背景世界宽/25`（16:9 下 0.8333），`GameInput.Grid`、`CombatBody/PlayerMotor/AbilityCaster.unitsPerCell` 必须同源。
- **三场 1v1 = 单图切镜**：只把玩家那一场摆进当前展示地图（中轴左右各 3 格、脚底贴地面上沿），其余两场停到地图右边界外；相机由 `CameraFollow.Clamp To Stage` 限制在场地矩形内。
- **角色移动 = 横版地面左右（2026-11-28 策划定稿）**：`CombatBody.lockToGround`（默认开）+ `groundLineY`，求解走 `MotionSolver.MoveGround`（只解算 X，Y 恒等于地面线）；A/D 左右、**W 与 Space 都是跳跃**（`PlayerMotor.moveUpAlsoJumps` 把 W 的按下沿并入跳跃）、**S 无功能**。跳跃只抬 `JumpOffset` 视觉子物体，根节点 Y 不动。落位仍走 `CombatBody.ConfigureArenaRect(min, max, units, groundY)`，地面线 = `stage.GroundSurfaceY + groundClearance`。四向自由移动保留在 `lockToGround=false` → `MoveRect`；圆形 `Move` 只作历史兜底，**不要给落位层用**。详见 `Docs/22`。
- **W 不进 Jump 键位槽**：`InputBindingTable.CollectIssues` 把「一键绑两动作」判为 Error，`GameInput.LoadBindings` 会因此清档回退；所以 W 保持 `MoveUp` 主键，由 `PlayerMotor` 解释为跳跃。
- **Z2 蒸汽井盖**：格子来源是 `Manholes_25格` 容器（可手工搭），`SteamMechanic` 有 `Build Manholes If Missing` 兜底生成 25 个固定 1×1 格；**不要再用 `Ground_地面25格` 的 `GetComponentsInChildren<SpriteRenderer>`**（会把它自身算进去，只得 1 格）。运行时生成的格子不写回场景 YAML。

## 文档
- 一般文字说明 → 生成 **docx**。
- 代码相关说明 → 生成 **markdown**（`Docs/` 目录）。
- 改动 UI 层级 / 显隐 / HUD 版式时，同步更新 `Docs/03_游戏流程与界面.md`。
- 地图几何 / 落位 / 切镜改动 → 同步更新 `Docs/20_地图坐标统一与单图切镜.md`。
- 角色移动口径 / 跳跃键位改动 → 同步更新 `Docs/22_横版地面锁定与跳跃键位.md`。

## 工具链
- 离线编译校验：`dotnet build .check/check.csproj --no-restore -v q --nologo`（`Assembly-CSharp` 全量），看 `0 个错误`。**必须加 `--no-restore`**：本机 Git Bash 缺 `APPDATA`/`ProgramData`，NuGet restore 会报 `Value cannot be null. (Parameter 'path1')`。belltest 同理；`dotnet run` 仍会失败，直接 `dotnet .check/belltest/bin/Debug/net10.0/belltest.dll` 运行。
- 解析 `SampleScene.unity` 时用**纯字符串 split**（不要用 `\s`/`\d` 正则，heredoc 转义会被吞），逐行状态机建 `GameObject / Transform` 表最可靠。
