using Ring.Abilities;
using Ring.Core;
using UnityEngine;

namespace Ring.Characters
{
    /// <summary>
    /// 一个人设的「基础数值」资产。六个角色各一份，全部可在 Inspector 里改，
    /// 对应文档的结论：「为了测试和改动方便，数据需要能够在 inspector 中配置」。
    ///
    /// 这里只放数值，不放任何行为。技能与特质的效果逻辑在各自的系统里，
    /// 用 <see cref="PersonaId"/> 反查本资产即可。
    /// </summary>
    [CreateAssetMenu(fileName = "Character_Swordsman", menuName = "钟声/角色基础数值", order = 0)]
    public sealed class CharacterBaseStats : ScriptableObject
    {
        [Header("身份")]
        [Tooltip("人设标识。数值表、钟声、技能卡都以它为主键，不要重复。")]
        [SerializeField] private PersonaId persona = PersonaId.Swordsman;

        [Tooltip("中文名，用于 UI 与日志。")]
        [SerializeField] private string displayName = "剑客";

        [Tooltip("定位，仅用于策划对照。")]
        [SerializeField] private string positioning = "最高机动，高频连击";

        [Header("特质（只记录归属，效果由特质系统实现）")]
        [Tooltip("该人设的固有特质名。")]
        [SerializeField] private string traitName = "剑意";

        [TextArea(2, 4)]
        [Tooltip("特质说明，仅用于 UI。")]
        [SerializeField] private string traitDescription = string.Empty;

        [Header("生存")]
        [Tooltip("生命上限。")]
        [SerializeField] private float maxHealth = 100f;

        [Tooltip("防御。文档公式下 每 500 点防御 = 100% 减伤。")]
        [SerializeField] private float defense = 70f;

        [Range(0f, 1f)]
        [Tooltip("减伤 0~1。文档给出的是折算后的结果，因此直接配置；公式 防御/500 只用于校验与回填。")]
        [SerializeField] private float damageReduction = 0.14f;

        [Header("移动")]
        [Tooltip("速度。文档的原始速度值，移速由它换算。")]
        [SerializeField] private float speed = 130f;

        [Tooltip("移速（格/秒）。文档公式为 速度 × 0.03。")]
        [SerializeField] private float moveSpeedCellsPerSecond = 3.9f;

        [Tooltip("跳跃高度（格）。六人相同，仍各自保存便于单独调整。")]
        [SerializeField] private float jumpHeightCells = 2f;

        [Header("普通攻击")]
        [Tooltip("基础攻击间隔（秒）。技能带来的攻速变化由战斗系统在运行时乘算，不改这里。")]
        [SerializeField] private float attackIntervalSeconds = 0.7f;

        [SerializeField] private BasicAttackProfile basicAttack = new BasicAttackProfile();

        [Header("闪避")]
        [Tooltip("闪避位移距离（格）。")]
        [SerializeField] private float dodgeDistanceCells = 3f;

        [Tooltip("闪避位移耗时（秒）。")]
        [SerializeField] private float dodgeDurationSeconds = 0.25f;

        [Tooltip("闪避冷却（秒）。")]
        [SerializeField] private float dodgeCooldownSeconds = 3f;

        [Tooltip("闪避期间是否无敌。")]
        [SerializeField] private bool dodgeGrantsInvulnerability = true;

        [Header("策划备注")]
        [TextArea(3, 8)]
        [Tooltip("文档里的存疑点、待定值写在这里，避免被后面的人当成定论。")]
        [SerializeField] private string designNotes = string.Empty;

        /// <summary>人设标识。</summary>
        public PersonaId Persona => persona;

        /// <summary>中文名。</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? persona.ToChineseName() : displayName;

        /// <summary>定位描述。</summary>
        public string Positioning => positioning;

        /// <summary>固有特质名。</summary>
        public string TraitName => traitName;

        /// <summary>特质说明。</summary>
        public string TraitDescription => traitDescription;

        /// <summary>生命上限。</summary>
        public float MaxHealth => maxHealth;

        /// <summary>防御。</summary>
        public float Defense => defense;

        /// <summary>减伤（0~1）。</summary>
        public float DamageReduction => damageReduction;

