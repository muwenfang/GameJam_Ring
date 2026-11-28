using Ring.Inputs;
using UnityEngine;

namespace Ring.Maps
{
    /// <summary>
    /// 一张地图的「可玩场地」几何快照：背景图在世界里的矩形 + 25×12 格坐标换算 + 地面顶面。
    ///
    /// 存在的理由（2026-11-28，对应《地图.docx》5.1「25×12 格坐标与角色坐标统一」）：
    /// MapKit 的地图是按 **25 列 × 12 行、每格 1 单位、左下角原点** 设计的，
    /// 而本项目相机是「中心原点、正交 5（可见 y ∈ [-5, 5]）」并且会把背景按视口等比缩放。
    /// 两套坐标如果不落到同一个快照上，就会出现「机制特效在 y=1、角色在 y=-4」这类错位。
    ///
    /// 本结构是**唯一换算口径**：
    /// - <see cref="WorldRect"/>：背景 sprite 缩放后在世界里的矩形（可能比视口宽，左右被裁）；
    /// - <see cref="Grid"/>：直接喂给 <see cref="GridSpace"/> 消费方（GameInput 选点、技能射程、机制格）；
    /// - <see cref="GroundSurfaceY"/>：背景图里画的那层地面的**上沿**，角色脚底应该落在这里。
    ///
    /// 纯数据，不持有 Transform，也不创建任何物体。由 <see cref="MapStageBuilder"/> 计算，
    /// 由 <see cref="MapScene"/> / <see cref="BattleMapPresenter"/> 对外只读暴露。
    /// </summary>
    public struct MapStage
    {
        /// <summary>背景 sprite 的世界矩形（已含等比缩放）。</summary>
        public Rect WorldRect;

        /// <summary>世界 ↔ 格 的换算配置：CellSize = WorldRect.width / Columns，原点在左下角。</summary>
        public GridSpace Grid;

        /// <summary>背景图里那层地面的上沿世界 Y（角色脚底落点）。</summary>
        public float GroundSurfaceY;

        /// <summary>是否是一份真实算出来的场地（false 时各字段不可信）。</summary>
        public bool IsValid;

        /// <summary>场地中心（背景矩形中心）。</summary>
        public Vector2 Center
        {
            get { return WorldRect.center; }
        }

        /// <summary>一格的世界尺寸。</summary>
        public float CellSize
        {
            get { return Grid.SafeCellSize; }
        }

        /// <summary>
        /// 角色可活动圆的圆心 Y：从地面上沿到背景顶边的一半。
        /// 用它做 <c>CombatBody</c> 的圆形边界，圆的最低点正好压在地面上沿，
        /// 角色不会陷进地面、也不会走出背景顶边。
        /// </summary>
        public float PlayableCenterY
        {
            get { return (GroundSurfaceY + WorldRect.yMax) * 0.5f; }
        }

        /// <summary>角色可活动圆半径（世界单位）。</summary>
        public float PlayableRadius
        {
            get { return Mathf.Max(0.01f, (WorldRect.yMax - GroundSurfaceY) * 0.5f); }
        }

        /// <summary>
        /// 可玩矩形的左下角（世界坐标），横向边界取背景矩形左右边。
        ///
        /// 竖向只剩「向下留白」的语义：角色由 <c>CombatBody</c> 锁定在 <see cref="GroundSurfaceY"/>
        /// 这条地面线上左右移动（2026-11-28 策划确认），竖向边界不再参与角色移动。
        /// </summary>
        public Vector2 PlayableRectMin
        {
            get { return new Vector2(WorldRect.xMin, WorldRect.yMin); }
        }

        /// <summary>矩形可玩区右上角（世界坐标）。</summary>
        public Vector2 PlayableRectMax
        {
            get { return new Vector2(WorldRect.xMax, WorldRect.yMax); }
        }

        /// <summary>格坐标（可以是小数格）→ 世界坐标。</summary>
        public Vector3 CellToWorld(float col, float row)
        {
            float cell = CellSize;
            return new Vector3(Grid.OriginX + col * cell, Grid.OriginY + row * cell, 0f);
        }

        /// <summary>第 col 列、站在地面上的世界落点（角色脚底）。</summary>
        public Vector3 GroundPoint(float col)
        {
            return new Vector3(Grid.OriginX + col * CellSize, GroundSurfaceY, 0f);
        }

        /// <summary>一格宽对应的世界宽度（与 <see cref="CellSize"/> 同值，语义化别名）。</summary>
        public float CellWidth
        {
            get { return CellSize; }
        }

