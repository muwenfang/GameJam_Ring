# -*- coding: utf-8 -*-
"""生成《场景组件挂载清单与接线说明》docx"""
import os
from docx import Document
from docx.shared import Pt, Cm, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.oxml.ns import qn
from docx.oxml import OxmlElement

CN = "微软雅黑"
CODE = "Consolas"
ACCENT = RGBColor(0x1F, 0x4E, 0x79)
GREY = RGBColor(0x59, 0x59, 0x59)

doc = Document()

# ---------- 全局样式 ----------
style = doc.styles["Normal"]
style.font.name = CN
style.font.size = Pt(10.5)
style.element.rPr.rFonts.set(qn("w:eastAsia"), CN)
style.paragraph_format.space_after = Pt(6)
style.paragraph_format.line_spacing = 1.25

for sec in doc.sections:
    sec.top_margin = Cm(2.2)
    sec.bottom_margin = Cm(2.2)
    sec.left_margin = Cm(2.4)
    sec.right_margin = Cm(2.4)


def shade(cell, hexcolor):
    tcPr = cell._tc.get_or_add_tcPr()
    shd = OxmlElement("w:shd")
    shd.set(qn("w:val"), "clear")
    shd.set(qn("w:color"), "auto")
    shd.set(qn("w:fill"), hexcolor)
    tcPr.append(shd)


def title(text, sub=""):
    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.LEFT
    r = p.add_run(text)
    r.font.size = Pt(20)
    r.font.bold = True
    r.font.color.rgb = ACCENT
    r.font.name = CN
    r._element.rPr.rFonts.set(qn("w:eastAsia"), CN)
    if sub:
        p2 = doc.add_paragraph()
        r2 = p2.add_run(sub)
        r2.font.size = Pt(9.5)
        r2.font.color.rgb = GREY
        r2.font.name = CN
        r2._element.rPr.rFonts.set(qn("w:eastAsia"), CN)
    hr = doc.add_paragraph()
    hr.paragraph_format.space_before = Pt(0)
    hr.paragraph_format.space_after = Pt(10)
    pPr = hr._p.get_or_add_pPr()
    pbdr = OxmlElement("w:pBdr")
    bottom = OxmlElement("w:bottom")
    bottom.set(qn("w:val"), "single")
    bottom.set(qn("w:sz"), "10")
    bottom.set(qn("w:color"), "1F4E79")
    pbdr.append(bottom)
    pPr.append(pbdr)


def h1(text):
    p = doc.add_paragraph()
    p.paragraph_format.space_before = Pt(16)
    p.paragraph_format.space_after = Pt(6)
    r = p.add_run(text)
    r.font.size = Pt(14)
    r.font.bold = True
    r.font.color.rgb = ACCENT
    r.font.name = CN
    r._element.rPr.rFonts.set(qn("w:eastAsia"), CN)


def h2(text):
    p = doc.add_paragraph()
    p.paragraph_format.space_before = Pt(10)
    p.paragraph_format.space_after = Pt(4)
    r = p.add_run(text)
    r.font.size = Pt(11.5)
    r.font.bold = True
    r.font.color.rgb = RGBColor(0x2E, 0x2E, 0x2E)
    r.font.name = CN
    r._element.rPr.rFonts.set(qn("w:eastAsia"), CN)


def para(text, bold_prefix=None, indent=0):
    p = doc.add_paragraph()
    if indent:
        p.paragraph_format.left_indent = Cm(indent)
    if bold_prefix:
        r = p.add_run(bold_prefix)
        r.font.bold = True
        r.font.name = CN
        r._element.rPr.rFonts.set(qn("w:eastAsia"), CN)
    r2 = p.add_run(text)
    r2.font.name = CN
    r2._element.rPr.rFonts.set(qn("w:eastAsia"), CN)
    return p


def code_para(text, indent=0.6):
    p = doc.add_paragraph()
    p.paragraph_format.left_indent = Cm(indent)
    p.paragraph_format.space_after = Pt(3)
    r = p.add_run(text)
    r.font.name = CODE
    r.font.size = Pt(9)
    r._element.rPr.rFonts.set(qn("w:eastAsia"), CODE)
    return p


