# -*- coding: utf-8 -*-
"""离线审计 Unity 场景的组件挂载情况（不需要打开 Unity）。

用法:
    python .tools/scene_audit/audit_scene.py [scene.unity] [--fields] [--missing]

它做三件事:
  1) 扫描 Assets 下所有 *.cs.meta，建立 script GUID -> 类名/路径 的映射；
  2) 解析目标场景，列出每个 MonoBehaviour 挂在哪个 GameObject 上（含激活状态）；
  3) --fields  额外打印该组件的序列化字段值（识别哪些 Inspector 槽位为空）；
     --missing 反向列出「项目里有、但场景里没有」的 MonoBehaviour，即尚未挂载的脚本。

注意: 场景 YAML 的 fileID 复用与嵌套 Prefab 不做展开；对 GameJam_Ring 这种单场景工程够用。
"""
import re
import sys
import os
from glob import glob

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def build_guid_map():
    """script guid -> (类名, 相对路径)"""
    out = {}
    for meta in glob(os.path.join(ROOT, "Assets", "**", "*.cs.meta"), recursive=True):
        cs = meta[:-5]
        try:
            text = open(meta, encoding="utf-8", errors="ignore").read()
        except OSError:
            continue
        m = re.search(r"guid:\s*([0-9a-f]{32})", text)
        if not m:
            continue
        try:
            src = open(cs, encoding="utf-8", errors="ignore").read()
        except OSError:
            continue
        cls = re.search(
            r"(?:public|internal)\s+(?:sealed\s+|abstract\s+|static\s+)*class\s+(\w+)\s*(?::\s*([\w<>, ]+))?",
            src)
        # 只认「类声明直接继承 MonoBehaviour / ZoneMechanicBase」，避免被注释里的 MonoBehaviour 误判
        base = (cls.group(2) or "") if cls else ""
        is_mb = ("MonoBehaviour" in base or "ZoneMechanicBase" in base)
        # abstract 基类 Unity 不允许挂，从「待挂载」清单里排除
        if re.search(r"\babstract\s+class\s+" + (cls.group(1) if cls else "___"), src):
            is_mb = False
        out[m.group(1)] = (cls.group(1) if cls else os.path.basename(cs),
                           os.path.relpath(cs, ROOT).replace("\\", "/"),
                           is_mb)
    return out


def parse_scene(path):
    text = open(path, encoding="utf-8", errors="ignore").read()
    parts = re.split(r"^--- !u!(\d+) &(\d+).*$", text, flags=re.M)
    objs = {}
    for i in range(1, len(parts), 3):
        objs[parts[i + 1]] = (parts[i], parts[i + 2])
    return objs


def go_name(objs, oid, depth=0):
    if depth > 3 or oid not in objs:
        return "?"
    cid, body = objs[oid]
    if cid == "1":
        m = re.search(r"m_Name: (.*)", body)
        return m.group(1).strip() or "(空名)"
    if cid in ("4", "224"):
        m = re.search(r"m_GameObject: \{fileID: (\d+)", body)
        return go_name(objs, m.group(1), depth + 1) if m else "?"
    return "?"


def go_active(objs, go_id):
    cid, body = objs.get(go_id, ("", ""))
    m = re.search(r"m_IsActive: (\d)", body)
    return m.group(1) == "1"


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    show_fields = "--fields" in sys.argv
    show_missing = "--missing" in sys.argv

    scene = args[0] if args else os.path.join(ROOT, "Assets", "Scenes", "SampleScene.unity")
    if not os.path.isabs(scene):
        scene = os.path.join(ROOT, scene)

    guids = build_guid_map()
    objs = parse_scene(scene)

    mounted = {}
    for oid, (cid, body) in objs.items():
        if cid != "114":
            continue
        m = re.search(r"m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32})", body)
        if not m or m.group(1) not in guids:
            continue
        cls, path, is_mb = guids[m.group(1)]
        go = re.search(r"m_GameObject: \{fileID: (\d+)", body)
        if not go:
            continue
        name = go_name(objs, go.group(1))
        mounted.setdefault(m.group(1), []).append((name, go_active(objs, go.group(1))))
        flag = "" if go_active(objs, go.group(1)) else "  [未激活]"
        print(f"{cls:28s} -> {name}{flag}")
        if show_fields:
            fields = body.split("m_EditorClassIdentifier:")[-1].strip()
            for line in fields.splitlines():
                if line.strip():
                    print("      " + line.strip()[:120])

    if show_missing:
        print("\n--- 场景里尚未挂载的 MonoBehaviour ---")
        for guid, (cls, path, is_mb) in sorted(guids.items(), key=lambda kv: kv[1][0]):
            if is_mb and guid not in mounted:
                print(f"{cls:28s} {path}")


if __name__ == "__main__":
    main()
