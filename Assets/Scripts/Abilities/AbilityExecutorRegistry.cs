using System;
using System.Collections.Generic;
using System.Text;
using Ring.Core;

namespace Ring.Abilities
{
    /// <summary>
    /// 一条 effectRef 经注册表解析后得到的「效果实例」描述。
    /// 纯数据、零 UnityEngine：战斗层可以拿它去真正生成投射物/地形，
    /// 离线测试与 Editor 校验也可以只打印它，不依赖场景。
    /// </summary>
    public readonly struct AbilityEffectInstance
    {
        public AbilityEffectInstance(
            AbilityEffectId effectId,
            string executorName,
            AbilityEffectScope scope,
            float parameter,
            string requiredFields,
            string summary,
            bool isImplemented)
        {
            EffectId = effectId;
            ExecutorName = executorName;
            Scope = scope;
            Parameter = parameter;
            RequiredFields = requiredFields;
            Summary = summary;
            IsImplemented = isImplemented;
        }

        public AbilityEffectId EffectId { get; }
        public string ExecutorName { get; }
        public AbilityEffectScope Scope { get; }
        public float Parameter { get; }
        public string RequiredFields { get; }
        public string Summary { get; }
        public bool IsImplemented { get; }

        public override string ToString()
        {
            return AbilityEffectIds.ToDisplayName(EffectId) + " → " + ExecutorName
                + "（" + AbilityEffectRef.ScopeToText(Scope) + "）"
                + (IsImplemented ? "" : " [执行器已登记，空间/表现待战斗层实现]")
                + (string.IsNullOrEmpty(Summary) ? "" : "：" + Summary);
        }
    }

    /// <summary>
    /// 效果执行器接口：把「技能定义 + 一条 effectRef」翻译成效果实例。
    /// 现阶段执行器只做**解析与摘要**（真正的命中判定/表现由战斗层消费实例后实现），
    /// 但映射本身是可查询、可校验、可替换的 —— 战斗层实现空间判定后只要
    /// <see cref="AbilityExecutorRegistry.Register"/> 覆盖同名执行器，框架代码不用改。
    /// </summary>
    public interface IAbilityEffectExecutor
    {
        AbilityEffectId EffectId { get; }
        string ExecutorName { get; }
        string RequiredFields { get; }

        /// <summary>该执行器的行为是否已在本工程里真正落地（区分「登记入口」与「已实现」）。</summary>
        bool IsImplemented { get; }

        /// <summary>按技能定义与引用生成一条效果实例描述。</summary>
        AbilityEffectInstance Describe(AbilityDefinition definition, AbilityEffectRef reference);
    }

    /// <summary>内置执行器基类：统一处理名称、字段清单与参数摘要。</summary>
    public abstract class AbilityEffectExecutorBase : IAbilityEffectExecutor
    {
        protected AbilityEffectExecutorBase(AbilityEffectId id, string executorName, bool isImplemented)
        {
            EffectId = id;
            ExecutorName = executorName;
            IsImplemented = isImplemented;
        }

        public AbilityEffectId EffectId { get; }
        public string ExecutorName { get; }
        public string RequiredFields => AbilityEffectIds.ToRequiredFields(EffectId);
        public bool IsImplemented { get; }

        public AbilityEffectInstance Describe(AbilityDefinition definition, AbilityEffectRef reference)
        {
            AbilityEffectScope scope = reference != null ? reference.Scope : AbilityEffectScope.Auto;
            float parameter = reference != null ? reference.Parameter : 0f;
            string summary = definition == null ? string.Empty : BuildSummary(definition);
            return new AbilityEffectInstance(
                EffectId, ExecutorName, scope, parameter, RequiredFields, summary, IsImplemented);
        }

        protected abstract string BuildSummary(AbilityDefinition d);

        protected static string Num(float value)
        {
            return AbilityMath.Number(value);
        }

        protected static void Append(StringBuilder builder, string label, float value, string unit = "")
        {
            if (value > 0f)
            {
                if (builder.Length > 0)
                {
                    builder.Append('、');
                }

                builder.Append(label).Append(' ').Append(Num(value)).Append(unit);
            }
        }
    }

    // ---------------- 13 类内置执行器 ----------------