        /// <summary>速度（文档原始值）。</summary>
        public float Speed => speed;

        /// <summary>移速（格/秒）。</summary>
        public float MoveSpeedCellsPerSecond => moveSpeedCellsPerSecond;

        /// <summary>跳跃高度（格）。</summary>
        public float JumpHeightCells => jumpHeightCells;

        /// <summary>基础攻击间隔（秒）。</summary>
        public float AttackIntervalSeconds => attackIntervalSeconds;

        /// <summary>普通攻击参数。</summary>
        public BasicAttackProfile BasicAttack => basicAttack;

        /// <summary>闪避距离（格）。</summary>
        public float DodgeDistanceCells => dodgeDistanceCells;

        /// <summary>闪避耗时（秒）。</summary>
        public float DodgeDurationSeconds => dodgeDurationSeconds;

        /// <summary>闪避冷却（秒）。</summary>
        public float DodgeCooldownSeconds => dodgeCooldownSeconds;

        /// <summary>闪避是否带无敌帧。</summary>
        public bool DodgeGrantsInvulnerability => dodgeGrantsInvulnerability;

        /// <summary>策划备注。</summary>
        public string DesignNotes => designNotes;

        /// <summary>按本资产的减伤折算计一次伤害。</summary>
        public float MitigateDamage(float rawDamage)
        {
            return CharacterBalance.ApplyReduction(rawDamage, damageReduction);
        }

        /// <summary>
        /// 按文档公式回填派生值：减伤 = 防御/500，移速 = 速度×0.03。
        /// <paramref name="includeDocumentedExceptions"/> 为 false 时跳过文档标注过的特例（精灵游侠 12.5%）。
        /// 返回是否真的改动过字段，方便 Editor 工具汇总「改了几个」。
        /// </summary>
        public bool ApplyFormulaValues(bool includeDocumentedExceptions = false)
        {
            bool changed = false;

            BalanceDecision decision = CharacterBalance.ActiveDecision;
            // 默认回填跳过「登记了覆盖值」的人设（保留 Q1 的 12.5%）；
            // 但策略切到 FormulaOnly 时，即使有覆盖也一律按公式 —— 由资产策略决定，不改代码。
            bool keepOverride = !includeDocumentedExceptions
                                && decision.Policy != ReductionPolicy.FormulaOnly
                                && decision.HasOverride(persona);

            if (!keepOverride)
            {
                float reduction = decision.ResolveReduction(persona, defense);
                if (!CharacterBalance.Approximately(damageReduction, reduction))
                {
                    damageReduction = reduction;
                    changed = true;
                }
            }

            float cellsPerSecond = CharacterBalance.CellsPerSecondFromSpeed(speed);
            if (!CharacterBalance.Approximately(moveSpeedCellsPerSecond, cellsPerSecond))
            {
                moveSpeedCellsPerSecond = cellsPerSecond;
                changed = true;
            }

            return changed;
        }

        /// <summary>收集本资产的配置问题。由 Editor 工具与运行时自检共用。</summary>
        public void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            string context = name + "（" + persona.ToChineseName() + "）";

            if (!PersonaIds.IsDefined(persona))
            {
                report.Error(context, "人设标识未定义。");
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                report.Warn(context, "没有填中文名，UI 会退回枚举名。");
            }

            if (maxHealth <= 0f)
            {
                report.Error(context, "生命上限必须大于 0。");
            }

            if (defense < 0f)
            {
                report.Error(context, "防御不能为负。");
            }

            if (damageReduction < 0f || damageReduction > 1f)
            {
                report.Error(context, "减伤必须在 0~1 之间。");
            }

            if (speed <= 0f)
            {
                report.Error(context, "速度必须大于 0。");
            }

            if (moveSpeedCellsPerSecond <= 0f)
            {
                report.Error(context, "移速必须大于 0。");
            }

            if (attackIntervalSeconds <= 0f)
            {
                report.Error(context, "攻击间隔必须大于 0。");
            }

            if (jumpHeightCells <= 0f)
            {
                report.Error(context, "跳跃高度必须大于 0。");
            }

            if (dodgeDistanceCells <= 0f)
            {
                report.Error(context, "闪避距离必须大于 0。");
            }