def bullet(text, level=0):
    p = doc.add_paragraph(style="List Bullet")
    p.paragraph_format.left_indent = Cm(0.75 + level * 0.6)
    p.paragraph_format.space_after = Pt(2)
    r = p.add_run(text)
    r.font.name = CN
    r.font.size = Pt(10.5)
    r._element.rPr.rFonts.set(qn("w:eastAsia"), CN)
    return p


def numbered(text, level=0):
    p = doc.add_paragraph(style="List Number")
    p.paragraph_format.left_indent = Cm(0.75 + level * 0.6)
    p.paragraph_format.space_after = Pt(2)
    r = p.add_run(text)
    r.font.name = CN
    r.font.size = Pt(10.5)
    r._element.rPr.rFonts.set(qn("w:eastAsia"), CN)
    return p


def table(headers, rows, widths=None, small=True):
    t = doc.add_table(rows=1, cols=len(headers))
    t.style = "Table Grid"
    t.alignment = WD_TABLE_ALIGNMENT.CENTER
    hdr = t.rows[0].cells
    for i, h in enumerate(headers):
        hdr[i].text = ""
        p = hdr[i].paragraphs[0]
        r = p.add_run(h)
        r.font.bold = True
        r.font.size = Pt(9)
        r.font.color.rgb = RGBColor(0xFF, 0xFF, 0xFF)
        r.font.name = CN
        r._element.rPr.rFonts.set(qn("w:eastAsia"), CN)
        shade(hdr[i], "1F4E79")
    for row in rows:
        cells = t.add_row().cells
        for i, val in enumerate(row):
            cells[i].text = ""
            p = cells[i].paragraphs[0]
            p.paragraph_format.space_after = Pt(0)
            mono = val.startswith("`") and val.endswith("`")
            txt = val.strip("`")
            r = p.add_run(txt)
            r.font.size = Pt(9 if not small else 8.5)
            r.font.name = CODE if mono else CN
            if mono:
                r.font.color.rgb = RGBColor(0x8B, 0x1A, 0x1A)
            else:
                r._element.rPr.rFonts.set(qn("w:eastAsia"), CN)
    if widths:
        for i, w in enumerate(widths):
            for row in t.rows:
                row.cells[i].width = Cm(w)
    return t


# ================================================================
title("场景组件挂载清单与接线说明",
      "GameJam_Ring · SampleScene · 基于 2026-11-26 的工程现状审计（Assets/Scripts 与 MapKit 共 87 个 .cs）")

h1("结论摘要")
para("项目里需要挂到物体上的只有 25 个 MonoBehaviour，可分成两类：挂在空物体上的「管理器 / 服务」共 7 个，"
     "必须挂在特定类型物体上的共 8 项。其余 60 余个脚本是纯 C# 规则、ScriptableObject 数据资产或编辑器工具，一律不需要挂。")
para("SampleScene 现状：9 个组件已挂在 7 个物体上，主流程（开局 → 三场 1v1 → 钟声 → 胜负 → 重开）可以跑通；"
     "缺的是「地图层」和「场景 HUD」两块，共 4 类。")
para("修订（2026-11-26 21:15）：第五节的三个问题已直接改到 SampleScene —— 残留物体已停用、B14 开关已打开、"
     "六个区域锚点已建好并填进 MatchSpawner；另外 BattleMapPresenter 已挂上并被 GameUiController 引用。"
     "地图层本身的搭建步骤见另一份文档《地图层搭建步骤》。")
para("另有三处需要你先拍板或清掉的问题：一个残留的 PlayerMotor 物体、B14 胜利口径在场景里被关掉、"
     "以及六区域计算落点与地图尺寸不匹配。详见第五节与第七节。")

# ---------------- 一 ----------------
h1("一、脚本三分类总览")

