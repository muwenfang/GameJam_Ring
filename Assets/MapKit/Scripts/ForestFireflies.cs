// 精灵森林：动态萤火虫光点 v2（2026-11-25）
// v1 的问题：每个光点是 3×3 的贴图、一格 = 8 像素，外圈是半透明的大方块，再用透明度连续淡入淡出，
//           看上去是一团糊掉的方块，和背景原画里“亮芯 + 一圈圈光晕”的像素画法不一样。
// v2：
//   · 光点按背景的像素画法逐格画：一格 = 背景上的 5 像素（和原画的像素颗粒一样大），
//     亮芯接近白色，往外依次是亮黄、黄绿、淡绿光晕，边缘是硬边阶梯，不做模糊；
//   · 4 档大小：大（7×7 格）/ 中 / 小 / 单个亮点；暖黄色为主，约三成是偏青的冷色光点；
//   · 闪烁不再用透明度渐变，而是在这 4 档之间跳帧：亮 → 变小 → 熄灭 → 再亮，像素游戏的做法；
//   · 位置对齐到同一套 5 像素网格，移动时不会出现半格的模糊；
//   · 运动（上飘、随风漂移、从地面重新出现）和网页版一致；不点 Play 在 Scene 窗口里也会动。
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MapKitTest
{
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    public class ForestFireflies : MonoBehaviour
    {
        [Header("数量与外观")]
        [Range(0, 120)] public int count = 40;
        [Tooltip("一格光点像素对应背景上几个像素（背景 64 像素 = 1 格地图）。5 = 和原画像素颗粒一样大")]
        public float texelInBgPixels = 5f;
        [Tooltip("冷色（偏青）光点的比例")] [Range(0f, 1f)] public float coolRatio = 0.3f;
        [Tooltip("比背景高几个排序层级（树叶是 +1）")] public int sortingOffset = 2;

        [Header("运动（单位：格/秒）")]
        public Vector2 riseSpeed = new Vector2(0.125f, 0.33f);
        [Tooltip("各自左右漂移的幅度")] public float wander = 0.21f;
        [Tooltip("被风带着走的幅度")] public float windDrift = 0.375f;
        [Tooltip("离顶部多远处消失、离地面多高处重新出现（格）")] public float topMargin = 0.6f, bottomMargin = 0.2f;
        [Tooltip("地面高度（格，从背景底边算起）")] public float groundHeight = 1f;

        [Header("闪烁")]
        [Tooltip("闪烁快慢")] public float flickerSpeed = 1.6f;
        [Tooltip("熄灭时间占比（0~1）")] [Range(0f, 0.8f)] public float offRatio = 0.4f;
        public int seed = 12345;

        // ---------- 像素画帧（7×7，'.' 为透明）----------
        // 等级 4 = 大，3 = 中，2 = 小，1 = 单个亮点
        static readonly string[][] Shapes =
        {
            null,
            new[] { ".......", ".......", ".......", "...c...", ".......", ".......", "......." },
            new[] { ".......", ".......", "...a...", "..aca..", "...a...", ".......", "......." },
            new[] { ".......", "..aaa..", ".abcba.", ".acWca.", ".abcba.", "..aaa..", "......." },
            new[] { "..aaa..", ".abbba.", "abcccba", "abcWcba", "abcccba", ".abbba.", "..aaa.." },
        };
        // 两套配色：W 亮芯 / c 亮色 / b 中间光晕 / a 外圈光晕（带透明度，叠在背景上自然变成绿色光晕）
        static readonly Color32[][] Palettes =
        {
            new Color32[] { new Color32(255,253,228,255), new Color32(246,255,104,255), new Color32(168,238,58,215), new Color32(78,190,58,105) },  // 暖黄
            new Color32[] { new Color32(236,255,246,255), new Color32(168,255,214,255), new Color32(86,222,170,205), new Color32(40,165,140,95) },  // 冷青
        };
        const int Cell = 7;

        class Fly { public float x, y, v, p; public int pal, maxLevel, level = -1; public SpriteRenderer sr; }
        const string RootName = "__Fireflies(自动生成)";
        readonly List<Fly> flies = new List<Fly>();
        readonly Sprite[,] sprites = new Sprite[2, 5];
        Transform root;
        Texture2D tex;
        System.Random rng;
        Rect area;
        float texel;           // 一格光点像素的世界尺寸
        double lastEditorTime;
        bool needRebuild;

        void OnEnable()
        {
            Build();
#if UNITY_EDITOR
            lastEditorTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= EditorTick;
            EditorApplication.update += EditorTick;
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.update -= EditorTick;
#endif
            Clear();
        }

        void OnValidate() { needRebuild = true; }

        static void Kill(Object o) { if (o == null) return; if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        void Clear()
        {
            var stale = transform.Find(RootName);
            if (stale != null) Kill(stale.gameObject);
            if (root != null) Kill(root.gameObject);
            for (int p = 0; p < 2; p++) for (int l = 0; l < 5; l++) { Kill(sprites[p, l]); sprites[p, l] = null; }
            Kill(tex);
            root = null; tex = null;
            flies.Clear();
        }

        // 把 2 套配色 × 4 档画进一张 28×14 的小图（Point 过滤、无压缩），每档切成一个 Sprite
        void MakeSprites(float ppu)
        {
            tex = new Texture2D(Cell * 4, Cell * 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave, name = "FireflyFrames"
            };
            var px = new Color32[tex.width * tex.height];
            for (int p = 0; p < 2; p++)
                for (int l = 1; l <= 4; l++)
                {
                    var shape = Shapes[l];
                    for (int y = 0; y < Cell; y++)
                        for (int x = 0; x < Cell; x++)
                        {
                            char ch = shape[y][x];
                            int idx = ch == 'W' ? 0 : ch == 'c' ? 1 : ch == 'b' ? 2 : ch == 'a' ? 3 : -1;
                            int tx = (l - 1) * Cell + x, ty = p * Cell + (Cell - 1 - y);   // 贴图从下往上
                            px[ty * tex.width + tx] = idx >= 0 ? Palettes[p][idx] : new Color32(0, 0, 0, 0);
                        }
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            for (int p = 0; p < 2; p++)
                for (int l = 1; l <= 4; l++)
                {
                    var sp = Sprite.Create(tex, new Rect((l - 1) * Cell, p * Cell, Cell, Cell), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
                    sp.hideFlags = HideFlags.DontSave;
                    sp.name = "Firefly_" + (p == 0 ? "warm" : "cool") + "_" + l;
                    sprites[p, l] = sp;
                }
        }

        void Build()
        {
            Clear();
            var bg = GetComponent<SpriteRenderer>();
            if (bg == null || bg.sprite == null) return;
            var b = bg.sprite.bounds;                                   // 背景在本地坐标里的范围（25×12 格）
            area = new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
            float bgPpu = bg.sprite.rect.width / Mathf.Max(0.001f, b.size.x);   // 背景实际每格像素数（正常是 64）
            texel = Mathf.Max(1f, texelInBgPixels) / bgPpu;
            MakeSprites(1f / texel);

            rng = new System.Random(seed);
            var go = new GameObject(RootName) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
            root = go.transform;
            root.SetParent(transform, false);

            for (int i = 0; i < count; i++)
            {
                double r = rng.NextDouble();
                var f = new Fly
                {
                    x = Rnd(area.xMin, area.xMax),
                    y = Rnd(area.yMin + groundHeight + bottomMargin, area.yMax - topMargin * 2f),
                    v = Rnd(riseSpeed.x, riseSpeed.y),
                    p = Rnd(0f, Mathf.PI * 2f),
                    pal = rng.NextDouble() < coolRatio ? 1 : 0,
                    maxLevel = r < 0.12 ? 4 : r < 0.47 ? 3 : r < 0.85 ? 2 : 1,   // 大 12% / 中 35% / 小 38% / 亮点 15%
                };
                var fg = new GameObject("Firefly_" + i.ToString("00")) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
                fg.transform.SetParent(root, false);
                f.sr = fg.AddComponent<SpriteRenderer>();
                f.sr.sortingLayerID = bg.sortingLayerID;
                f.sr.sortingOrder = bg.sortingOrder + sortingOffset;
                flies.Add(f);
            }
            Step(0f, CurrentTime());
        }

        float Rnd(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

        float CurrentTime()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return (float)(EditorApplication.timeSinceStartup % 10000.0);
#endif
            return Time.time;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            if (needRebuild) { needRebuild = false; Build(); }
            Step(Time.deltaTime, Time.time);
        }

#if UNITY_EDITOR
        void EditorTick()
        {
            if (this == null || Application.isPlaying) return;
            if (needRebuild) { needRebuild = false; Build(); }
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Min(0.05f, (float)(now - lastEditorTime));
            lastEditorTime = now;
            if (!EditorPrefs.GetBool("MapKit.WindPreviewInEditMode", true)) return;   // 菜单 Tools/MapKit/编辑模式预览风摇
            Step(dt, (float)(now % 10000.0));
            SceneView.RepaintAll();
        }
#endif

        // 和网页版同一股风：两层正弦 + 每约 13 秒一阵从左往右扫过的阵风
        static float Gust(float x, float t) { float c = Mathf.Repeat(t * 0.12f, 1.6f) - 0.3f; float d = x - c; return 0.9f * Mathf.Exp(-d * d * 18f); }
        static float Wind(float x, float t) { return Mathf.Sin(t * 0.9f + x * 5f) * 0.6f + Mathf.Sin(t * 2.1f + x * 13f) * 0.25f + Gust(x, t); }

        void Step(float dt, float t)
        {
            if (flies.Count == 0 || texel <= 0f) return;
            float top = area.yMax - topMargin, bottom = area.yMin + groundHeight + bottomMargin;
            float offThr = Mathf.Lerp(-1f, 1f, offRatio);   // 正弦低于这个值就熄灭
            foreach (var f in flies)
            {
                if (f.sr == null) continue;
                float xn = Mathf.InverseLerp(area.xMin, area.xMax, f.x);
                f.y += f.v * dt;
                f.x += (Mathf.Sin(t * 0.8f + f.p) * wander + Wind(xn, t) * windDrift) * dt;
                if (f.y > top) { f.y = bottom; f.x = Rnd(area.xMin, area.xMax); }
                if (f.x > area.xMax + 0.2f) f.x = area.xMin - 0.2f;
                if (f.x < area.xMin - 0.2f) f.x = area.xMax + 0.2f;

                // 亮度 0~1：一段时间熄灭，亮着时再有呼吸起伏；然后量化成 0~maxLevel 档，按档换帧（不做透明度渐变）
                float s = Mathf.Sin(t * flickerSpeed + f.p * 3f);
                float on = Mathf.InverseLerp(offThr, Mathf.Min(1f, offThr + 0.35f), s);
                float breath = 0.75f + 0.25f * Mathf.Sin(t * 2.2f + f.p);
                int level = Mathf.Clamp(Mathf.CeilToInt(on * breath * f.maxLevel - 0.15f), 0, f.maxLevel);
                if (level != f.level)
                {
                    f.level = level;
                    f.sr.enabled = level > 0;
                    if (level > 0) f.sr.sprite = sprites[f.pal, level];
                }

                // 对齐到背景的 5 像素网格（以背景左下角为原点），移动时不会半格模糊
                float gx = area.xMin + Mathf.Round((f.x - area.xMin) / texel) * texel + texel * 0.5f;
                float gy = area.yMin + Mathf.Round((f.y - area.yMin) / texel) * texel + texel * 0.5f;
                f.sr.transform.localPosition = new Vector3(gx, gy, 0f);
            }
        }
    }
}
