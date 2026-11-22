using System;
using Ring.Bells;
using Ring.Characters;
using UnityEngine;

namespace Ring.Flow
{
    /// <summary>
    /// 场上一个角色实例：把「基础数值资产」「钟声参与者」「血量」三件事绑在一个 GameObject 上。
    ///
    /// 本轮没有美术与战斗，所以视觉是一个纯色方块占位；换皮时只需要替换 BuildVisual 里的 SpriteRenderer。
    /// 血量与死亡只是给流程层推进用的，命中判定留给后续战斗层。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/角色实例 CharacterInstance")]
    public sealed class CharacterInstance : MonoBehaviour
    {
        private static Sprite placeholderSprite;

        [Header("身份")]
        [SerializeField] private int participantId;
        [SerializeField] private bool isPlayer;
        [SerializeField] private PersonaId persona = PersonaId.Swordsman;

        [Header("血量（运行时）")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float currentHealth = 100f;

        private bool deathRaised;

        /// <summary>死亡事件（只触发一次）。</summary>
        public event Action<CharacterInstance> Died;

        /// <summary>血量变化事件，HUD 用。</summary>
        public event Action<CharacterInstance> HealthChanged;

        public int Id
        {
            get { return participantId; }
        }

        public bool IsPlayer
        {
            get { return isPlayer; }
        }

        /// <summary>基础数值资产（生命、防御、移速等的来源）。</summary>
        public CharacterBaseStats Stats { get; private set; }

        /// <summary>钟声系统的参与者视图（手上有什么牌都在这里）。</summary>
        public BellParticipant Participant { get; private set; }

        public PersonaId Persona
        {
            get { return Stats != null ? Stats.Persona : persona; }
        }

        public string DisplayName
        {
            get { return Participant != null ? Participant.DisplayName : name; }
        }

        public float MaxHealth
        {
            get { return maxHealth; }
        }

        public float CurrentHealth
        {
            get { return currentHealth; }
        }

        public float HealthRatio
        {
            get { return maxHealth <= 0f ? 0f : Mathf.Clamp01(currentHealth / maxHealth); }
        }

        public bool IsDead
        {
            get { return currentHealth <= 0f || deathRaised; }
        }

        /// <summary>是否已凑出一套完整人设（B13/B14 的判定口径）。</summary>
        public bool HasCompleteSet
        {
            get { return Participant != null && Participant.HasCompleteSet; }
        }

        public PersonaId CompletedPersona
        {
            get { return Participant != null ? Participant.CompletedPersona : default; }
        }

        /// <summary>由生成器调用：绑定 id、数值资产与是否玩家。</summary>
        public void Initialize(int id, CharacterBaseStats stats, bool player)
        {
            participantId = id;
            Stats = stats;
            isPlayer = player;
            persona = stats != null ? stats.Persona : PersonaId.Swordsman;
            deathRaised = false;

            maxHealth = stats != null ? stats.MaxHealth : 100f;
            currentHealth = maxHealth;

            string displayName = stats != null ? stats.DisplayName : ("角色" + id);
            gameObject.name = (player ? "玩家_" : "人机_") + displayName;
            Participant = new BellParticipant(id, displayName);

            BuildVisual();
        }

        /// <summary>按防御减伤后扣血；扣到 0 时触发一次 Died。</summary>
        public void TakeDamage(float rawDamage)
        {
            if (IsDead || rawDamage <= 0f)
            {
                return;
            }

            float actual = Stats != null ? Stats.MitigateDamage(rawDamage) : rawDamage;
            currentHealth = Mathf.Max(0f, currentHealth - actual);
            RaiseHealthChanged();

            if (currentHealth <= 0f)
            {
                RaiseDied();
            }
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f)
            {
                return;
            }

            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            RaiseHealthChanged();
        }

        /// <summary>直接判死（B11 回血、演示脚本、剧情处决都用这个）。</summary>
        public void Kill()
        {
            if (IsDead)
            {
                return;
            }

            currentHealth = 0f;
            RaiseHealthChanged();
            RaiseDied();
        }

        /// <summary>总表里没有指定人设时，把已经生成的角色提升为玩家（换色 + 改名）。</summary>
        public void PromoteToPlayer()
        {
            if (isPlayer)
            {
                return;
            }

            isPlayer = true;
            gameObject.name = "玩家_" + DisplayName;
            BuildVisual();
        }

        /// <summary>
        /// B8：普通阶段阵亡只是「被重新分配到某个区域并恢复满血」，不是淘汰。
        /// </summary>
        public void Revive()
        {
            currentHealth = maxHealth;
            deathRaised = false;
            if (Participant != null)
            {
                Participant.IsAlive = true;
                Participant.IsInPlay = true;
            }

            RaiseHealthChanged();
        }

        public void Despawn()
        {
            if (Application.isPlaying)
            {
                Destroy(gameObject);
            }
            else
            {
                DestroyImmediate(gameObject);
            }
        }

        private void RaiseDied()
        {
            if (deathRaised)
            {
                return;
            }

            deathRaised = true;
            if (Participant != null)
            {
                Participant.IsAlive = false;
            }

            if (Died != null)
            {
                Died(this);
            }
        }

        private void RaiseHealthChanged()
        {
            if (HealthChanged != null)
            {
                HealthChanged(this);
            }
        }

        // ---------- 占位视觉 ----------

        private void BuildVisual()
        {
            Transform visual = transform.Find("Visual");
            GameObject go;
            if (visual == null)
            {
                go = new GameObject("Visual");
                go.transform.SetParent(transform, false);
            }
            else
            {
                go = visual.gameObject;
            }

            var renderer = go.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                renderer = go.AddComponent<SpriteRenderer>();
            }

            renderer.sprite = GetPlaceholderSprite();
            renderer.color = GetPersonaColor(persona, isPlayer);
            renderer.sortingOrder = isPlayer ? 10 : 5;

            // 1×1 的白色 sprite，靠 transform 缩放给出方块大小。
            go.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
            go.transform.localPosition = Vector3.zero;
        }

        private static Sprite GetPlaceholderSprite()
        {
            if (placeholderSprite != null)
            {
                return placeholderSprite;
            }

            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                name = "RingPlaceholder",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            var colors = new Color32[16];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = new Color32(255, 255, 255, 255);
            }

            texture.SetPixels32(colors);
            texture.Apply();

            placeholderSprite = Sprite.Create(
                texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
            placeholderSprite.name = "RingPlaceholder";
            return placeholderSprite;
        }

        /// <summary>六人设各一个辨识色，玩家额外加亮。</summary>
        public static Color GetPersonaColor(PersonaId persona, bool player)
        {
            Color color;
            switch (persona)
            {
                case PersonaId.Swordsman: color = new Color(0.42f, 0.60f, 0.86f); break;
                case PersonaId.SteamMechanic: color = new Color(0.85f, 0.55f, 0.25f); break;
                case PersonaId.Spartan: color = new Color(0.78f, 0.30f, 0.28f); break;
                case PersonaId.Ranger: color = new Color(0.40f, 0.72f, 0.44f); break;
                case PersonaId.Clockmaker: color = new Color(0.62f, 0.45f, 0.80f); break;
                case PersonaId.Strongman: color = new Color(0.80f, 0.72f, 0.35f); break;
                default: color = Color.gray; break;
            }

            return player ? Color.Lerp(color, Color.white, 0.35f) : color;
        }
    }
}
