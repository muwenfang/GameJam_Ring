# 12 · 输入层：`InputIntent` 与鼠标选点（D12）

> 本轮范围：M0 输入层。对应《钟声_架构分析_详版》D3（玩家 / AI 同构）、D12（鼠标选点）、§4.1（`InputIntent` 字段）、§1.2（选点返回格坐标）。
> 前提：**工程未安装新 Input System**，`Packages/manifest.json` 无 `com.unity.inputsystem`，`ProjectSettings.asset` 的 `activeInputHandler: 0`（旧版 Input Manager）。本轮全部走 `UnityEngine.Input`，没有引入任何新包。

---

## 一、检查结论

改造前的输入层只做了「按键 → 动作」，D12 要求的后半条链是断开的：

| # | 缺口 | 影响 |
|---|---|---|
| 1 | 没有 `InputIntent` 类型 | D3「玩家 / AI 同构的唯一入口」无法成立；M2 / M3 / M6 没有统一输入契约 |
| 2 | `GameInput.PointerScreenPosition` 只有屏幕坐标 | 没有相机投影、没有世界坐标、没有格坐标 |
| 3 | 没有「世界 → 格」换算 | 游侠超我「鼠标指定前方 4 格内任一格」拿不到落点格；M5 机关选点也无处复用 |
| 4 | 平A = 左键、闪避 = 右键（Docs/01 键位表） | 若把「确认选点」也绑左键，一次点击会同时触发平A 与选点 |
| 5 | 没有 UI 遮挡判断 | 点 HUD 时会把 HUD 背后的格子当成落点 |

本轮补齐为一条完整链路，并把选点做成**不占用任何按键**的默认交互：

```
UnityEngine.Input.mousePosition（屏幕像素）
      │  Camera.ScreenPointToRay + Plane.Raycast(z = gamePlaneZ)
      ▼
世界 XY（精确浮点坐标）
      │  GridSpace.WorldToCell  →  Math.Floor
      ▼
格坐标 GridCoord(col, row)  +  是否在 25×12 边界内
      │
      ▼
InputIntent.aimPoint（世界 + 格 + 有效性标志）
      │
      ▼
M2 / M3 / M6 同一个入口（AI 直接构造同结构）
```

---

## 二、换算口径（数学）

设 `GridSpace` 的 `CellSize = s`（默认 1，必须与 `PlayerMotor.unitsPerCell`、`AbilityCaster.unitsPerCell` 相同），
格网左下角世界坐标为 `(Ox, Oy)`，列数 `C = 25`、行数 `R = 12`：

```
col = floor((worldX - Ox) / s)
row = floor((worldY - Oy) / s)

格中心世界坐标 = ( Ox + (col + 0.5) * s ,  Oy + (row + 0.5) * s )

界内条件： 0 <= col < C  且  0 <= row < R
```

三个容易踩的点，已写成离线断言：

1. **必须用 `Math.Floor`，不能用 C# 的 `(int)` 强转。** `(int)(-0.5f) == 0`，会让地图左半边整整错一格；`Math.Floor(-0.5) == -1` 才对。
2. **边界外的格子照实返回**，不静默钳制。技能层要自己判「前方 4 格内」的射程；需要兜底时用 `GridSpace.Clamp()`。
3. **屏幕 → 世界用射线与平面求交**，不用 `ScreenToWorldPoint` 硬塞 z 距离。正交与透视相机都成立；相机朝向背面时 `Plane.Raycast` 同样能命中。

---

## 三、文件清单

新增（3 个脚本，均在 `Assets/Scripts/Inputs/`）：

| 文件 | 依赖 | 职责 |
|---|---|---|
| `GridSpace.cs` | 纯 C#（仅 `System` + `Ring.Core`） | `GridCoord`（整数格坐标）、`GridSpace`（世界↔格换算、边界、钳制、格中心、`Sanitize`、`CollectIssues`） |
| `InputIntent.cs` | 纯 C# | `AimMode` 枚举 + `InputIntent`（架构 §4.1 的六个字段；`aimPoint` 拆成世界 XY / 格 / 有效性标志） |
| `InputIntentUnity.cs` | UnityEngine | `Vector2` / `Vector2Int` 的无状态转换扩展，让 Unity 侧好用而不污染纯 C# 那半边 |

