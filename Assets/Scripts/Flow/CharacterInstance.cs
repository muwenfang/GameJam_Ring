using System;
using System.Collections.Generic;
using Ring.Abilities;
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

        private AbilityLoadout loadout;
        private AbilityRuntime abilities;

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

        /// <summary>
        /// 五个能力槽：特质 / 普攻 / 本我 / 自我 / 超我。
        /// 钟声只管特质与三张技能卡，普攻卡在生成时直接给，但三者都落到这一张装备表里。
        /// </summary>
        public AbilityLoadout Loadout
        {
            get { return loadout; }
        }

        /// <summary>能力运行时：冷却、叠层、限时增益、生命快照。</summary>
        public AbilityRuntime Abilities
        {
            get { return abilities; }
        }

        /// <summary>当前派生战斗数值（已含特质被动与限时增益）。</summary>
        public DerivedAbilityStats Derived
        {
            get { return abilities != null ? abilities.ComputeDerived() : default(DerivedAbilityStats); }
        }

        /// <summary>移动速度（格/秒），已含「过载损耗」「风之优雅」等移速修正。</summary>
        public float MoveSpeedCellsPerSecond
        {
            get
            {
                if (abilities != null)
                {
                    return abilities.ComputeDerived().MoveSpeedCellsPerSecond;
                }

                return Stats != null ? Stats.MoveSpeedCellsPerSecond : 0f;
            }
        }

        /// <summary>是否处于无敌（绝对防御等）。</summary>
        public bool IsInvulnerable
        {
            get { return abilities != null && abilities.ComputeDerived().Invulnerable; }
        }

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

            loadout = new AbilityLoadout();
            loadout.TryAdd(new AbilityCard(persona, AbilitySlot.BasicAttack), out _);
            abilities = new AbilityRuntime(loadout, new CharacterStatAdapter(stats));
            abilities.SetHealth(currentHealth, maxHealth);
            RefreshLoadout();

            BuildVisual();
        }

        private void Update()
        {
            if (abilities == null)
            {
                return;
            }

            abilities.Tick(Time.deltaTime);
            abilities.NotifyHealth(currentHealth);

            if (IsDead)
            {
                return;
            }

            // 斯巴达韧性、沉腰调息这类持续回血由运行时累计，这里统一落到身上。
            float regen = abilities.ConsumeRegen();
            if (regen > 0f)
            {
                HealSilently(regen);
            }
        }

        /// <summary>
        /// 受击入口：先看无敌（绝对防御等），再按派生减伤与受伤倍率结算，
        /// 致死一击会先问「逆时」能不能回滚；真正扣到 0 才触发 Died。
        /// </summary>
        public void TakeDamage(float rawDamage)
        {
            if (IsDead || rawDamage <= 0f)
            {
                return;
            }

            if (abilities == null)
            {
                float plain = Stats != null ? Stats.MitigateDamage(rawDamage) : rawDamage;
                currentHealth = Mathf.Max(0f, currentHealth - plain);
                RaiseHealthChanged();
                if (currentHealth <= 0f)
                {
                    RaiseDied();
                }

                return;
            }

            DerivedAbilityStats derived = abilities.ComputeDerived();
            if (derived.Invulnerable)
            {
                return;
            }

            float actual = CharacterBalance.ApplyReduction(rawDamage, derived.DamageReduction)
                           * Mathf.Max(0f, derived.DamageTakenMultiplier);
            if (actual <= 0f)
            {
                return;
            }

            if (currentHealth - actual <= 0f)
            {
                float restored;
                if (abilities.TryConsumeLethalHit(out restored))
                {
                    // 「逆时」把生命倒回 3 秒前，这一击不算死亡。
                    ApplyRestoredHealth(restored);
                    return;
                }
            }

            currentHealth = Mathf.Max(0f, currentHealth - actual);
            abilities.NotifyDamageTaken(actual);
            abilities.NotifyHealth(currentHealth);
            RaiseHealthChanged();

            if (currentHealth <= 0f)
            {
                RaiseDied();
            }
        }

        /// <summary>自己造成一次伤害后调用（剑意叠层等）。</summary>
        public void NotifyDamageDealt()
        {
            if (abilities != null)
            {
                abilities.NotifyDamageDealt();
            }
        }

        /// <summary>
        /// 平A 命中一个目标：结算风之优雅 / 重箭、力量眩晕计数、过载损耗蓄势击退。
        /// 返回的 BonusDamage / KnockbackCells / Stunned 由战斗层消费。
        /// </summary>
        public BasicAttackHitResult NotifyBasicAttackHit(int targetId)
        {
            return abilities != null ? abilities.NotifyBasicAttackHit(targetId) : default(BasicAttackHitResult);
        }

        /// <summary>
        /// 钟声换牌之后把「手上持有的特质与技能卡」同步进五个能力槽。
        /// 普攻槽不受影响，会在同步里保留下来。
        /// </summary>
        public void RefreshLoadout()
        {
            if (loadout == null || Participant == null)
            {
                return;
            }

            List<AbilityCard> rejected;
            loadout.SyncFromBellCards(Participant.Traits, Participant.Skills, out rejected);

            if (rejected != null && rejected.Count > 0)
            {
                var builder = new System.Text.StringBuilder();
                for (int i = 0; i < rejected.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append('、');
                    }

                    builder.Append(rejected[i].ToDisplayName());
                }

                Debug.LogWarning("[" + DisplayName + "] 有 " + rejected.Count + " 张卡放不进能力槽：" + builder, this);
            }

            if (abilities != null)
            {
                abilities.NotifyHealth(currentHealth);
            }
        }

        /// <summary>发放本命普攻卡（普攻不属于钟声卡池，生成时直接给）。</summary>
        public void GrantBasicAttack()
        {
            if (loadout == null)
            {
                return;
            }

            loadout.TryAdd(new AbilityCard(persona, AbilitySlot.BasicAttack), out _);
        }

        private void ApplyRestoredHealth(float restored)
        {
            currentHealth = Mathf.Clamp(restored, 1f, maxHealth);
            deathRaised = false;
            if (Participant != null)
            {
                Participant.IsAlive = true;
            }

            if (abilities != null)
            {
                abilities.NotifyHealth(currentHealth);
            }

            RaiseHealthChanged();
        }

        private void HealSilently(float amount)
        {
            if (IsDead || amount <= 0f)
            {
                return;
            }

            float next = Mathf.Min(maxHealth, currentHealth + amount);
            if (Mathf.Abs(next - currentHealth) < 0.01f)
            {
                currentHealth = next;
                return;
            }

            currentHealth = next;
            RaiseHealthChanged();
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

            if (abilities != null)
            {
                // 满血重新分配：冷却、增益、叠层、计数与生命快照一起清掉。
                abilities.Reset(maxHealth, maxHealth);
            }

            RefreshLoadout();
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

            renderer.sortingOrder = isPlayer ? 10 : 5;

            // 剑士走序列帧：颜色、尺寸与脚底对齐都由 SwordsmanSpriteAnimation 接管。
            // 这里不能套占位色块——否则整张水墨图会被染成人设色，方块尺寸也会盖住人物。
            if (persona == PersonaId.Swordsman)
            {
                renderer.color = Color.white;
                go.transform.localPosition = Vector3.zero;
                go.transform.localScale = Vector3.one;

                var animation = go.GetComponent<Ring.Characters.SwordsmanSpriteAnimation>();
                if (animation == null)
                {
                    // AddComponent 会立即触发 Awake，由组件自己加载帧并套用脚底对齐。
                    go.AddComponent<Ring.Characters.SwordsmanSpriteAnimation>();
                }
                else
                {
                    // 角色被提升为玩家时外观会重建，让组件重新套用当前状态。
                    animation.RefreshVisual();
                }

                return;
            }

            // 其余五个人设继续用纯色方块占位。
            renderer.sprite = GetPlaceholderSprite();
            renderer.color = GetPersonaColor(persona, isPlayer);

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
