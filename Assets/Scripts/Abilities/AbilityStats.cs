using System;
using System.Collections.Generic;
using Ring.Characters;

namespace Ring.Abilities
{
    /// <summary>
    /// 角色「身体」的基础数值——生命、防御、移速、攻击间隔。
    /// 技能与特质都只通过这个接口读身体数值，因此离线测试可以用
    /// <see cref="CharacterStatBlock"/> 代替 Unity 的 ScriptableObject。
    /// </summary>
    public interface IAbilityStats
    {
        /// <summary>生命上限。</summary>
        float MaxHealth { get; }

        /// <summary>防御（文档公式 减伤 = 防御 / 500）。</summary>
        float Defense { get; }

        /// <summary>减伤 0~1（文档给的折算结果，游侠是特例 12.5%）。</summary>
        float DamageReduction { get; }

        /// <summary>文档的原始速度值。</summary>
        float Speed { get; }

        /// <summary>移速（格/秒）。</summary>
        float MoveSpeedCellsPerSecond { get; }

        /// <summary>跳跃高度（格）。</summary>
        float JumpHeightCells { get; }

        /// <summary>基础攻击间隔（秒）。</summary>
        float AttackIntervalSeconds { get; }
    }

    /// <summary>
    /// 身体数值的不可变快照。离线测试用 <see cref="FromDocument"/> 拿文档值，
    /// 运行时由 Characters 层的适配器从 <c>CharacterBaseStats</c> 转过来。
    /// </summary>
    public readonly struct CharacterStatBlock : IAbilityStats
    {
        public CharacterStatBlock(
            PersonaId persona,
            float maxHealth,
            float defense,
            float damageReduction,
            float speed,
            float moveSpeedCellsPerSecond,
            float jumpHeightCells,
            float attackIntervalSeconds)
        {
            Persona = persona;
            MaxHealth = maxHealth;
            Defense = defense;
            DamageReduction = damageReduction;
            Speed = speed;
            MoveSpeedCellsPerSecond = moveSpeedCellsPerSecond;
            JumpHeightCells = jumpHeightCells;
            AttackIntervalSeconds = attackIntervalSeconds;
        }

        public PersonaId Persona { get; }

        public float MaxHealth { get; }

        public float Defense { get; }

        public float DamageReduction { get; }

        public float Speed { get; }

        public float MoveSpeedCellsPerSecond { get; }

        public float JumpHeightCells { get; }

        public float AttackIntervalSeconds { get; }

        /// <summary>按文档总表取某个人设的身体数值。</summary>
        public static CharacterStatBlock FromDocument(PersonaId persona)
        {
            switch (persona)
            {
                case PersonaId.Swordsman:
                    return new CharacterStatBlock(persona, 100f, 70f, 0.14f, 130f, 3.9f, 2f, 0.7f);
                case PersonaId.SteamMechanic:
                    return new CharacterStatBlock(persona, 110f, 82f, 0.164f, 108f, 3.24f, 2f, 0.8f);
                case PersonaId.Spartan:
                    return new CharacterStatBlock(persona, 116f, 120f, 0.24f, 72f, 2.16f, 2f, 0.9f);
                case PersonaId.Ranger:
                    // 文档自己标注的特例：防御 75 按公式是 15%，表里写 12.5%。
                    return new CharacterStatBlock(persona, 75f, 75f, 0.125f, 110f, 3.3f, 2f, 0.6f);
                case PersonaId.Clockmaker:
                    return new CharacterStatBlock(persona, 96f, 96f, 0.192f, 108f, 3.24f, 2f, 0.8f);
                case PersonaId.Strongman:
                    return new CharacterStatBlock(persona, 120f, 90f, 0.18f, 90f, 2.7f, 2f, 0.9f);
                default:
                    return new CharacterStatBlock(persona, 100f, 70f, 0.14f, 130f, 3.9f, 2f, 0.7f);
            }
        }

        /// <summary>六份文档快照，顺序同 <see cref="PersonaIds.All"/>。</summary>
        public static IReadOnlyList<CharacterStatBlock> All
        {
            get { return AllBlocks; }
        }

        private static readonly CharacterStatBlock[] AllBlocks =
        {
            FromDocument(PersonaId.Swordsman),
            FromDocument(PersonaId.SteamMechanic),
            FromDocument(PersonaId.Spartan),
            FromDocument(PersonaId.Ranger),
            FromDocument(PersonaId.Clockmaker),
            FromDocument(PersonaId.Strongman),
        };

        public override string ToString()
        {
            return PersonaIds.ToChineseName(Persona) + " 生命 " + MaxHealth.ToString("0.##") + " 防御 "
                + Defense.ToString("0.##") + " 移速 " + MoveSpeedCellsPerSecond.ToString("0.##") + " 格/s";
        }
    }

    /// <summary>技能系统内部用的纯数学工具（不引用 UnityEngine，离线测试可编译）。</summary>
    public static class AbilityMath
    {
        public const float Epsilon = 0.0005f;

        public static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }

        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        public static float Max(float a, float b)
        {
            return a > b ? a : b;
        }

        public static float Min(float a, float b)
        {
            return a < b ? a : b;
        }

        public static bool Approximately(float a, float b)
        {
            return Math.Abs(a - b) <= Epsilon;
        }

        /// <summary>保留两位小数的百分比文本，日志与 UI 用。</summary>
        public static string Percent(float value, int decimals = 1)
        {
            return (value * 100f).ToString("0." + new string('#', decimals)) + "%";
        }

        public static string Number(float value)
        {
            return value.ToString("0.###");
        }
    }
}
