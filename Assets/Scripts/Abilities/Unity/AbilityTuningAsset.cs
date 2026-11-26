using System.Collections.Generic;
using Ring.Characters;
using Ring.Core;
using UnityEngine;

namespace Ring.Abilities
{
    /// <summary>
    /// 技能数值总表资产 —— M1「改表不改码」的落点。
    ///
    /// 过去 30 条技能数值是 <see cref="AbilityCatalog"/> 里的静态代码常量，
    /// 改一个数字要改 C# 并重新编译。本资产把 30 条定义（含 effectRefs）、
    /// 以及 Q1 的 <see cref="BalanceDecision"/> 全部搬到 Inspector：
    ///   · 策划在 Inspector 里改数值 / 改效果组合 / 改减伤策略，保存即生效；
    ///   · <see cref="Apply"/> 把数据注入 <see cref="AbilityCatalog"/> 与
    ///     <see cref="Ring.Characters.CharacterBalance"/>，战斗层不用改；
    ///   · 资产缺失或未拖入时，<see cref="ApplyFromResourcesOrDefault"/> 回退到代码种子，
    ///     行为与改造前完全一致，不会全员 0 数值。
    ///
    /// 接线：把本资产拖进场景 <c>GameFlow</c> 的「数值层 / Ability Tuning」字段即可。
    /// </summary>
    [CreateAssetMenu(fileName = "AbilityTuning", menuName = "钟声/技能数值总表", order = 0)]
    public sealed class AbilityTuningAsset : ScriptableObject
    {
        public const string DefaultAssetPath = "Assets/Data/Abilities/AbilityTuning.asset";

        [Header("30 条能力数值（改完保存即生效，不需要改代码）")]
        [Tooltip("Definitions 里每条对应 (人设, 槽位)；EffectRefs 指向 13 类效果原语。")]
        [SerializeField] private AbilityTuningData data = new AbilityTuningData();

        [Header("Q1 减伤决策见 Data.Balance")]
        [TextArea(2, 6)]
        [Tooltip("策划改数值时在这里记一笔，方便与代码种子做差异比对。")]
        [SerializeField] private string changeLog = "";

        [SerializeField]
        [Tooltip("Apply 时是否打印当前数值来源。")]
        private bool logOnApply = true;

        /// <summary>数据体；首次访问时兜底创建，避免资产被清空后空引用。</summary>
        public AbilityTuningData Data
        {
            get { return data ?? (data = new AbilityTuningData()); }
        }

        /// <summary>Q1 决策与换算参数。</summary>
        public BalanceDecision Balance => Data.Balance;

        public string ChangeLog => changeLog;

        /// <summary>
        /// 把资产数据注入运行时。调用后 <see cref="AbilityCatalog.Get"/> /
        /// <see cref="AbilityCard.Definition"/> / <see cref="Ring.Characters.CharacterBalance"/>
        /// 全部改读本资产，不需要重新编译代码。
        /// </summary>
        public void Apply()
        {
            AbilityTuningData snapshot = Data;
            if (!snapshot.IsComplete())
            {
                Debug.LogWarning("[" + name + "] 数值表不完整（应为 30 条且覆盖全部 人设×槽位），"
                    + "已跳过注入并回退代码种子。请用菜单「钟声/数值层/从代码种子生成技能数值资产」修复。", this);
                AbilityCatalog.ResetToSeed();
                CharacterBalance.ResetToDefault();
                return;
            }

            snapshot.Apply();
            if (logOnApply)
            {
                Debug.Log("[钟声] 数值层已注入：" + snapshot.SourceLabel
                    + "（30 条能力｜" + (snapshot.Balance != null ? snapshot.Balance.ToSummary() : "Balance 为空")
                    + "）｜来源：" + AbilityCatalog.SourceName, this);
            }
        }

        /// <summary>从代码种子重建整张表（Editor 菜单用；会覆盖当前资产里的手改值）。</summary>
        public void SeedFromCode()
        {
            data = AbilityTuningData.CreateFromSeed();
        }

        /// <summary>为缺少 effectRefs 的条目按字段自动补全，返回补全条数。</summary>
        public int EnsureEffectRefs()
        {
            return Data.EnsureEffectRefs();
        }

        public IReadOnlyList<string> CompareWithSeed()
        {
            return Data.CompareWithSeed();
        }

        public void CollectIssues(ValidationReport report)
        {
            Data.CollectIssues(report);
            if (report == null)
            {
                return;
            }

            report.Info("技能数值资产", "资产名 " + name + "｜路径 " + DefaultAssetPath
                + "｜变更记录：" + (string.IsNullOrWhiteSpace(changeLog) ? "（空）" : changeLog));
        }

        public string ToOverview()
        {
            return Data.ToOverview();
        }

        /// <summary>
        /// 运行时兜底：优先加载 <c>Resources/AbilityTuning</c>（若用户把资产放进 Resources），
        /// 否则显式恢复代码种子 + 文档默认 Q1 决策，保证域重载后状态确定。
        /// </summary>
        public static void ApplyFromResourcesOrDefault()
        {
            AbilityTuningAsset asset = Resources.Load<AbilityTuningAsset>("AbilityTuning");
            if (asset != null)
            {
                asset.Apply();
                return;
            }

            AbilityCatalog.ResetToSeed();
            CharacterBalance.ResetToDefault();
        }
    }
}
