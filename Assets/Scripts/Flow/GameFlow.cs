using System;
using Ring.Bells;
using Ring.Maps;
using UnityEngine;

namespace Ring.Flow
{
    /// <summary>
    /// 把纯流程状态机 <see cref="MatchFlow"/> 接到 Unity 上：生成角色、订阅钟声、判胜负、切面板。
    ///
    /// 分工很清楚：
    /// - 规则与状态（能不能开始、什么时候进淘汰赛、什么时候算赢）都在 MatchFlow，纯 C#；
    /// - 本类只做副作用，且只通过事件把结果告诉 UI；
    /// - UI 层（GameUiController）不认钟声、不认角色，只认 MatchState。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/游戏流程 GameFlow")]
    public sealed class GameFlow : MonoBehaviour
    {
        private static GameFlow instance;

        [SerializeField]
        [Tooltip("角色生成器；留空时自动在场景里找。")]
        private MatchSpawner spawner;

        [SerializeField]
        [Tooltip("钟声系统；留空时自动用场景里的 BellSystem。")]
        private BellSystem bellSystem;

        [SerializeField]
        [Tooltip("B14：是否要求玩家集齐完整人设才判胜。默认关闭（剩自己即胜利）。")]
        private bool requireCompleteSetForVictory;

        [SerializeField]
        [Tooltip("是否把流程切换打印到 Console。")]
        private bool logFlow = true;

        [Header("本局地图")]
        [SerializeField]
        [Tooltip("每局随机选一张地图（共 6 张）；关掉则固定用 fixedMap，方便调试单张地图。")]
        private bool randomizeMap = true;

        [SerializeField]
        [Tooltip("randomizeMap 关闭时使用的固定地图。")]
        private BattleMapId fixedMap = BattleMapId.Map01;

        /// <summary>场景里的流程控制器。</summary>
        public static GameFlow Instance
        {
            get { return instance; }
        }

        /// <summary>纯流程状态机（离线测试用的同一个类）。</summary>
        public MatchFlow Flow { get; private set; }

        public MatchState State
        {
            get { return Flow != null ? Flow.State : MatchState.MainMenu; }
        }

        public int PlayerId
        {
            get { return Flow != null ? Flow.PlayerId : -1; }
        }

        public CharacterInstance Player
        {
            get { return spawner != null ? spawner.Player : null; }
        }

        public MatchSpawner Spawner
        {
            get { return spawner; }
        }

        public BellSystem Bells
        {
            get { return bellSystem; }
        }

        /// <summary>状态变化：(from, to)。UI 只订阅这一个事件即可。</summary>
        public event Action<MatchState, MatchState> StateChanged;

        /// <summary>本局结束（胜利 / 失败）。</summary>
        public event Action<MatchState> MatchFinished;

        /// <summary>
        /// 本局随机到的地图。主菜单状态下不保证有意义（保留的是上一局的值）。
        /// 地图实体还没做，UI / 场地逻辑拿到这个编号后自行决定怎么展示。
        /// </summary>
        public BattleMapId CurrentMap { get; private set; }

        /// <summary>开局选定本局地图后触发。UI 订阅它来切换地图场景。</summary>
        public event Action<BattleMapId> MapChanged;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning("[流程] 场景里已存在 GameFlow，销毁重复实例：" + name, this);
                Destroy(this);
                return;
            }