h2("A 类：需要挂在「空物体」上（管理器 / 服务，共 7 个）")
table(
    ["脚本（类名）", "建议物体名", "当前场景", "作用"],
    [
        ["`GameInput`", "InputSystem", "已挂", "输入轮询、改键、鼠标选点 D12"],
        ["`BellSystem`", "BellSystem", "已挂", "钟声系统 Unity 外壳，驱动 30 秒计时"],
        ["`GameFlow`", "GameFlow", "已挂", "流程总控：开局、判胜负、切面板、重开局"],
        ["`MatchSpawner`", "MatchState", "已挂", "生成六人并落位到三场 1v1"],
        ["`MatchDirector`", "MatchState", "已挂", "演示推进器（补牌 / 处决），接正式战斗后关掉"],
        ["`GameUiController`", "GameUIController", "已挂", "主菜单 / 战斗画布 / HUD / 结算切换"],
        ["`BattleMapPresenter`", "BattleMapPresenter", "已挂（槽位待填）", "六张地图六选一显隐"],
    ],
    widths=[4.2, 3.6, 1.9, 6.5])

h2("B 类：必须挂在「特定物体」上（共 8 项）")
table(
    ["脚本（类名）", "必须挂在什么物体上", "谁负责挂", "当前场景"],
    [
        ["`CameraFollow`", "Main Camera", "手工", "已挂"],
        ["`CharacterInstance`", "角色根物体（脚底原点）", "MatchSpawner 运行时生成", "无需手挂"],
        ["`PlayerMotor`", "玩家角色根物体 + CharacterInstance 同物体", "MatchSpawner 运行时 AddComponent", "无需手挂"],
        ["`AbilityCaster`", "玩家角色根物体 + CharacterInstance 同物体", "MatchSpawner 运行时 AddComponent", "无需手挂"],
        ["`SwordsmanSpriteAnimation`", "角色下的 Visual 子物体 + SpriteRenderer 同物体", "CharacterInstance 运行时 AddComponent", "无需手挂"],
        ["`BattleHud`", "Canvas 下的 HUD 根物体（唯一的 HUD 根）", "手工 / 菜单生成", "缺"],
        ["`MapScene` ×6", "每张地图的根节点各一个", "菜单生成", "缺"],
        ["六区机制 + 森林脚本", "对应区域根节点或其子物体", "手工", "缺"],
    ],
    widths=[4.2, 5.4, 4.0, 2.6])

para("说明：A 类与 B 类不是互斥的。GameFlow / MatchSpawner 这类管理器在文档里习惯叫「挂在空物体上」，"
     "本质上也是挂在普通 GameObject 上；区别只在于它们不依赖任何特定的 Transform 位置或同物体组件。")

h2("C 类：不需要挂载（60 个）")
bullet("纯 C# 规则核心：MatchFlow、MatchState、ArenaPlan、BellRuntime、BellRules、BellParticipant、"
       "三个 IBellEffect 实现、AbilityRuntime、AbilityCatalog、AbilityDefinition、InputIntent、GridSpace 等。")
bullet("ScriptableObject 数据资产：CharacterBaseStats、CharacterRoster、BellSettings、InputSettings、"
       "AbilityTuningAsset。它们放进 Inspector 的是「资产引用」，不是组件。")
bullet("静态工具类：UiBuilder、BattleHudBuilder、HudSprites、BattleMaps / BattleMapId。")
bullet("编辑器工具（走菜单栏「钟声 / …」与「Tools / MapKit / …」）：RingTools、HudSetupTools、"
       "MapSceneSetupTools、AbilityTuningTools、InputRebindWindow、MapKit 的四个 Editor 脚本。")
bullet("抽象基类 ZoneMechanicBase 不能挂，只能挂它的六个子类（架构上是 MonoBehaviour，但 Unity 不允许挂 abstract）。")

# ---------------- 二 ----------------
doc.add_page_break()
h1("二、A 类逐项：空物体与管理器")

