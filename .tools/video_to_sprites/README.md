# 视频 → Unity Sprite 序列帧

处理「素材其一.mp4」（水墨剑客攻击）时验证过的一条链路。后续素材按同样方式跑。

## 依赖

用 WorkBuddy 管理的 Python 环境（已装好 opencv / numpy / pillow）：

```
C:/Users/Muwen/.workbuddy/binaries/python/envs/default/Scripts/python.exe
```

若该环境丢失，pip 直连索引会失败（代理下 pip 解析不到 wheel），绕法：

1. `curl` 抓 `https://pypi.tuna.tsinghua.edu.cn/simple/<包名>/` 存成 html
2. 正则挑出 `cp313-cp313-win_amd64.whl` / `cp37-abi3-win_amd64.whl` 的最大版本
3. `curl -O` 下载到本地，`pip install --no-index *.whl`

## 四步

| 步骤 | 脚本 | 产物 |
|---|---|---|
| 1 | `1_extract_matte.py` | `Temp/build/rgba_XX.png`，1280×720 全幅 RGBA 抠图 |
| 2 | `2_write_sprites.py` | `Assets/.../swordsman_atk_XX.png`，1024×576 + 预览图 |
| 3 | `3_clean_residue.py` | 抹掉左下角不连主体的地面残墨 |
| 4 | `4_write_meta.py` | 重写 `.png.meta`（Sprite 导入配置） |

## 每次要改的常量

- `1_extract_matte.py`
  - `SRC`：视频路径
  - `TIMES`：要哪几秒（改帧数就改这里）
  - `zone[385:, :345]` / `zone[600:, :]`：固定背景装饰区（左下墨团 + 地面墨带），参数是 `[起始行, 结束列]`
  - `a[636:, :] = 0`：底部只放背景墨的条带
- `2_write_sprites.py`
  - `DEST`、`OUT_W/OUT_H`、`PIVOT`、`PPU`
- `3_clean_residue.py`：`cx < 470 and cy > 380` 的左下判定框
- `4_write_meta.py`
  - `DEST`、`PIVOT`、`PPU`，以及 `guid` 里的命名空间前缀（`Ring/Swordsman/`）

## 抠像原理（为什么不是简单阈值）

这部片的背景不是纯色：纸张浅灰 ~235–245，左下有一大团水墨，底部有墨带，而且这团墨在时间上还在缓慢流动（逐像素标准差 46–61），任何「静态背景」假设都会失效。

实际用的组合：

1. **绝对亮度软阈值**取 alpha（`218 → 0`，`100 → 1`，再 `^0.85` 提亮中间调）。人物是深墨，纸白天然为 0，所以人物不会被挖洞——这是关键，用中值背景做减法会把人物身体打穿。
2. **静态墨迹抑制**：`dark_ratio > 0.9 且 std < 12 且 bg < 230` 的像素，若当帧与中值背景接近，则抑制。用于清掉纸张上的淡墨。
3. **固定装饰区空间抑制**（最后执行，避免被孔洞回填补回）：区域内只保留亮度 < 60 的极黑像素，其余抹平。这一招才真正清掉左下墨团。
4. **孔洞回填**：从四角 flood fill，填掉被围住的透明洞，修复躯体上偶发的缺口。
5. **颜色扩散**：把透明区的 RGB 向外扩散 14 圈，防止 Unity 双线性采样时把纸白拉进边缘形成白边。
6. **连通域过滤**：只保留与主体相邻的墨迹，丢掉孤立背景残块。

## 输出约定

- 画布统一 1024×576（全幅缩放 0.8），所有帧原点一致，换帧不抖
- 轴心 `Pivot = (0.45, 0.12)`，落在脚底（实测各帧脚底都在 y=508，距底 68px）
- `PPU = 100`，`alignment: 9`（Custom），`textureType: 8`（Sprite），`alphaIsTransparency: 1`
- Unity 2022.3 的 `TextureImporter.serializedVersion` 是 **11**（不是 12），字段顺序可参照
  `Library/PackageCache/com.unity.2d.animation@9.2.0/.../Animation.IKCCD On.png.meta`
