using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ring.Flow;
using UnityEngine;

namespace Ring.Characters
{
    /// <summary>
    /// 精灵游侠超我「缚地荆棘」的地形表现与「离开即缠绕」判定。
    ///
    /// 表现：BrambleGrow（8 帧 16fps，0.5s 长出）→ BrambleIdle（6 帧 8fps 循环）
    /// → BrambleFade（3 帧 10fps，0.3s 消散）→ 销毁。
    /// 素材 876×292、PPU 292，正好是 3 格长 × 1 格宽；摆放在施法时锁定的朝向轴上。
    ///
    /// 判定：只对「曾经在范围内、之后走出矩形」的目标生效，命中即定身（默认 0.75s）；
    /// 施法者本人不受影响（规格：游侠自身穿越不受影响）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ElfRangerBrambleZone : MonoBehaviour
    {
        private const string ResourceFolder = "ElfRanger";
        private const float PixelsPerUnit = 292f;

        [SerializeField, Min(1f)] private float growFps = 16f;
        [SerializeField, Min(1f)] private float idleFps = 8f;
        [SerializeField, Min(1f)] private float fadeFps = 10f;
        [SerializeField, Min(0f)] private float exitRootSeconds = 0.75f;

        private Sprite[] grow, idle, fade;
        private SpriteRenderer spriteRenderer;
        private readonly HashSet<int> inside = new HashSet<int>();
        private readonly List<int> leaving = new List<int>(8);

        private int ownerId;
        private float lengthCells = 3f;
        private float widthCells = 1f;
        private Vector2 lengthAxis = Vector2.right;
        private float duration = 2f;
        private Coroutine routine;

        /// <summary>
        /// 在 <paramref name="center"/> 生成一片荆棘。<paramref name="direction"/> 决定长边朝向，
        /// <paramref name="ownerId"/> 为施法者（不受缠绕）。
        /// </summary>
        public static ElfRangerBrambleZone Spawn(Vector3 center, Vector2 direction,
            float lengthCells, float widthCells, float duration, int ownerId)
        {
            GameObject go = new GameObject("BrambleZone");
            go.transform.position = center;

            ElfRangerBrambleZone zone = go.AddComponent<ElfRangerBrambleZone>();
            zone.Init(direction, lengthCells, widthCells, duration, ownerId);
            return zone;
        }

        private void Init(Vector2 direction, float length, float width, float lifetime, int owner)
        {
            lengthCells = Mathf.Max(0.5f, length);
            widthCells = Mathf.Max(0.5f, width);
            duration = Mathf.Max(0.2f, lifetime);
            ownerId = owner;

            if (direction.sqrMagnitude > 0.0001f)
            {
                lengthAxis = direction.normalized;
            }

            float angle = Mathf.Atan2(lengthAxis.y, lengthAxis.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);

            LoadFrames();
            EnsureRenderer();

            if (spriteRenderer != null && grow.Length > 0)
            {
                spriteRenderer.sprite = grow[0];
            }

            routine = StartCoroutine(PlaySequence());
        }

        private void Update()
        {
            // 逐帧刷新「谁在荆棘里」，离开者立刻结算缠绕。
            CharacterInstance[] all = FindObjectsByType<CharacterInstance>(FindObjectsInactive.Exclude);
            leaving.Clear();

            for (int i = 0; i < all.Length; i++)
            {
                CharacterInstance candidate = all[i];
                if (candidate == null || candidate.IsDead || candidate.Id == ownerId)
                {
                    continue;
                }

                if (candidate.Participant != null && !candidate.Participant.IsInPlay)
                {
                    continue;
                }

                bool inZone = Contains(candidate.transform.position);
                if (inZone)
                {
                    inside.Add(candidate.Id);
                }
                else if (inside.Contains(candidate.Id))
                {
                    inside.Remove(candidate.Id);
                    leaving.Add(candidate.Id);
                }
            }

            for (int i = 0; i < leaving.Count; i++)
            {
                CharacterInstance victim = FindById(all, leaving[i]);
                if (victim != null && !victim.IsDead && exitRootSeconds > 0f)
                {
                    victim.ApplyRoot(exitRootSeconds);
                }
            }
        }

        private bool Contains(Vector3 worldPosition)
        {
            Vector2 offset = (Vector2)(worldPosition - transform.position);
            float along = Mathf.Abs(Vector2.Dot(offset, lengthAxis));
            float across = Mathf.Abs(Vector2.Dot(offset, new Vector2(-lengthAxis.y, lengthAxis.x)));
            return along <= lengthCells * 0.5f && across <= widthCells * 0.5f;
        }

        private static CharacterInstance FindById(CharacterInstance[] all, int id)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].Id == id)
                {
                    return all[i];
                }
            }

            return null;
        }

        private IEnumerator PlaySequence()
        {
            float growTime = 1f / Mathf.Max(1f, growFps);
            for (int i = 0; i < grow.Length; i++)
            {
                SetSprite(grow[i]);
                yield return new WaitForSeconds(growTime);
            }

            // 保持段：扣掉长出与消散的时间，剩余时长循环播放 idle。
            float tail = fade.Length / Mathf.Max(1f, fadeFps);
            float holdSeconds = Mathf.Max(0f, duration - grow.Length / Mathf.Max(1f, growFps) - tail);
            float idleTime = 1f / Mathf.Max(1f, idleFps);
            float elapsed = 0f;
            int index = 0;

            while (elapsed < holdSeconds && idle.Length > 0)
            {
                SetSprite(idle[index]);
                index = (index + 1) % idle.Length;
                elapsed += idleTime;
                yield return new WaitForSeconds(idleTime);
            }

            float fadeTime = 1f / Mathf.Max(1f, fadeFps);
            for (int i = 0; i < fade.Length; i++)
            {
                SetSprite(fade[i]);
                yield return new WaitForSeconds(fadeTime);
            }

            routine = null;
            Destroy(gameObject);
        }

        private void SetSprite(Sprite sprite)
        {
            if (spriteRenderer != null && sprite != null)
            {
                spriteRenderer.sprite = sprite;
            }
        }

        private void EnsureRenderer()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
            {
                spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            }

            // 地形压在地图之上、角色之下。
            spriteRenderer.sortingOrder = 2;
            spriteRenderer.color = Color.white;

            // 贴图按 PPU 292 导入即为 3×1 世界单位，无需再缩放；
            // 但若 Inspector 里改过 PPU，这里按「3 格 = lengthCells」兜底校正。
            float nativeWidth = 876f / PixelsPerUnit;
            float scale = lengthCells / Mathf.Max(0.01f, nativeWidth);
            transform.localScale = new Vector3(scale, scale, 1f);
        }

        private void LoadFrames()
        {
            Sprite[] all = Resources.LoadAll<Sprite>(ResourceFolder);
            if (all == null || all.Length == 0)
            {
                Debug.LogWarning("[游侠动画] 荆棘地形没找到 Sprite，将不可见。", this);
                grow = idle = fade = Array.Empty<Sprite>();
                return;
            }

            grow = Filter(all, "BrambleGrow_");
            idle = Filter(all, "BrambleIdle_");
            fade = Filter(all, "BrambleFade_");
        }

        private static Sprite[] Filter(Sprite[] all, string suffixPrefix)
        {
            string prefix = ResourceFolder + "_" + suffixPrefix;
            return all
                .Where(s => s != null && s.name.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(s => s.name, StringComparer.Ordinal)
                .ToArray();
        }

        private void OnDestroy()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }
        }
    }
}