def a_block(name, objname, status, fields, notes):
    h2(name + "  →  " + objname)
    para(status, bold_prefix="当前状态：")
    para("", bold_prefix="必须填的 Inspector 字段：")
    for f in fields:
        bullet(f, level=1)
    if notes:
        para("", bold_prefix="注意：")
        for n in notes:
            bullet(n, level=1)


a_block("GameInput", "InputSystem（场景已有同名物体）", "已挂，字段齐全。",
        ["Input Settings → Assets/Data/Inputs 下的 InputSettings 资产（已在场景里赋值）。",
         "World Camera → 场景主相机（已赋值；留空时会退回 Camera.main，需要相机带 MainCamera 标签）。",
         "Grid → Cell Size 必须和 PlayerMotor / AbilityCaster 的 Units Per Cell 一致（默认都是 1）。"],
        ["组件带 [DefaultExecutionOrder(-500)]，会早于所有消费者执行，不要改。",
         "Persist Across Scenes 打开时会自动 DontDestroyOnLoad，因此这个物体不能有父级，否则跨场景保留失效。"])

a_block("BellSystem", "BellSystem（场景已有同名物体）", "已挂，字段齐全。",
        ["Settings → Assets/Data/Bells/BellSettings.asset（已赋值）。",
         "Auto Update 勾上，否则 30 秒计时不会自己走。"],
        ["单例：场景里出现第二个会被自动 Destroy，并打警告。"])

a_block("GameFlow", "GameFlow（场景已有同名物体）", "已挂；Spawner 为空但会 FindObjectOfType 自动找 MatchSpawner。",
        ["Ability Tuning → Assets/Data/Abilities/AbilityTuning.asset（已赋值）。",
         "Spawner 留空即可（自动找）；Bell System 已赋值。",
         "Require Complete Set For Victory → 这是 B14 严格胜利口径的开关，代码默认 true，场景里当前是 false。",
         "Battle Count = 3，Randomize Map = 开，Fixed Map 用于单图调试。"],
        ["单例。[DisallowMultipleComponent]。",
         "该物体不要设父级：重开局时会调用 InitializeGame 清场，挂在子物体上容易跟 UI 一起被隐藏。"])

a_block("MatchSpawner", "MatchState（场景已有同名物体）", "已挂；Roster 已赋值，Region Anchors 六个槽位全为空。",
        ["Roster → Assets/Data/Characters/CharacterRoster.asset（已赋值）。",
         "Player Persona → 玩家用哪个人设（当前 Swordsman）。",
         "Region Anchors[0..5] → 可选。留空时按 Arena Spacing 14 / Duel Offset 2.2 算 2 行 3 列布局（见第七节问题 3）。",
         "Attach Player Motor 勾上，玩家会自动获得 PlayerMotor + AbilityCaster。"],
        ["角色是运行时 new GameObject 生成的，不会在 Hierarchy 里预先存在。"])

a_block("MatchDirector", "MatchState（与 MatchSpawner 同一物体）", "已挂；GameFlow / BellSystem 都已连。",
        ["无需额外引用，两个字段已分别指向 GameFlow 与 BellSystem。",
         "Auto Start On Play 当前关闭，需要自动开局时再勾。"],
        ["纯演示推进：收集阶段每 8 秒给玩家补牌，淘汰阶段每 5 秒处决一个人机；K 键处决玩家、L 键处决人机。",
         "接正式 AI / 战斗后把组件 enabled 关掉即可，流程与 UI 都不依赖它。"])

a_block("GameUiController", "GameUIController（场景已有同名物体）", "已挂；主菜单与战斗画布引用全部连好。",
        ["Main Menu Canvas / Setting Panel / Start / Exit / Full Screen / Volume Slider → 已连。",
         "Battle Canvas → 已连（场景里的 BattleGround，兼容旧名）。",
         "Battle Map Presenter、Battle Hud、Map Name Label、Map Status Label → 当前全为空。",
         "Auto Build Ui 打开：HUD / 结算面板找不到时会运行时兜底搭一套。"],
        ["空着的四个字段不是错误：Presenter 缺失会打一次警告，HUD 缺失会运行时兜底，两个 Label 缺失则地图名不显示。",
         "主菜单靠物体名匹配（MainMenu / SettingPanel / Start / Setting / Exit / FullScreen / CloseSetting / AudioSlider），改名要同步改字段。"])