    public sealed class LineProjectileExecutor : AbilityEffectExecutorBase
    {
        public LineProjectileExecutor() : base(AbilityEffectId.LineProjectile, "LineProjectileExecutor", false) { }
        protected override string BuildSummary(AbilityDefinition d)
        {
            var b = new StringBuilder();
            Append(b, "射程", d.RangeCells, "格");
            Append(b, "宽", d.AreaWidthCells, "格");
            Append(b, "弹速", d.ProjectileSpeed, "格/s");
            Append(b, "伤害", d.Damage);
            return b.ToString();
        }
    }

    public sealed class FanPelletsExecutor : AbilityEffectExecutorBase
    {
        public FanPelletsExecutor() : base(AbilityEffectId.FanPellets, "FanPelletsExecutor", false) { }
        protected override string BuildSummary(AbilityDefinition d)
        {
            var b = new StringBuilder();
            Append(b, "散射角", d.SpreadDegrees, "°");
            if (d.PelletCount > 0)
            {
                if (b.Length > 0) { b.Append('、'); }
                b.Append(d.PelletCount).Append('×').Append(Num(d.PelletDamage));
            }
            Append(b, "弹速", d.ProjectileSpeed, "格/s");
            return b.ToString();
        }
    }

    public sealed class ReturningProjectileExecutor : AbilityEffectExecutorBase
    {
        public ReturningProjectileExecutor() : base(AbilityEffectId.ReturningProjectile, "ReturningProjectileExecutor", false) { }
        protected override string BuildSummary(AbilityDefinition d)
        {
            var b = new StringBuilder();
            Append(b, "去程", d.Damage);
            Append(b, "回程", d.SecondaryDamage);
            Append(b, "弹速", d.ProjectileSpeed, "格/s");
            return b.ToString();
        }
    }

    public sealed class InstantAreaExecutor : AbilityEffectExecutorBase
    {
        public InstantAreaExecutor() : base(AbilityEffectId.InstantArea, "InstantAreaExecutor", false) { }
        protected override string BuildSummary(AbilityDefinition d)
        {
            var b = new StringBuilder();
            if (d.AreaLengthCells > 0f && d.AreaWidthCells > 0f)
            {
                b.Append(Num(d.AreaLengthCells)).Append('×').Append(Num(d.AreaWidthCells)).Append('格');
            }
            Append(b, "半径", d.RadiusCells, "格");
            Append(b, "伤害", d.Damage);
            return b.ToString();
        }
    }

    public sealed class DashExecutor : AbilityEffectExecutorBase
    {
        public DashExecutor() : base(AbilityEffectId.Dash, "DashExecutor", false) { }
        protected override string BuildSummary(AbilityDefinition d)
        {
            var b = new StringBuilder();
            Append(b, "位移", d.DashCells, "格");
            Append(b, "冲刺速度", d.DashSpeedCellsPerSecond, "格/s");
            Append(b, "前摇", d.WindupSeconds, "s");
            return b.ToString();
        }
    }

    public sealed class ForcedDisplacementExecutor : AbilityEffectExecutorBase
    {
        public ForcedDisplacementExecutor() : base(AbilityEffectId.ForcedDisplacement, "ForcedDisplacementExecutor", false) { }
        protected override string BuildSummary(AbilityDefinition d)
        {
            var b = new StringBuilder();
            Append(b, "射程", d.RangeCells, "格");
            Append(b, "定身", d.RootSeconds, "s");
            Append(b, "伤害", d.Damage);
            return b.ToString();
        }
    }

    public sealed class KnockbackExecutor : AbilityEffectExecutorBase
    {
        public KnockbackExecutor() : base(AbilityEffectId.Knockback, "KnockbackExecutor", true) { }
        protected override string BuildSummary(AbilityDefinition d)
        {
            var b = new StringBuilder();
            Append(b, "击退", d.KnockbackCells, "格");
            Append(b, "触发所需命中", d.RequiredHitsForTrigger);
            return b.ToString();
        }
    }

    public sealed class CrowdControlExecutor : AbilityEffectExecutorBase
    {
        public CrowdControlExecutor() : base(AbilityEffectId.CrowdControl, "CrowdControlExecutor", true) { }
        protected override string BuildSummary(AbilityDefinition d)
        {
            var b = new StringBuilder();
            Append(b, "减速", d.SlowPercent * 100f, "%");
            Append(b, "减速时长", d.SlowSeconds, "s");
            Append(b, "眩晕", d.StunSeconds, "s");
            Append(b, "定身", d.RootSeconds, "s");
            Append(b, "眩晕抗性", d.StunResistanceSeconds, "s");
            return b.ToString();
        }
    }

