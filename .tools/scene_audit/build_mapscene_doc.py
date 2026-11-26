# -*- coding: utf-8 -*-
"""生成《地图层搭建步骤（MapScene ×6）》docx"""
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
st = doc.styles["Normal"]
st.font.name = CN
st.font.size = Pt(10.5)
st.element.rPr.rFonts.set(qn("w:eastAsia"), CN)
st.paragraph_format.space_after = Pt(6)
st.paragraph_format.line_spacing = 1.25

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
    b = OxmlElement("w:bottom")
    b.set(qn("w:val"), "single")
    b.set(qn("w:sz"), "10")
    b.set(qn("w:color"), "1F4E79")
    pbdr.append(b)
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
    p.paragraph_format.space_after = Pt(2)
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


def table(headers, rows, widths=None, fs=8.5):
    t = doc.add_table(rows=1, cols=len(headers))
    t.style = "Table Grid"
    t.alignment = WD_TABLE_ALIGNMENT.CENTER
    for i, h in enumerate(headers):
        c = t.rows[0].cells[i]
        c.text = ""
        p = c.paragraphs[0]
        r = p.add_run(h)
        r.font.bold = True
        r.font.size = Pt(9)
        r.font.color.rgb = RGBColor(0xFF, 0xFF, 0xFF)
        r.font.name = CN
        r._element.rPr.rFonts.set(qn("w:eastAsia"), CN)
        shade(c, "1F4E79")
    for row in rows:
        cells = t.add_row().cells
        for i, val in enumerate(row):
            cells[i].text = ""
            p = cells[i].paragraphs[0]
            p.paragraph_format.space_after = Pt(0)
            mono = val.startswith("`") and val.endswith("`")
            r = p.add_run(val.strip("`"))
            r.font.size = Pt(fs)
            if mono:
                r.font.name = CODE
                r.font.color.rgb = RGBColor(0x8B, 0x1A, 0x1A)
            else:
                r.font.name = CN
                r._element.rPr.rFonts.set(qn("w:eastAsia"), CN)
    if widths:
        for i, w in enumerate(widths):
            for row in t.rows:
                row.cells[i].width = Cm(w)
    return t


# ================================================================
title("地图层搭建步骤 · MapScene × 6",
      "GameJam_Ring · SampleScene · 基于 2026-11-26 现状")

h1("零、开始前的现状")
table(
    ["对象", "状态"],
    [
        ["`BattleMapPresenter`", "场景里已存在空物体（名为 BattleMapPresenter），六个槽位为空；GameUiController 已指向它"],
        ["`MapScene` ×6", "**完全没有**，这是本文要补的部分"],
        ["`MatchSpawner.Region Anchors[0..5]`", "已填 6 个锚点（ArenaAnchor_Map01..06，3 列 2 行，均在单张地图可见范围内）"],
        ["六区机制脚本", "未挂"],
        ["`BattleHud`", "未挂（GameUiController 会运行时兜底，与本文无关）"],
    ],
    widths=[5.4, 10.8])

para("下面每一步都可以单独回退：地图骨架由菜单工具生成、可一键移除；机制脚本只是组件，删掉即可。")

# ---------------- 步骤 1 ----------------
h1("步骤 1 · 生成六张地图骨架（一次菜单）")
para("", bold_prefix="前置条件（三条都要满足）：")
bullet("已在 Unity 里打开 Assets/Scenes/SampleScene.unity；")
bullet("场景已保存过（磁盘上有 .unity 文件，不能是未命名场景）；")
bullet("处于非 Play 模式。")
para("", bold_prefix="操作：")
para("点菜单栏 钟声 / 地图 / 生成六张地图与碰撞地面。", indent=0.6)
para("", bold_prefix="它会生成这些东西：")
table(
    ["层级", "内容"],
    [
        ["BattleMaps（容器）", "六个地图根节点的父物体，位置在原点"],
        ["Z1_雨夜竹林 … Z6_洞窟", "六个根节点，各挂一个 `MapScene`，Map Id 依次写成 Map01..Map06"],
        ["└ BG_背景", "SpriteRenderer，贴图取该区 Backgrounds 下的图，Sorting Order = -20，缩放到 18×10 铺满相机视野"],
        ["└ Ground_地面25格", "SpriteRenderer（Order = -10）+ BoxCollider2D，18 宽 1 厚，**顶面在 y = -3.5**"],
        ["BattleMapPresenter", "已有则复用，把六个根节点写进 Scenes[0..5]，并把 GameUiController 的引用连上"],
    ],
    widths=[5.0, 11.2])
