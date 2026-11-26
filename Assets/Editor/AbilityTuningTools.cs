using System.Text;
using Ring.Abilities;
using Ring.Characters;
using Ring.Core;
using UnityEditor;
using UnityEngine;

namespace Ring.EditorTools
{
    /// <summary>
    /// 数值层（M1）的 Editor 工具：把「改表不改码」变成可操作的菜单。
    ///
    /// 关键能力：
    ///   · 从代码种子生成 / 重建技能数值资产（用 Unity 原生序列化写盘，格式 100% 正确）；
    ///   · 校验 30 条定义、effectRefs → 执行器映射、Q1 决策；
    ///   · 列出「资产 vs 代码种子」的差异，改了哪些数值一目了然；
    ///   · 把 Q1 决策一键同步到六份 Character_*.asset（不改代码）。
    /// </summary>
    public static class AbilityTuningTools
    {
        public const string AssetPath = "Assets/Data/Abilities/AbilityTuning.asset";
        public const string RosterPath = "Assets/Data/Characters/CharacterRoster.asset";

        /// <summary>从代码种子重建资产。会覆盖资产里的手改值，因此先弹确认。</summary>
        [MenuItem("钟声/数值层/从代码种子生成技能数值资产", false, 0)]
        public static void GenerateFromSeed()
        {
            if (!EditorUtility.DisplayDialog(
                    "从代码种子生成技能数值资产",
                    "会用 AbilityCatalog 里的 30 条默认数值覆盖当前资产（含 effectRefs 与 Q1 决策）。\n"
                    + "已手改过的数值会丢失，确定吗？",
                    "生成 / 覆盖",
                    "取消"))
            {
                return;
            }

            AbilityTuningAsset asset = AssetDatabase.LoadAssetAtPath<AbilityTuningAsset>(AssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<AbilityTuningAsset>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }

            asset.SeedFromCode();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 立即注入并校验一次，确认「生成 → 生效」链路是通的。
            asset.Apply();
            var report = new ValidationReport();
            asset.CollectIssues(report);

            Debug.Log("【钟声】已从代码种子生成 " + AssetPath + "；来源=" + AbilityCatalog.SourceName
                + "\n" + AbilityExecutorRegistry.ToOverview()
                + "\n" + report.ToText());
            EditorUtility.DisplayDialog("钟声 · 数值层", "已生成并注入技能数值资产。\n"
                + (report.HasErrors ? "但自检有错误，详见 Console。" : "自检通过，详见 Console。"), "好");
        }

        /// <summary>完整校验：表结构 + 每条定义 + effectRefs 映射 + Q1 决策。</summary>
        [MenuItem("钟声/数值层/校验数值资产（含 effectRefs 与 Q1）", false, 1)]
        public static void Validate()
        {
            var report = new ValidationReport();
            AbilityTuningAsset asset = AssetDatabase.LoadAssetAtPath<AbilityTuningAsset>(AssetPath);
            if (asset == null)
            {
                report.Error("技能数值资产", "找不到 " + AssetPath + "；请先「从代码种子生成」。");
            }
            else
            {
                asset.CollectIssues(report);
            }

            Debug.Log("【钟声】数值层校验\n" + report.ToText()
                + "\n\n" + AbilityExecutorRegistry.ToOverview()
                + "\n\n" + (asset != null ? asset.Balance.ToSummary() : ""));

            string headline = report.IsClean
                ? "数值层全部通过（" + report.Count + " 条提示）"
                : "错误 " + CountSeverity(report, ValidationSeverity.Error)
                  + " 条，警告 " + CountSeverity(report, ValidationSeverity.Warning) + " 条，详见 Console。";
            EditorUtility.DisplayDialog("钟声 · 数值层校验", headline, "好");
        }

        /// <summary>列出资产与代码种子的逐字段差异，方便策划核对自己改了什么。</summary>
        [MenuItem("钟声/数值层/列出资产与代码种子差异", false, 2)]
        public static void DiffFromSeed()
        {
            AbilityTuningAsset asset = AssetDatabase.LoadAssetAtPath<AbilityTuningAsset>(AssetPath);
            if (asset == null)
            {
                Debug.LogError("【钟声】找不到 " + AssetPath);
                return;
            }

            var builder = new StringBuilder();
            builder.Append("【钟声】技能数值资产 vs 代码种子\n");
            var diffs = asset.CompareWithSeed();
            foreach (string line in diffs)
            {
                builder.Append("  · ").Append(line).Append('\n');
            }

            if (diffs.Count == 0)
            {
                builder.Append("  （完全一致：资产仍是代码种子的原样拷贝）\n");
            }

            builder.Append("\nQ1 决策：").Append(asset.Balance.ToSummary());
            Debug.Log(builder.ToString());
        }