    public sealed class StatModifierExecutor : AbilityEffectExecutorBase
    {
        public StatModifierExecutor() : base(AbilityEffectId.StatModifier, "StatModifierExecutor", true) { }
        protected override string BuildSummary(AbilityDefinition d)
        {
            var b = new StringBuilder();
            Append(b, "移速", d.MoveSpeedMultiplier, "×");
            Append(b, "攻速", d.AttackSpeedMultiplier, "×");
            Append(b, "防御", d.DefenseMultiplier, "×");
            Append(b, "受伤", d.DamageTakenMultiplier, "×");
            Append(b, "易伤", d.DamageTakenBonusPercent * 100f, "%");
            Append(b, "减伤加成", d.DamageReductionBonus * 100f, "%");
            return b.ToString();
        }
    }

    public sealed class HealShieldExecutor : AbilityEffectExecutorBase
    {
        public HealShieldExecutor() : base(AbilityEffectId.HealShield, "HealShieldExecutor", true) { }
        protected override string BuildSummary(AbilityDefinition d)
        {
            var b = new StringBuilder();
            Append(b, "治疗", d.HealAmount);
            Append(b, "每秒回复", d.HealPerSecond, "/s");
            Append(b, "护盾", d.ShieldAmount);
            Append(b, "护盾时长", d.ShieldSeconds, "s");
            return b.ToString();
        }
    }

    public sealed class CleanseExecutor : AbilityEffectExecutorBase
    {
        public CleanseExecutor() : base(AbilityEffectId.Cleanse, "CleanseExecutor", false) { }
        protected override string BuildSummary(AbilityDefinition d)
        {
            return string.IsNullOrEmpty(d.Notes) ? "清负面（细则见 Notes）" : "清负面：" + d.Notes;
        }
    }

    public sealed class StanceExecutor : AbilityEffectExecutorBase
    {
        public StanceExecutor() : base(AbilityEffectId.Stance, "StanceExecutor", true) { }
        protected override string BuildSummary(AbilityDefinition d)
        {
            var b = new StringBuilder();
            Append(b, "持续", d.DurationSeconds, "s");
            Append(b, "平A伤害覆盖", d.BasicAttackDamageOverride);
            Append(b, "攻击间隔覆盖", d.AttackIntervalOverride, "s");
            if (d.MoveSpeedScalesWithHealth) { b.Append(b.Length > 0 ? "、" : "").Append("移速随生命比例"); }
            if (d.CanCancelEarly) { b.Append(b.Length > 0 ? "、" : "").Append("可提前结束"); }
            return b.ToString();
        }
    }

    public sealed class TerrainTimeFieldExecutor : AbilityEffectExecutorBase
    {
        public TerrainTimeFieldExecutor() : base(AbilityEffectId.TerrainTimeField, "TerrainTimeFieldExecutor", false) { }
        protected override string BuildSummary(AbilityDefinition d)
        {
            var b = new StringBuilder();
            Append(b, "持续", d.DurationSeconds, "s");
            Append(b, "半径", d.RadiusCells, "格");
            Append(b, "动作速度", d.MoveSpeedMultiplier, "×");
            Append(b, "减速", d.SlowPercent * 100f, "%");
            return b.ToString();
        }
    }

    /// <summary>
    /// 「effectRefs → 执行器」的全局注册表。
    ///
    /// 解决架构文档 §6 的缺口：<c>SkillDefinition.effectRefs[]</c> 过去只在文档里出现，
    /// 代码里没有任何「原语 ID → 可执行对象」的映射。本类把 13 类原语全部登记，
    /// 并提供 <see cref="Register"/> 让战斗层替换实现而不改框架代码（M3 3.1 验收标准）。
    /// </summary>
    public static class AbilityExecutorRegistry
    {
        private static readonly Dictionary<AbilityEffectId, IAbilityEffectExecutor> Executors = BuildDefaults();

        private static Dictionary<AbilityEffectId, IAbilityEffectExecutor> BuildDefaults()
        {
            var map = new Dictionary<AbilityEffectId, IAbilityEffectExecutor>(AbilityEffectIds.Count);
            Add(map, new LineProjectileExecutor());
            Add(map, new FanPelletsExecutor());
            Add(map, new ReturningProjectileExecutor());
            Add(map, new InstantAreaExecutor());
            Add(map, new DashExecutor());
            Add(map, new ForcedDisplacementExecutor());
            Add(map, new KnockbackExecutor());
            Add(map, new CrowdControlExecutor());
            Add(map, new StatModifierExecutor());
            Add(map, new HealShieldExecutor());
            Add(map, new CleanseExecutor());
            Add(map, new StanceExecutor());
            Add(map, new TerrainTimeFieldExecutor());
            return map;
        }

