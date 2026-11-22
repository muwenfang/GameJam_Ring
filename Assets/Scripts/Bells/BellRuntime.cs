using System;
using System.Collections.Generic;
using Ring.Core;

namespace Ring.Bells
{
    /// <summary>
    /// 钟声系统的纯逻辑核心：计时、触发判定、三种钟声的分发、阶段切换、追踪与组件池。
    /// 完全不引用 UnityEngine，因此可以在 dotnet 控制台工程里直接跑离线测试；
    /// MonoBehaviour 侧只需要每帧调用 Update(deltaTime) 并把结果转成表现。
    ///
    /// 规则出处（钟声_全文.txt / 数据和结构.txt 钟声条款）：
    ///  B1 三种钟声各自的交换效果；B3 死亡必触发位置钟声；B4 普通阶段每 30s 随机一种；
    ///  B5 选择跟踪时本次必为位置钟声；B6 击杀二选一；B7 追踪持续两次钟声；
    ///  B8 普通阶段 3 场 1v1 与死亡重置；B9 有人集齐转淘汰制；B11 淘汰者卡片入池；
    ///  B12 淘汰制奇数人数落单者打复制体；B13 集齐者豁免特质/技能钟声；B14 胜利条件。
    /// </summary>
    public sealed class BellRuntime
    {
        private readonly Dictionary<BellKind, IBellEffect> effects = new Dictionary<BellKind, IBellEffect>();
        private float elapsedSeconds;
        private bool suppressTimer;

        public BellRuntime(BellParticipantRegistry registry, BellRules rules = null, IBellRandom random = null)
        {
            Registry = registry ?? new BellParticipantRegistry();
            Rules = (rules ?? new BellRules()).Clone();
            Random = random ?? BellRandom.CreateWithTimeSeed();
            Tracking = new BellTrackingLedger();
            Pool = new BellComponentPool();

            RegisterEffect(new TraitBellEffect());
            RegisterEffect(new SkillBellEffect());
            RegisterEffect(new PositionBellEffect());
            ResetTimer();
        }

        public BellParticipantRegistry Registry { get; }

        public BellRules Rules { get; }

        public IBellRandom Random { get; }

        public BellTrackingLedger Tracking { get; }

        public BellComponentPool Pool { get; }

        /// <summary>B9：有人集齐完整人设后切换到 Elimination。</summary>
        public BellPhase Phase { get; private set; }

        /// <summary>本局已经成功执行过多少次钟声。</summary>
        public int SequenceNumber { get; private set; }

        public BellResult LastResult { get; private set; }

        /// <summary>是否允许定时自动敲钟（B4）。关掉后只能手动/死亡触发。</summary>
        public bool TimerEnabled = true;

        /// <summary>距离下次定时钟声还剩多少秒，供 UI 倒计时。</summary>
        public float SecondsUntilNextBell
        {
            get
            {
                if (!TimerEnabled || Phase == BellPhase.Elimination)
                {
                    return 0f;
                }

                float remain = Rules.IntervalSeconds - elapsedSeconds;
                return remain < 0f ? 0f : remain;
            }
        }

        /// <summary>每次钟声执行后触发（含未执行的结果）。表现层（钟声动画、语音、UI）挂这里。</summary>
        public event Action<BellResult> BellRung;

        /// <summary>阶段切换（普通 → 淘汰制）时触发。</summary>
        public event Action<BellPhase> PhaseChanged;

        /// <summary>有角色被淘汰时触发（B11）。</summary>
        public event Action<IBellParticipant> ParticipantEliminated;

        public void RegisterEffect(IBellEffect effect)
        {
            if (effect == null)
            {
                return;
            }

            effects[effect.Kind] = effect;
        }

        public IBellEffect GetEffect(BellKind kind)
        {
            IBellEffect effect;
            return effects.TryGetValue(kind, out effect) ? effect : null;
        }

        public void ResetTimer()
        {
            elapsedSeconds = 0f;
        }

        /// <summary>
        /// 每帧推进计时。B4：无人集齐人设时每隔固定时间触发一次，种类随机；
        /// 淘汰制下不再定时敲钟（该阶段只由死亡触发）。
        /// </summary>
        public void Update(float deltaSeconds)
        {
            if (!TimerEnabled || Phase != BellPhase.Normal || deltaSeconds <= 0f || suppressTimer)
            {
                return;
            }

            elapsedSeconds += deltaSeconds;
            if (elapsedSeconds >= Rules.IntervalSeconds)
            {
                elapsedSeconds = 0f;
                RingRandomKind(BellTrigger.Timer);
            }
        }

