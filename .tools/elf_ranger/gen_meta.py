# -*- coding: utf-8 -*-
"""为 Assets/Resources/ElfRanger 下 101 张序列帧生成 .meta（含唯一 GUID）。

GUID 由「文件名 + 固定盐」做 md5 得出，重复执行结果稳定，不会每次刷新都变。
PPU 按素材用途分组，保证世界尺寸与既有三套角色同一量级：
- 角色 / 光环：800x800 画布，人物 583px，PPU 292 → 2.0 世界单位（再乘组件 visualScale）
- 荆棘：876x292 = 3x1 格，PPU 292 → 3.0 x 1.0 世界单位
- 箭矢：644x210，PPU 500 → 1.29 x 0.42 世界单位
"""
import hashlib
import json
import os

ROOT = r"D:\unity\GameJam_Ring"
DST = os.path.join(ROOT, "Assets", "Resources", "ElfRanger")
MANIFEST = os.path.join(ROOT, ".tools", "elf_ranger", "manifest.json")

SALT = "elfranger-2026-11-28"

PPU_DEFAULT = 292
PPU_BY_GROUP = {
    "Arrow": 500,
}


def guid_for(name):
    return hashlib.md5((SALT + "|guid|" + name).encode("utf-8")).hexdigest()


def sprite_id_for(name):
    return hashlib.md5((SALT + "|sprite|" + name).encode("utf-8")).hexdigest()


TEMPLATE = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 12
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
  isReadable: 0
  streamingMipmaps: 0
  textureType: 8
  textureShape: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  spriteMode: 1
  spritePixelsToUnits: {ppu}
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  alphaIsTransparency: 1
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: {sprite_id}
    internalID: 0
  platformSettings: []
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def main():
    pngs = sorted(f for f in os.listdir(DST) if f.lower().endswith(".png"))
    if not pngs:
        raise SystemExit("没有找到 png，先跑 build_frames.py")

    manifest = {}
    seen = {}
    for name in pngs:
        group = name.split("_")[1]
        ppu = PPU_BY_GROUP.get(group, PPU_DEFAULT)
        guid = guid_for(name)
        if guid in seen:
            raise SystemExit("GUID 冲突: %s 与 %s" % (name, seen[guid]))
        seen[guid] = name
        meta_path = os.path.join(DST, name + ".meta")
        with open(meta_path, "w", encoding="utf-8", newline="\n") as f:
            f.write(TEMPLATE.format(guid=guid, ppu=ppu, sprite_id=sprite_id_for(name)))
        manifest[name[:-4]] = {"guid": guid, "ppu": ppu, "group": group}

    with open(MANIFEST, "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=1, sort_keys=True)

    groups = {}
    for name, info in manifest.items():
        groups.setdefault(info["group"], []).append(name)
    print("meta 写出:", len(manifest), "->", DST)
    for g in sorted(groups):
        print("  %-12s n=%-3d ppu=%d" % (g, len(groups[g]), manifest[groups[g][0]]["ppu"]))
    print("manifest:", MANIFEST)


if __name__ == "__main__":
    main()
