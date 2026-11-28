// Z1 雨夜竹林：全黑视野 + 折线落雷（LineRenderer 程序生成）。
// 普通雷(#7FA6FF)从顶部往下、不落地、停在画面30%~60%处并加分叉；
// 伤害雷(#B0127A, 2%)劈到某一格地面并在落点加光斑。
// 雨丝/溅落用粒子系统，透明度随闪电变化（平时3%、打雷时50%）。
using System.Collections;
using UnityEngine;

namespace MapKitTest
{
    public class BambooNightMechanic : ZoneMechanicBase
    {
        [Header("雨/溅落素材")]
        public Sprite rainDropSprite;
        public Sprite rainSplashSprite;

        SpriteRenderer cover;          // 全屏黑幕
        ParticleSystem rainPs;         // 雨丝粒子
        ParticleSystem splashPs;       // 溅落粒子
        float coverAlpha = 0.99f;
        int strikeCount, damageCount;
        string lastEvent = "-";

        void Start()
        {
            base.Awake();
            if (cfg == null) return;
            BuildScene();
            StartCoroutine(ThunderLoop());
        }

        void BuildScene()
        {
            // 全屏黑幕：平时 alpha=0.99，打雷时降到 0.13。
            // MakeSolidSprite 用 ppu=1 生成 2×2 单位的图，故缩放 12.5×6 才是 25×12 单位，
            // 严格限制在本区域内，不串到相邻地图。
            var go = new GameObject("FX_夜幕遮罩");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(12.5f, 6f, 0f);
            go.transform.localScale = new Vector3(12.5f, 6f, 1f);
            cover = go.AddComponent<SpriteRenderer>();
            cover.sprite = MakeSolidSprite("黑幕", Color.black, 1f, new Vector2(0.5f, 0.5f));
            cover.color = new Color(0f, 0f, 0f, cfg.bamboo.darknessAlpha);
            cover.sortingOrder = 100;

            rainPs = BuildRain("FX_雨丝", rainDropSprite != null ? rainDropSprite : MakeSolidSprite("雨", Color.white, 16f, new Vector2(0.5f, 0.5f)));
            splashPs = BuildSplash("FX_溅落", rainSplashSprite != null ? rainSplashSprite : MakeSolidSprite("溅", Color.white, 16f, new Vector2(0.5f, 0.5f)));
        }

        // 雨丝 / 溅落（2026-11-25 修正）
        // 旧版的问题：
        //  1. 粒子 sortingOrder=90，压在 sortingOrder=100 的黑幕下面，平时完全看不见；
        //  2. 打雷时改 main.startColor 只影响"之后新生成"的粒子，屏幕上已有的雨还是 3% 透明度，闪一下也看不出雨；
        //  3. 默认 Billboard 是正方形，16×128 的雨丝贴图被压成 0.05~0.12 的小方块，看上去是点不是线；
        //  4. 溅落从 y=0.6 发射，在地面图块里面（地面上沿是 y=1），还被当成小球往上抛。
        // 现在：雨画在黑幕之上（101），用材质 _Color 的透明度统一控制所有粒子，
        //       平时 3%、打雷时按亮度升到 55%；雨丝用 3D 尺寸拉成细长条；溅落贴在地面上沿原地绽开。
        Material rainMat, splashMat;
        const float RainAlphaDark = 0.03f, RainAlphaLit = 0.55f;

        ParticleSystem BuildRain(string name, Sprite sprite)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(12.5f, 12.4f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = 0.8f;                                    // 14 单位/秒 × 0.8 ≈ 11.2，正好落到地面上沿 y≈1
            main.startSpeed = new ParticleSystem.MinMaxCurve(13.5f, 14.5f);
            main.startSize3D = true;                                      // 细长雨丝：宽 0.02~0.04，长 0.35~0.6
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startSizeZ = 1f;
            main.startColor = new Color(0.85f, 0.9f, 1f, 1f);             // 透明度交给材质控制
            main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravityModifier = 0f;

            var em = ps.emission;
            em.rateOverTime = 420f;

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.rotation = new Vector3(90f, 0f, 0f);    // Box 默认沿 +Z 发射，转到 -Y（向下）
            sh.scale = new Vector3(25f, 0.1f, 0.6f);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            rainMat = new Material(Shader.Find("Sprites/Default")) { name = "Rain(运行时)" };
            rainMat.mainTexture = sprite.texture;
            r.sharedMaterial = rainMat;
            r.sortingOrder = 101;                      // 在黑幕(100)之上、闪电(102)之下

            ps.Play();
            return ps;
        }

