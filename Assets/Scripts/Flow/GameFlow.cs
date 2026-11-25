using System;
using System.Collections.Generic;
using Ring.Bells;
using Ring.Maps;
using UnityEngine;

namespace Ring.Flow
{
    /// <summary>
    /// 把纯流程状态机 <see cref="MatchFlow"/> 接到 Unity 上：生成角色、订阅钟声、判胜负、切面板。
    ///
    /// 分工（沿用原有分层，规则核心不放进壳里）：
    /// - 状态能不能流转、什么时候算赢，在纯 C# 的 MatchFlow；
    /// - 「六区域抽三场 / 三场 1v1 配对 / 位置钟声重匹配 / B8 死亡重分配」的纯计算在
    ///   <see cref="ArenaPlanGenerator"/>（Ring.Maps，零 UnityEngine，可离线回归）；
    /// - 本类只做副作用：生成、落位、订阅钟声、把结果转成事件；
    /// - UI 层只认 MatchState / BattleMapId / ArenaPlan 这些事件与只读数据，不反向改规则。
    ///
    /// 本轮完善的流程口径（对应文档 B3/B4/B8/B9/B14 与需求）：
    /// 1) 六个区域 = BattleMapId.Map01..Map06（= MapKit Z1..Z6）；每轮抽 3 个作为 3 场 1v1 场地；
    /// 2) 开局随机分配特质、技能（BellRuntime.DealInitialCards）与「位置 / 区域 + 对手」（ArenaPlan）；
    /// 3) 出现死亡**一定**触发位置钟声（收集与淘汰制都不例外），并重置 30 秒定时计数；
    /// 4) 无人集齐人设时（收集阶段），被杀方满血复活 + 重新分配到六区域之一 + 重新匹配，仍是 3 场 1v1；
    /// 5) 有人集齐 → 淘汰制，死亡即淘汰（卡牌进组件池），规则与之前多数一致；
    /// 6) 胜利 = 集齐完整特质与技能（合成完整人设）**且** 只剩最后一人（B14）；
    /// 7) 游戏结束点击返回、或再来一局，都会走 <see cref="InitializeGame"/> 做一次幂等清场。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/游戏流程 GameFlow")]
    public sealed class GameFlow : MonoBehaviour
    {
        private static GameFlow instance;
        private static readonly BattleMapId[] NoMaps = new BattleMapId[0];

        [SerializeField]
        [Tooltip("角色生成器；留空时自动在场景里找。")]
        private MatchSpawner spawner;

        [SerializeField]
        [Tooltip("钟声系统；留空时自动用场景里的 BellSystem。")]
        private BellSystem bellSystem;

        [Header("胜利条件（B14）")]
        [SerializeField]
        [Tooltip("B14：胜利需同时满足「集齐完整特质与技能、合成完整人设」与「只剩最后一人」。默认开启；关闭则只剩自己即胜。")]
        private bool requireCompleteSetForVictory = true;

        [Header("三场 1v1 与六区域")]
        [SerializeField]
        [Tooltip("并行 1v1 场次数；文档为 3 场（6 人）。")]
        [Min(1)]
        private int battleCount = ArenaPlanGenerator.DefaultBattleCount;

        [SerializeField]
        [Tooltip("是否从六区域随机抽场地；关掉则把 fixedMap 作为第一场地，其余按编号补足，方便调试单张地图。")]
        private bool randomizeMap = true;

        [SerializeField]
        [Tooltip("randomizeMap 关闭时使用的第一场地：Map01=Z1 雨夜竹林 … Map06=Z6 洞穴。")]
        private BattleMapId fixedMap = BattleMapId.Map01;

        [SerializeField]
        [Tooltip("是否把流程切换打印到 Console。")]
        private bool logFlow = true;

        [SerializeField]
        [Tooltip("是否把三场 1v1 的场地与配对打印到 Console。")]
        private bool logArenas = true;

        // ---------- 运行时（不序列化） ----------
        private System.Random randomSource;
        private readonly List<BattleMapId> initialMaps = new List<BattleMapId>();
        private BattleMapId displayedMap = BattleMapId.Map01;
        private CharacterInstance pendingDeathVictim;

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
        /// 本轮三场 1v1 的场地与对手安排；主菜单 / 未开局时为 null。
        /// 这是「角色 ↔ 区域 ↔ 对手」的唯一权威映射，只在开局、位置钟声、B8 重分配时重建。
        /// </summary>
        public ArenaPlan CurrentPlan { get; private set; }

        /// <summary>本轮抽中的区域（开局 3 个）；未开局为空表。</summary>
        public IReadOnlyList<BattleMapId> ActiveMaps
        {
            get { return CurrentPlan != null ? CurrentPlan.ActiveMaps : NoMaps; }
        }

