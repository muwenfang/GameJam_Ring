"""Write the repaired 12 frames as *_fix_XX.png (originals untouched) + Unity sprite meta,
then build the sprite-swap AnimationClip and AnimatorController."""
import os
import glob
import hashlib
import cv2
import numpy as np

TMP = r"D:\unity\GameJam_Ring\Temp\v10"
DEST = r"D:\unity\GameJam_Ring\Assets\Art\Characters\Swordsman\Attack"
DOCS = r"D:\unity\GameJam_Ring\Docs"
os.makedirs(DEST, exist_ok=True)

OUT_W, OUT_H = 1024, 576
PPU = 100
PIVOT = (0.45, 0.12)
TIMES = [0.42, 1.29, 2.62, 3.46, 4.33, 4.79, 5.21, 6.08, 6.54, 6.96, 7.83, 9.58]

META = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 11
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMasterTextureLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 0
  alignment: 9
  spritePivot: {{x: {px}, y: {py}}}
  spritePixelsToUnits: {ppu}
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: {sid}
    internalID: 0
    vertices: []
    indices: 
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {{}}
  pSDRemoveMatte: 0
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def guid_for(name):
    return hashlib.md5(("Ring/Swordsman/Fix/" + name).encode("utf-8")).hexdigest()


files = sorted(glob.glob(os.path.join(TMP, "rgba_*.png")))
print("source frames:", len(files))
guids = []
for i, fp in enumerate(files):
    img = cv2.imread(fp, cv2.IMREAD_UNCHANGED)
    if img is None or img.shape[2] != 4:
        print("bad", fp)
        continue
    out = cv2.resize(img, (OUT_W, OUT_H), interpolation=cv2.INTER_AREA)
    name = "swordsman_atk_fix_%02d.png" % (i + 1)
    dst = os.path.join(DEST, name)
    cv2.imwrite(dst, out)
    g = guid_for(name)
    sid = hashlib.md5(("spriteid/" + name).encode("utf-8")).hexdigest()
    with open(dst + ".meta", "w", encoding="utf-8", newline="\n") as f:
        f.write(META.format(guid=g, px=PIVOT[0], py=PIVOT[1], ppu=PPU, sid=sid))
    guids.append(g)
    print("wrote", name, out.shape[:2], g[:8])

# ---------- AnimationClip: 12 sprites at 12 fps, looping ----------
FPS = 12.0
anim_guid = hashlib.md5(b"Ring/Swordsman/SwordsmanAttack.anim").hexdigest()
ctrl_guid = hashlib.md5(b"Ring/Swordsman/SwordsmanAttack.controller").hexdigest()

curves = []
mapping = []
for i, g in enumerate(guids):
    t = i / FPS
    curves.append('    - time: %s\n      value: {fileID: 21300000, guid: %s, type: 3}' % ("%.6f" % t, g))
    mapping.append('    - {fileID: 21300000, guid: %s, type: 3}' % g)

anim = """%%YAML 1.1
%%TAG !u! tag:unity3d.com,2011:
--- !u!74 &7400000
AnimationClip:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_Name: SwordsmanAttack
  serializedVersion: 6
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
  - curve:
%s
    attribute: m_Sprite
    path: 
    classID: 212
    script: {fileID: 0}
  m_SampleRate: 12
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
    pptrCurveMapping:
%s
  m_AnimationClipSettings:
    serializedVersion: 2
    m_AdditiveReferencePoseClip: {fileID: 0}
    m_AdditiveReferencePoseTime: 0
    m_StartTime: 0
    m_StopTime: %s
    m_OrientationOffsetY: 0
    m_Level: 0
    m_CycleOffset: 0
    m_HasAdditiveReferencePose: 0
    m_LoopTime: 1
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
""" % ("\n".join(curves), "\n".join(mapping), repr(12 / FPS))

anim_path = os.path.join(DEST, "SwordsmanAttack.anim")
with open(anim_path, "w", encoding="utf-8", newline="\n") as f:
    f.write(anim)

with open(anim_path + ".meta", "w", encoding="utf-8", newline="\n") as f:
    f.write("""fileFormatVersion: 2
guid: %s
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 7400000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
""" % anim_guid)

