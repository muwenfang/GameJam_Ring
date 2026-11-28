# -*- coding: utf-8 -*-
"""生成精灵游侠 ElfRanger 的 AnimationClip 资产（与 Swordsman/Strongman/Spartan 同构）。

用法：python .tools/elf_ranger/gen_anims.py [--force]
- 从 Assets/Resources/ElfRanger/*.png.meta 读取真实 Sprite GUID；
- 帧序/帧率与 Assets/Scripts/Characters/ElfRangerSpriteAnimation.cs 严格一致；
- 已存在的 .anim 默认跳过，避免覆盖用户在 Animation 窗口里的手工微调。

注意：Unity YAML 里大量字面花括号（{fileID: 0}），模板不能用 str.format()，
一律用 __TOKEN__ 占位符替换。
"""
import argparse
import glob
import os
import re
import uuid

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RES = os.path.join(ROOT, "Assets", "Resources", "ElfRanger")
OUT = os.path.join(ROOT, "Assets", "Animations", "ElfRanger")
ANIM_ROOT = os.path.join(ROOT, "Assets", "Animations")

PREFIX = "ElfRanger_"


def frames(group, count):
    return ["%s%s_%02d" % (PREFIX, group, i) for i in range(count)]


# (剪辑名, 帧名列表, 采样率, 是否循环)
SPECS = [
    ("ElfRanger_Idle", frames("Idle", 6), 8, True),
    ("ElfRanger_Run", frames("Run", 6), 12, True),
    ("ElfRanger_Attack", frames("Attack", 10), 20, False),
    ("ElfRanger_Heavy", frames("Heavy", 12), 18, False),
    ("ElfRanger_Heal", frames("Heal", 12), 15, False),
    ("ElfRanger_Jump", frames("Jump", 6), 12, False),
    ("ElfRanger_DodgeFwd", frames("DodgeFwd", 6), 15, False),
    ("ElfRanger_DodgeBack", frames("DodgeBack", 6), 15, False),
    ("ElfRanger_Halo", frames("Halo", 8), 14, True),
    ("ElfRanger_BrambleGrow", frames("BrambleGrow", 8), 16, False),
    ("ElfRanger_BrambleIdle", frames("BrambleIdle", 6), 8, True),
    ("ElfRanger_BrambleFade", frames("BrambleFade", 3), 10, False),
    ("ElfRanger_Arrow", frames("Arrow", 12), 30, True),
]

CLIP_TEMPLATE = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!74 &7400000
AnimationClip:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_Name: __NAME__
  serializedVersion: 7
  m_Legacy: 0
  m_Compressed: 0
  m_UseHighQualityCurve: 1
  m_RotationCurves: []
  m_CompressedRotationCurves: []
  m_EulerCurves: []
  m_PositionCurves: []
  m_ScaleCurves: []
  m_FloatCurves: []
  m_PPtrCurves:
  - serializedVersion: 2
    curve:
__CURVE__
    attribute: m_Sprite
    path: 
    classID: 212
    script: {fileID: 0}
    flags: 2
  m_SampleRate: __FPS__
  m_WrapMode: 0
  m_Bounds:
    m_Center: {x: 0, y: 0, z: 0}
    m_Extent: {x: 0, y: 0, z: 0}
  m_ClipBindingConstant:
    genericBindings:
    - serializedVersion: 2
      path: 0
      attribute: 0
      script: {fileID: 0}
      typeID: 212
      customType: 23
      isPPtrCurve: 1
      isIntCurve: 0
      isSerializeReferenceCurve: 0
    pptrCurveMapping:
__MAPPING__
  m_AnimationClipSettings:
    serializedVersion: 2
    m_AdditiveReferencePoseClip: {fileID: 0}
    m_AdditiveReferencePoseTime: 0
    m_StartTime: 0
    m_StopTime: __STOP__
    m_OrientationOffsetY: 0
    m_Level: 0
    m_CycleOffset: 0
    m_HasAdditiveReferencePose: 0
    m_LoopTime: __LOOP__
    m_LoopBlend: 0
    m_LoopBlendOrientation: 0
    m_LoopBlendPositionY: 0
    m_LoopBlendPositionXZ: 0
    m_KeepOriginalOrientation: 0
    m_KeepOriginalPositionY: 1
    m_KeepOriginalPositionXZ: 0
    m_HeightFromFeet: 0
    m_Mirror: 0
  m_EditorCurves: []
  m_EulerEditorCurves: []
  m_HasGenericRootTransform: 0
  m_HasMotionFloatCurves: 0
  m_Events: []
"""

CLIP_META = """fileFormatVersion: 2
guid: __GUID__
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 7400000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