修改（4 处）：

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/Inputs/GameInput.cs` | 新增 D12 字段、`Intent` 每帧快照、`BuildIntent()`、`TryProjectToPlane()`、`BlockedByUI()`、`AimPicked` / `IntentUpdated` 事件、选点 API、`CollectIssues` 自检；`[DefaultExecutionOrder(-500)]` 保证消费者读到本帧快照 |
| `Assets/Scripts/Flow/AbilityCaster.cs` | 超我施放时把 `Intent.aimPoint` 记进 `LastAimCell` 并追加到日志（**只记录，不改任何伤害 / 命中数值**） |
| `.check/belltest/belltest.csproj` | 纳入两个新纯 C# 文件 |
| `.check/belltest/Program.cs` | 新增 5 组 D12 / D3 断言 |

> 没有新建或自动生成的场景物体、UI、ScriptableObject 资产。相机引用与格参数全部由用户在 Inspector 手工指派。
> 3 个新 `.cs` 的 `.meta` 交给 Unity 刷新（Ctrl+R）时自动生成，不要手写 GUID。

---

## 四、Inspector 接线（用户手工操作）

`SampleScene` 里已有 `InputSystem` 物体（挂 `GameInput`，见 `Docs/00` 第八节）。选中它，在 `GameInput` 组件上：

| 字段 | 默认值 | 怎么填 |
|---|---|---|
| `World Camera` | 空 | **把场景主相机拖进来**（推荐）。留空则回退 `Camera.main`，此时相机必须带 `MainCamera` 标签；都没有时 `HasValidAim` 恒为 false，自检会报 Warning |
| `Grid` → `Cell Size` | `1` | 与 `PlayerMotor` / `AbilityCaster` 的 `unitsPerCell` 保持一致 |
| `Grid` → `Origin X / Y` | `0 / 0` | 格 `(0,0)` 左下角的世界坐标。地图按擂台中心摆放时填左下角坐标，或运行时调 `SetGridOrigin()` |
| `Grid` → `Columns / Rows` | `25 / 12` | 文档口径；当前 `SampleScene` 是圆形擂台（`Docs/09` 说明其尺度不是 25 格铺满），按实际场地改即可，边界只用于「是否界内」判定 |
| `Game Plane Z` | `0` | 游戏平面所在 Z。2D 俯视保持 0 |
| `Aim Mode` | `Hover` | 见下节 |
| `Aim Click Button` | `2`（中键） | 仅 `Click` 模式生效：`0` 左 / `1` 右 / `2` 中 |
| `Ignore Pointer Over UI` | 开 | 需要场景里有 `EventSystem` 才生效；没有时自检给 Info 提示 |
| `Log Aim Picks` | 关 | 验收时打开，Console 会打印「屏幕 → 世界 → 格」三段；`Hover` 模式仅在跨格时打印，不刷屏 |

**关于 `Aim Mode` 的选择（这是本轮唯一的设计决策）：**

| 模式 | 行为 | 适用 |
|---|---|---|
| `Hover`（默认） | 每帧跟随鼠标，选点**不占用任何按键**；按 `3` 施放超我时取当帧鼠标位置 | 与现有键位表零冲突，改键面板不受影响，推荐直接用 |
| `Click` | 只有按下 `Aim Click Button` 的那一帧才刷新并锁定选点，同时触发 `AimPicked` 事件 | 需要「先按技能、再点位置确认」的两段式玩法时切过去 |

默认中键而非左键，是因为左键已绑平A、右键已绑闪避；若确实要用左键确认，把 `Aim Click Button` 设为 `0`，自检会给出一条 Warning 提示冲突。

> UI 部分一律由用户搭建后拖入 Inspector，本轮代码不创建 Canvas / Image / 高亮格等任何对象。若要做「选点预览高亮」，可在 `AimPicked` 或 `IntentUpdated` 的订阅里读 `AimCell` 自己摆。

---

## 五、API 速查

```csharp
using Ring.Inputs;

GameInput input = GameInput.Instance;

// 1) 本帧统一意图（D3：玩家与 AI 同构的唯一入口）
InputIntent intent = input.Intent;
Vector2 moveDir   = intent.MoveDir();          // InputIntentUnity 扩展
bool     jump     = intent.Jump;
bool     attack   = intent.BasicAttack;
bool     dodge    = intent.Dodge;
int      skillIdx = intent.SkillIndex;          // 0 无 / 1 本我 / 2 自我 / 3 超我
GridCoord cell    = intent.AimCell;             // 选点格
Vector2  world    = intent.AimWorld();          // 选点世界坐标
bool     inGrid   = intent.AimInsideGrid;

// 2) 更稳的取法（无相机 / 射线未命中 / 鼠标压在 UI 上时返回 false）
if (input.TryGetAimCellInGrid(out GridCoord c)) { /* 界内格 */ }
if (input.TryGetAimWorld(out Vector2 w))         { /* 精确世界坐标 */ }

// 3) 复用同一套换算，避免各写一份 floor
GridCoord c2 = input.WorldToCell(someWorldPosition);
Vector2  center = input.CellToWorldCenter(c2);

// 4) 事件
input.IntentUpdated += i => { /* 每帧快照 */ };
input.AimPicked     += c => { /* Click 模式确认了一次选点 */ };