            if (dodgeDurationSeconds <= 0f)
            {
                report.Error(context, "闪避耗时必须大于 0。");
            }

            if (dodgeCooldownSeconds < dodgeDurationSeconds)
            {
                report.Warn(context, "闪避冷却小于闪避耗时，连续闪避会有重叠。");
            }

            basicAttack?.CollectIssues(context, report);

            CheckAgainstFormula(context, report);
            CheckAgainstDocument(context, report);
        }

        /// <summary>
        /// 校验减伤/移速是否符合「当前资产决策」解析出来的值。
        /// Q1 的特例不再是这里一条含糊的 Warn：它由 BalanceDecision.CollectIssues 带依据地报 Info，
        /// 而本处只在「实际配置与策略解析值不符」时才报警，避免把刻意保留的文档值误判成错误。
        /// </summary>
        private void CheckAgainstFormula(string context, ValidationReport report)
        {
            float expectedReduction = CharacterBalance.ReductionFor(persona, defense);
            if (!CharacterBalance.Approximately(damageReduction, expectedReduction))
            {
                report.Warn(context, "减伤 " + Percent(damageReduction) + " 与当前策略解析值 " + Percent(expectedReduction)
                    + " 不一致（防御 " + defense + "，策略 " + CharacterBalance.ActiveDecision.Policy
                    + "）。若是有意的 Q1 特例，请在 AbilityTuning.asset 的覆盖表里登记，而不是靠校验器放行。");
            }

            float expectedMove = CharacterBalance.CellsPerSecondFromSpeed(speed);
            if (!CharacterBalance.Approximately(moveSpeedCellsPerSecond, expectedMove))
            {
                report.Warn(context, "移速 " + Round(moveSpeedCellsPerSecond) + " 格/秒 与公式 速度×0.03 = "
                    + Round(expectedMove) + " 不一致（速度 " + speed + "）。");
            }
        }

        /// <summary>与文档总表逐项对照，防止手改后偏离设计。</summary>
        private void CheckAgainstDocument(string context, ValidationReport report)
        {
            CharacterReferenceRow expected = CharacterReferenceTable.Get(persona);
            if (expected == null)
            {
                return;
            }

            Compare(context, report, "生命上限", maxHealth, expected.MaxHealth);
            Compare(context, report, "防御", defense, expected.Defense);
            Compare(context, report, "减伤", damageReduction, expected.DamageReduction);
            Compare(context, report, "速度", speed, expected.Speed);
            Compare(context, report, "移速", moveSpeedCellsPerSecond, expected.MoveSpeedCellsPerSecond);
            Compare(context, report, "攻击间隔", attackIntervalSeconds, expected.AttackIntervalSeconds);
            Compare(context, report, "跳跃高度", jumpHeightCells, expected.JumpHeightCells);
            Compare(context, report, "平A伤害", basicAttack.Damage, expected.BasicAttackDamage);
            Compare(context, report, "平A前摇", basicAttack.WindupSeconds, expected.BasicAttackWindupSeconds);
            Compare(context, report, "平A射程", basicAttack.RangeCells, expected.BasicAttackRangeCells);
            Compare(context, report, "平A弹丸速度", basicAttack.ProjectileSpeed, expected.BasicAttackProjectileSpeed);
        }

        private static void Compare(string context, ValidationReport report, string field, float actual, float expected)
        {
            if (!CharacterBalance.Approximately(actual, expected))
            {
                report.Info(context, field + " 现在是 " + Round(actual) + "，文档是 " + Round(expected) + "。");
            }
        }

        private static string Percent(float value)
        {
            return (value * 100f).ToString("0.##") + "%";
        }

        private static string Round(float value)
        {
            return value.ToString("0.###");
        }

        /// <summary>调试用摘要。</summary>
        public string ToSummary()
        {
            return DisplayName + "｜生命 " + Round(maxHealth) + "｜防御 " + Round(defense) + "（减伤 " + Percent(damageReduction)
                + "）｜速度 " + Round(speed) + "（" + Round(moveSpeedCellsPerSecond) + " 格/s）｜攻击间隔 "
                + Round(attackIntervalSeconds) + "s｜" + basicAttack.ToSummary();
        }
    }
}
