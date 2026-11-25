// 精灵森林：树叶自然摆动 v2（2026-11-25）
// v1 的问题：① 新图按默认 PPU=100 导入，叶团（按 64 算）比背景大一圈、飘在外面；
//           ② 每团叶子按 x 方向的正弦波各自平移，相邻叶团此起彼伏，看着像水波纹，不像风吹树叶。
// v2 做法（像素游戏里常用的手法）：
//   · 每个叶团预先画好 5 帧：挂点（顶部）不动，越往下越偏，偏移按 4 像素一格整块移动，像素不变形；
//   · 整片树冠一起慢慢摆（同一相位），阵风大约每 20 秒从左往右慢慢吹过，到哪团哪团就被吹偏、抖两下；
//   · 帧切换带回差，不会在两帧之间来回闪；
//   · 尺寸和位置按背景 Sprite 实际的 PPU 计算，导入设置不对也能对齐。
using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MapKitTest
{
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    public class ForestLeafSway : MonoBehaviour
    {
        [Serializable] public class Frame { public int ax, ay; }
        [Serializable] public class Clump { public int x, y, w, h; public Frame[] frames; public float phase, amp; }
        [Serializable] public class Data { public int version, texWidth, texHeight, atlasWidth, atlasHeight, pixelStep, frameCount; public Clump[] clumps; }

        [Header("素材")]
        public Texture2D leafAtlas;     // elf_forest_leaf_atlas.png
        public TextAsset clumpData;     // elf_forest_leaf_clumps.json

        [Header("摆动")]
        [Tooltip("整体摆动强度，1 = 最大用到第 ±2 帧")] [Range(0f, 1.5f)] public float strength = 1f;
        [Tooltip("整体速度倍率")] [Range(0.2f, 3f)] public float speed = 1f;
        [Tooltip("阵风强度")] [Range(0f, 2f)] public float gustStrength = 1.1f;
        [Tooltip("帧切换回差，越大越稳、越不容易来回闪")] [Range(0.5f, 0.95f)] public float hysteresis = 0.65f;
        [Tooltip("叶团比背景高几个排序层级")] public int sortingOffset = 1;

        const string RootName = "__LeafClumps(自动生成)";
        Data data;
        Transform root;
        readonly List<SpriteRenderer> leaves = new List<SpriteRenderer>();
        readonly List<Sprite[]> clumpSprites = new List<Sprite[]>();
        readonly List<int> current = new List<int>();
        readonly List<float> xNorm = new List<float>();
        bool needRebuild;

        void OnEnable()
        {
            Build();
#if UNITY_EDITOR
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

        static void Kill(UnityEngine.Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        void Clear()
        {
            foreach (var arr in clumpSprites) foreach (var sp in arr) if (sp != null) Kill(sp);
            clumpSprites.Clear();
            if (root != null) Kill(root.gameObject);
            var stale = transform.Find(RootName);
            if (stale != null) Kill(stale.gameObject);
            root = null; leaves.Clear(); current.Clear(); xNorm.Clear();
        }

        void Build()
        {
            Clear();
            if (leafAtlas == null || clumpData == null) return;
            data = JsonUtility.FromJson<Data>(clumpData.text);
            if (data == null || data.clumps == null || data.clumps.Length == 0) return;
            if (data.frameCount <= 0 || data.clumps[0].frames == null || data.clumps[0].frames.Length == 0)
            {
                Debug.LogError("[MapKit] elf_forest_leaf_clumps.json 是旧格式，请用 v2 的数据文件");
                return;
            }

            var bg = GetComponent<SpriteRenderer>();
            // 按背景 Sprite 实际的 PPU 换算：背景贴图宽 texWidth 像素
            float ppu = 64f;
            if (bg.sprite != null && bg.sprite.rect.width > 0)
                ppu = bg.sprite.pixelsPerUnit * data.texWidth / bg.sprite.rect.width;
            Vector2 pv = bg.sprite != null ? new Vector2(bg.sprite.pivot.x / bg.sprite.rect.width, bg.sprite.pivot.y / bg.sprite.rect.height) : new Vector2(0.5f, 0.5f);
            // 背景轴心不在中心时也能对齐
            float left = -pv.x * data.texWidth / ppu, top = (1f - pv.y) * data.texHeight / ppu;

            leafAtlas.filterMode = FilterMode.Point;

            var go = new GameObject(RootName) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
            root = go.transform;
            root.SetParent(transform, false);

            for (int i = 0; i < data.clumps.Length; i++)
            {
                var c = data.clumps[i];
                var arr = new Sprite[c.frames.Length];
                for (int f = 0; f < c.frames.Length; f++)
                {
                    // 轴心在顶部中间：叶子从树枝上吊着晃
                    arr[f] = Sprite.Create(leafAtlas, new Rect(c.frames[f].ax, c.frames[f].ay, c.w, c.h), new Vector2(0.5f, 1f), ppu, 0, SpriteMeshType.FullRect);
                    arr[f].name = "LeafClump_" + i + "_" + f;
                    arr[f].hideFlags = HideFlags.DontSave;
                }
                clumpSprites.Add(arr);

                var lg = new GameObject("Leaf_" + i.ToString("00")) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
                lg.transform.SetParent(root, false);
                lg.transform.localPosition = new Vector3(left + (c.x + c.w * 0.5f) / ppu, top - c.y / ppu, 0f);

                var r = lg.AddComponent<SpriteRenderer>();
                r.sprite = arr[arr.Length / 2];
                r.sortingLayerID = bg.sortingLayerID;
                r.sortingOrder = bg.sortingOrder + sortingOffset;
                r.color = bg.color;
                r.flipX = bg.flipX;
                leaves.Add(r);
                current.Add(0);
                xNorm.Add((c.x + c.w * 0.5f) / data.texWidth);
            }
            Tick(CurrentTime());
        }

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
            Tick(Time.time);
        }

#if UNITY_EDITOR
        void EditorTick()
        {
            if (this == null || Application.isPlaying) return;
            if (needRebuild) { needRebuild = false; Build(); }
            if (!EditorPrefs.GetBool("MapKit.WindPreviewInEditMode", true)) return;   // 菜单 Tools/MapKit/编辑模式预览风摇
            Tick(CurrentTime());
            SceneView.RepaintAll();
        }
#endif

        // 整片树冠同相位慢摆 + 一阵慢慢从左往右吹过的阵风 + 每团一点点自己的晃
        float Sway(float xn, float t, Clump c)
        {
            float gc = Mathf.Repeat(t * 0.09f, 1.8f) - 0.4f;
            float d = (xn - gc) / 0.22f;
            float gust = Mathf.Exp(-d * d);
            float s = 0.45f * Mathf.Sin(t * 0.75f) + 0.2f * Mathf.Sin(t * 1.31f + 0.9f);
            s += gustStrength * gust * (0.6f + 0.4f * Mathf.Sin(t * 2.4f + c.phase));
            s += 0.12f * Mathf.Sin(t * 2.9f + c.phase);
            return s * c.amp * strength;
        }

        void Tick(float time)
        {
            if (data == null || leaves.Count == 0) return;
            float t = time * speed;
            int kmax = (data.frameCount - 1) / 2;
            for (int i = 0; i < leaves.Count; i++)
            {
                if (leaves[i] == null) continue;
                float v = Sway(xNorm[i], t, data.clumps[i]) * kmax;
                int k = current[i];
                if (v > k + hysteresis) k++;
                else if (v < k - hysteresis) k--;
                k = Mathf.Clamp(k, -kmax, kmax);
                if (k != current[i] || leaves[i].sprite == null)
                {
                    current[i] = k;
                    int fi = k + kmax;
                    if (leaves[i].flipX) fi = data.frameCount - 1 - fi;   // 背景翻转时风向保持一致
                    leaves[i].sprite = clumpSprites[i][fi];
                }
            }
        }
    }
}
