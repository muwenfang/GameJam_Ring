using UnityEngine;
using UnityEngine.UI;

namespace Ring.UI
{
    /// <summary>
    /// 用代码搭一套战斗 HUD 的默认布局（左右血条 + 状态栏、中间倒计时与回合数）。
    ///
    /// 用途有两个：
    /// 1）场景里没有 HUD 时，<see cref="GameUiController"/> 在运行时兜底搭一套，保证「点开始就有界面」；
    /// 2）编辑器菜单「钟声 / 界面 / 生成战斗 HUD（场景）」调它，把同一套结构落成场景里的真实物体，
    ///    之后就可以在 Hierarchy 里随便挪、在 Inspector 里随便调，不再受代码约束。
    ///
    /// 尺寸按 1920×1080 参考分辨率给（与 <see cref="UiBuilder.CreateCanvas"/> 的 CanvasScaler 一致），
    /// 左右两块各 820 宽、中间 200 宽，留出 28 的边距，互不重叠。
    /// </summary>
    public static class BattleHudBuilder
    {
        private const float Margin = 28f;
        private const float SideHeight = 176f;
        private const float NameHeight = 30f;
        private const float BarHeight = 34f;
        private const float BarTop = 34f;
        private const float TraitBoxHeight = 84f;
        private const float TraitBoxTop = 78f;
        private const float BadgeSize = 112f;

        /// <summary>左右两块各占屏幕宽度的这一比例（1920 下约 820 像素），窄屏也不会挤到中间。</summary>
        private const float SideWidthRatio = 0.44f;

        /// <summary>在给定父级下搭一套 HUD，返回挂在根物体上的 <see cref="BattleHud"/>。</summary>
        public static BattleHud Build(Transform parent, string rootName = "BattleHud")
        {
            RectTransform root = UiBuilder.CreateRect(parent, rootName);
            UiBuilder.Stretch(root);

            // 先关掉再挂组件：Awake 会去解析引用，等零件都建好、回填完再让它跑。
            root.gameObject.SetActive(false);
            var hud = root.gameObject.AddComponent<BattleHud>();

            var parts = new BattleHudParts();
            BuildSide(root, "Player", true, new Vector2(Margin, -16f), ref parts);
            BuildSide(root, "Opponent", false, new Vector2(-Margin, -16f), ref parts);
            BuildCenter(root, ref parts);
            BuildDetail(root, ref parts);

            hud.AssignParts(parts);
            return hud;
        }

        /// <summary>搭一个带 Canvas / CanvasScaler / GraphicRaycaster 的 HUD 画布（编辑器工具用）。</summary>
        public static BattleHud BuildOnNewCanvas(string canvasName = "HudCanvas", int sortingOrder = 90)
        {
            Canvas canvas = UiBuilder.CreateCanvas(canvasName, sortingOrder);
            return Build(canvas.transform);
        }

        // ---------- 一侧 ----------

        private static void BuildSide(Transform root, string prefix, bool playerSide, Vector2 position, ref BattleHudParts parts)
        {
            // 宽度用相对锚点（左右各占 44%），窄屏时血条跟着缩短，不会挤到中间的倒计时。
            RectTransform side = UiBuilder.CreateRect(root, prefix);
            side.anchorMin = new Vector2(playerSide ? 0f : 1f - SideWidthRatio, 1f);
            side.anchorMax = new Vector2(playerSide ? SideWidthRatio : 1f, 1f);
            side.pivot = new Vector2(playerSide ? 0f : 1f, 1f);
            side.offsetMin = playerSide
                ? new Vector2(Margin, position.y - SideHeight)
                : new Vector2(0f, position.y - SideHeight);
            side.offsetMax = playerSide
                ? new Vector2(0f, position.y)
                : new Vector2(-Margin, position.y);

            // 名字
            Text name = CreateText(
                side, prefix + "Name", playerSide ? "玩家" : "对手", 26,
                new Color(0.96f, 0.97f, 0.99f, 1f),
                playerSide ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, NameHeight));

            // 血条
            RectTransform bar = CreateRect(
                side, prefix + "Bar",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -BarTop), new Vector2(0f, BarHeight));

            var barImage = bar.gameObject.AddComponent<Image>();
            barImage.color = new Color(0.9f, 0.92f, 0.95f, 0.94f);
            barImage.raycastTarget = false;

            RectTransform border = UiBuilder.CreateRect(bar, prefix + "BarBorder");
            UiBuilder.Stretch(border);
            var borderImage = border.gameObject.AddComponent<Image>();
            borderImage.color = new Color(0.14f, 0.19f, 0.29f, 1f);
            borderImage.raycastTarget = false;