        /// <summary>为漏填 effectRefs 的条目按字段自动补齐。</summary>
        [MenuItem("钟声/数值层/补齐 effectRefs", false, 3)]
        public static void EnsureEffectRefs()
        {
            AbilityTuningAsset asset = AssetDatabase.LoadAssetAtPath<AbilityTuningAsset>(AssetPath);
            if (asset == null)
            {
                Debug.LogError("【钟声】找不到 " + AssetPath);
                return;
            }

            int filled = asset.EnsureEffectRefs();
            if (filled > 0)
            {
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }

            Debug.Log("【钟声】已按字段自动补齐 " + filled + " 条能力的 effectRefs。\n"
                + AbilityExecutorRegistry.ToOverview());
        }

        /// <summary>
        /// 把资产里的 Q1 决策应用到六份角色资产。
        /// 例如策划把 Policy 切到 FormulaOnly 后，游侠的减伤会一键从 12.5% 改成 15%，
        /// 全程不改 C# 代码。
        /// </summary>
        [MenuItem("钟声/数值层/应用 Q1 决策到六个角色资产", false, 10)]
        public static void ApplyQ1ToCharacters()
        {
            AbilityTuningAsset asset = AssetDatabase.LoadAssetAtPath<AbilityTuningAsset>(AssetPath);
            if (asset == null || asset.Balance == null)
            {
                Debug.LogError("【钟声】找不到数值资产或 Q1 决策：" + AssetPath);
                return;
            }

            CharacterRoster roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
            if (roster == null)
            {
                Debug.LogError("【钟声】找不到角色总表：" + RosterPath);
                return;
            }

            BalanceDecision decision = asset.Balance;
            int changed = 0;
            for (int i = 0; i < roster.Characters.Count; i++)
            {
                CharacterBaseStats stats = roster.Characters[i];
                if (stats == null)
                {
                    continue;
                }

                float expected = decision.ResolveReduction(stats.Persona, stats.Defense);
                var serialized = new SerializedObject(stats);
                SerializedProperty reduction = serialized.FindProperty("damageReduction");
                if (reduction == null)
                {
                    Debug.LogWarning("【钟声】" + stats.name + " 上没有 damageReduction 字段。", stats);
                    continue;
                }

                float previous = reduction.floatValue;
                if (!Mathf.Approximately(previous, expected))
                {
                    reduction.floatValue = expected;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(stats);
                    changed++;
                    Debug.Log("【钟声】" + stats.DisplayName + " 减伤 " + (previous * 100f).ToString("0.##")
                        + "% → " + (expected * 100f).ToString("0.##") + "%（策略 " + decision.Policy + "）");
                }
            }

            AssetDatabase.SaveAssets();
            asset.Apply();
            Debug.Log("【钟声】Q1 决策已应用：" + decision.ToSummary() + "；共改写 " + changed + " 份角色资产。");
            EditorUtility.DisplayDialog("钟声 · Q1 决策", "已同步 " + changed + " 份角色资产。\n"
                + decision.ToSummary(), "好");
        }

        /// <summary>在编辑器里立即注入资产（不进 PlayMode 也能验证 Get/Definition 读的是资产）。</summary>
        [MenuItem("钟声/数值层/立即注入当前数值资产", false, 20)]
        public static void ApplyNow()
        {
            AbilityTuningAsset asset = AssetDatabase.LoadAssetAtPath<AbilityTuningAsset>(AssetPath);
            if (asset == null)
            {
                Debug.LogError("【钟声】找不到 " + AssetPath);
                return;
            }

            asset.Apply();
            Debug.Log("【钟声】已注入数值资产：" + AbilityCatalog.SourceName
                + "\n" + AbilityCatalog.ToOverview());
        }

        /// <summary>恢复代码种子（不删除资产文件）。</summary>
        [MenuItem("钟声/数值层/恢复代码种子（不删资产）", false, 21)]
        public static void ResetToSeed()
        {
            AbilityCatalog.ResetToSeed();
            CharacterBalance.ResetToDefault();
            Debug.Log("【钟声】运行时数值来源已恢复为代码种子 + 文档默认 Q1 决策；资产文件保持不变。");
        }

        private static int CountSeverity(ValidationReport report, ValidationSeverity severity)
        {
            int count = 0;
            for (int i = 0; i < report.Issues.Count; i++)
            {
                if (report.Issues[i].Severity == severity)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