        /// <summary>执行指定种类的钟声。返回本次结果（未执行也会返回结果对象）。</summary>
        public BellResult Ring(BellKind kind, BellTrigger trigger)
        {
            // B13：敲钟前先把「集齐即豁免」同步到角色的标记上，效果与外部读到的状态一致。
            SyncImmunityFlags();

            IBellEffect effect = GetEffect(kind);
            if (effect == null)
            {
                BellResult missing = BellResult.CreateNotExecuted(
                    kind, trigger, Phase, SequenceNumber + 1, "没有注册 " + kind.ToDisplayName() + " 的效果实现。");
                LastResult = missing;
                RaiseBellRung(missing);
                return missing;
            }

            var context = new BellContext(
                kind, trigger, Phase, SequenceNumber + 1, Registry, Random, Tracking, Pool, Rules);

            if (!effect.CanExecute(context))
            {
                BellResult skipped = BellResult.CreateNotExecuted(
                    kind, trigger, Phase, SequenceNumber + 1, kind.ToDisplayName() + " 当前条件不满足，未执行。");
                LastResult = skipped;
                RaiseBellRung(skipped);
                return skipped;
            }

            suppressTimer = true;
            BellResult result;
            try
            {
                result = effect.Execute(context);
            }
            finally
            {
                suppressTimer = false;
            }

            if (result == null)
            {
                result = BellResult.CreateNotExecuted(
                    kind, trigger, Phase, SequenceNumber + 1, "效果返回了空结果。");
            }

            if (result.Executed)
            {
                SequenceNumber++;
                // 追踪次数在本次判定之后递减（B7）。
                Tracking.OnBellResolved();
                ResetTimer();
            }

            LastResult = result;
            UpdatePhaseFromCompleteSets();
            RaiseBellRung(result);
            return result;
        }

        /// <summary>B4：随机挑一种钟声敲响。三种等概率。</summary>
        public BellResult RingRandomKind(BellTrigger trigger)
        {
            var kinds = new[] { BellKind.Trait, BellKind.Skill, BellKind.Position };
            BellKind kind = kinds[Random.NextInt(0, 3)];
            return Ring(kind, trigger);
        }

        /// <summary>
        /// B3：出现死亡一定触发钟声，且必定为位置钟声。
        /// B8：普通阶段下被杀方由上层重新分配到 6 区域之一，生命恢复至满，钟声重新匹配对手。
        /// B11：淘汰制下死亡即淘汰，其所有技能与特质进入组件池。
        /// </summary>
        public BellResult NotifyDeath(int deadParticipantId)
        {
            IBellParticipant dead = Registry.Get(deadParticipantId);
            if (dead == null)
            {
                return BellResult.CreateNotExecuted(
                    BellKind.Position, BellTrigger.Death, Phase, SequenceNumber + 1,
                    "找不到 ID 为 " + deadParticipantId + " 的参与者。");
            }

            dead.IsAlive = false;
            string phaseNote;

            if (Phase == BellPhase.Elimination)
            {
                int stored = StoreEliminatedCards(dead.Id);
                phaseNote = "B11 淘汰 " + dead.DisplayName + "，" + stored + " 张卡进入组件池。";
                if (ParticipantEliminated != null)
                {
                    ParticipantEliminated(dead);
                }
            }
            else
            {
                // B8：普通阶段不淘汰，只重置位置与生命，实际效果由角色层执行。
                phaseNote = "B8 " + dead.DisplayName + " 阵亡，应被重新分配到 "
                    + Rules.RegionCount + " 个区域之一并恢复至满生命。";
            }

            BellResult result = Ring(BellKind.Position, BellTrigger.Death);
            result.AddNote(phaseNote);
            return result;
        }