a_block("BattleMapPresenter", "建议新建空物体 BattleMapPresenter（或挂到 battleCanvas 上）",
        "缺，场景里没有这个组件。",
        ["Scenes[0..5] → 六个地图根节点，顺序对应 Map01..Map06。",
         "Log Maps 建议打开，方便确认每次切图。"],
        ["六个槽位可以全留空：运行时会优雅降级并打一次性警告，不会崩。",
         "若六个 MapScene 的 Map Id 恰好构成 0..5 的唯一排列，拖错顺序也会被自动校正。"])

# ---------------- 三 ----------------
doc.add_page_break()
h1("三、B 类逐项：必须挂在特定物体上")

h2("CameraFollow  →  Main Camera")
para("已挂。Target 留空 + Auto Bind Player 打开时会自动绑 GameFlow.Player，推荐保持留空。"
     "Smooth Time 0.15、Offset 0、Follow X/Y 全开；Snap On Arenas Changed 打开后，位置钟声换擂台时相机会直接对齐而不滑过去。")

h2("CharacterInstance  →  角色根物体")
para("由 MatchSpawner 在运行时 new GameObject 生成并 AddComponent，不预先存在于场景。"
     "它的 transform 原点就是脚底（配合 SwordsmanSpriteAnimation 的脚底锚定），所以不要给角色根物体加 Y 偏移。")

h2("PlayerMotor + AbilityCaster  →  玩家角色根物体")
para("两个组件都带 [RequireComponent(typeof(CharacterInstance))]，必须和 CharacterInstance 挂在同一个物体上。"
     "正常流程下由 MatchSpawner（Attach Player Motor = 开）在生成玩家时自动 AddComponent，不需要手挂。"
     "AbilityCaster 的按键是 1/2/3 放本我/自我/超我、鼠标左键平A；PlayerMotor 读 GameInput 的移动轴，右键闪避、空格跳跃。")

h2("SwordsmanSpriteAnimation  →  角色下的 Visual 子物体")
para("CharacterInstance.BuildVisual() 会为剑士人设自动创建 Visual 子物体、加 SpriteRenderer，再把本组件 AddComponent 上去。"
     "它带 [DisallowMultipleComponent]，同一个物体只能有一份；SpriteRenderer 留空时自动取本物体或子物体上的那个。"
     "其余五个人设目前仍是纯色方块占位，不走这个组件。")

h2("BattleHud  →  Canvas 下的 HUD 根物体（当前缺）")
para("本组件挂在 HUD 的根物体上（它的子物体才是血条、名字、特性框），所以物体本身应当是 Canvas 的子物体、"
     "且一个场景只应该有一套——GameUiController 会把它整体 SetActive 开关。它有 21 个引用槽位：")
code_para("PlayerName / PlayerBar / PlayerBarBorder / PlayerBarFill / PlayerBarValue")
code_para("PlayerTraitBox / PlayerTraitRow / PlayerTraitEmpty")
code_para("OpponentName / OpponentBar / OpponentBarBorder / OpponentBarFill / OpponentBarValue")
code_para("OpponentTraitBox / OpponentTraitRow / OpponentTraitEmpty")
code_para("BellBadge / BellBadgeBorder / BellValue / RoundValue / Detail")
para("全部留空也能跑：BattleHud 会按上述名字在子物体里递归查找。所以自己手搭 HUD 时，"
     "只要子物体按这套名字命名，就不必逐个拖引用。圆形 / 虚线贴图是运行时按像素尺寸生成的，"
     "自己给 Image 指定了 Sprite 则不会被覆盖。")

