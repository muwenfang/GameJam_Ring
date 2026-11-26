using Ring.Abilities;
using UnityEngine;

namespace Ring.Characters
{
    /// <summary>
    /// 人物数值的换算规则。文档只给了「结果」，这里把推导过程固化成可复用公式，
    /// 让 Inspector 里配置的数值始终能被校验。
    ///
    /// 依据《数据和结构.txt》「六人设基础数值总表」：
    ///   减伤    = 防御 / 500        （70→14%，82→16.4%，120→24%，96→19.2%，90→18%）
    ///   移速    = 速度 * 0.03 格/s  （130→3.9，108→3.24，72→2.16，110→3.3，90→2.7）
    ///
    /// Q1（游侠 12.5% vs 公式 15%）已从「本类里一句硬编码判断」升级为
    /// <see cref="BalanceDecision"/>：公式除数、策略、特例覆盖表都在
    /// <c>AbilityTuning.asset</c> 里配置，本类只保留代码种子默认值并委托给运行时决策。
    /// 因此「策划改主意」不再需要改 C# 代码。
    /// </summary>
    public static class CharacterBalance
    {
        /// <summary>代码种子默认：每 500 点防御提供 100% 减伤。实际生效值以资产决策为准。</summary>
        public const float DefenseToReductionDivisor = 500f;

        /// <summary>代码种子默认：速度到移速（格/秒）的换算系数。</summary>
        public const float SpeedToCellsPerSecond = 0.03f;

        /// <summary>代码种子默认容差。</summary>
        public const float Tolerance = 0.0005f;

        /// <summary>当前生效的换算决策（来自资产，未注入时为文档默认）。</summary>
        public static BalanceDecision ActiveDecision => BalanceDecisionRuntime.Active;

        /// <summary>注入一份换算决策（由 <c>AbilityTuningAsset.Apply()</c> 调用）。</summary>
        public static void Configure(BalanceDecision decision)
        {
            BalanceDecisionRuntime.Configure(decision);
        }

        /// <summary>恢复文档默认决策。</summary>
        public static void ResetToDefault()
        {
            BalanceDecisionRuntime.ResetToDefault();
        }

        /// <summary>
        /// 是否登记了「减伤覆盖值」。过去这里硬编码 <c>persona == Ranger</c>，
        /// 现在由资产里的覆盖表决定；换特例 / 加特例都不用改代码。
        /// </summary>
        public static bool HasDocumentedException(PersonaId persona)
        {
            return ActiveDecision.HasOverride(persona);
        }

        /// <summary>按纯公式由防御推算减伤（0~1），不看覆盖表。</summary>
        public static float ReductionFromDefense(float defense)
        {
            if (defense <= 0f)
            {
                return 0f;
            }

            return ActiveDecision.ReductionFromDefense(defense);
        }

        /// <summary>按当前策略解析某人设的最终减伤（含 Q1 覆盖）。</summary>
        public static float ReductionFor(PersonaId persona, float defense)
        {
            return ActiveDecision.ResolveReduction(persona, defense);
        }

        /// <summary>按文档公式由速度推算移速（格/秒）。</summary>
        public static float CellsPerSecondFromSpeed(float speed)
        {
            return ActiveDecision.ResolveMoveSpeed(speed);
        }

        /// <summary>防御减伤后的实际伤害；减伤 0.14 表示吃 86%。</summary>
        public static float ApplyReduction(float rawDamage, float damageReduction)
        {
            return Mathf.Max(0f, rawDamage) * (1f - Mathf.Clamp01(damageReduction));
        }

        /// <summary>容差比较，避免 0.164f 这类十进制字面量的微小误差被判成不一致。</summary>
        public static bool Approximately(float a, float b)
        {
            return Mathf.Abs(a - b) <= ActiveDecision.Tolerance;
        }
    }

    /// <summary>
    /// 文档「六人设基础数值总表」的代码快照。
    /// 只用于三件事：Inspector 校验、一键回填默认值、新人设资产生成。
    /// 运行时的一切读取都应该走 <see cref="CharacterBaseStats"/>，不要从这里读。
    /// </summary>
    public sealed class CharacterReferenceRow
    {
        public PersonaId Persona;
        public string DisplayName;
        public string Positioning;
        public string TraitName;