            instance = this;
            Flow = new MatchFlow { RequireCompleteSetForVictory = requireCompleteSetForVictory };
            Flow.StateChanged += HandleFlowStateChanged;
            Flow.MatchFinished += HandleMatchFinished;
            Flow.TransitionRejected += HandleTransitionRejected;
        }

        private void Start()
        {
            ResolveDependencies();
            SubscribeBells();
        }

        private void OnDestroy()
        {
            if (Flow != null)
            {
                Flow.StateChanged -= HandleFlowStateChanged;
                Flow.MatchFinished -= HandleMatchFinished;
                Flow.TransitionRejected -= HandleTransitionRejected;
            }

            UnsubscribeBells();

            if (spawner != null)
            {
                spawner.DespawnAll();
            }

            if (instance == this)
            {
                instance = null;
            }
        }

        // ---------- 对外入口（按钮调这两个） ----------

        /// <summary>点击「开始游戏」：生成六人 → 进入收集人设阶段。</summary>
        public bool StartMatch()
        {
            ResolveDependencies();

            // 先定本局地图再调 Flow.StartMatch()：StateChanged(MainMenu→Loading) 触发时，
            // UI 读到的 CurrentMap 就已经是这一局的值，不会闪一帧上一局的地图。
            // 随机源留在 Unity 侧，纯映射逻辑在 BattleMaps（零 UnityEngine，可离线回归）。
            BattleMapId selected = randomizeMap
                ? BattleMaps.Pick(UnityEngine.Random.Range(0, BattleMaps.Count))
                : BattleMaps.ClampToValid(fixedMap);
            CurrentMap = selected;

            if (!Flow.StartMatch())
            {
                return false;
            }

            if (logFlow)
            {
                Debug.Log("[流程] 本局地图：" + selected.ToDisplayName(), this);
            }

            if (MapChanged != null)
            {
                MapChanged(selected);
            }

            if (bellSystem == null || spawner == null)
            {
                Debug.LogError("[流程] 缺少 BellSystem 或 MatchSpawner，无法开局。", this);
                Flow.ReturnToMenu();
                return false;
            }

            int playerId;
            if (!spawner.SpawnMatch(out playerId))
            {
                Flow.ReturnToMenu();
                return false;
            }

            for (int i = 0; i < spawner.Instances.Count; i++)
            {
                CharacterInstance character = spawner.Instances[i];
                if (character != null)
                {
                    character.Died += HandleCharacterDied;
                }
            }

            Flow.NotifyParticipantsSpawned(playerId, spawner.SpawnedCount);
            return true;
        }

        /// <summary>点击「返回主菜单」：清掉本局角色与钟声状态。</summary>
        public bool ReturnToMenu()
        {
            ResolveDependencies();

            // spawner 可能压根没有（场景里没挂），必须先判空再摸 Instances，否则菜单一按就空引用。
            if (spawner != null)
            {
                for (int i = 0; i < spawner.Instances.Count; i++)
                {
                    CharacterInstance character = spawner.Instances[i];
                    if (character != null)
                    {
                        character.Died -= HandleCharacterDied;
                    }
                }

                spawner.DespawnAll();
            }

            if (bellSystem != null && bellSystem.Runtime != null)
            {
                bellSystem.Runtime.Registry.Clear();
                bellSystem.Runtime.Tracking.Clear();
                bellSystem.Runtime.ResetRound();
            }

            return Flow.ReturnToMenu();
        }

        /// <summary>把 RequireCompleteSetForVictory 同步进状态机（Inspector 改动后调用）。</summary>
        public void ApplySettings()
        {
            if (Flow != null)
            {
                Flow.RequireCompleteSetForVictory = requireCompleteSetForVictory;
            }
        }

        // ---------- 角色死亡 ----------

        private void HandleCharacterDied(CharacterInstance character)
        {
            if (character == null)
            {
                return;
            }

            bool isPlayer = character.Id == PlayerId;

            // B8：普通阶段（这里对应「收集人设」）阵亡不是淘汰，而是被重新分配到区域并恢复满血。
            if (Flow.State == MatchState.Collect)
            {
                character.Revive();
                if (logFlow)
                {
                    Debug.Log("[流程] B8：" + character.DisplayName + " 在收集阶段阵亡，已满血重新分配。", this);
                }

                return;
            }

            if (bellSystem != null)
            {
                bellSystem.NotifyDeath(character.Id);
            }

            if (isPlayer)
            {
                Flow.NotifyPlayerDefeated("玩家阵亡");
                return;
            }

            if (logFlow)
            {
                Debug.Log("[流程] " + character.DisplayName + " 阵亡。", this);
            }

            EvaluateOutcome();
        }

        // ---------- 钟声 ----------

        private void SubscribeBells()
        {
            if (bellSystem == null)
            {
                Debug.LogWarning("[流程] 场景里没有 BellSystem，钟声不会推进。", this);
                return;
            }

            bellSystem.BellRung += HandleBellRung;
            bellSystem.PhaseChanged += HandleBellPhaseChanged;
        }

        private void UnsubscribeBells()
        {
            if (bellSystem == null)
            {
                return;
            }

            bellSystem.BellRung -= HandleBellRung;
            bellSystem.PhaseChanged -= HandleBellPhaseChanged;
        }

        private void HandleBellPhaseChanged(BellPhase phase)
        {
            if (phase == BellPhase.Elimination)
            {
                // B9：有人集齐完整人设 → 进入淘汰赛。
                Flow.NotifyEliminationStarted();
            }
        }

        private void HandleBellRung(BellResult result)
        {
            // 钟声刚换过牌：把新的特质/技能同步进每个人的五个能力槽，再判胜负。
            if (spawner != null)
            {
                spawner.RefreshLoadouts();
            }

            EvaluateOutcome();
        }

        /// <summary>按「在场人数」判胜负：只剩玩家 → 胜利；只剩一个人机 → 失败。</summary>
        private void EvaluateOutcome()
        {
            if (Flow.State != MatchState.Collect && Flow.State != MatchState.Elimination)
            {
                return;
            }

            if (bellSystem == null || bellSystem.Runtime == null)
            {
                return;
            }

            var inPlay = bellSystem.Runtime.Registry.GetInPlay();
            if (inPlay.Count != 1)
            {
                return;
            }

            IBellParticipant survivor = inPlay[0];
            if (survivor.Id == PlayerId)
            {
                bool complete = Player != null && Player.HasCompleteSet;
                Flow.NotifyPlayerLastStanding(complete);
            }
            else
            {
                Flow.NotifyOpponentWon(survivor.Id);
            }
        }

        // ---------- 内部 ----------

        private void ResolveDependencies()
        {
            if (spawner == null)
            {
                spawner = FindObjectOfType<MatchSpawner>();
            }

            if (bellSystem == null)
            {
                bellSystem = BellSystem.Instance;
                if (bellSystem == null)
                {
                    bellSystem = FindObjectOfType<BellSystem>();
                }
            }
        }

        private void HandleFlowStateChanged(MatchState from, MatchState to)
        {
            if (logFlow)
            {
                Debug.Log("[流程] " + from.ToDisplayName() + " → " + to.ToDisplayName(), this);
            }

            if (StateChanged != null)
            {
                StateChanged(from, to);
            }
        }

        private void HandleMatchFinished(MatchState result)
        {
            if (logFlow)
            {
                Debug.Log("[流程] 本局结束：" + result.ToDisplayName(), this);
            }

            if (MatchFinished != null)
            {
                MatchFinished(result);
            }
        }

        private void HandleTransitionRejected(string reason)
        {
            if (logFlow)
            {
                Debug.Log("[流程] 流转被拒绝：" + reason, this);
            }
        }
    }
}
