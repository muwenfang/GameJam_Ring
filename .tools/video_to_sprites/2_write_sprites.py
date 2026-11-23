import os, glob, hashlib
import cv2
import numpy as np

TMP = r"D:\unity\GameJam_Ring\Temp\build"
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
  serializedVersion: 12
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
  spriteMeshType: 1
  alignment: 0
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
  applyGammaDecoding: 1
  cookieLightType: 0
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
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: 
    internalID: 0
    vertices: []
    indices: 
    edges: []
    weights: []
    secondaryTextures: []
  spritePackingTag: 
  pSDRemoveMatte: 0
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def checker(size, cell=32):
    h, w = size
    yy, xx = np.mgrid[0:h, 0:w]
    c = (((yy // cell) + (xx // cell)) % 2).astype(np.uint8)
    return np.where(c == 0, 235, 255).astype(np.uint8)


def guid_for(name):
    return hashlib.md5(("Ring/Swordsman/" + name).encode("utf-8")).hexdigest()


files = sorted(glob.glob(os.path.join(TMP, "rgba_*.png")))
print("found", len(files))
thumbs = []
for i, fp in enumerate(files):
    img = cv2.imread(fp, cv2.IMREAD_UNCHANGED)
    if img is None or img.shape[2] != 4:
        print("bad", fp)
        continue
    out = cv2.resize(img, (OUT_W, OUT_H), interpolation=cv2.INTER_AREA)
    name = "swordsman_atk_%02d.png" % (i + 1)
    dst = os.path.join(DEST, name)
    cv2.imwrite(dst, out)
    with open(dst + ".meta", "w", encoding="utf-8", newline="\n") as f:
        f.write(META.format(guid=guid_for(name), px=PIVOT[0], py=PIVOT[1], ppu=PPU))
    # preview over light checker
    al = (out[:, :, 3:4].astype(np.float32) / 255.0)
    chk = checker((OUT_H, OUT_W))[:, :, None].repeat(3, axis=2).astype(np.float32)
    comp = (out[:, :, :3].astype(np.float32) * al + chk * (1 - al)).astype(np.uint8)
    th = cv2.resize(comp, (480, 270), interpolation=cv2.INTER_AREA)
    cv2.putText(th, "%02d  %.2fs" % (i + 1, TIMES[i]), (8, 24), cv2.FONT_HERSHEY_SIMPLEX,
                0.55, (30, 30, 30), 1, cv2.LINE_AA)
    thumbs.append(th)
    print("wrote", name, out.shape)

cols = 4
rows = (len(thumbs) + cols - 1) // cols
th_, tw_ = thumbs[0].shape[:2]
sheet = np.full((rows * th_, cols * tw_, 3), 245, np.uint8)
for i, t in enumerate(thumbs):
    r, c = divmod(i, cols)
    sheet[r * th_:(r + 1) * th_, c * tw_:(c + 1) * tw_] = t
cv2.imwrite(os.path.join(DOCS, "art_preview_swordsman_atk.png"), sheet)
print("sheet saved", sheet.shape)
