using System.Collections.Generic;
using UnityEngine;

namespace Ring.UI
{
    /// <summary>
    /// 战斗 HUD 用到的程序化贴图：圆角实心块、圆角描边、虚线圆角描边。
    ///
    /// 为什么不用美术资源：本轮 HUD 仍是占位阶段，但设计稿里血条与状态框都是圆角 + 细描边，
    /// 纯色矩形看着像坏图。这里按「目标像素尺寸」直接生成 1:1 的贴图，ImageView 用 Simple 类型即可，
    /// 不需要 9 宫格；代价是元素尺寸变了要重新生成（<see cref="BattleHud"/> 会检测尺寸变化）。
    ///
    /// 贴图统一生成为白色 + alpha，颜色交给 Image.color 染色，于是一张图能被玩家/对手血条复用。
    /// 生成结果按 (宽, 高, 圆角, 描边, 虚线参数) 缓存，避免每帧 new Texture2D。
    /// </summary>
    public static class HudSprites
    {
        /// <summary>生成出来的贴图名统一前缀，用于区分「程序生成的」与「用户自己指定的」。</summary>
        public const string GeneratedPrefix = "HudGen_";

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(32);

        /// <summary>圆角实心块（血条底槽、倒计时底牌、特性小标签）。</summary>
        public static Sprite RoundedFill(int width, int height, int radius)
        {
            width = Mathf.Max(2, width);
            height = Mathf.Max(2, height);
            radius = Mathf.Clamp(radius, 0, Mathf.Min(width, height) / 2);

            string key = GeneratedPrefix + "fill_" + width + "x" + height + "_r" + radius;
            Sprite cached;
            if (Cache.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            var pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                float py = y + 0.5f - halfH;
                for (int x = 0; x < width; x++)
                {
                    float px = x + 0.5f - halfW;
                    float sdf = RoundedRectSdf(px, py, halfW, halfH, radius);
                    pixels[(y * width) + x] = Alpha(1f - sdf); // 内部 sdf<0 → 1；边上做 1px 羽化
                }
            }

            Sprite sprite = Build(key, pixels, width, height);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>圆角描边（血条外框、倒计时底牌外框）。</summary>
        public static Sprite RoundedOutline(int width, int height, int radius, int thickness)
        {
            width = Mathf.Max(2, width);
            height = Mathf.Max(2, height);
            radius = Mathf.Clamp(radius, 0, Mathf.Min(width, height) / 2);
            thickness = Mathf.Clamp(thickness, 1, Mathf.Min(width, height) / 2);

            string key = GeneratedPrefix + "line_" + width + "x" + height + "_r" + radius + "_t" + thickness;
            Sprite cached;
            if (Cache.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            float halfThickness = thickness * 0.5f;
            var pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                float py = y + 0.5f - halfH;
                for (int x = 0; x < width; x++)
                {
                    float px = x + 0.5f - halfW;
                    float sdf = RoundedRectSdf(px, py, halfW, halfH, radius);
                    float band = Mathf.Abs(sdf + halfThickness) - halfThickness;
                    pixels[(y * width) + x] = Alpha(0.5f - band);
                }
            }

            Sprite sprite = Build(key, pixels, width, height);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// 虚线圆角描边。设计稿里血条下方的「状态栏」是虚线框；因为 HUD 尺寸在运行时才知道，
        /// 这里按目标尺寸逐像素画：直边按 x / y 取虚线相位，四角圆弧保持实线。
        /// </summary>
        public static Sprite DashedRoundedOutline(
            int width, int height, int radius, int thickness, float dashPeriod, float dashRatio)
        {
            width = Mathf.Max(2, width);
            height = Mathf.Max(2, height);
            radius = Mathf.Clamp(radius, 0, Mathf.Min(width, height) / 2);
            thickness = Mathf.Clamp(thickness, 1, Mathf.Min(width, height) / 2);
            dashPeriod = Mathf.Max(4f, dashPeriod);
            dashRatio = Mathf.Clamp01(dashRatio);

            int periodKey = Mathf.RoundToInt(dashPeriod * 10f);
            int ratioKey = Mathf.RoundToInt(dashRatio * 100f);
            string key = GeneratedPrefix + "dash_" + width + "x" + height + "_r" + radius + "_t" + thickness
                         + "_p" + periodKey + "_d" + ratioKey;
            Sprite cached;
            if (Cache.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            float halfThickness = thickness * 0.5f;
            float cornerX = halfW - radius;
            float cornerY = halfH - radius;
            var pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                float py = y + 0.5f - halfH;
                for (int x = 0; x < width; x++)
                {
                    float px = x + 0.5f - halfW;
                    float sdf = RoundedRectSdf(px, py, halfW, halfH, radius);
                    float band = Mathf.Abs(sdf + halfThickness) - halfThickness;
                    float coverage = Mathf.Clamp01(0.5f - band);
                    if (coverage <= 0f)
                    {
                        continue;
                    }

                    bool inCorner = Mathf.Abs(px) > cornerX && Mathf.Abs(py) > cornerY;
                    if (!inCorner)
                    {
                        // 上下边按 x 取相位，左右边按 y 取相位；短边两端各留半格，避免角上出现半截虚线。
                        float along = Mathf.Abs(px) > cornerX ? py : px;
                        float phase = Mathf.Repeat(along + (dashPeriod * 0.5f), dashPeriod);
                        if (phase > dashPeriod * dashRatio)
                        {
                            continue;
                        }
                    }

                    pixels[(y * width) + x] = Alpha(coverage);
                }
            }

            Sprite sprite = Build(key, pixels, width, height);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>这张图是不是本工具生成的（用户自己在 Inspector 里指定的贴图不应该被覆盖）。</summary>
        public static bool IsGenerated(Sprite sprite)
        {
            return sprite == null || sprite.name.StartsWith(GeneratedPrefix, System.StringComparison.Ordinal);
        }

        // ---------- 内部 ----------

        private static Color32 Alpha(float coverage)
        {
            byte a = (byte)(Mathf.Clamp01(coverage) * 255f);
            return new Color32(255, 255, 255, a);
        }

        /// <summary>圆角矩形的有向距离场：内部为负、外部为正。</summary>
        private static float RoundedRectSdf(float x, float y, float halfW, float halfH, float radius)
        {
            float qx = Mathf.Abs(x) - (halfW - radius);
            float qy = Mathf.Abs(y) - (halfH - radius);
            float ox = Mathf.Max(qx, 0f);
            float oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt((ox * ox) + (oy * oy)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        private static Sprite Build(string name, Color32[] pixels, int width, int height)
        {
            // 窗口反复缩放会让「宽度」变来变去，缓存无上限地长下去就是泄漏；到量就整个丢掉重建。
            if (Cache.Count > 96)
            {
                Cache.Clear();
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);

            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