para("", bold_prefix="如果弹出「已存在 BattleMaps」：")
para("选「只补连线」。它会保留你已有的物体，只重新连接展示器与 GameUiController 的引用；"
     "选「重新生成」会删掉现有六个根节点重来（在里面挂过的机制会丢）。")

h2("生成后的 Hierarchy 形状")
code_para("SampleScene")
code_para("├─ BattleMaps")
code_para("│   ├─ Z1_雨夜竹林      [MapScene: Map01]")
code_para("│   │   ├─ BG_背景      [SpriteRenderer]")
code_para("│   │   └─ Ground_地面25格  [SpriteRenderer + BoxCollider2D]")
code_para("│   ├─ Z2_蒸汽井盖      [MapScene: Map02]")
code_para("│   └─ …（共六个）")
code_para("├─ BattleMapPresenter   [BattleMapPresenter]")
code_para("└─ …（原有物体不动）")
para("六个根节点在 Awake 时会被统一收起，只有本局抽中、且是玩家所在的那一张会被激活。"
     "所以 Scene 视图里同时看到六个叠在一起是正常的。", indent=0.4)

# ---------------- 步骤 2 ----------------
doc.add_page_break()
h1("步骤 2 · 给每个区域挂机制脚本")
para("机制是「选中根节点 → Add Component → 搜类名」。挂完再按表填素材。"
     "六个机制都没有对战斗数值的依赖，纯视觉演示，日志前缀是 [机制验证]。")

h2("2.1 素材对照总表")
table(
    ["区域 / 根节点", "要挂的组件", "需要拖的字段 → 素材路径（Assets/MapKit/Maps/…）", "额外依赖"],
    [
        ["Z1_雨夜竹林", "`BambooNightMechanic`",
         "Rain Drop Sprite → 01_BambooNight/Sprites/ink_rain_drop.png\nRain Splash Sprite → …/ink_rain_splash.png",
         "无"],
        ["Z2_蒸汽井盖", "`SteamMechanic`",
         "Manhole → 02_Steam/steam_manhole.png\nWarn → …/steam_manhole_warn.png\nPuff → …/steam_puff_1.png\nWisp → …/steam_wisp.png",
         "必须有子节点名恰为 Ground_地面25格，其下挂 25 个 SpriteRenderer（工具生成的地面就是这个名）"],
        ["Z3_竞技场", "`ArenaMechanic`",
         "Buff Icon → 03_Arena 下没有专用图标素材，可留空（只影响提示图标，机制照跑）",
         "无"],
        ["Z4_精灵森林", "`ForestMechanic`",
         "Sprout Frames[3] → 04_ElfForest/Sprites/GraspingVine/gv_sprout_1..3.png\nPop Frame → gv_pop.png\nMature Frames[2] → gv_mature_1..2.png\nWither Frames[3] → gv_wither_1..3.png\nGround Surface Y → 保持 1",
         "无"],
        ["Z5_钟塔", "`ClockMechanic`",
         "Icon Rewind → 05_ClockTower/Sprites/clock_icon_rewind.png\nIcon Haste → clock_icon_haste.png\nIcon Slow → clock_icon_slow.png",
         "必须有子节点 PROP_时针 / PROP_分针（见 2.3）"],
        ["Z6_洞窟", "`CaveMechanic`",
         "Rock Sprite → 06_Cave/Sprites/cave_falling_rock.png\nShadow Sprite → cave_rock_shadow.png",
         "无"],
    ],
    widths=[2.9, 3.2, 7.4, 4.0], fs=8)

h2("2.2 通用操作顺序（六个区域一样）")
bullet("在 Hierarchy 展开 BattleMaps，选中要配的区域根节点（例如 Z1_雨夜竹林）。")
bullet("Inspector 底部 Add Component，输入机制类名（如 BambooNightMechanic）回车。")
bullet("按上表把 Sprite 字段逐个从 Project 窗口拖进来。数组类字段（Sprout Frames 等）先设 Size，再逐项拖。")
bullet("六个区域都做完后，逐个点开 MapScene 确认 Map Id 没有被改错（Map01..Map06 一一对应）。")