        public float MaxHealth;
        public float Defense;
        public float DamageReduction;
        public float Speed;
        public float MoveSpeedCellsPerSecond;
        public float AttackIntervalSeconds;
        public float JumpHeightCells;

        // 普通攻击（文档「（2）细节」里的平A 条目，数值是可配置的基础项，不含技能逻辑）
        public string BasicAttackName;
        public float BasicAttackDamage;
        public float BasicAttackWindupSeconds;
        public float BasicAttackRangeCells;
        public float BasicAttackWidthCells;
        public float BasicAttackProjectileSpeed;
        public int BasicAttackProjectileCount;
        public float BasicAttackSpreadDegrees;
        public bool BasicAttackReturns;

        public string Notes;
    }

    /// <summary>文档快照表本体。</summary>
    public static class CharacterReferenceTable
    {
        private static readonly CharacterReferenceRow[] Rows =
        {
            new CharacterReferenceRow
            {
                Persona = PersonaId.Swordsman,
                DisplayName = "剑客",
                Positioning = "最高机动，高频连击",
                TraitName = "剑意",
                MaxHealth = 100f,
                Defense = 70f,
                DamageReduction = 0.14f,
                Speed = 130f,
                MoveSpeedCellsPerSecond = 3.9f,
                AttackIntervalSeconds = 0.7f,
                JumpHeightCells = 2f,
                BasicAttackName = "近战横斩",
                BasicAttackDamage = 4f,
                BasicAttackWindupSeconds = 0.15f,
                BasicAttackRangeCells = 2f,
                BasicAttackWidthCells = 2f,
                BasicAttackProjectileSpeed = 0f,
                BasicAttackProjectileCount = 1,
                BasicAttackSpreadDegrees = 0f,
                BasicAttackReturns = false,
                Notes = "文档对平A范围的表述自相矛盾：「正前方 2 格」与「1 格长 × 2 格宽」并存，这里按 2 格长、2 格宽记录，等战斗原型确认后再改。",
            },
            new CharacterReferenceRow
            {
                Persona = PersonaId.SteamMechanic,
                DisplayName = "蒸汽机械师",
                Positioning = "近身最高爆发，打完拉开",
                TraitName = "过载损耗",
                MaxHealth = 110f,
                Defense = 82f,
                DamageReduction = 0.164f,
                Speed = 108f,
                MoveSpeedCellsPerSecond = 3.24f,
                AttackIntervalSeconds = 0.8f,
                JumpHeightCells = 2f,
                BasicAttackName = "蒸汽铳（散弹）",
                BasicAttackDamage = 2f, // 每命中 1 枚的伤害
                BasicAttackWindupSeconds = 0.2f,
                BasicAttackRangeCells = 5f,
                BasicAttackWidthCells = 0f, // 扇形，宽度由散射角决定
                BasicAttackProjectileSpeed = 6f,
                BasicAttackProjectileCount = 4,
                BasicAttackSpreadDegrees = 28f,
                BasicAttackReturns = false,
                Notes = "扇形 28°（半角 ±14°），4 枚弹片均匀分布在 -14°/-4.7°/+4.7°/+14°。",
            },
            new CharacterReferenceRow
            {
                Persona = PersonaId.Spartan,
                DisplayName = "斯巴达战士",
                Positioning = "狂战士 / 举盾坦",
                TraitName = "斯巴达韧性",
                MaxHealth = 116f,
                Defense = 120f,
                DamageReduction = 0.24f,
                Speed = 72f,
                MoveSpeedCellsPerSecond = 2.16f,
                AttackIntervalSeconds = 0.9f,
                JumpHeightCells = 2f,
                BasicAttackName = "长矛突刺",
                BasicAttackDamage = 5f,
                BasicAttackWindupSeconds = 0.2f,
                BasicAttackRangeCells = 2f,
                BasicAttackWidthCells = 1f,
                BasicAttackProjectileSpeed = 0f,
                BasicAttackProjectileCount = 1,
                BasicAttackSpreadDegrees = 0f,
                BasicAttackReturns = false,
                Notes = "特质的「半举盾」额外 +8 防御与每秒 0.5 回血属于特质逻辑，不在基础数值里。",
            },
            new CharacterReferenceRow
            {
                Persona = PersonaId.Ranger,
                DisplayName = "精灵游侠",
                Positioning = "射程 8 格最远，脆皮",
                TraitName = "精灵之目",
                MaxHealth = 75f,
                Defense = 75f,
                // Q1：不在这里再写死 0.125，改由文档默认决策解析（当前为 12.5%，公式值为 15%）。
                DamageReduction = BalanceDecision.DocumentDefault.ResolveReduction(PersonaId.Ranger, 75f),
                Speed = 110f,
                MoveSpeedCellsPerSecond = 3.3f,
                AttackIntervalSeconds = 0.6f,
                JumpHeightCells = 2f,
                BasicAttackName = "游侠短弓",
                BasicAttackDamage = 3f,
                BasicAttackWindupSeconds = 0.15f,
                BasicAttackRangeCells = 8f,
                BasicAttackWidthCells = 1f,
                BasicAttackProjectileSpeed = 10f,
                BasicAttackProjectileCount = 1,
                BasicAttackSpreadDegrees = 0f,
                BasicAttackReturns = false,
                Notes = "⚠️ 文档标注：防御 75 按 公式应为 15% 减伤，表里写 12.5%（75/600）。这里保留文档值 12.5%，校验工具会提示这条偏差。",
            },
            new CharacterReferenceRow
            {
                Persona = PersonaId.Clockmaker,
                DisplayName = "钟表师",
                Positioning = "时间系：回溯 / 减速 / 致死撤销",
                TraitName = "逆时",
                MaxHealth = 96f,
                Defense = 96f,
                DamageReduction = 0.192f,
                Speed = 108f,
                MoveSpeedCellsPerSecond = 3.24f,
                AttackIntervalSeconds = 0.8f,
                JumpHeightCells = 2f,
                BasicAttackName = "时钟飞刃",
                BasicAttackDamage = 4f, // 去程 / 回程各 4 点
                BasicAttackWindupSeconds = 0.2f,
                BasicAttackRangeCells = 5f,
                BasicAttackWidthCells = 1f,
                BasicAttackProjectileSpeed = 8f,
                BasicAttackProjectileCount = 1,
                BasicAttackSpreadDegrees = 0f,
                BasicAttackReturns = true,
                Notes = "飞刃折返前不能再次攻击；「逆时」致死回滚冷却 30 秒属于特质逻辑。",
            },
            new CharacterReferenceRow
            {
                Persona = PersonaId.Strongman,
                DisplayName = "力士",
                Positioning = "近战控制 / 击退 / 眩晕",
                TraitName = "力量",
                MaxHealth = 120f,
                Defense = 90f,
                DamageReduction = 0.18f,
                Speed = 90f,
                MoveSpeedCellsPerSecond = 2.7f,
                AttackIntervalSeconds = 0.9f,
                JumpHeightCells = 2f,
                BasicAttackName = "出拳",
                BasicAttackDamage = 3f,
                BasicAttackWindupSeconds = 0.2f,
                BasicAttackRangeCells = 1f,
                BasicAttackWidthCells = 2f,
                BasicAttackProjectileSpeed = 0f,
                BasicAttackProjectileCount = 1,
                BasicAttackSpreadDegrees = 0f,
                BasicAttackReturns = false,
                Notes = "每第 3 次命中同一目标的眩晕属于特质逻辑。",
            },
        };

        /// <summary>按文档顺序排列的快照行。</summary>
        public static System.Collections.Generic.IReadOnlyList<CharacterReferenceRow> All => Rows;

        /// <summary>取某个人设的文档快照；未定义时返回 null。</summary>
        public static CharacterReferenceRow Get(PersonaId persona)
        {
            for (int i = 0; i < Rows.Length; i++)
            {
                if (Rows[i].Persona == persona)
                {
                    return Rows[i];
                }
            }

            return null;
        }
    }
}