FOLDER_META = """fileFormatVersion: 2
guid: __GUID__
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def fmt(value):
    """Unity YAML 浮点：最多 7 位有效小数，去掉尾随 0。"""
    if value == 0:
        return "0"
    return ("%.7f" % value).rstrip("0").rstrip(".")


def new_guid(key, taken):
    """确定性生成 32 位十六进制 GUID，且保证不与工程内既有 GUID 冲突。"""
    candidate = uuid.uuid5(uuid.NAMESPACE_URL, "ring/elfranger/" + key).hex
    while candidate in taken:
        candidate = uuid.uuid5(uuid.NAMESPACE_URL, "ring/elfranger/" + key + "/" + candidate).hex
    taken.add(candidate)
    return candidate


def render(template, **tokens):
    text = template
    for key, value in tokens.items():
        text = text.replace("__" + key.upper() + "__", str(value))
    return text


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--force", action="store_true", help="覆盖已存在的 .anim")
    args = parser.parse_args()

    # 1. 收集 Sprite GUID（键保留 .png 后缀）
    sprite_guid = {}
    for meta in sorted(glob.glob(os.path.join(RES, "*.png.meta"))):
        text = open(meta, encoding="utf-8").read()
        m = re.search(r"^guid:\s*([0-9a-fA-F]{32})\s*$", text, re.M)
        if not m:
            raise RuntimeError("无效 GUID: " + meta)
        name = os.path.basename(meta)[:-5]  # 去掉 .meta，保留 ElfRanger_Run_00.png
        sprite_guid[name] = m.group(1)

    # 2. 收集工程内既有 GUID，避免新资产撞车
    taken = set()
    for meta in glob.glob(os.path.join(ROOT, "Assets", "**", "*.meta"), recursive=True):
        text = open(meta, encoding="utf-8", errors="ignore").read()
        m = re.search(r"^guid:\s*([0-9a-fA-F]{32})\s*$", text, re.M)
        if m:
            taken.add(m.group(1))

    os.makedirs(OUT, exist_ok=True)

    # 3. 目录 .meta
    folder_meta = os.path.join(ANIM_ROOT, "ElfRanger.meta")
    if not os.path.exists(folder_meta):
        open(folder_meta, "w", encoding="utf-8", newline="\n").write(
            render(FOLDER_META, guid=new_guid("folder/ElfRanger", taken)))
        print("+ 目录 meta: Assets/Animations/ElfRanger.meta")

    # 4. 逐个生成剪辑
    for name, frames_list, fps, loop in SPECS:
        missing = [f for f in frames_list if (f + ".png") not in sprite_guid]
        if missing:
            raise RuntimeError("%s 缺少帧: %s" % (name, missing))
        guids = [sprite_guid[f + ".png"] for f in frames_list]

        curve = "".join(
            "    - time: %s\n      value: {fileID: 21300000, guid: %s, type: 3}\n"
            % (fmt(i / float(fps)), g) for i, g in enumerate(guids))
        mapping = "".join(
            "    - {fileID: 21300000, guid: %s, type: 3}\n" % g for g in guids)
        clip = render(
            CLIP_TEMPLATE, name=name, curve=curve.rstrip("\n"),
            mapping=mapping.rstrip("\n"), fps=fps,
            stop=fmt(len(frames_list) / float(fps)), loop=1 if loop else 0)

        path = os.path.join(OUT, name + ".anim")
        if os.path.exists(path) and not args.force:
            print("= 跳过（已存在）: " + name + ".anim  —— 加 --force 可覆盖")
        else:
            open(path, "w", encoding="utf-8", newline="\n").write(clip)
            print("+ 生成: %-26s %2d 帧 / %2d fps / %s s / loop=%s"
                  % (name + ".anim", len(frames_list), fps,
                     fmt(len(frames_list) / float(fps)), loop))

        meta_path = path + ".meta"
        if not os.path.exists(meta_path):
            open(meta_path, "w", encoding="utf-8", newline="\n").write(
                render(CLIP_META, guid=new_guid("clip/" + name, taken)))
            print("+ 生成: " + name + ".anim.meta")

    # 5. 回读校验
    print("\n--- 校验 ---")
    ok = True
    for name, frames_list, fps, loop in SPECS:
        path = os.path.join(OUT, name + ".anim")
        text = open(path, encoding="utf-8").read()
        found = re.findall(r"guid: ([0-9a-fA-F]{32})", text)
        expected = [sprite_guid[f + ".png"] for f in frames_list]
        times = [float(x) for x in re.findall(r"- time: ([0-9.]+)", text)]
        checks = [
            ("curve 帧数", len(times) == len(frames_list)),
            ("mapping 帧数", len(found) == 2 * len(frames_list)),
            ("GUID 顺序", found[:len(frames_list)] == expected and found[len(frames_list):] == expected),
            ("时间递增", all(times[i] < times[i + 1] for i in range(len(times) - 1))),
            ("m_Name 一致", ("m_Name: " + name) in text),
            ("SampleRate", ("m_SampleRate: %d" % fps) in text),
            ("LoopTime", ("m_LoopTime: %d" % (1 if loop else 0)) in text),
            ("meta 存在", os.path.exists(path + ".meta")),
        ]
        bad = [k for k, v in checks if not v]
        print("  %-24s %s" % (name, "OK" if not bad else "FAIL " + ",".join(bad)))
        ok = ok and not bad

    for p in [os.path.join(ANIM_ROOT, "ElfRanger.meta")] + \
             [os.path.join(OUT, n + ".anim.meta") for n, _, _, _ in SPECS]:
        g = re.search(r"^guid:\s*([0-9a-fA-F]{32})\s*$",
                      open(p, encoding="utf-8").read(), re.M)
        if not g:
            print("  FAIL meta guid: " + p)
            ok = False
    print("\n结果: " + ("全部通过" if ok else "存在失败项"))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
