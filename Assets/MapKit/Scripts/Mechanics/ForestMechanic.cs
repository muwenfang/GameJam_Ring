// Z4 精灵森林（层林）：每 4~6 秒一批藤蔓在随机位置生长；冒芽 0.7 秒→弹出→成熟摆动保持 2 秒→凋亡 0.3 秒→销毁。
// 成批数量约为场内 25 格的 20%，散落随机、不连成 4 株以上。（抓取/缠绕需角色参与，纯地图演示不含）
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MapKitTest
{
    public class ForestMechanic : ZoneMechanicBase
    {
        [Header("藤蔓动画帧")]
        public Sprite[] sproutFrames;   // gv_sprout_1/2/3
        public Sprite popFrame;         // gv_pop
        public Sprite[] matureFrames;   // gv_mature_1/2
        public Sprite[] witherFrames;   // gv_wither_1/2/3

        [Header("藤蔓随风摆动（留空则自动用 MapKit/WindSwayBackground 生成）")]
        public Material vineMaterial;
        [Tooltip("地面上沿的高度（单位）。背景里画的地面线在 y=1")]
        public float groundSurfaceY = 1f;
        [Tooltip("藤蔓是否用整图扭曲着色器摆动。默认关：扭曲会产生水波纹感，成熟期的两帧动画本身已经在摆")]
        public bool useWindShaderOnVines = false;
        Material vineMatRuntime;

        int batchCount;
        string lastEvent = "-";

        void Start()
        {
            base.Awake();
            if (cfg == null) return;
            vineMatRuntime = vineMaterial != null ? vineMaterial : (useWindShaderOnVines ? BuildVineMaterial() : null);
            Debug.Log("[机制验证] 层林 动画帧 sprout=" + (sproutFrames != null ? sproutFrames.Length : 0)
                      + " mature=" + (matureFrames != null ? matureFrames.Length : 0));
            StartCoroutine(GrowLoop());
        }

        // 藤蔓用和背景同一个风摇着色器：贴图 43×37，位移按 1 像素对齐，底部几个像素不动
        Material BuildVineMaterial()
        {
            var sh = Shader.Find("MapKit/WindSwayBackground");
            if (sh == null) { Debug.LogWarning("[机制] 找不到 MapKit/WindSwayBackground，藤蔓不摆动"); return null; }
            var m = new Material(sh) { name = "GV_WindSway(运行时)" };
            m.SetFloat("_Amp", 0.03f);
            m.SetFloat("_GroundV", 0.06f);
            m.SetFloat("_PixelStep", 1f);
            m.SetFloat("_ZoneWidth", 25f);
            m.SetFloat("_LeafJitter", 0.15f);
            m.SetVector("_TexSize", new Vector4(43f, 37f, 0f, 0f));
            return m;
        }

        IEnumerator GrowLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(cfg.forest.batchInterval.Random());
                var cells = PickCells();
                Debug.Log("[机制验证] 层林生长批次#" + (++batchCount) + " 格=" + string.Join(",", cells));
                lastEvent = "生长" + cells.Count + "株";
                foreach (int c in cells) StartCoroutine(GrowPlant(c));
            }
        }

        List<int> PickCells()
        {
            int want = Mathf.Max(1, Mathf.RoundToInt(25 * cfg.forest.ratioOfEmpty));
            var picked = new List<int>();
            int guard = 0;
            while (picked.Count < want && guard++ < 500)
            {
                int c = Random.Range(0, 25);
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

        IEnumerator GrowPlant(int cell)
        {
            var go = new GameObject("FX_藤蔓_格" + (cell + 1).ToString("00"));
            go.transform.SetParent(transform, false);
            // 旧版放在 y=0，等于埋进了 1 格厚的地面里；轴心在贴图底部，放到地面上沿
            go.transform.localPosition = new Vector3(cell + 0.5f, groundSurfaceY, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            if (vineMatRuntime != null) sr.sharedMaterial = vineMatRuntime;
            sr.sortingOrder = 6;
            sr.flipX = Random.value < 0.5f;

            // 冒芽 0.7 秒（3 帧）
            yield return Animate(sr, sproutFrames, cfg.forest.growSeconds);
            // 弹出
            if (popFrame != null) { sr.sprite = popFrame; yield return new WaitForSeconds(0.12f); }
            // 成熟摆动保持 2 秒（2 帧循环）
            float t = 0f;
            while (t < cfg.forest.matureSeconds)
            {
                if (matureFrames != null && matureFrames.Length > 0)
                    sr.sprite = matureFrames[(Mathf.FloorToInt(t * 3f) % matureFrames.Length)];
                t += Time.deltaTime;
                yield return null;
            }
            // 凋亡 0.3 秒（3 帧）
            yield return Animate(sr, witherFrames, cfg.forest.witherSeconds);
            Destroy(go);
        }

        IEnumerator Animate(SpriteRenderer sr, Sprite[] frames, float dur)
        {
            if (frames == null || frames.Length == 0) { yield return new WaitForSeconds(dur); yield break; }
            float step = dur / frames.Length;
            for (int i = 0; i < frames.Length; i++)
            {
                sr.sprite = frames[i];
                yield return new WaitForSeconds(step);
            }
        }

        public override string StatusText() { return "层林生长批次 " + batchCount + "，最近：" + lastEvent; }
    }
}