        /// <summary>地面带的世界高度（背景矩形高 / 总行数 × 地面行数）。</summary>
        public float GroundHeight
        {
            get
            {
                float rows = Grid.SafeRows;
                float band = GroundSurfaceY - WorldRect.yMin;
                return Mathf.Max(0f, band > 0f ? band : WorldRect.height / Mathf.Max(1f, rows));
            }
        }

        public string Describe()
        {
            if (!IsValid)
            {
                return "（无场地：未接背景 sprite 或相机）";
            }

            return "场地 " + WorldRect.width.ToString("F2") + "×" + WorldRect.height.ToString("F2")
                   + " @(" + WorldRect.xMin.ToString("F2") + "," + WorldRect.yMin.ToString("F2") + ")"
                   + "，格 " + Grid.SafeColumns + "×" + Grid.SafeRows + "，格宽 " + CellSize.ToString("F3")
                   + "，地面顶面 Y=" + GroundSurfaceY.ToString("F2");
        }
    }

    /// <summary>
    /// 把「相机视口 + 背景 sprite + 地面节点」算成 <see cref="MapStage"/>，并把背景/地面摆到位。
    ///
    /// 与 <see cref="MapScreenFitter"/> 的关系：fitter 是 Inspector 上的可调外壳，
    /// 真正的数学只在这里实现一份；Editor 工具、运行时自动对齐、纯手工接线走的是同一个函数，
    /// 不会出现「菜单算一套、运行时算另一套」。
    /// </summary>
    public static class MapStageBuilder
    {
        /// <summary>MapKit 设计口径的列数。</summary>
        public const int DesignColumns = 25;

        /// <summary>MapKit 设计口径的行数。</summary>
        public const int DesignRows = 12;

        /// <summary>背景 sprite 缺失时的兜底原始尺寸（世界单位，PPU=64 下的 1600×768）。</summary>
        public static readonly Vector2 FallbackSpriteSize = new Vector2(25f, 12f);