        /// <summary>
        /// B6：击杀对方后选择「夺取其 1 个特质或技能」。返回是否真的拿到了一张。
        /// </summary>
        public bool TryStealOneCard(int winnerId, int victimId)
        {
            IBellParticipant winner = Registry.Get(winnerId);
            IBellParticipant victim = Registry.Get(victimId);
            if (winner == null || victim == null || winnerId == victimId)
            {
                return false;
            }

            // 先尝试特质，再尝试技能（B6 未规定顺序，按卡面稀有度取特质优先）。
            for (int i = 0; i < victim.Traits.Count; i++)
            {
                TraitCard card = victim.Traits[i];
                if (victim.RemoveTrait(card) && winner.AddTrait(card))
                {
                    return true;
                }
            }

            for (int i = 0; i < victim.Skills.Count; i++)
            {
                SkillCard card = victim.Skills[i];
                if (victim.RemoveSkill(card) && winner.AddSkill(card))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// B5/B6：击杀后选择「追踪」。本次钟声必定触发位置交换（触发来源记为 Tracking）。
        /// B10：淘汰制下追踪变为一次性（只持续本次钟声，不再跨两次）。
        /// </summary>
        public BellResult NotifyTracking(int trackerId, int targetId)
        {
            int duration = Phase == BellPhase.Elimination ? 1 : Rules.TrackingDurationBells;
            if (!Tracking.BeginTracking(trackerId, targetId, duration))
            {
                return BellResult.CreateNotExecuted(
                    BellKind.Position, BellTrigger.Tracking, Phase, SequenceNumber + 1,
                    "追踪参数非法（不能追自己或持续次数为 0）。");
            }

            BellResult result = Ring(BellKind.Position, BellTrigger.Tracking);
            result.AddNote("B7 " + trackerId + " 追踪 " + targetId + "，持续 "
                + duration + " 次钟声"
                + (duration == 1 ? "（B10 淘汰制一次性追踪）。" : "。"));
            return result;
        }

        /// <summary>B11：把被淘汰者身上的卡收进组件池并让他离场，返回收走的张数。</summary>
        public int StoreEliminatedCards(int participantId)
        {
            IBellParticipant participant = Registry.Get(participantId);
            if (participant == null)
            {
                return 0;
            }

            int stored = Pool.StoreAllFrom(participant);
            participant.IsInPlay = false;
            return stored;
        }

        /// <summary>B9：有人集齐完整人设即切换到淘汰制。返回是否发生了切换。</summary>
        public bool UpdatePhaseFromCompleteSets()
        {
            if (Phase == BellPhase.Elimination)
            {
                return false;
            }

            if (!Registry.AnyCompleteSet)
            {
                return false;
            }

            Phase = BellPhase.Elimination;
            if (PhaseChanged != null)
            {
                PhaseChanged(Phase);
            }

            return true;
        }

        /// <summary>
        /// B14：胜利条件 = 集齐完整特质与技能 + 合成完整人设 + 只剩最后一人。
        /// 满足时返回胜者，否则返回 null。
        /// </summary>
        public IBellParticipant CheckVictory()
        {
            IReadOnlyList<IBellParticipant> inPlay = Registry.GetInPlay();
            if (inPlay.Count != 1)
            {
                return null;
            }

            IBellParticipant survivor = inPlay[0];
            return BellCards.HasCompleteSet(survivor.Traits, survivor.Skills) ? survivor : null;
        }

        /// <summary>把一局重置到初始状态（不含参与者本身）。</summary>
        public void ResetRound()
        {
            Tracking.Clear();
            Pool.Clear();
            Phase = BellPhase.Normal;
            SequenceNumber = 0;
            LastResult = null;
            ResetTimer();
            if (PhaseChanged != null)
            {
                PhaseChanged(Phase);
            }
        }

        /// <summary>
        /// B13：把「已集齐完整人设」同步成角色身上的豁免标记，方便 UI 与其他系统读取。
        /// 真正的判定仍以卡片组为准（见 BellParticipantRegistry.GetRedistributable）。
        /// </summary>
        public void SyncImmunityFlags()
        {
            IReadOnlyList<IBellParticipant> inPlay = Registry.GetInPlay();
            for (int i = 0; i < inPlay.Count; i++)
            {
                IBellParticipant p = inPlay[i];
                p.IsImmuneToRedistribution = BellCards.HasCompleteSet(p.Traits, p.Skills);
            }
        }

        /// <summary>
        /// 文档 [005]：开局随机分配特质、技能与位置。
        /// 特质与每一级技能各自独立洗牌，因此开局通常不会直接凑齐一套（只是随机，不保证）。
        /// </summary>
        public void DealInitialCards()
        {
            IReadOnlyList<IBellParticipant> inPlay = Registry.GetInPlay();
            for (int i = 0; i < inPlay.Count; i++)
            {
                inPlay[i].ClearCards();
                inPlay[i].IsImmuneToRedistribution = false;
            }

            // 1) 特质：6 张洗牌后一人一张。
            var traits = new List<TraitCard>(BellCards.CreateAllTraits());
            Random.Shuffle(traits);
            for (int i = 0; i < inPlay.Count; i++)
            {
                inPlay[i].AddTrait(traits[i % traits.Count]);
            }

            // 2) 技能：每一级（本我/自我/超我）单独洗牌，保证「每种等级各 6 张」守恒。
            SkillTier[] tiers = BellCards.Tiers;
            IReadOnlyList<SkillCard> allSkills = BellCards.CreateAllSkills();
            for (int t = 0; t < tiers.Length; t++)
            {
                var perTier = new List<SkillCard>(inPlay.Count);
                for (int i = 0; i < allSkills.Count; i++)
                {
                    if (allSkills[i].Tier == tiers[t])
                    {
                        perTier.Add(allSkills[i]);
                    }
                }

                Random.Shuffle(perTier);
                for (int i = 0; i < inPlay.Count; i++)
                {
                    inPlay[i].AddSkill(perTier[i % perTier.Count]);
                }
            }
        }

        public void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            Registry.CollectIssues(report);
            Rules.CollectIssues(report);
            Tracking.CollectIssues(report);
            Pool.CollectIssues(report);

            for (int kind = 0; kind <= 2; kind++)
            {
                if (!effects.ContainsKey((BellKind)kind))
                {
                    report.Error("BellRuntime", "缺少 " + ((BellKind)kind).ToDisplayName() + " 的效果实现。");
                }
            }
        }

        private void RaiseBellRung(BellResult result)
        {
            if (BellRung != null)
            {
                BellRung(result);
            }
        }
    }
}
