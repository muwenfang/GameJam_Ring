# -*- coding: utf-8 -*-
"""对 SampleScene.unity 做三处外科手术（无 Unity 编辑器参与）：
  1) 停用残留的 PlayerMotor 物体；
  2) GameFlow.requireCompleteSetForVictory 0 → 1（恢复 B14 严格口径）；
  3) 新增 6 个区域锚点空物体并填进 MatchSpawner.regionAnchors。
"""
import re
import sys

PATH = "Assets/Scenes/SampleScene.unity"
BASE = 550000000  # 新物体 fileID 基址，确保不与既有 ID 冲突

ANCHORS = [
    ("ArenaAnchor_Map01", -5.5, 1.6),
    ("ArenaAnchor_Map02", 0.0, 1.6),
    ("ArenaAnchor_Map03", 5.5, 1.6),
    ("ArenaAnchor_Map04", -5.5, -2.8),
    ("ArenaAnchor_Map05", 0.0, -2.8),
    ("ArenaAnchor_Map06", 5.5, -2.8),
]

text = open(PATH, encoding="utf-8").read()
existing_ids = set(re.findall(r"^--- !u!\d+ &(\d+)", text, flags=re.M))

GO_TPL = """--- !u!1 &{go}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {tr}}}
  m_Layer: 0
  m_Name: {name}
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
"""

TR_TPL = """--- !u!4 &{tr}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go}}}
  serializedVersion: 2
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: {x}, y: {y}, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {{fileID: 0}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
"""


def doc_span(src, header):
    """返回以 header 开头的 YAML 文档在 src 中的 [start, end)"""
    start = src.index(header)
    nxt = src.find("\n--- !u!", start + 1)
    return start, (len(src) if nxt < 0 else nxt + 1)


def replace_in_doc(src, header, old, new, expect=1):
    s, e = doc_span(src, header)
    seg = src[s:e]
    cnt = seg.count(old)
    if cnt != expect:
        raise SystemExit(f"[ABORT] {header}: 期望 {expect} 处 '{old}'，实际 {cnt}")
    return src[:s] + seg.replace(old, new) + src[e:]


# ---------- 1) 停用残留 PlayerMotor 物体 ----------
text = replace_in_doc(text, "--- !u!1 &1374149785\nGameObject:", "m_IsActive: 1", "m_IsActive: 0")
print("[1] 已停用残留 PlayerMotor 物体")

# ---------- 2) B14 严格胜利口径 ----------
text = replace_in_doc(text, "requireCompleteSetForVictory: 0", "", "", expect=0) if False else text
s = text.index("requireCompleteSetForVictory: 0")
text = (text[:s] + "requireCompleteSetForVictory: 1"
        + text[s + len("requireCompleteSetForVictory: 0"):])
print("[2] GameFlow.requireCompleteSetForVictory 0 -> 1")

# ---------- 3) 六个区域锚点 ----------
new_docs = []
root_ids = []
for i, (name, x, y) in enumerate(ANCHORS):
    go, tr = BASE + i * 2 + 1, BASE + i * 2 + 2
    assert str(go) not in existing_ids and str(tr) not in existing_ids, "fileID 冲突"
    new_docs.append(GO_TPL.format(go=go, tr=tr, name=name))
    new_docs.append(TR_TPL.format(tr=tr, go=go, x=x, y=y))
    root_ids.append(tr)

scene_roots_header = "--- !u!1660057539 &9223372036854775807"
s, _ = doc_span(text, scene_roots_header)
text = text[:s] + "".join(new_docs) + text[s:]

# 写进 SceneRoots.m_Roots，否则物体不会出现在场景里
s, e = doc_span(text, scene_roots_header)
seg = text[s:e]
assert seg.count("  - {fileID: 666675881}") == 1
seg = seg.replace("  - {fileID: 666675881}",
                  "  - {fileID: 666675881}" + "".join(f"\n  - {{fileID: {r}}}" for r in root_ids))
text = text[:s] + seg + text[e:]
print("[3] 已新增 6 个区域锚点并注册到 SceneRoots")

# ---------- 4) 填进 MatchSpawner.regionAnchors ----------
s = text.index("regionAnchors:")
e = text.index("arenaSpacing:", s)
block = text[s:e]
assert block.count("- {fileID: 0}") == 6, block
lines = ["regionAnchors:"] + [f"  - {{fileID: {r}}}" for r in root_ids] + [""]
text = text[:s] + "\n".join(lines) + text[e:]
print("[4] MatchSpawner.regionAnchors 已填 6 个锚点")

open(PATH, "w", encoding="utf-8", newline="\n").write(text)
print("write done")