        /// <summary>
        /// 玩家当前所在的区域，也就是 <see cref="BattleMapPresenter"/> 该展示的那一张地图。
        /// 未开局 / 玩家不在场时返回上一轮记录的展示区域，保证 UI 读到的永远合法。
        /// </summary>
        public BattleMapId CurrentMap
        {
            get
            {
                if (CurrentPlan != null && Flow != null && Flow.PlayerId >= 0
                    && CurrentPlan.Contains(Flow.PlayerId))
                {
                    return CurrentPlan.MapOf(Flow.PlayerId);
                }

                return displayedMap;
            }
        }

        /// <summary>本局地图在 MapKit 资源包里的静态身份；非法编号返回 null。</summary>
        public BattleMapInfo CurrentMapInfo
        {
            get { return BattleMaps.Info(CurrentMap); }
        }

        /// <summary>开局选定本局区域后、或位置钟声把玩家换到新区域时触发。UI 订阅它来切换地图场景。</summary>
        public event Action<BattleMapId> MapChanged;

        /// <summary>三场 1v1 的场地 / 对手发生变化时触发。HUD 订阅它立刻刷新「当前对手」。</summary>
        public event Action<ArenaPlan> ArenasChanged;

        /// <summary>
        /// <see cref="InitializeGame"/> 完成、上一局运行时状态已清干净时触发。
        /// 相机跟随、HUD 之类需要在重开时重新绑目标的组件订阅它。
        /// </summary>
        public event Action GameInitialized;

        private System.Random RandomSource
        {
            get
            {
                if (randomSource == null)
                {
                    randomSource = new System.Random(UnityEngine.Random.Range(0, int.MaxValue));
                }

                return randomSource;
            }
        }

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
            // 首次进场景也执行一次清场，避免编辑器里残留的运行时状态影响第一局。
            InitializeGame();
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

        // ---------- 对外入口（按钮 / 代码调这几个） ----------

        /// <summary>
        /// 显式初始化：把上一局的**全部运行时状态**清干净，使「返回主菜单」「再来一局」
        /// 「重开后重新 initializeGame」都从同一份干净状态出发。
        ///
        /// 幂等、无参，可直接绑到 UI 按钮的 OnClick；不会改动 MatchFlow 的状态，
        /// 状态流转仍由 <see cref="ReturnToMenu"/> / <see cref="StartMatch"/> 负责。
        /// </summary>
        public void InitializeGame()
        {
            ResolveDependencies();

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
                BellRuntime runtime = bellSystem.Runtime;
                runtime.Registry.Clear();
                runtime.Tracking.Clear();
                runtime.Pool.Clear();
                runtime.ResetRound();
                runtime.ResetTimer();
                runtime.TimerEnabled = true;
            }

            CurrentPlan = null;
            initialMaps.Clear();
            pendingDeathVictim = null;
            randomSource = null;
            displayedMap = BattleMaps.ClampToValid(fixedMap);

            // 设置面板可能把 timeScale 留在 0；重开必须恢复正常时间流速。
            Time.timeScale = 1f;

            // 状态机自身的本局数据（玩家 id / 人数 / 拒绝原因）也一并清掉。
            if (Flow != null)
            {
                Flow.InitializeGame();
            }

            if (GameInitialized != null)
            {
                GameInitialized();
            }

            if (logFlow)
            {
                Debug.Log("[流程] InitializeGame：运行时状态已清空（角色 / 名册 / 追踪 / 组件池 / 钟声计时 / 场地配对）。", this);
            }
        }