# ---------- AnimatorController ----------
ctrl = """%%YAML 1.1
%%TAG !u! tag:unity3d.com,2011:
--- !u!91 &9100000
AnimatorController:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_Name: SwordsmanAttack
  serializedVersion: 5
  m_AnimatorParameters: []
  m_AnimatorLayers:
  - serializedVersion: 5
    m_Name: Base Layer
    m_StateMachine: {fileID: 110700000}
    m_Mask: {fileID: 0}
    m_Motions: []
    m_Behaviours: []
    m_BlendingMode: 0
    m_SyncedLayerIndex: -1
    m_DefaultWeight: 1
    m_IKPass: 0
    m_SyncedLayerAffectsTiming: 0
    m_Controller: {fileID: 9100000}
--- !u!1102 &110200000
AnimatorState:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_Name: Attack
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions: []
  m_StateMachineBehaviours: []
  m_Position: {x: 50, y: 50, z: 0}
  m_IKOnFeet: 0
  m_WriteDefaultValues: 1
  m_Mirror: 0
  m_SpeedParameterActive: 0
  m_MirrorParameterActive: 0
  m_CycleOffsetParameterActive: 0
  m_TimeParameterActive: 0
  m_Motion: {fileID: 7400000, guid: %s, type: 2}
  m_Tag: 
  m_SpeedParameter: 
  m_MirrorParameter: 
  m_CycleOffsetParameter: 
  m_TimeParameter: 
--- !u!1107 &110700000
AnimatorStateMachine:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_Name: Base Layer
  m_ChildStates:
  - serializedVersion: 1
    m_State: {fileID: 110200000}
    m_Position: {x: 280, y: 120, z: 0}
  m_ChildStateMachines: []
  m_AnyStateTransitions: []
  m_EntryTransitions: []
  m_StateMachineTransitions: {}
  m_StateMachineBehaviours: []
  m_AnyStatePosition: {x: 50, y: 20, z: 0}
  m_EntryPosition: {x: 50, y: 120, z: 0}
  m_ExitPosition: {x: 800, y: 120, z: 0}
  m_ParentStateMachinePosition: {x: 800, y: 20, z: 0}
  m_DefaultState: {fileID: 110200000}
""" % anim_guid

ctrl_path = os.path.join(DEST, "SwordsmanAttack.controller")
with open(ctrl_path, "w", encoding="utf-8", newline="\n") as f:
    f.write(ctrl)

with open(ctrl_path + ".meta", "w", encoding="utf-8", newline="\n") as f:
    f.write("""fileFormatVersion: 2
guid: %s
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 9100000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
""" % ctrl_guid)

# ---------- preview: dark + light ----------
def make_sheet(bgv, path, txt):
    tiles = []
    fs = sorted(glob.glob(os.path.join(DEST, "swordsman_atk_fix_*.png")))
    for i, fp in enumerate(fs):
        im = cv2.imread(fp, cv2.IMREAD_UNCHANGED).astype(np.float32)
        a = (im[:, :, 3:4] / 255.0)
        bg = np.full((OUT_H, OUT_W, 3), bgv, np.float32)
        comp = np.clip(im[:, :, :3] * a + bg * (1 - a), 0, 255).astype(np.uint8)
        comp = cv2.resize(comp, (512, 288), interpolation=cv2.INTER_AREA)
        cv2.putText(comp, "%02d" % (i + 1), (10, 28), cv2.FONT_HERSHEY_SIMPLEX, 0.8, txt, 2, cv2.LINE_AA)
        tiles.append(comp)
    sheet = np.full((4 * 288, 3 * 512, 3), bgv, np.uint8)
    for i, t in enumerate(tiles):
        r, c = divmod(i, 3)
        sheet[r * 288:(r + 1) * 288, c * 512:(c + 1) * 512] = t
    cv2.imwrite(path, sheet)
    print("preview", path)


make_sheet((38, 36, 40), os.path.join(DOCS, "art_preview_swordsman_atk_fix_dark.png"), (190, 190, 190))
make_sheet((235, 235, 235), os.path.join(DOCS, "art_preview_swordsman_atk_fix.png"), (90, 90, 90))
print("anim guid", anim_guid)
print("ctrl guid", ctrl_guid)