            RectTransform fill = UiBuilder.CreateRect(bar, prefix + "BarFill");
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = new Vector2(3f, 3f);
            fill.offsetMax = new Vector2(-3f, -3f);
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = playerSide
                ? new Color(0.28f, 0.75f, 0.51f, 1f)
                : new Color(0.88f, 0.35f, 0.31f, 1f);
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = playerSide ? (int)Image.OriginHorizontal.Left : (int)Image.OriginHorizontal.Right;
            fillImage.fillAmount = 1f;
            fillImage.raycastTarget = false;

            Text value = CreateText(
                bar, prefix + "BarValue", string.Empty, 20, Color.white, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            // 特性栏（虚线框 + 一排小标签）
            RectTransform box = CreateRect(
                side, prefix + "TraitBox",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -TraitBoxTop), new Vector2(0f, TraitBoxHeight));

            var boxImage = box.gameObject.AddComponent<Image>();
            boxImage.color = new Color(0.85f, 0.88f, 0.93f, 0.55f);
            boxImage.raycastTarget = false;

            RectTransform row = UiBuilder.CreateRect(box, prefix + "TraitRow");
            row.anchorMin = Vector2.zero;
            row.anchorMax = Vector2.one;
            row.offsetMin = new Vector2(10f, 6f);
            row.offsetMax = new Vector2(-10f, -6f);

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = playerSide ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(0, 0, 0, 0);

            Text empty = CreateText(
                box, prefix + "TraitEmpty", "状态栏", 18,
                new Color(0.86f, 0.89f, 0.94f, 0.45f), TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            if (playerSide)
            {
                parts.playerName = name;
                parts.playerBar = barImage;
                parts.playerBarBorder = borderImage;
                parts.playerBarFill = fillImage;
                parts.playerBarValue = value;
                parts.playerTraitBox = boxImage;
                parts.playerTraitRow = row;
                parts.playerTraitEmpty = empty;
            }
            else
            {
                parts.opponentName = name;
                parts.opponentBar = barImage;
                parts.opponentBarBorder = borderImage;
                parts.opponentBarFill = fillImage;
                parts.opponentBarValue = value;
                parts.opponentTraitBox = boxImage;
                parts.opponentTraitRow = row;
                parts.opponentTraitEmpty = empty;
            }
        }

        // ---------- 中间 ----------

        private static void BuildCenter(Transform root, ref BattleHudParts parts)
        {
            RectTransform center = CreateRect(
                root, "Center",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -8f), new Vector2(200f, 180f));

            RectTransform badge = CreateRect(
                center, "BellBadge",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(BadgeSize, BadgeSize));

            var badgeImage = badge.gameObject.AddComponent<Image>();
            badgeImage.color = new Color(0.93f, 0.94f, 0.96f, 1f);
            badgeImage.raycastTarget = false;

            RectTransform badgeBorder = UiBuilder.CreateRect(badge, "BellBadgeBorder");
            UiBuilder.Stretch(badgeBorder);
            var badgeBorderImage = badgeBorder.gameObject.AddComponent<Image>();
            badgeBorderImage.color = new Color(0.14f, 0.19f, 0.29f, 1f);
            badgeBorderImage.raycastTarget = false;

            Text bellValue = CreateText(
                badge, "BellValue", "30", 56, new Color(0.10f, 0.14f, 0.22f, 1f), TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, false);

            Text roundValue = CreateText(
                center, "RoundValue", "回合 1 / 3", 22, new Color(0.9f, 0.92f, 0.96f, 1f), TextAnchor.UpperCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -BadgeSize - 8f), new Vector2(240f, 30f));

            parts.bellBadge = badgeImage;
            parts.bellBadgeBorder = badgeBorderImage;
            parts.bellValue = bellValue;
            parts.roundValue = roundValue;
        }

        // ---------- 左下明细 ----------

        private static void BuildDetail(Transform root, ref BattleHudParts parts)
        {
            Text detail = CreateText(
                root, "Detail", string.Empty, 20, new Color(0.86f, 0.88f, 0.92f, 0.9f), TextAnchor.LowerLeft,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0f),
                new Vector2(Margin, 24f), new Vector2(900f, 220f));

            detail.horizontalOverflow = HorizontalWrapMode.Wrap;
            detail.verticalOverflow = VerticalWrapMode.Overflow;
            detail.gameObject.SetActive(false);
            parts.detailLabel = detail;
        }

        // ---------- 小工具 ----------

        private static RectTransform CreateRect(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 position,
            Vector2 size)
        {
            RectTransform rect = UiBuilder.CreateRect(parent, name);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Text CreateText(
            Transform parent,
            string name,
            string content,
            int fontSize,
            Color color,
            TextAnchor alignment,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 position,
            Vector2 size,
            bool addOutline = true)
        {
            RectTransform rect = UiBuilder.CreateRect(parent, name);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Text text = rect.gameObject.AddComponent<Text>();
            text.text = content;
            text.font = UiBuilder.GetFont();
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            if (addOutline)
            {
                var outline = rect.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
            }

            return text;
        }
    }
}