h2("MapScene ×6  →  每张地图的根节点各一个（当前缺）")
para("MapScene 是「一整棵场景层级」的根组件，不是 Image。需要填的字段：")
bullet("Map Id → 必须与槽位顺序一致（Map01..Map06），菜单生成时会自动写入。", 1)
bullet("Region Anchors[0..5] → 可选，供出生点与位置钟声消费，现在可以全空。", 1)
bullet("Zone Mechanic → 可选，只用于 HUD 读 StatusText()，不拖不影响机制运行。", 1)
bullet("Ground Root / Ground Collider → 碰撞地面，留空时自动从 Ground Root 上找 Collider2D。", 1)

h2("六区机制脚本  →  对应区域根节点或其子物体（当前缺）")
table(
    ["区域", "要挂的机制脚本", "额外依赖"],
    [
        ["Map01 Z1 雨夜竹林", "`BambooNightMechanic`", "无（雷雨随机落雷）"],
        ["Map02 Z2 蒸汽井盖", "`SteamMechanic`", "依赖子节点名 Ground_地面25格，取其下 SpriteRenderer[] 当井盖格"],
        ["Map03 Z3 竞技场", "`ArenaMechanic`", "无（每 15 秒叠 15% 攻击）"],
        ["Map04 Z4 精灵森林", "`ForestMechanic`", "另有可选表现脚本见下"],
        ["Map05 Z5 钟塔", "`ClockMechanic`", "依赖子节点 PROP_时针 / PROP_分针；需指定 iconRewind / iconHaste / iconSlow"],
        ["Map06 Z6 洞窟", "`CaveMechanic`", "无（每 8 秒落石）"],
    ],
    widths=[3.8, 4.2, 8.2])
para("Map04 精灵森林的两个可选表现脚本（不挂也能跑，挂了只是好看）：")
bullet("ForestLeafSway → 挂在有 SpriteRenderer 的物体上（[RequireComponent(SpriteRenderer)]，一般就是 BG_背景）。"
       "需要指定 Leaf Atlas（elf_forest_leaf_atlas.png）和 Clump Data（elf_forest_leaf_clumps.json）。", 1)
bullet("ForestFireflies → 同样需要 SpriteRenderer 同物体。可调数量、大小、冷色比例、排序偏移。", 1)
para("注意：ZoneMechanicBase 是 abstract，Unity 不允许挂，只能挂上面六个子类。"
     "所有机制都是纯视觉演示，日志前缀 [机制验证]，不含角色 / 战斗耦合。")

# ---------------- 四 ----------------
doc.add_page_break()
h1("四、当前场景审计结果")

h2("已挂载（9 个组件 / 7 个物体）")
table(
    ["GameObject", "挂着的组件"],
    [
        ["InputSystem", "`GameInput`"],
        ["BellSystem", "`BellSystem`"],
        ["GameFlow", "`GameFlow`"],
        ["MatchState", "`MatchSpawner`、`MatchDirector`"],
        ["GameUIController", "`GameUiController`"],
        ["Main Camera", "`CameraFollow`"],
        ["PlayerMotor（残留物体）", "`CharacterInstance`、`PlayerMotor`"],
    ],
    widths=[6.0, 10.2])

h2("缺失（4 类）")
table(
    ["缺失项", "影响", "补齐方式"],
    [
        ["`BattleMapPresenter` 的六个槽位", "已挂组件但槽位全空：每次开局警告「该槽位还没有地图实体」，地图不展示，流程照跑", "执行《地图层搭建步骤》后自动填好"],
        ["`BattleHud`", "无影响：GameUiController 会运行时兜底搭一套（不能在 Hierarchy 里调）", "菜单生成后手改，或完全手搭"],
        ["`MapScene` ×6 + 六区机制", "开局按区域抽图逻辑照跑，但看不到任何地图与地面", "菜单生成 + 手挂机制"],
    ],
    widths=[4.4, 7.0, 4.8])

# ---------------- 五 ----------------
h1("五、三处问题的处理记录（已代改）")

