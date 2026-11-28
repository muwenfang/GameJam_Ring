// Z2 蒸汽井盖：地面由 25 个固定 1×1 的井盖格子铺满。每 4~5 秒一批：随机约 20% 井口预警 1 秒
// （换成 warn 图 + 少量慢速 wisp），1 秒后喷发（大量 puff 粒子向上 13~18 单位/秒、带阻力、
// 逐渐变大淡出）。这批井口不连成 4 个以上。
//
// 2026-11-25 修复「只剩一个会动的井盖」：
// 旧实现直接对 "Ground_地面25格" 调 GetComponentsInChildren<SpriteRenderer>(true)，
// 该方法包含地面节点自身，而场景里该节点只有 1 张被拉成长条的井盖贴图、没有子格，
// 于是 cells.Length==1，PickVents 的采样上限变成 1，喷烟永远只在 x=0.5 一处。
// 现在改为：
//   1) 优先用 Inspector 指定 / 场景里已存在的 "Manholes_25格" 容器（用户手工搭的优先）；
//   2) 没搭够且允许兜底时，运行时生成 25 个固定 1×1 的井盖格子排成一排（开关可关）；
//   3) cells 只取这 25 个格子并按局部 X 排序，保证索引 = 25×12 设计坐标的列号；
//   4) 生成成功后隐藏地面节点自身那张长条贴图，避免与 25 个格子重叠。
// 刻意不引入 using System; —— 本文件多处用 UnityEngine.Random，
// 引入 System 会让 Random 变成二义性引用（CS0104）。需要 Array.Sort 时用全限定名。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MapKitTest
{
    public class SteamMechanic : ZoneMechanicBase
    {
        [Header("素材引用")]
        public Sprite manholeSprite;   // 正常井盖（地面贴图）
        public Sprite warnSprite;      // 预警井盖
        public Sprite puffSprite;      // 喷发蒸汽
        public Sprite wispSprite;      // 预警轻烟

        [Header("井盖布局（25×12 设计格）")]
        [Tooltip("一排井盖的数量；25 格地面就是 25 个。")]
        [SerializeField] private int manholeCount = 25;

        [Tooltip("单个井盖在设计坐标系里的宽度（格）。固定值，不随地面长条拉伸。")]
        [SerializeField] private float manholeWidthCells = 1f;

        [Tooltip("井盖中心的局部 Y（设计格）。地面带中心一般是 0.5。")]
        [SerializeField] private float manholeLocalY = 0.5f;

        [Tooltip("井盖绘制层级：地面 -10、角色约 -4，井盖取 -9 夹在中间。")]
        [SerializeField] private int manholeSortingOrder = -9;

        [Tooltip("可选：手工搭好的井盖容器。留空时自动找名为 Manholes_25格 的子节点。")]
        [SerializeField] private Transform manholeContainer;

        [Tooltip("开：场景里没搭够井盖格子时，运行时按固定 1×1 生成一排（兜底）。关：只用已有格子。")]
        [SerializeField] private bool buildManholesIfMissing = true;

        private const string ContainerName = "Manholes_25格";
        private const string GroundChildName = "Ground_地面25格";

        SpriteRenderer[] cells;        // 25 个井盖格子（索引 = 列号）
        Sprite resolvedNormalSprite;   // 喷发结束后还原用的正常井盖图
        int batchCount;
        string lastEvent = "-";

        void Start()
        {
            base.Awake();
            if (cfg == null) return;
            cells = ResolveManholeCells();
            Debug.Log("[机制验证] 蒸汽 井盖数=" + (cells != null ? cells.Length : 0));
            StartCoroutine(VentLoop());
        }

        /// <summary>
        /// 井盖格子解析：手工容器优先 → 运行时兜底生成 → 旧逻辑（可能为 1 个）。
        /// 返回数组按局部 X 排序，保证 cells[c] 对应 25×12 设计坐标的第 c 列，
        /// 这样 Wisp/Puff 里的 (cell + 0.5) 才能和井盖位置对齐。
        /// </summary>
        SpriteRenderer[] ResolveManholeCells()
        {
            Transform container = manholeContainer;
            if (container == null)
            {
                container = transform.Find(ContainerName);
            }

            if (container != null)
            {
                SpriteRenderer[] existing = CollectCells(container);
                if (existing.Length >= 2)
                {
                    resolvedNormalSprite = ResolveNormalSprite(existing[0]);
                    return SortByColumn(existing, container);
                }
            }

            if (!buildManholesIfMissing)
            {
                SpriteRenderer[] fallback = FallbackGroundCells();
                resolvedNormalSprite = fallback.Length > 0 ? ResolveNormalSprite(fallback[0]) : manholeSprite;
                return fallback;
            }

            int count = Mathf.Max(1, manholeCount);
            if (container == null)
            {
                var go = new GameObject(ContainerName);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.zero;
                container = go.transform;
                manholeContainer = container;
            }

            // 容器已存在但格子不够：只补缺的数量，不重复创建已有格子。
            SpriteRenderer[] current = CollectCells(container);
            if (current.Length < count)
            {
                for (int c = current.Length; c < count; c++)
                {
                    CreateManhole(container, c);
                }
            }

            SpriteRenderer[] built = CollectCells(container);
            resolvedNormalSprite = built.Length > 0 ? ResolveNormalSprite(built[0]) : manholeSprite;

            // 地面节点自身那张贴图被拉成了长条，会和 25 个格子重叠；生成成功后隐藏它。
            // 只影响本 Z2 根节点，不动碰撞体，也不动其它五张地图。
            HideStretchedGroundSprite();
            return SortByColumn(built, container);
        }

        /// <summary>取容器下所有 SpriteRenderer，但排除容器自身（容器只挂 Transform）。</summary>
        static SpriteRenderer[] CollectCells(Transform container)
        {
            var all = container.GetComponentsInChildren<SpriteRenderer>(true);
            var list = new List<SpriteRenderer>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].transform != container)
                {
                    list.Add(all[i]);
                }
            }

            return list.ToArray();
        }

        /// <summary>生成一个固定大小的井盖格子，位置按 25×12 设计坐标的第 column 列。</summary>
        void CreateManhole(Transform container, int column)
        {
            var go = new GameObject("井盖_" + column.ToString("00"));
            go.transform.SetParent(container, false);
            // 第 column 列中心 = column + 0.5，与 Wisp/Puff 的 x 完全对齐。
            go.transform.localPosition = new Vector3(column + 0.5f, manholeLocalY, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            Sprite sprite = manholeSprite != null ? manholeSprite : GroundFallbackSprite();
            sr.sprite = sprite != null
                ? sprite
                : MakeSolidSprite("井盖", Hex("#4A4A52"), 16f, new Vector2(0.5f, 0.5f));
            sr.sortingOrder = manholeSortingOrder;
            sr.color = Color.white;

            // 固定大小：按 sprite 原始宽度等比缩放到 manholeWidthCells 格。
            // 25 个格子用同一张图、同一个缩放系数，因此大小完全一致、不会变形，
            // 也不会出现「一张长条贴图被拉成 25 格宽」的情况。
            if (sr.sprite != null)
            {
                float width = sr.sprite.bounds.size.x;
                float scale = width > 0.0001f ? Mathf.Max(0.01f, manholeWidthCells) / width : 1f;
                go.transform.localScale = new Vector3(scale, scale, 1f);
            }
            else
            {
                go.transform.localScale = Vector3.one;
            }
        }

        /// <summary>喷发结束后还原用的正常井盖图；Inspector 没拖时借地面贴图或格子当前图。</summary>
        Sprite ResolveNormalSprite(SpriteRenderer sample)
        {
            if (manholeSprite != null) return manholeSprite;
            Sprite ground = GroundFallbackSprite();
            if (ground != null) return ground;
            return sample != null ? sample.sprite : null;
        }

        Sprite GroundFallbackSprite()
        {
            Transform ground = transform.Find(GroundChildName);
            if (ground == null) return null;
            var sr = ground.GetComponent<SpriteRenderer>();
            return sr != null ? sr.sprite : null;
        }

        void HideStretchedGroundSprite()
        {
            Transform ground = transform.Find(GroundChildName);
            if (ground == null) return;
            var sr = ground.GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;
        }

        /// <summary>兜底关闭时退回旧行为（可能只有 1 个格子），保留是为了不破坏手工搭好的场景。</summary>
        SpriteRenderer[] FallbackGroundCells()
        {
            Transform ground = transform.Find(GroundChildName);
            return ground != null
                ? ground.GetComponentsInChildren<SpriteRenderer>(true)
                : new SpriteRenderer[0];
        }

        /// <summary>按相对容器的局部 X 排序，索引即列号。</summary>
        static SpriteRenderer[] SortByColumn(SpriteRenderer[] source, Transform container)
        {
            System.Array.Sort(source, (a, b) =>
            {
                float ax = container.InverseTransformPoint(a.transform.position).x;
                float bx = container.InverseTransformPoint(b.transform.position).x;
                return ax.CompareTo(bx);
            });
            return source;
        }

        IEnumerator VentLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(cfg.steam.batchInterval.Random());
                var vents = PickVents();
                if (vents.Count == 0) continue;

                // 预警 1 秒：井盖换 warn 图 + 少量慢速 wisp
                foreach (int c in vents)
                {
                    if (cells != null && c < cells.Length && cells[c] != null) cells[c].sprite = warnSprite;
                    StartCoroutine(Wisp(c));
                }
                lastEvent = "预警" + vents.Count + "口";
                Debug.Log("[机制验证] 蒸汽批次#" + (++batchCount) + " 预警井口=" + string.Join(",", vents));
                yield return new WaitForSeconds(cfg.steam.warningSeconds);

                // 喷发：大量 puff 粒子，井盖还原成正常图
                foreach (int c in vents)
                {
                    StartCoroutine(Puff(c));
                    if (cells != null && c < cells.Length && cells[c] != null && resolvedNormalSprite != null)
                        cells[c].sprite = resolvedNormalSprite;
                }
                lastEvent = "喷发" + vents.Count + "口";
            }
        }

        // 选约 20% 井口，且不连成 4 个以上
        List<int> PickVents()
        {
            int n = cells != null && cells.Length > 0 ? cells.Length : 25;
            int want = Mathf.Max(1, Mathf.RoundToInt(n * cfg.steam.ventRatio));
            var picked = new List<int>();
            int guard = 0;
            while (picked.Count < want && guard++ < 500)
            {
                int c = Random.Range(0, n);
                if (picked.Contains(c)) continue;
                picked.Add(c);
                picked.Sort();
                int run = 1;
                for (int i = 1; i < picked.Count; i++)
                {
                    if (picked[i] == picked[i - 1] + 1)
                    {
                        run++;
                        if (run >= 4) { picked.Remove(c); break; }
                    }
                    else run = 1;
                }
            }
            return picked;
        }

        IEnumerator Wisp(int cell)
        {
            for (int k = 0; k < 3; k++)
            {
                var go = NewFx("FX_井盖轻烟", wispSprite, new Vector3(cell + 0.5f, 0.6f + k * 0.25f, 0f), 80);
                var sr = go.GetComponent<SpriteRenderer>();
                float t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime;
                    go.transform.localPosition += Vector3.up * 0.6f * Time.deltaTime;
                    sr.color = new Color(1f, 1f, 1f, 0.5f * (1f - t));
                    yield return null;
                }
                Destroy(go);
            }
        }

        IEnumerator Puff(int cell)
        {
            for (int k = 0; k < 10; k++)
            {
                var go = NewFx("FX_蒸汽喷发", puffSprite, new Vector3(cell + 0.5f + Random.Range(-0.25f, 0.25f), 0.6f, 0f), 85);
                var sr = go.GetComponent<SpriteRenderer>();
                float vy = Random.Range(13f, 18f);
                float t = 0f, life = 0.7f;
                float sc = Random.Range(0.3f, 0.6f);
                go.transform.localScale = Vector3.one * sc;
                while (t < life)
                {
                    t += Time.deltaTime;
                    vy -= 14f * Time.deltaTime;
                    go.transform.localPosition += Vector3.up * vy * Time.deltaTime;
                    go.transform.localScale = Vector3.one * (sc + t * 0.9f);
                    sr.color = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - t / life) * 0.85f);
                    yield return null;
                }
                Destroy(go);
            }
        }

        GameObject NewFx(string name, Sprite sprite, Vector3 localPos, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite != null ? sprite : MakeSolidSprite("烟", Color.white, 16f, new Vector2(0.5f, 0.5f));
            sr.sortingOrder = order;
            return go;
        }

        public override string StatusText() { return "蒸汽批次 " + batchCount + "，最近：" + lastEvent; }
    }
}
