// Z2 蒸汽井盖：地面由井盖铺满。每 4~5 秒一批：随机约 20% 井口预警 1 秒（换成 warn 图 + 少量慢速 wisp），
// 1 秒后喷发（大量 puff 粒子向上 13~18 单位/秒、带阻力、逐渐变大淡出）。这批井口不连成 4 个以上。
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

        SpriteRenderer[] cells;        // 25 个井盖格子
        int batchCount;
        string lastEvent = "-";

        void Start()
        {
            base.Awake();
            if (cfg == null) return;
            var g = transform.Find("Ground_地面25格");
            if (g != null) cells = g.GetComponentsInChildren<SpriteRenderer>(true);
            Debug.Log("[机制验证] 蒸汽 井盖数=" + (cells != null ? cells.Length : 0));
            StartCoroutine(VentLoop());
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

                // 喷发：大量 puff 粒子
                foreach (int c in vents)
                {
                    StartCoroutine(Puff(c));
                    if (cells != null && c < cells.Length && cells[c] != null) cells[c].sprite = manholeSprite;
                }
                lastEvent = "喷发" + vents.Count + "口";
            }
        }

        // 选约 20% 井口，且不连成 4 个以上
        List<int> PickVents()
        {
            int n = cells != null ? cells.Length : 25;
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