// 5) 运行时配置（换地图 / 换擂台中心 / 切场景相机）
input.SetGridOrigin(new Vector2(-12.5f, -6f));
input.SetGrid(GridSpace.DocumentDefault);
input.SetWorldCamera(mainCamera);
```

`AbilityCaster` 已做最小消费示范：施放超我时把 `input.AimCell` 写进 `LastAimCell` 与日志。
M3 真正实现「缚地荆棘 3×1 地形」时，直接读 `input.TryGetAimCellInGrid(out cell)` 或 `AbilityCaster.LastAimCell` 即可，
再叠加「前方 4 格内 / 朝向」的射程校验——**这一步属于 M3，不在本轮输入层范围内**。

AI 同构示例（M6 可直接构造，M2 / M3 不需要按输入来源分支）：

```csharp
var aiIntent = new InputIntent
{
    MoveX = 1f, MoveY = 0f,
    SkillIndex = 3,
    HasAimPoint = true,
    AimWorldX = 8.5f, AimWorldY = 2.5f,
    AimCell = new GridCoord(8, 2),
    AimInsideGrid = true,
};
```

---

## 六、验收

**离线（不启动 Unity）**

```bash
export APPDATA='C:\Users\Muwen\AppData\Roaming'
export LOCALAPPDATA='C:\Users\Muwen\AppData\Local'
export PROGRAMFILES='C:\Program Files'
export HOMEDRIVE='C:'
export HOMEPATH='\Users\Muwen'
export USERPROFILE='C:\Users\Muwen'

cd D:/unity/GameJam_Ring
dotnet build .check/belltest/belltest.csproj -v q --nologo   # 逻辑回归
dotnet .check/belltest/bin/Debug/net10.0/belltest.dll          # 退出码 0 = 全通过
dotnet build .check/check.csproj -t:Rebuild -v q --nologo     # 引用真实 Unity 程序集的全量类型检查
```

本轮实测结果：

- 逻辑回归：**通过 527 条，失败 0 条**，退出码 0；其中新增 5 组 D12 用例覆盖正/负坐标取整、格线、边界、钳制、25×12 全 300 格中心往返、原点平移、自定义格距、配置自检、`InputIntent` 合法/非法自检。
- 全量类型检查：**0 个错误**（2 条 `CS0169` 警告来自既有 `ClockMechanic` / `ForestFireflies`，与本次改动无关）。

> Bash 里跑 `dotnet` 必须先 export 上面几个 Windows 环境变量，否则 NuGet 还原会报
> `Value cannot be null. (Parameter 'path1')`；这是环境问题，不是代码问题。

**Unity 内（离线测不到的部分，必须实测）**

1. 进 Play，把 `Log Aim Picks` 打开，移动鼠标，Console 应打印 `[输入/D12] 屏幕(...) → 世界(...) → 格(x, y)`。
2. 按 `3` 施放超我，日志末尾应追加 `，选点 格(x, y)（世界 (..., ...)）`；把鼠标移到 HUD 上时应显示「选点无效」。
3. 菜单「钟声 / 校验 / 校验全部数据」会一并跑 `GridSpace.CollectIssues` 与相机 / EventSystem / 左键冲突的检查。
4. 若 `World Camera` 留空且场景相机没有 `MainCamera` 标签，选点恒为无效——自检会报 Warning。

**离线校验的能力边界**：`MonoBehaviour` 生命周期、`Camera.ScreenPointToRay` 的实际投影结果、
`EventSystem.IsPointerOverGameObject()`、以及 Inspector 序列化字段的连线，离线工程都验证不了，必须回 Unity 实测。
本轮只保证「类型可编译 + 纯换算逻辑正确」。

---

## 七、已知约束与未做部分

| 项目 | 状态 |
|---|---|
| 新 Input System | **未装、未用**。全部 `UnityEngine.Input`。将来换包时只需替换 `GameInput.Poll()` 与 `BuildIntent()` 两处轮询，映射表、`InputIntent`、`GridSpace` 均不用动 |
| `activeInputHandler` | 当前为 `0`（旧版）。若改成 `1`（仅新系统），旧版 `Input.*` 会抛异常，必须先迁移轮询 |
| 选点与键位冲突 | 默认 `Hover` 不占键；`Click` 默认中键。左键确认会与平A 同时触发，自检会警告 |
| 25×12 边界 | 文档口径的默认配置；当前 `SampleScene` 是圆形擂台（`Docs/09`），`Origin` / `Columns` / `Rows` 需按实际摆放调整，否则 `AimInsideGrid` 可能大量为 false |
| `Game Plane Z` | 2D 俯视固定 0；若改成带 Z 层次的场景，需按目标层调整 |
| UI 遮挡 | 依赖场景里的 `EventSystem`（uGUI）。没有时 `Ignore Pointer Over UI` 静默失效，自检给 Info |
| 执行顺序 | `GameInput` 加了 `[DefaultExecutionOrder(-500)]`，无需在 Project Settings 里手工设置 |
| M3 选点技能 | 「缚地荆棘」的 3×1 地形、前方 4 格射程与朝向校验**尚未实现**；本轮只把落点格送到 `AbilityCaster.LastAimCell` 与日志 |
| M6 AI | 只有同构的数据结构，AI 行为决策器本身仍未实现（与 `Docs/00` 第五节「人机不会主动走位」一致） |