        /// <summary>
        /// 按相机视口把背景铺满、把地面压到图片最底 <paramref name="groundRows"/> 行，
        /// 并输出对应的 <see cref="MapStage"/>。
        ///
        /// 只修改已存在物体的 <c>localScale</c> / <c>localPosition</c> 与 <c>BoxCollider2D.size</c>，
        /// 不创建、不销毁任何物体或组件。
        /// </summary>
        /// <returns>算出了有效场地返回 true；缺相机/背景节点/地面节点返回 false。</returns>
        public static bool TryApply(
            Camera camera,
            Transform mapRoot,
            SpriteRenderer background,
            Transform groundRoot,
            BoxCollider2D groundCollider,
            float designColumns,
            float designRows,
            float groundRows,
            Vector2 fallbackSpriteSize,
            out MapStage stage)
        {
            stage = default(MapStage);

            if (camera == null || mapRoot == null || background == null || groundRoot == null || groundCollider == null)
            {
                return false;
            }

            Vector2 spriteSize = fallbackSpriteSize;
            Vector2 boundsCenter = Vector2.zero;
            if (background.sprite != null)
            {
                Bounds bounds = background.sprite.bounds;
                spriteSize = new Vector2(bounds.size.x, bounds.size.y);
                boundsCenter = new Vector2(bounds.center.x, bounds.center.y);
            }

            if (spriteSize.x <= 0.0001f || spriteSize.y <= 0.0001f)
            {
                return false;
            }

            float viewH = 2f * camera.orthographicSize;
            float viewW = viewH * camera.aspect;
            if (viewH <= 0.0001f || viewW <= 0.0001f)
            {
                return false;
            }

            // cover：等比缩放到「刚好盖住视口」的最小倍数。图片不变形、不留黑边，
            // 比设计比例更窄的屏幕会裁掉左右两侧（背景是横向延展的场景图，裁边不影响构图）。
            float scale = Mathf.Max(viewH / spriteSize.y, viewW / spriteSize.x);

            Vector3 camWorld = new Vector3(camera.transform.position.x, camera.transform.position.y, 0f);
            float worldWidth = spriteSize.x * scale;
            float worldHeight = spriteSize.y * scale;
            float worldLeft = camWorld.x - worldWidth * 0.5f;
            float worldBottom = camWorld.y - worldHeight * 0.5f;

            float columns = Mathf.Max(1f, designColumns);
            float rows = Mathf.Max(1f, designRows);
            float bandRows = Mathf.Clamp(groundRows, 0.0001f, rows);

            // ---- 关键：把「地图根节点」整体放进 25×12 的设计坐标系 ----
            // 根节点缩放 = s，原点落在背景左下角。这样：
            //   · BG 子物体在 local (12.5, 6)、缩放归一化后正好铺满 25×12；
            //   · 地面在 local (12.5, 0.5)、碰撞体 25×1；
            //   · **MapKit 六个机制脚本完全不用改**：它们把藤蔓放在 local y=1、
            //     雷电从 local y=12 落下，换算到世界就是「地面上沿 / 背景顶边」。
            // 如果只缩放 BG / Ground 两个子物体，机制特效会停在 y=1 的半空、x 跑到 25 列外，
            // 这正是 Docs/15 第七节遗留的「仍缺一次坐标换算」。
            Transform parent = mapRoot.parent;
            Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;
            float localScaleX = scale / Mathf.Max(Mathf.Abs(parentScale.x), 0.0001f);
            float localScaleY = scale / Mathf.Max(Mathf.Abs(parentScale.y), 0.0001f);
            mapRoot.localScale = new Vector3(localScaleX, localScaleY, 1f);

            Vector3 worldBottomLeft = new Vector3(worldLeft, worldBottom, 0f);
            mapRoot.localPosition = parent != null
                ? parent.InverseTransformPoint(worldBottomLeft)
                : worldBottomLeft;

            // 背景：把 sprite 的原始尺寸归一化到 25×12，再把几何中心放到设计空间中心。
            float bgScaleX = columns / spriteSize.x;
            float bgScaleY = rows / spriteSize.y;
            background.transform.localScale = new Vector3(bgScaleX, bgScaleY, 1f);
            background.transform.localPosition = new Vector3(
                columns * 0.5f - boundsCenter.x * bgScaleX,
                rows * 0.5f - boundsCenter.y * bgScaleY,
                0f);

            // 地面：设计空间里就是「最底 bandRows 行、整宽」的一条。
            // 缩放保持 1，碰撞体直接用设计单位（25 × 1），世界尺寸再由根节点缩放得到。
            // 地面 SpriteRenderer 默认禁用（视觉地面已画在背景里），若要显示平铺砖可自行启用。
            groundRoot.localScale = Vector3.one;
            groundRoot.localPosition = new Vector3(columns * 0.5f, bandRows * 0.5f, 0f);
            groundCollider.size = new Vector2(columns, bandRows);
            groundCollider.offset = Vector2.zero;

            stage = new MapStage
            {
                WorldRect = new Rect(worldLeft, worldBottom, worldWidth, worldHeight),
                Grid = new GridSpace
                {
                    // 一格 = 背景宽 / 25，与图片里的格严格对齐。
                    CellSize = worldWidth / columns,
                    OriginX = worldLeft,
                    OriginY = worldBottom,
                    Columns = Mathf.RoundToInt(columns),
                    Rows = Mathf.RoundToInt(rows),
                },
                // 地面顶面 = 矩形底边 + 地面行数占的高度。
                GroundSurfaceY = worldBottom + worldHeight * (bandRows / rows),
                IsValid = true,
            };

            return true;
        }

        /// <summary>
        /// 没有背景 sprite（纯手工场景 / 还没接美术）时的兜底：把整个相机视口当作 25×12 场地。
        /// 这样格坐标、角色落位、地面高度仍然有一套可用口径，不会退回到「各写一份魔数」。
        /// </summary>
        public static bool TryFallback(Camera camera, float designColumns, float designRows, float groundRows, out MapStage stage)
        {
            stage = default(MapStage);

            if (camera == null)
            {
                return false;
            }

            float viewH = 2f * camera.orthographicSize;
            float viewW = viewH * camera.aspect;
            if (viewH <= 0.0001f || viewW <= 0.0001f)
            {
                return false;
            }

            float columns = Mathf.Max(1f, designColumns);
            float rows = Mathf.Max(1f, designRows);
            float bandRows = Mathf.Clamp(groundRows, 0.0001f, rows);

            Vector3 center = camera.transform.position;
            var rect = new Rect(center.x - viewW * 0.5f, center.y - viewH * 0.5f, viewW, viewH);

            stage = new MapStage
            {
                WorldRect = rect,
                Grid = new GridSpace
                {
                    CellSize = rect.width / columns,
                    OriginX = rect.xMin,
                    OriginY = rect.yMin,
                    Columns = Mathf.RoundToInt(columns),
                    Rows = Mathf.RoundToInt(rows),
                },
                GroundSurfaceY = rect.yMin + rect.height * (bandRows / rows),
                IsValid = true,
            };

            return true;
        }
    }
}
