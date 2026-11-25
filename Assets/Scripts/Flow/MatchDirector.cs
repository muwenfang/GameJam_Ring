using System.Collections.Generic;
using Ring.Bells;
using Ring.Characters;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Ring.Flow
{
    /// <summary>
    /// 演示用推进器：没有战斗时靠它把一局「跑到底」，方便看流程与 UI 是否正常。
    ///
    /// 它做三件事：
    /// 1）收集人设阶段——每隔一段时间优先给玩家补一张缺牌，让玩家集齐并触发 B9；
    /// 2）淘汰赛阶段——每隔一段时间处决一个人机，直到只剩玩家（胜利）；
    /// 3）调试键——K 处决玩家、L 处决一个人机。
    ///
    /// 为什么要优先给玩家补牌：B14 要求「集齐完整人设 + 只剩最后一人」才判胜，
    /// 若让人机先集齐，演示会走到「玩家活到最后却没集齐 → 失败」，看不到胜利界面。
    ///
    /// 注意：收集阶段按 B8 死亡只满血复活，所以 K 键在收集阶段看不到失败界面；
    /// 想看失败界面请在淘汰赛阶段按 K。
    ///
    /// 真正的战斗接进来之后，把这个组件关掉（enabled = false）即可，流程与 UI 都不依赖它。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/演示推进 MatchDirector")]
    public sealed class MatchDirector : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("游戏流程控制器；留空时自动取 GameFlow.Instance。")]
        private GameFlow gameFlow;

        [SerializeField]
        [Tooltip("钟声系统；留空时自动取 BellSystem.Instance。")]
        private BellSystem bellSystem;

        [SerializeField]
        [Tooltip("是否在 Play 时自动点一次「开始游戏」。")]
        private bool autoStartOnPlay;

        [Header("收集人设阶段")]
        [SerializeField]
        [Tooltip("每隔多少秒给一个人机补一张牌（把 B9 推进出来）。")]
        private float giftIntervalSeconds = 8f;

        [Header("淘汰赛阶段")]
        [SerializeField]
        [Tooltip("每隔多少秒处决一个人机。")]
        private float executionIntervalSeconds = 5f;

        [Header("调试键")]
        [SerializeField]
        [Tooltip("是否启用 K / L 调试键。")]
        private bool debugKeysEnabled = true;

        [SerializeField]
        [Tooltip("处决玩家（看失败界面）。")]
        private KeyCode killPlayerKey = KeyCode.K;

        [SerializeField]
        [Tooltip("处决一个人机。")]
        private KeyCode executeOpponentKey = KeyCode.L;

        [SerializeField]
        [Tooltip("是否打印演示推进日志。")]
        private bool logDirector = true;

        private float timer;

        private void Start()
        {
            if (gameFlow == null)
            {
                gameFlow = GameFlow.Instance;
            }

            if (bellSystem == null)
            {
                bellSystem = BellSystem.Instance;
            }

            if (autoStartOnPlay && gameFlow != null)
            {
                gameFlow.StartMatch();
            }
        }

        private void Update()
        {
            if (gameFlow == null)
            {
                return;
            }

            if (debugKeysEnabled)
            {
                if (Input.GetKeyDown(killPlayerKey))
                {
                    KillPlayer();
                }

                if (Input.GetKeyDown(executeOpponentKey))
                {
                    ExecuteOneOpponent();
                }
            }

            MatchState state = gameFlow.State;
            if (state != MatchState.Collect && state != MatchState.Elimination)
            {
                return;
            }

            timer += Time.deltaTime;

            if (state == MatchState.Collect)
            {
                if (timer >= giftIntervalSeconds)
                {
                    timer = 0f;
                    GiftOneCard();
                }
            }
            else if (timer >= executionIntervalSeconds)
            {
                timer = 0f;
                ExecuteOneOpponent();
            }
        }

        /// <summary>优先给玩家补一张缺的牌（B14 需要玩家集齐），然后敲一次钟让 B9 有机会触发。</summary>
        private void GiftOneCard()
        {
            MatchSpawner spawner = gameFlow.Spawner;
            if (spawner == null || bellSystem == null || bellSystem.Runtime == null)
            {
                return;
            }

            // B14：胜利同时要求「集齐完整人设」和「只剩最后一人」。
            // 演示必须先让玩家集齐（集齐后按 B13 豁免，不会被钟声洗掉），
            // 否则淘汰赛打到只剩玩家时会被判「未集齐 → 失败」，永远看不到胜利界面。
            CharacterInstance target = gameFlow.Player;
            if (target != null && target.HasCompleteSet)
            {
                target = null;
            }

            if (target == null)
            {
                var candidates = new List<CharacterInstance>();
                for (int i = 0; i < spawner.Instances.Count; i++)
                {
                    CharacterInstance character = spawner.Instances[i];
                    if (character != null && !character.IsPlayer && !character.HasCompleteSet)
                    {
                        candidates.Add(character);
                    }
                }

                if (candidates.Count == 0)
                {
                    return;
                }

                target = candidates[Random.Range(0, candidates.Count)];
            }
            if (!GiveMissingCard(target.Participant))
            {
                return;
            }

            if (logDirector)
            {
                Debug.Log("[演示] 给 " + target.DisplayName + " 补牌：" + target.Participant.Describe(), this);
            }

            // 敲一次钟，BellRuntime 会在结算时检查「是否有人集齐」，从而切到淘汰赛（B9）。
            bellSystem.RingNowRandom(BellTrigger.Manual);
        }

        /// <summary>优先补齐已有特质对应的技能等级；没有特质就随机发一张缺的特质。</summary>
        private static bool GiveMissingCard(IBellParticipant participant)
        {
            if (participant == null)
            {
                return false;
            }

            for (int i = 0; i < participant.Traits.Count; i++)
            {
                PersonaId persona = participant.Traits[i].Persona;
                for (int tier = 1; tier <= 3; tier++)
                {
                    var card = new SkillCard(persona, (SkillTier)tier);
                    if (HasSkill(participant.Skills, card))
                    {
                        continue;
                    }

                    participant.AddSkill(card);
                    return true;
                }
            }

            var pool = new List<TraitCard>();
            IReadOnlyList<TraitCard> all = BellCards.CreateAllTraits();
            for (int i = 0; i < all.Count; i++)
            {
                if (!BellCards.HasTrait(participant.Traits, all[i].Persona))
                {
                    pool.Add(all[i]);
                }
            }

            if (pool.Count == 0)
            {
                return false;
            }

            participant.AddTrait(pool[Random.Range(0, pool.Count)]);
            return true;
        }

        private static bool HasSkill(IReadOnlyList<SkillCard> skills, SkillCard card)
        {
            for (int i = 0; i < skills.Count; i++)
            {
                if (skills[i].Equals(card))
                {
                    return true;
                }
            }

            return false;
        }

        private void ExecuteOneOpponent()
        {
            MatchSpawner spawner = gameFlow.Spawner;
            if (spawner == null)
            {
                return;
            }

            var candidates = new List<CharacterInstance>();
            for (int i = 0; i < spawner.Instances.Count; i++)
            {
                CharacterInstance character = spawner.Instances[i];
                if (character != null && !character.IsPlayer && !character.IsDead
                    && character.Participant != null && character.Participant.IsInPlay)
                {
                    candidates.Add(character);
                }
            }

            if (candidates.Count == 0)
            {
                return;
            }

            CharacterInstance target = candidates[Random.Range(0, candidates.Count)];
            if (logDirector)
            {
                Debug.Log("[演示] 处决 " + target.DisplayName + "。", this);
            }

            target.Kill();
        }

        private void KillPlayer()
        {
            CharacterInstance player = gameFlow.Player;
            if (player != null && !player.IsDead)
            {
                player.Kill();
            }
        }
    }
}