        /// <summary>点击「开始游戏」：初始化 → 抽三区域 → 生成六人 → 三场 1v1 → 进入收集人设阶段。</summary>
        public bool StartMatch()
        {
            ResolveDependencies();

            // 先判状态再清场：避免在局中误调 StartMatch 时「清了场却没开局」，把状态卡死。
            if (Flow == null || !Flow.State.CanStartMatch())
            {
                return false;
            }

            // 需求：游戏结束后点击返回、重新开始游戏后需要 initializeGame。
            // 放在最前面，保证从主菜单 / 胜利 / 失败 / 甚至局中重开都不会带上残留状态。
            InitializeGame();

            // 先把本轮要用的三个区域定下来，Loading 阶段 UI 读 CurrentMap 就是这一局的值。
            initialMaps.Clear();
            if (randomizeMap)
            {
                initialMaps.AddRange(BattleMaps.PickDistinct(battleCount, RandomSource));
            }
            else
            {
                initialMaps.Add(BattleMaps.ClampToValid(fixedMap));
                for (int i = 0; i < BattleMaps.Count && initialMaps.Count < battleCount; i++)
                {
                    BattleMapId candidate = BattleMaps.All[i];
                    if (!initialMaps.Contains(candidate))
                    {
                        initialMaps.Add(candidate);
                    }
                }
            }

            if (initialMaps.Count > 0)
            {
                displayedMap = initialMaps[0];
            }

            if (!Flow.StartMatch())
            {
                return false;
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

            // 开局随机分配「位置 / 区域 + 对手」，仍是 3 场 1v1。
            BattleMapId beforeMap = CurrentMap;
            RebuildArenas(null, -1, initialMaps);

            Flow.NotifyParticipantsSpawned(playerId, spawner.SpawnedCount);

            // 展示器在 Loading→Collect 时按 CurrentMap 幂等 Show；这里再补发一次，
            // 保证玩家区域与开局抽中的第一场地不一致时也能切对图。
            if (MapChanged != null && CurrentMap != beforeMap)
            {
                MapChanged(CurrentMap);
            }

            return true;
        }

        /// <summary>点击「返回主菜单」：先统一清场，再回主菜单。</summary>
        public bool ReturnToMenu()
        {
            ResolveDependencies();
            InitializeGame();
            return Flow.ReturnToMenu();
        }

        /// <summary>把 B14 开关同步进状态机（Inspector 改动后调用）。</summary>
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

            MatchState state = Flow.State;
            if (state != MatchState.Collect && state != MatchState.Elimination)
            {
                return;
            }

            bool isPlayer = character.Id == PlayerId;
            // 收集人设 = 尚无人集齐完整人设 = 普通阶段（B8 复活重分配）。
            bool normalPhase = state == MatchState.Collect;

            // B3：出现死亡一定触发钟声，且必定为位置钟声；两个都不例外。
            // NotifyDeath 内部会敲位置钟声并重置 30 秒计时（B4 的「未触发死亡钟声」前提）。
            pendingDeathVictim = normalPhase ? character : null;
            if (bellSystem != null)
            {
                bellSystem.NotifyDeath(character.Id);
            }

            if (!normalPhase)
            {
                // 淘汰制：死亡即淘汰，卡牌进组件池（BellRuntime.NotifyDeath 已处理）。
                if (isPlayer)
                {
                    Flow.NotifyPlayerDefeated("玩家在淘汰赛阵亡");
                    return;
                }

                if (logFlow)
                {
                    Debug.Log("[流程] " + character.DisplayName + " 在淘汰赛阵亡，已离场。", this);
                }

                EvaluateOutcome();
            }
            // 普通阶段：B8 复活 / 重新分配区域 / 重新匹配都在 HandleBellRung 里完成。
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
                // B9：有人集齐完整人设 → 进入淘汰制。规则与之前多数一致（B10~B12 由钟声系统处理）。
                Flow.NotifyEliminationStarted();
            }
        }

        private void HandleBellRung(BellResult result)
        {
            if (result == null)
            {
                return;
            }

            int relocateId = pendingDeathVictim != null ? pendingDeathVictim.Id : -1;

            // 位置钟声（B1/B3/B5）：消费 Swaps 重新匹配对手，并按需重抽场地。
            if (result.Kind == BellKind.Position && result.Executed)
            {
                RebuildArenas(result.Swaps, relocateId, null);
            }

            // 钟声刚换过牌：把新的特质 / 技能同步进每个人的五个能力槽。
            if (spawner != null)
            {
                spawner.RefreshLoadouts();
            }

            // B8：普通阶段被杀的一方恢复至满生命，并已被重新分配到六区域之一。
            if (pendingDeathVictim != null)
            {
                CharacterInstance victim = pendingDeathVictim;
                pendingDeathVictim = null;
                victim.Revive();

                if (logFlow)
                {
                    BattleMapId map = CurrentPlan != null ? CurrentPlan.MapOf(victim.Id) : displayedMap;
                    Debug.Log("[流程] B8：" + victim.DisplayName
                        + " 已恢复满生命并重新分配到 " + map.ToDisplayName()
                        + "，本轮仍是 " + (CurrentPlan != null ? CurrentPlan.Duels.Count : 0) + " 场 1v1。", this);
                }
            }

            EvaluateOutcome();
        }

        /// <summary>按「在场人数 + 是否集齐」判胜负（B14）：只剩玩家且集齐 → 胜利；只剩人机 → 失败。</summary>
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

            IReadOnlyList<IBellParticipant> inPlay = bellSystem.Runtime.Registry.GetInPlay();
            if (inPlay.Count != 1)
            {
                return;
            }

            IBellParticipant survivor = inPlay[0];
            if (survivor.Id != PlayerId)
            {
                Flow.NotifyOpponentWon(survivor.Id);
                return;
            }

            bool complete = Player != null && Player.HasCompleteSet;
            if (complete || !requireCompleteSetForVictory)
            {
                Flow.NotifyPlayerLastStanding(complete);
                return;
            }

            // B14 的兜底：活到最后但没集齐完整人设，胜利条件缺一。
            // 场上已无对手、也不再可能通过钟声凑齐，直接结算失败，避免无法结束的死局。
            Flow.NotifyPlayerDefeated("B14：活到最后但未集齐完整特质与技能，无法合成完整人设。");
        }

        // ---------- 三场 1v1 / 六区域 ----------

        /// <summary>
        /// 重建一轮三场 1v1 的场地与对手，并把角色落到对应区域。
        /// </summary>
        /// <param name="swaps">位置钟声的配对结果；null 表示开局重新洗牌配对。</param>
        /// <param name="relocateId">B8 需要重新分配区域的被杀者 id；-1 表示只是普通位置钟声。</param>
        /// <param name="preferredMaps">开局抽中的三个区域；null 表示沿用当前区域或重抽。</param>
        private void RebuildArenas(
            IReadOnlyList<BellPositionSwap> swaps,
            int relocateId,
            IReadOnlyList<BattleMapId> preferredMaps)
        {
            if (bellSystem == null || bellSystem.Runtime == null)
            {
                return;
            }

            IReadOnlyList<IBellParticipant> inPlay = bellSystem.Runtime.Registry.GetInPlay();
            var ids = new List<int>(inPlay.Count);
            for (int i = 0; i < inPlay.Count; i++)
            {
                ids.Add(inPlay[i].Id);
            }

            List<int> ordered = swaps != null && swaps.Count > 0
                ? ExpandSwaps(swaps, ids)
                : ArenaPlanGenerator.ShuffleIds(ids, RandomSource);

            bool allowClone = Flow.State == MatchState.Elimination;
            ArenaPlan previous = CurrentPlan;
            BattleMapId previousPlayerMap = CurrentMap;

            // 死亡触发（B8）：重抽场地，保证被杀者被重新分配到六区域之一；
            // 其它位置钟声：只换对手，场地沿用当前这一轮。
            bool relocate = relocateId >= 0;
            IReadOnlyList<BattleMapId> mapsForBuild = relocate
                ? null
                : (preferredMaps ?? (previous != null ? previous.ActiveMaps : null));

            ArenaPlan plan = ArenaPlanGenerator.Create(
                ordered, mapsForBuild, battleCount, RandomSource, allowClone);

            // B8 语义加强：若重抽后仍落在原区域，再抽几次，尽量让「重新分配」看得出来。
            if (relocate && previous != null)
            {
                BattleMapId oldMap = previous.MapOf(relocateId);
                for (int attempt = 0; attempt < 3 && plan.MapOf(relocateId) == oldMap; attempt++)
                {
                    plan = ArenaPlanGenerator.Create(ordered, null, battleCount, RandomSource, allowClone);
                }
            }

            CurrentPlan = plan;
            if (spawner != null)
            {
                spawner.ApplyPlan(plan);
            }

            if (ArenasChanged != null)
            {
                ArenasChanged(plan);
            }

            if (CurrentMap != previousPlayerMap && MapChanged != null)
            {
                MapChanged(CurrentMap);
            }

            if (logArenas)
            {
                Debug.Log("[流程] 三场 1v1：" + plan.Describe()
                    + (relocate ? "（B8 死亡重分配）" : string.Empty), this);
            }
        }

        /// <summary>把 BellResult.Swaps 展开成「0-1 一对、2-3 一对」的有序 id 列表。</summary>
        private static List<int> ExpandSwaps(
            IReadOnlyList<BellPositionSwap> swaps, IReadOnlyList<int> inPlayIds)
        {
            var ordered = new List<int>(inPlayIds.Count);
            var used = new HashSet<int>();

            for (int i = 0; i < swaps.Count; i++)
            {
                AddIfInPlay(swaps[i].ParticipantAId, inPlayIds, used, ordered);
                AddIfInPlay(swaps[i].ParticipantBId, inPlayIds, used, ordered);
            }

            // B7 排除者、落单者一律追加到末尾，保证每个人都被安排进某一场。
            for (int i = 0; i < inPlayIds.Count; i++)
            {
                AddIfInPlay(inPlayIds[i], inPlayIds, used, ordered);
            }

            return ordered;
        }

        private static void AddIfInPlay(
            int id, IReadOnlyList<int> inPlayIds, HashSet<int> used, List<int> ordered)
        {
            if (!used.Add(id))
            {
                return;
            }

            for (int i = 0; i < inPlayIds.Count; i++)
            {
                if (inPlayIds[i] == id)
                {
                    ordered.Add(id);
                    return;
                }
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
