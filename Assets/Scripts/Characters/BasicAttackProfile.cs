using System.Collections.Generic;
using Ring.Core;
using UnityEngine;

namespace Ring.Characters
{
    /// <summary>
    /// 普通攻击的基础参数。这里只描述「打出去长什么样」，
    /// 命中判定与伤害结算留给战斗系统，符合本轮「技能/战斗暂不动」的边界。
    /// </summary>
    [System.Serializable]
    public sealed class BasicAttackProfile
    {
        [Tooltip("攻击名，只用于日志与策划对表。")]
        [SerializeField] private string attackName = "近战横斩";

        [Tooltip("单次命中的基础伤害。散弹类填「每枚弹片」的伤害。")]
        [SerializeField] private float damage = 4f;

        [Tooltip("前摇（秒）：从按下到判定生效的时间。")]
        [SerializeField] private float windupSeconds = 0.15f;

        [Tooltip("射程（格）。近战为矩形的长，弹丸为最大飞行距离。")]
        [SerializeField] private float rangeCells = 2f;

        [Tooltip("近战矩形的宽（格）。0 表示不适用（例如扇形弹丸由散射角决定）。")]
        [SerializeField] private float widthCells = 2f;

        [Tooltip("弹丸速度（格/秒）。0 表示近战，不生成飞行物。")]
        [SerializeField] private float projectileSpeed;

        [Tooltip("弹丸数量。近战固定 1。")]
        [SerializeField] private int projectileCount = 1;

        [Tooltip("散射角（度，总角）。0 表示直线。>0 时弹丸均匀分布在 ±总角/2 内。")]
        [SerializeField] private float spreadDegrees;

        [Tooltip("弹丸是否折返（钟表师的时钟飞刃）。")]
        [SerializeField] private bool returns;

        /// <summary>攻击名。</summary>
        public string AttackName => attackName;

        /// <summary>单次命中的基础伤害。</summary>
        public float Damage => damage;

        /// <summary>前摇（秒）。</summary>
        public float WindupSeconds => windupSeconds;

        /// <summary>射程（格）。</summary>
        public float RangeCells => rangeCells;

        /// <summary>近战矩形的宽（格），0 表示不适用。</summary>
        public float WidthCells => widthCells;

        /// <summary>弹丸速度（格/秒），0 表示近战。</summary>
        public float ProjectileSpeed => projectileSpeed;

        /// <summary>弹丸数量。</summary>
        public int ProjectileCount => projectileCount;

        /// <summary>散射总角（度）。</summary>
        public float SpreadDegrees => spreadDegrees;

        /// <summary>弹丸是否折返。</summary>
        public bool Returns => returns;

        /// <summary>是否生成飞行物。</summary>
        public bool IsProjectile => projectileSpeed > 0f;

        /// <summary>是否为多弹片扇形（机械师散弹）。</summary>
        public bool IsFan => IsProjectile && projectileCount > 1 && spreadDegrees > 0f;

        /// <summary>第 index 枚弹丸相对朝向的角度偏移（度）。单发时返回 0。</summary>
        public float GetPelletAngleDegrees(int index)
        {
            if (!IsFan)
            {
                return 0f;
            }

            int count = Mathf.Max(1, projectileCount);
            if (count == 1)
            {
                return 0f;
            }

            // 把 count 枚弹丸均匀铺满 [-半角, +半角]。
            float half = spreadDegrees * 0.5f;
            float step = spreadDegrees / (count - 1);
            int clamped = Mathf.Clamp(index, 0, count - 1);
            return -half + step * clamped;
        }

        /// <summary>收集本配置的问题。</summary>
        public void CollectIssues(string context, ValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(attackName))
            {
                report.Warn(context, "普通攻击没有名字，日志里会看不出是谁在出手。");
            }

            if (damage <= 0f)
            {
                report.Error(context, "普通攻击伤害必须大于 0。");
            }

            if (windupSeconds < 0f)
            {
                report.Error(context, "前摇不能为负。");
            }

            if (rangeCells <= 0f)
            {
                report.Error(context, "射程必须大于 0。");
            }

            if (!IsProjectile)
            {
                if (widthCells <= 0f)
                {
                    report.Warn(context, "近战攻击的宽度为 0，命中区域会退化。");
                }

                if (projectileCount != 1)
                {
                    report.Error(context, "近战攻击的弹丸数量应为 1（当前 " + projectileCount + "）。");
                }

                if (returns)
                {
                    report.Warn(context, "近战攻击不会折返，勾选「折返」不生效。");
                }
            }
            else
            {
                if (projectileSpeed > rangeCells * 10f)
                {
                    report.Info(context, "弹丸速度 " + projectileSpeed + " 格/秒 明显高于射程 " + rangeCells + " 格，飞行时间会很短。");
                }

                if (projectileCount < 1)
                {
                    report.Error(context, "弹丸数量至少为 1。");
                }

                if (spreadDegrees < 0f || spreadDegrees > 360f)
                {
                    report.Error(context, "散射角应在 0~360 度之间。");
                }
            }
        }

        /// <summary>调试用多行摘要。</summary>
        public string ToSummary()
        {
            string shape = IsProjectile
                ? "弹丸 " + projectileCount + " 枚 / " + projectileSpeed + " 格每秒" + (IsFan ? " / 散射 " + spreadDegrees + "°" : string.Empty)
                : "近战 " + rangeCells + "×" + widthCells + " 格";
            return attackName + "（" + damage + " 伤害，" + windupSeconds + "s 前摇，" + shape + (returns ? "，折返" : string.Empty) + "）";
        }
    }
}