h2("2.3 Z5 钟塔：先建两根指针子物体")
para("ClockMechanic 按名字找子物体，找不到指针就不会转。做法：")
bullet("选中 Z5_钟塔，右键 Create Empty，改名 PROP_时针。")
bullet("给它 Add Component → Sprite Renderer，Sprite 拖 05_ClockTower/Sprites/clock_hand_hour.png，"
       "Sorting Order 设成比 BG_背景（-20）高的值，例如 -5；Pivot 需要指到根部，"
       "在 sprite 的 Sprite Editor 里把 Pivot 设为 Bottom，或在 Scene 里用 Pivot 模式把轴心拖到指针根部。")
bullet("同样做 PROP_分针，Sprite 用 clock_hand_minute.png，Sorting Order 比时针再高 1。")
bullet("把这两个 Transform 分别拖进 ClockMechanic 的 Hour Hand / Minute Hand 槽位（留空时它会按名字自动找，所以命名对上就够）。")

h2("2.4 Z4 精灵森林的两个可选表现脚本（挂不挂都能跑）")
table(
    ["脚本", "挂在哪", "要填什么"],
    [
        ["`ForestLeafSway`", "BG_背景（必须同物体有 SpriteRenderer）",
         "Leaf Atlas → 04_ElfForest/Sprites/Leaves/elf_forest_leaf_atlas.png\nClump Data → 同目录 elf_forest_leaf_clumps.json"],
        ["`ForestFireflies`", "可新建空物体挂（同物体需 SpriteRenderer）",
         "数量 / 冷色比例 / 排序偏移，用默认值即可"],
    ],
    widths=[3.4, 5.4, 8.6])

# ---------------- 步骤 3 ----------------
h1("步骤 3 · 把机制拖进 MapScene 的 Zone Mechanic（可选）")
para("每个 MapScene 有一个 Zone Mechanic 槽位。把该区域刚挂上的机制组件拖进去，HUD 的「机制：」一栏才会显示运行状态"
     "（例如「预警2口」「钟声#3 加速×1.15」）。不拖也不影响机制运行，HUD 只会一直显示「机制：待命」。")
para("这一步是纯拖拽：选中 Z1_雨夜竹林 → Inspector 里 MapScene 组件 → Zone Mechanic 槽 ← 拖 BambooNightMechanic。六个都照做。")

# ---------------- 步骤 4 ----------------
h1("步骤 4 · 校对 BattleMapPresenter 的六个槽位")
para("用菜单生成时这一步已经自动完成。若手搭或想核对，BattleMapPresenter.Scenes[0..5] 的顺序必须是：")
table(
    ["槽位", "0", "1", "2", "3", "4", "5"],
    [["应填", "Z1_雨夜竹林", "Z2_蒸汽井盖", "Z3_竞技场", "Z4_精灵森林", "Z5_钟塔", "Z6_洞窟"],
     ["对应", "Map01", "Map02", "Map03", "Map04", "Map05", "Map06"]],
    widths=[2.0, 2.6, 2.6, 2.6, 2.6, 2.6, 2.6])
para("六个 MapScene 的 Map Id 如果恰好构成 0..5 的唯一排列，即使拖错顺序也会在运行时自动校正；"
     "但只要有一个是空槽或两个编号重复，就退回按槽位下标匹配。所以「顺序正确 + 编号正确」最稳。")

# ---------------- 步骤 5 ----------------
doc.add_page_break()
h1("步骤 5 · 区域锚点（已代填，可按需微调）")
para("MatchSpawner.Region Anchors 决定三场 1v1 落在世界的哪六个点。之前留空时用的是间距 14 的计算布局，"
     "会把角色摆到地图背景之外；现在已改成场景里六个空物体，坐标都在单张地图的可见范围内：")
table(
    ["锚点", "Map01", "Map02", "Map03", "Map04", "Map05", "Map06"],
    [["坐标 (x, y)", "(-5.5, 1.6)", "(0, 1.6)", "(5.5, 1.6)", "(-5.5, -2.8)", "(0, -2.8)", "(5.5, -2.8)"]],
    widths=[2.2, 2.4, 2.2, 2.4, 2.4, 2.4, 2.4])