        private static void Add(Dictionary<AbilityEffectId, IAbilityEffectExecutor> map, IAbilityEffectExecutor executor)
        {
            map[executor.EffectId] = executor;
        }

        /// <summary>已登记的执行器数量（正常为 13）。</summary>
        public static int Count => Executors.Count;

        /// <summary>全部已登记执行器（按原语 ID 排序）。</summary>
        public static IReadOnlyList<IAbilityEffectExecutor> All
        {
            get
            {
                var list = new List<IAbilityEffectExecutor>(Executors.Count);
                for (int i = 0; i < AbilityEffectIds.All.Length; i++)
                {
                    IAbilityEffectExecutor executor;
                    if (Executors.TryGetValue(AbilityEffectIds.All[i], out executor))
                    {
                        list.Add(executor);
                    }
                }

                return list;
            }
        }

        public static bool IsRegistered(AbilityEffectId id)
        {
            return Executors.ContainsKey(id);
        }

        public static bool TryResolve(AbilityEffectId id, out IAbilityEffectExecutor executor)
        {
            return Executors.TryGetValue(id, out executor);
        }

        public static IAbilityEffectExecutor Resolve(AbilityEffectId id)
        {
            IAbilityEffectExecutor executor;
            return TryResolve(id, out executor) ? executor : null;
        }

        /// <summary>
        /// 注册 / 替换一个执行器。战斗层实现真实命中判定后调用此方法即可，
        /// 不需要修改 <see cref="AbilityDefinition"/> 或 <see cref="AbilityCatalog"/>。
        /// </summary>
        public static void Register(IAbilityEffectExecutor executor)
        {
            if (executor == null || !AbilityEffectIds.IsDefined(executor.EffectId))
            {
                return;
            }

            Executors[executor.EffectId] = executor;
        }

        /// <summary>恢复全部内置执行器（离线测试之间隔离用）。</summary>
        public static void ResetToBuiltins()
        {
            Executors.Clear();
            var defaults = BuildDefaults();
            foreach (KeyValuePair<AbilityEffectId, IAbilityEffectExecutor> pair in defaults)
            {
                Executors[pair.Key] = pair.Value;
            }
        }

        /// <summary>把一条技能的全部 effectRefs 解析成效果实例列表。</summary>
        public static IReadOnlyList<AbilityEffectInstance> DescribeAll(AbilityDefinition definition)
        {
            var list = new List<AbilityEffectInstance>();
            if (definition == null || definition.EffectRefs == null)
            {
                return list;
            }

            for (int i = 0; i < definition.EffectRefs.Count; i++)
            {
                AbilityEffectRef reference = definition.EffectRefs[i];
                if (reference == null)
                {
                    continue;
                }

                IAbilityEffectExecutor executor = Resolve(reference.EffectId);
                if (executor == null)
                {
                    list.Add(new AbilityEffectInstance(
                        reference.EffectId, "<未注册>", reference.Scope, reference.Parameter,
                        AbilityEffectIds.ToRequiredFields(reference.EffectId), "执行器缺失", false));
                    continue;
                }

                list.Add(executor.Describe(definition, reference));
            }

            return list;
        }

        /// <summary>注册表自检：13 类原语是否全部登记、键与执行器声明是否一致。</summary>
        public static void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            const string context = "效果执行器注册表";
            for (int i = 0; i < AbilityEffectIds.All.Length; i++)
            {
                AbilityEffectId id = AbilityEffectIds.All[i];
                IAbilityEffectExecutor executor;
                if (!Executors.TryGetValue(id, out executor) || executor == null)
                {
                    report.Error(context, "原语「" + AbilityEffectIds.ToDisplayName(id) + "」没有登记执行器。");
                    continue;
                }

                if (executor.EffectId != id)
                {
                    report.Error(context, "执行器 " + executor.ExecutorName + " 声明的 ID 与注册键不一致。");
                }

                if (string.IsNullOrEmpty(executor.ExecutorName))
                {
                    report.Warn(context, "原语「" + AbilityEffectIds.ToDisplayName(id) + "」的执行器没有名字。");
                }
            }

