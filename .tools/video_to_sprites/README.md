# 视频 → 序列帧 Sprite 链路

把水墨风角色视频抠成带透明通道的 Sprite，并生成 Unity 序列帧动画。
素材：`C:\Users\Muwen\Downloads\素材其一.mp4`（1280×720 / 24fps / 10s）

## 运行

```bash
PY="C:/Users/Muwen/.workbuddy/binaries/python/envs/default/Scripts/python.exe"
"$PY" .tools/video_to_sprites/1_extract_matte.py    # 抠图，输出 Temp/v10/rgba_XX.png
"$PY" .tools/video_to_sprites/2_build_assets.py     # 输出 sprite + meta + .anim + .controller
```

`1_extract_matte.py` 首次运行需要 `Temp/bg_stats.npz`（整段时间统计），
它由早期脚本生成；换新视频时先跑一次统计（见文末「换素材」）。

## 这套抠图的三个关键点

**1. 不能按亮度做 alpha。**
背景纸 luma=231，刀刃/手掌也是 228–231。任何 `alpha = f(亮度)` 的映射都会把白刃和白手
判成背景。第一版就是这样，刀身中段整段消失、手掌透明。

**2. 刀身靠「直线带状闭运算」找回。**
刀是画面里最长的直线结构。用 `HoughLinesP`（minLineLength=150，maxLineGap=5，
排除 y>560 的地面带）拿到刀轴，在 ±17px 的窄带内做 21×21 椭圆核闭运算，
把刀刃上缘与刀脊两条平行边缘之间填实，再与亮度的基础 alpha 取 max。

窄带限制是关键：早期直接在整帧做闭运算，会把墨爆的缝隙、袖褶、腿间空隙一起填成白块。

**3. 手掌靠「小面积纸色连通域 + 挨着袖子」找回。**
背景纸是一个与画面边界连通的超大连通域；手掌是被手臂围住的小连通域（350–5200 px²）
且与浅灰袖子相邻。按这个条件筛，手掌能被填成实体，背景纸不会。

## 逐帧常量

| 位置 | 值 | 说明 |
|---|---|---|
| `TIMES` | 0.42 … 9.58 共 12 点 | 关键帧时间，秒 |
| `MANUAL_BLADE` | `{5: [...]}` | 第 05 帧刀刃上缘完全没有可见边界，用人工刀轴补一段 |
| `zone` | `y>385 & x<345`、`y>600` | 固定装饰墨区（左下墨团、地面墨带） |
| `a[636:, :] = 0` | — | 画面最底部只有地面墨，直接清 |
| `PIVOT` | `(0.45, 0.12)` | 脚底锚点；实测各帧脚底距底 68px / 576 |

## 输出

- `Assets/Art/Characters/Swordsman/Attack/swordsman_atk_fix_01..12.png`（1024×576，RGBA）
- 同名 `.png.meta`：Sprite(Single)、PPU 100、Custom Pivot、Alpha Is Transparency
- `SwordsmanAttack.anim`：12 帧 @12fps，1 秒循环
- `SwordsmanAttack.controller`：单 state「Attack」，默认循环

原始 `swordsman_atk_01..12.png` 保持不动（旧版抠图），两套并存便于对比。

## 换素材要改的地方

1. `1_extract_matte.py`：`SRC`、`TIMES`、`zone` 坐标、`MANUAL_BLADE`（大多数视频不需要）
2. `2_build_assets.py`：`TIMES`、`OUT_W/OUT_H`、`PIVOT`、资源目录
3. 先重建时间统计：

```python
cap = cv2.VideoCapture(SRC)          # 累加 luma 的 median / std / 暗帧占比
# 结果存为 Temp/bg_stats.npz -> {"bg", "std", "dark"}
```

## 已知限制

- **03、11 帧刀尖超出原视频画面上边界**，被硬切平。原片如此，画布内没有可恢复的像素；
  要补只能扩展画布后人工生成刀锋。
- 刀刃的白与纸的白在灰度上完全同值，极端角度（刀刃正对镜头、无可见刀脊）时带状填充
  可能漏一小段，需要往 `MANUAL_BLADE` 里加人工轴。