para("地图可见范围是 x ∈ [-9, 9]、y ∈ [-5, 5]，地面顶面在 y = -3.5。每场 1v1 的两人站在锚点左右各偏移 2.2，"
     "所以每场实际占用约 4.4 宽，三列间距 5.5 刚好不重叠。")
para("", bold_prefix="想调整时直接拖 Hierarchy 里的 ArenaAnchor_Map05 之类的空物体即可，不需要改代码；"
     "MatchSpawner 每次开局都会重新读这些 Transform 的 position。")
para("两个可调的相关参数：")
bullet("PlayerMotor 的 Arena Radius 默认 6.5，比 5.5 的锚点间距大，玩家在边界上可能走进隔壁擂台。"
       "想让边界贴合单个擂台，可把 PlayerMotor.cs 里 arenaRadius 的默认值改到 2.5~3（运行时由 MatchSpawner AddComponent，"
       "改的是代码默认值，不是场景值）。", 1)
bullet("MatchSpawner 的 Duel Offset（默认 2.2）决定同场两人的间距，改小可以给三场留更多空隙。", 1)

# ---------------- 步骤 6 ----------------
h1("步骤 6 · 校验与 Play 验收")

h2("6.1 先跑两个校验菜单")
table(
    ["菜单", "关注什么"],
    [
        ["钟声 / 地图 / 校验地图接线", "六个 MapScene 是否齐全、槽位顺序、地面与 Presenter 引用是否断"],
        ["钟声 / 校验 / 校验全部数据", "角色、技能、钟声、输入的数据问题（与地图无关，顺手跑一遍）"],
    ],
    widths=[6.4, 9.8])

h2("6.2 Play 后的验收清单")
bullet("点「开始游戏」→ Console 出现「[地图] 展示 Zx_…：Zx_xxx（资源 0x_xxx），地面顶面 Y=-3.50」。")
bullet("只有一个地图根节点是激活的，其余五个在 Hierarchy 里是灰的。")
bullet("玩家脚下有地面贴图，人被摆在 3×2 网格的某一格上，不再跑到画面外。")
bullet("HUD 的「机制：」一栏出现该区域的机制状态文字（前提是做了步骤 3）。")
bullet("点「返回主菜单」→ Console 出现「[地图] 已收起所有地图」，六个根节点全部隐藏。")
bullet("位置钟声把玩家换到别的区域时，Console 会出现新区域的展示日志，地图跟着换。")

# ---------------- 步骤 7 ----------------
h1("步骤 7 · 常见现象对照")
table(
    ["现象", "原因", "处理"],
    [
        ["Console 警告「该槽位还没有地图实体」", "BattleMapPresenter 的 Scenes 有空槽", "跑菜单「只补连线」或手工把缺的根节点拖进去"],
        ["地图不显示，但流程正常", "根本没生成骨架", "执行步骤 1"],
        ["地图显示了，但人物站在画面外", "Region Anchors 为空，回退到间距 14 的计算布局", "确认 MatchSpawner 的六个锚点已填（现已被代填）"],
        ["人物有影子没人、或脚下没有地面", "地面子物体被删 / 碰撞体被关掉", "MapScene 的 Ground Root 指向 Ground_地面25格；Enter() 会自动恢复 Collider 的 enabled"],
        ["Z2 蒸汽没有井盖", "地面子节点不叫 Ground_地面25格，或下面没有 25 个 SpriteRenderer", "改名或手动把井盖格补进数组"],
        ["Z5 指针不转", "缺 PROP_时针 / PROP_分针，或两个槽位与子物体名都不匹配", "按 2.3 建子物体"],
        ["机制在跑但 HUD 一直显示「机制：待命」", "MapScene 的 Zone Mechanic 没拖组件", "执行步骤 3"],
        ["菜单工具报「当前场景还没保存过」", "新建场景未落盘", "先 Ctrl+S 保存场景"],
        ["生成后六个地图叠在一起", "Awake 之前 Scene 视图就会这样", "点 Play 即只激活一张；这是设计行为"],
    ],
    widths=[4.6, 5.6, 6.0], fs=8)

doc.save(r"D:\unity\GameJam_Ring\output\地图层搭建步骤.docx")
print("OK")