            report.Info(context, "已登记 " + Executors.Count + "/" + AbilityEffectIds.Count
                + " 类原语；其中数值型已由 AbilityRuntime 落地，空间判定型仅登记入口。");
        }

        /// <summary>一行式映射总表，Editor 与文档生成用。</summary>
        public static string ToOverview()
        {
            var builder = new StringBuilder();
            builder.Append("effectRefs → 执行器映射（").Append(Executors.Count).Append(" 类）：");
            for (int i = 0; i < AbilityEffectIds.All.Length; i++)
            {
                IAbilityEffectExecutor executor = Resolve(AbilityEffectIds.All[i]);
                builder.Append('\n').Append((i + 1).ToString("00")).Append(". ")
                    .Append(AbilityEffectIds.ToDisplayName(AbilityEffectIds.All[i]))
                    .Append(" → ")
                    .Append(executor != null ? executor.ExecutorName : "<未登记>")
                    .Append(executor != null && executor.IsImplemented ? " [已落地]" : " [登记入口]");
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// effectRefs 构建器：把「少量通用字段 + AbilityShape」的扁平定义，
    /// 反推出它用到了 13 类原语中的哪几类。
    ///
    /// 这一步让 Docs/04 第六节那张「13 类原语 → 字段映射」表从**文档说明**变成**可执行规则**：
    /// 代码种子生成资产时自动为 30 条技能填好 effectRefs，策划在 Inspector 里再按需增删。
    /// </summary>
    public static class AbilityEffectRefs
    {
        public static List<AbilityEffectRef> InferFrom(AbilityDefinition d)
        {
            var refs = new List<AbilityEffectRef>(6);
            if (d == null)
            {
                return refs;
            }

            bool selfShape = d.Shape == AbilityShape.Self || d.Shape == AbilityShape.None;

            // ① 直线弹道 / ② 扇形多弹丸 / ③ 折返弹丸
            if (d.Shape == AbilityShape.Fan || d.PelletCount > 1)
            {
                refs.Add(new AbilityEffectRef(AbilityEffectId.FanPellets, AbilityEffectScope.Area, d.PelletDamage));
            }
            else if (d.Returns)
            {
                refs.Add(new AbilityEffectRef(AbilityEffectId.ReturningProjectile, AbilityEffectScope.Area, d.SecondaryDamage));
            }
            else if (d.Shape == AbilityShape.Line && d.ProjectileSpeed > 0f)
            {
                refs.Add(new AbilityEffectRef(AbilityEffectId.LineProjectile, AbilityEffectScope.Target, d.Damage));
            }

            // ④ 矩形/圆形即时判定（地面形状单独归 ⑬）
            if (d.Shape == AbilityShape.Rect || d.Shape == AbilityShape.Circle)
            {
                refs.Add(new AbilityEffectRef(AbilityEffectId.InstantArea, AbilityEffectScope.Area, d.Damage));
            }
            else if ((d.Shape == AbilityShape.Rect || d.Shape == AbilityShape.Circle || d.Shape == AbilityShape.Line)
                     && (d.AreaLengthCells > 0f || d.RadiusCells > 0f) && d.Shape != AbilityShape.Ground)
            {
                refs.Add(new AbilityEffectRef(AbilityEffectId.InstantArea, AbilityEffectScope.Area, d.Damage));
            }

            // ⑤ 突进 / 冲刺
            if (d.DashCells > 0f)
            {
                refs.Add(new AbilityEffectRef(AbilityEffectId.Dash, AbilityEffectScope.Self, d.DashCells));
            }

            // ⑥ 强制拉回（锚点语义写在 Notes 里）
            bool isAnchor = !string.IsNullOrEmpty(d.Notes) && d.Notes.Contains("锚点");
            if (isAnchor && d.RootSeconds > 0f)
            {
                refs.Add(new AbilityEffectRef(AbilityEffectId.ForcedDisplacement, AbilityEffectScope.Target, d.RootSeconds,
                    "1s 后拉回锚点坐标，由战斗层记录锚点"));
            }

            // ⑦ 击退
            if (d.KnockbackCells > 0f)
            {
                refs.Add(new AbilityEffectRef(AbilityEffectId.Knockback, AbilityEffectScope.Target, d.KnockbackCells));
            }

            // ⑧ 控制
            if (d.SlowPercent > 0f || d.StunSeconds > 0f || d.RootSeconds > 0f || d.StunResistanceSeconds > 0f)
            {
                refs.Add(new AbilityEffectRef(AbilityEffectId.CrowdControl,
                    selfShape ? AbilityEffectScope.Self : AbilityEffectScope.Target, d.SlowPercent));
            }

            // ⑨ 增益减益与乘区
            if (HasStatModifier(d))
            {
                refs.Add(new AbilityEffectRef(AbilityEffectId.StatModifier,
                    selfShape ? AbilityEffectScope.Self : AbilityEffectScope.Target));
            }

            // ⑩ 治疗与护盾
            if (d.HealAmount > 0f || d.HealPerSecond > 0f || d.ShieldAmount > 0f)
            {
                refs.Add(new AbilityEffectRef(AbilityEffectId.HealShield, AbilityEffectScope.Self, d.HealAmount));
            }

            // ⑪ 净化
            if (!string.IsNullOrEmpty(d.Notes)
                && (d.Notes.Contains("清除") || d.Notes.Contains("净化") || d.Notes.Contains("清负面")))
            {
                refs.Add(new AbilityEffectRef(AbilityEffectId.Cleanse, AbilityEffectScope.Self));
            }

            // ⑫ 姿态 / 状态切换
            if (d.BasicAttackDamageOverride > 0f || d.AttackIntervalOverride > 0f
                || d.MoveSpeedScalesWithHealth || d.CanCancelEarly || d.DefenseFlatBonus > 0f)
            {
                refs.Add(new AbilityEffectRef(AbilityEffectId.Stance, AbilityEffectScope.Self, d.DurationSeconds));
            }

            // 时间回滚 / 状态快照（逆时、回溯）：13 类原语里没有独立的「快照」类，
            // M3 侧统一登记为「姿态/状态切换」入口；真正的 3 秒生命快照与增益回滚
            // 由 M2.5 / AbilityRuntime.TryConsumeLethalHit 负责。这样 30 条能力都有可解析的
            // effectRefs，同时不会虚报「M3 已实现快照」。
            bool isRewind = !string.IsNullOrEmpty(d.Notes)
                && (d.Notes.Contains("快照") || d.Notes.Contains("回滚")
                    || d.Notes.Contains("倒回") || d.Notes.Contains("还原"));
            if (isRewind && !refs.Exists(r => r != null && r.EffectId == AbilityEffectId.Stance))
            {
                refs.Add(new AbilityEffectRef(AbilityEffectId.Stance, AbilityEffectScope.Self, d.DurationSeconds,
                    "状态快照/回滚由 M2.5 与 AbilityRuntime 实现，M3 仅登记状态切换入口"));
            }

            // ⑬ 地形 / 时间流速场
            if (d.Shape == AbilityShape.Ground
                || (d.Shape == AbilityShape.Circle && d.DurationSeconds > 0f && d.MoveSpeedMultiplier > 0f))
            {
                refs.Add(new AbilityEffectRef(AbilityEffectId.TerrainTimeField, AbilityEffectScope.Area, d.DurationSeconds,
                    d.RequiresPointerTarget ? "需鼠标选点" : ""));
            }

            return refs;
        }

        private static bool HasStatModifier(AbilityDefinition d)
        {
            return d.MoveSpeedMultiplier > 0f || d.AttackSpeedMultiplier > 0f || d.DefenseMultiplier > 0f
                || d.DamageTakenMultiplier > 0f || d.DamageTakenBonusPercent > 0f || d.DamageReductionBonus > 0f
                || d.DefenseFlatBonus > 0f || d.StackDamageBonus > 0f || d.StackMoveSpeedBonus > 0f
                || d.StackAttackDamageBonus > 0f || d.RangeCells > 0f && d.Shape == AbilityShape.None;
        }

        /// <summary>一条技能的效果摘要（Editor 列表 / 文档生成用）。</summary>
        public static string ToSummary(AbilityDefinition d)
        {
            var refs = d != null && d.EffectRefs != null ? d.EffectRefs : new List<AbilityEffectRef>();
            if (refs.Count == 0)
            {
                return "（未配置 effectRefs）";
            }

            var names = new List<string>(refs.Count);
            for (int i = 0; i < refs.Count; i++)
            {
                if (refs[i] != null)
                {
                    names.Add(AbilityEffectIds.ToDisplayName(refs[i].EffectId));
                }
            }

            return string.Join(" + ", names);
        }
    }
}