h2("问题 1：残留的「PlayerMotor」物体")
para("场景根下有一个名为 PlayerMotor 的激活物体，挂着 CharacterInstance + PlayerMotor，"
     "但 CharacterInstance 的 participantId = 0、isPlayer = false，且从未调用 Initialize()。后果有三个：")
bullet("它会读 GameInput 的移动轴跟着 WASD 一起动，但没有任何视觉，属于看不见的幽灵物体。", 1)
bullet("它的 participantId 与真正生成的剑士（id 0）冲突，又没注册到钟声系统，"
       "在 AbilityCaster 的 FindObjectsOfType<CharacterInstance>() 里会被当成合法攻击目标。", 1)
bullet("PlayerMotor 上的 Arena Center 永远停在 (0,0)，三场 1v1 后圆形边界也是错的。", 1)
para("该物体的 m_IsActive 已置为 0（Inspector 里是未勾选状态），组件仍然保留、随时可以重新启用。真正的玩家角色由 MatchSpawner 生成，不依赖它。", bold_prefix="处理：")

h2("问题 2：B14 胜利口径在场景里被关掉了")
para("GameFlow 的 Require Complete Set For Victory 当前序列化值为 false，而代码默认值与文档 B14 都是 true。"
     "关掉后胜利条件退化为「活到最后即胜」，不再要求集齐完整特质与技能。"
     "已处理：改回 1。若之后有意放宽，把勾去掉并同步 Docs/10 第六节的口径。")

h2("问题 3：六区域计算落点与地图尺寸不匹配")
para("这是接地图时一定会踩到的坑。当前两套坐标口径不一致：")
bullet("MatchSpawner 没拖 Region Anchors 时按 2 行 3 列算：中心距 14，双方偏移 2.2，"
       "于是三场对局分别落在 x = ±14、0，y = ±4.2 附近。", 1)
bullet("地图工具（MapSceneSetupTools）把六张地图根节点全部摆在原点，背景宽 18、高 10，"
       "即 x ∈ [-9, 9]、y ∈ [-5, 5]；地面只在 y = -3.5 一层。", 1)
para("结果：玩家被分配到 Map03（x = 14 那场）时，会被摆到背景之外，相机跟过去只能看到空白，脚下也没有地面。"
     "两种改法，任选一种：")
bullet("改法 A（推荐）：在场景里放 6 个空物体，全部摆进「一张地图的可见范围内」（比如 x ∈ [-7, 7]、y ∈ [-3, 4]），"
       "再依次拖进 MatchSpawner 的 Region Anchors[0..5]。这样不管抽到哪张图，三场 1v1 都落在图内，切图时角色位置不变。", 1)
bullet("改法 B：把 Arena Spacing 和 Duel Offset 调小（例如 6 / 2），让计算布局自然收进 ±9 的范围，"
       "代价是三场擂台会挤在一起、同屏重叠。", 1)

# ---------------- 六 ----------------
doc.add_page_break()
h1("六、剩下的做法（按顺序执行）")

para("下面这些都需要在 Unity 编辑器里点操作，脚本侧能做的自动化已经放在菜单里了。执行前请先保存场景"
     "（地图生成工具会拒绝在未保存的场景上工作）。")

h2("步骤 1 · 生成六张地图与地面")
para("菜单：钟声 / 地图 / 生成六张地图与碰撞地面", bold_prefix="")
para("会在场景里建一个 BattleMaps 容器，内含六个根节点（Z1_雨夜竹林 … Z6_洞窟），"
     "每个根节点挂好 MapScene、一个 BG_背景 子物体（SpriteRenderer）和一个 Ground_地面25格 子物体"
     "（SpriteRenderer + BoxCollider2D），并在场景里补一个 BattleMapPresenter 自动连好六个槽位与 GameUiController。", indent=0.6)
para("如果场景里已经有 BattleMaps，弹窗会让你选「重新生成」还是「只补连线」——只补连线不会丢手工调整。", indent=0.6)