        ParticleSystem BuildSplash(string name, Sprite sprite)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(12.5f, 1.06f, 0f);  // 贴着地面上沿
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.startLifetime = 0.2f;
            main.startSpeed = 0f;                                         // 原地绽开，不再往上抛
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.18f, 0.3f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.09f, 0.15f);
            main.startSizeZ = 1f;
            main.startColor = new Color(0.9f, 0.95f, 1f, 1f);
            main.maxParticles = 120;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var em = ps.emission;
            em.rateOverTime = 160f;

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(25f, 0.02f, 0.02f);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.2f));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            splashMat = new Material(Shader.Find("Sprites/Default")) { name = "Splash(运行时)" };
            splashMat.mainTexture = sprite.texture;
            r.sharedMaterial = splashMat;
            r.sortingOrder = 101;

            ps.Play();
            return ps;
        }

        // 每帧按黑幕亮度同步雨的透明度：黑幕 0.99→雨 3%，黑幕 0.13→雨 55%
        void Update()
        {
            if (cfg == null) return;
            float dark = cfg.bamboo.darknessAlpha;
            float light = Mathf.Clamp01(Mathf.InverseLerp(dark, CoverAlphaLit, coverAlpha));
            float a = Mathf.Lerp(RainAlphaDark, RainAlphaLit, light);
            if (rainMat != null) rainMat.color = new Color(1f, 1f, 1f, a);
            if (splashMat != null) splashMat.color = new Color(1f, 1f, 1f, a);
        }

        IEnumerator ThunderLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(cfg.bamboo.intervalSeconds.Random());
                Strike(false);
                if (Random.value < cfg.bamboo.extraChance)
                {
                    // 追加雷至少等上一发闪完（0.9×flashSeconds）再落，
                    // 否则 0~extraCheckEvery 的随机区间会把两次闪叠成一团密集频闪。
                    float gap = Mathf.Max(cfg.bamboo.flashSeconds * 0.9f,
                        Random.Range(0f, cfg.bamboo.extraCheckEvery));
                    yield return new WaitForSeconds(gap);
                    Strike(false);
                }
            }
        }

        void Strike(bool forceDamage)
        {
            strikeCount++;
            bool isDamage = forceDamage || Random.value < cfg.bamboo.damageChance;
            Color col = isDamage ? Hex(cfg.bamboo.damageColor) : Hex(cfg.bamboo.normalColor);
            float x = Random.Range(1f, 24f);

            // 伤害雷劈到地面 y=1；普通雷停在画面高度 30%~60%（= y 3.6~7.2）
            float endY = isDamage
                ? 1f
                : cfg.bamboo.normalBoltEndHeight.Random() * 12f;
            DrawBolt(x, endY, col, isDamage);

            lastEvent = isDamage ? "伤害雷" : "普通雷";
            if (isDamage)
            {
                damageCount++;
                StartCoroutine(GroundFlash(x, col));
            }
            StartCoroutine(FlashAndHide());
            Debug.Log("[机制验证] 竹林落雷#" + strikeCount + " " + lastEvent + " x=" + x.ToString("0.0"));
        }

        // 用 LineRenderer 生成折线闪电：每段向下 14~44px（PPU64），左右随机偏移
        void DrawBolt(float startX, float endY, Color col, bool toGround)
        {
            float px = 1f / 64f;
            var pts = new System.Collections.Generic.List<Vector3>();
            float cx = startX, cy = 12f;
            pts.Add(new Vector3(cx, cy, 0f));
            int guard = 0;
            while (cy > endY && guard++ < 200)
            {
                float dy = Random.Range(14f, 44f) * px;
                cy = Mathf.Max(endY, cy - dy);
                cx += Random.Range(-22f, 22f) * px;
                pts.Add(new Vector3(cx, cy, 0f));
            }
            MakeLine("FX_雷电主线", pts, col, 0.055f);

            // 普通雷加 2~4 条向下分叉，末端渐隐（不落地）
            if (!toGround)
            {
                int branches = Random.Range(2, 5);
                for (int b = 0; b < branches; b++)
                {
                    int idx = Random.Range(1, Mathf.Max(2, pts.Count - 1));
                    var bp = new System.Collections.Generic.List<Vector3> { pts[idx] };
                    float bx = pts[idx].x, by = pts[idx].y;
                    int steps = Random.Range(2, 5);
                    for (int s = 0; s < steps; s++)
                    {
                        by = Mathf.Max(endY, by - Random.Range(14f, 30f) * px);
                        bx += Random.Range(-18f, 18f) * px;
                        bp.Add(new Vector3(bx, by, 0f));
                    }
                    MakeLine("FX_雷电分叉", bp, col, 0.025f);
                }
            }
        }

        void MakeLine(string name, System.Collections.Generic.List<Vector3> pts, Color col, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = pts.Count;
            lr.SetPositions(pts.ToArray());
            lr.startColor = col;
            lr.endColor = new Color(col.r, col.g, col.b, 0.15f);
            lr.startWidth = width;
            lr.endWidth = width * 0.4f;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.sortingOrder = 102;
            // 存活时间跟住一次完整闪光（含第二次亮），否则闪到第二下时闪电已经消失
            Destroy(go, Mathf.Max(0.2f, cfg.bamboo.flashSeconds * 0.78f));
        }

        IEnumerator GroundFlash(float x, Color col)
        {
            var go = new GameObject("FX_落点光斑");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(Mathf.Floor(x) + 0.5f, 0.5f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = MakeSolidSprite("光斑", col, 1f, new Vector2(0.5f, 0.5f));
            sr.sortingOrder = 103;
            float t = 0f;
            while (t < 0.4f)
            {
                t += Time.deltaTime;
                float a = 1f - t / 0.4f;
                sr.color = new Color(col.r, col.g, col.b, a);
                // sprite 是 2×2 单位，缩放到约 1~2 单位覆盖一格落点
                go.transform.localScale = Vector3.one * (0.25f + t * 1f);
                yield return null;
            }
            Destroy(go);
        }

        // 黑幕「打亮」时的透明度；Update() 用它反推雨的亮度，两处必须同源
        const float CoverAlphaLit = 0.13f;

        IEnumerator FlashAndHide()
        {
            // 2026-11-28 放慢：旧版硬编码「亮 0.05s → 暗 0.04s → 亮 0.05s」，
            // 50/40ms 的白黑对切是最刺眼的频闪来源，而且不随 flashSeconds 变化。
            // 现在四个阶段全部按 flashSeconds 比例分配，只要调配置就能整体减速。
            float dark = cfg.bamboo.darknessAlpha;
            float total = Mathf.Max(0.2f, cfg.bamboo.flashSeconds);

            float lit1 = total * 0.20f;   // 第一次亮
            float dim = total * 0.17f;   // 中间回落（不到全黑，避免硬对切）
            float lit2 = total * 0.20f;   // 第二次亮
            float fade = Mathf.Max(0.05f, total - lit1 - dim - lit2);

            coverAlpha = CoverAlphaLit; ApplyCover();
            yield return new WaitForSeconds(lit1);

            coverAlpha = Mathf.Lerp(CoverAlphaLit, dark, 0.55f); ApplyCover();
            yield return new WaitForSeconds(dim);

            coverAlpha = CoverAlphaLit; ApplyCover();
            yield return new WaitForSeconds(lit2);

            float t = 0f;
            while (t < fade)
            {
                t += Time.deltaTime;
                coverAlpha = Mathf.Lerp(CoverAlphaLit, dark, t / fade);
                ApplyCover();
                yield return null;
            }
            coverAlpha = dark; ApplyCover();
        }

        void ApplyCover()
        {
            if (cover != null) cover.color = new Color(0f, 0f, 0f, coverAlpha);
        }

        public override string StatusText()
        {
            return "落雷 " + strikeCount + " 次（伤害雷 " + damageCount + "），最近：" + lastEvent;
        }
    }
}