h2("步骤 2 · 给每个区域挂机制")
para("按第三节的对照表，把六个 XXXMechanic 拖到对应根节点上。两个对层级名有依赖的注意一下：")
bullet("SteamMechanic 找子节点 Ground_地面25格，取其下所有 SpriteRenderer 当 25 个井盖格——"
       "如果地面子物体的名字被改了，就手动把井盖格数组填进去。", 1)
bullet("ClockMechanic 找 PROP_时针 / PROP_分针：Z5 钟塔根节点下要有这两个子物体，否则指针不转。", 1)
para("Z4 精灵森林另外可选挂 ForestLeafSway（BG_背景 上）与 ForestFireflies，赋值 leafAtlas 和 clumpData。", indent=0.6)

h2("步骤 3 · 把机制拖进 MapScene 的 Zone Mechanic（可选）")
para("每个 MapScene 的 Zone Mechanic 槽位拖对应机制组件。不拖机制照常跑，只是 HUD 上的「机制：」会一直显示「待命」。")

h2("步骤 4 · 校对 BattleMapPresenter 的六个槽位")
para("菜单生成时会自动连好。手工搭的话：Scenes[0..5] 依次是 Map01..Map06，"
     "对应 Z1_雨夜竹林 / Z2_蒸汽井盖 / Z3_竞技场 / Z4_精灵森林 / Z5_钟塔 / Z6_洞窟。"
     "六个 MapScene 的 Map Id 若是 0..5 的唯一排列，顺序拖错也会自动校正。")

h2("步骤 5 · 战斗 HUD（二选一）")
bullet("省事：菜单「钟声 / 界面 / 生成战斗 HUD（场景）」生成一套真实物体，引用已自动填好，之后在 Hierarchy 里随便挪、"
       "在 Inspector 里改字色字号。已有 BattleHud 时工具会拒绝重复生成。", 1)
bullet("全手工：自己搭 HUD 结构，把 BattleHud 挂在 HUD 根物体上，子物体按第三节的 21 个约定名命名，引用全部留空即可。"
       "最后把 HUD 根物体拖进 GameUiController 的 Battle Hud 槽位（不拖也会被 FindObjectOfType 找到）。", 1)

h2("步骤 6 · 清理残留物体")
para("停用或删除场景根下的 PlayerMotor 物体（见第五节问题 1）。")

h2("步骤 7 · 跑校验菜单")
table(
    ["菜单", "检查内容"],
    [
        ["钟声 / 校验 / 校验全部数据", "钟声规则、角色名单、技能总表、输入映射"],
        ["钟声 / 界面 / 校验 HUD 接线", "BattleHud 的 21 个引用是否齐全"],
        ["钟声 / 地图 / 校验地图接线", "六个 MapScene、槽位顺序、地面与 Presenter 引用"],
        ["钟声 / 场景 / 校验主菜单界面", "主菜单 Canvas、按钮、设置面板"],
    ],
    widths=[6.4, 9.8])
para("校验输出是逐条列出的问题清单，红字（Error）必须修，黄字（Warn）可以按需忽略。")

# ---------------- 七 ----------------
h1("七、最小可运行集合")
para("如果只想尽快跑通一局、暂时不要地图和 HUD，当前的场景已经够了。例外是那个残留的 PlayerMotor 物体建议先停用。"
     "完整可运行（含地图展示与独立 HUD）需要补完第六节的全部七步。")
table(
    ["目标", "必须补的东西"],
    [
        ["能开局、三场 1v1、钟声、胜负、重开", "无需补充（现状即可）"],
        ["+ 地图背景与地面", "菜单生成六张地图 + BattleMapPresenter"],
        ["+ HUD 可在编辑器里调", "菜单生成战斗 HUD"],
        ["+ 区域机制演出", "六区机制脚本 + ClockMechanic 的指针子节点 + SteamMechanic 的井盖格"],
        ["+ 角色落在地图内", "6 个 Region Anchors（第五节问题 3 改法 A）"],
    ],
    widths=[8.2, 8.0])

doc.save(r"D:\unity\GameJam_Ring\output\场景组件挂载清单与接线说明.docx")
print("OK")
