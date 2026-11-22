using UnityEditor;
using UnityEngine;
using Ring.Bells;
using Ring.Characters;
using Ring.Core;
using Ring.Inputs;

namespace Ring.EditorTools
{
    /// <summary>
    /// 菜单里的校验与一键回填。
    ///
    /// 这些工具不做任何「设计决策」，只把代码里已经定好的规则（文档数值、键位默认值、钟声条款）
    /// 拿去比对资产，替人检查 Inspector 里手改出来的偏差。
    /// </summary>
    public static class RingTools
    {
        public const string CharacterRosterPath = "Assets/Data/Characters/CharacterRoster.asset";
        public const string InputSettingsPath = "Assets/Data/Inputs/InputSettings.asset";
        public const string BellSettingsPath = "Assets/Data/Bells/BellSettings.asset";

        /// <summary>把人设数值、输入映射、钟声规则一次性跑一遍校验。</summary>
        [MenuItem("钟声/校验/校验全部数据", false, 0)]
        public static void ValidateAll()
        {
            var report = new ValidationReport();

            CharacterRoster roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(CharacterRosterPath);
            if (roster == null)
            {
                report.Error("角色总表", "找不到 " + CharacterRosterPath + "。");
            }
            else
            {
                if (roster.Count != PersonaIds.Count)
                {
                    report.Warn("角色总表", "文档是 6 个人设，总表里有 " + roster.Count + " 个。");
                }

                roster.CollectIssues(report);

                for (int i = 0; i < roster.Characters.Count; i++)
                {
                    CharacterBaseStats stats = roster.Characters[i];
                    if (stats != null)
                    {
                        stats.CollectIssues(report);
                    }
                }
            }

            InputSettings inputSettings = AssetDatabase.LoadAssetAtPath<InputSettings>(InputSettingsPath);
            if (inputSettings == null)
            {
                report.Error("输入映射", "找不到 " + InputSettingsPath + "。");
            }
            else
            {
                inputSettings.CollectIssues(report);
                inputSettings.CreateTable().CollectIssues(report);
            }

            BellSettings bellSettings = AssetDatabase.LoadAssetAtPath<BellSettings>(BellSettingsPath);
            if (bellSettings == null)
            {
                report.Error("钟声规则", "找不到 " + BellSettingsPath + "。");
            }
            else
            {
                bellSettings.CollectIssues(report);
            }

            Debug.Log("【钟声】数据校验\n" + report.ToText());

            string headline = report.IsClean
                ? "全部通过（" + report.Count + " 条提示）"
                : "错误 " + Count(report, ValidationSeverity.Error)
                  + " 条，警告 " + Count(report, ValidationSeverity.Warning)
                  + " 条，详见 Console。";
            EditorUtility.DisplayDialog("钟声 · 数据校验", headline, "好");
        }

        /// <summary>按文档公式重算减伤与移速；文档自己标注过的特例（精灵游侠 12.5%）默认保留。</summary>
        [MenuItem("钟声/校验/按公式回填角色派生数值", false, 1)]
        public static void RefillFormulas()
        {
            if (!EditorUtility.DisplayDialog(
                    "按公式回填",
                    "将用文档公式重算：\n减伤 = 防御 / 500\n移速 = 速度 × 0.03\n\n精灵游侠的 12.5% 是文档里用 ⚠️ 标注过的特例，默认不覆盖。",
                    "回填",
                    "取消"))
            {
                return;
            }

            Refill(false);
        }

        /// <summary>连文档特例一起覆盖 —— 只在策划明确要求「一切按公式」时用。</summary>
        [MenuItem("钟声/校验/按公式回填（含文档特例）", false, 2)]
        public static void RefillFormulasIncludingExceptions()
        {
            if (!EditorUtility.DisplayDialog(
                    "按公式回填（含特例）",
                    "会把精灵游侠的减伤从 12.5% 改成 15%，与文档总表的数值不再一致。确定吗？",
                    "覆盖",
                    "取消"))
            {
                return;
            }

            Refill(true);
        }

        /// <summary>把输入映射恢复成文档键位表。</summary>
        [MenuItem("钟声/输入映射/重置为文档默认", false, 20)]
        public static void ResetInputBindings()
        {
            InputSettings settings = AssetDatabase.LoadAssetAtPath<InputSettings>(InputSettingsPath);
            if (settings == null)
            {
                Debug.LogError("【钟声】找不到 " + InputSettingsPath + "。");
                return;
            }

            settings.ResetToDocumentDefaults();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("【钟声】输入映射已重置为文档默认（平A = 鼠标左键，闪避 = 鼠标右键）。");
        }

        /// <summary>打开改键面板。</summary>
        [MenuItem("钟声/输入映射/打开改键面板", false, 21)]
        public static void OpenRebindWindow()
        {
            InputRebindWindow.Open();
        }

        private static void Refill(bool includeDocumentedExceptions)
        {
            CharacterRoster roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(CharacterRosterPath);
            if (roster == null)
            {
                Debug.LogError("【钟声】找不到 " + CharacterRosterPath + "。");
                return;
            }

            int changed = 0;
            for (int i = 0; i < roster.Characters.Count; i++)
            {
                CharacterBaseStats stats = roster.Characters[i];
                if (stats == null)
                {
                    continue;
                }

                if (stats.ApplyFormulaValues(includeDocumentedExceptions))
                {
                    EditorUtility.SetDirty(stats);
                    changed++;
                    Debug.Log("【钟声】已回填 " + stats.DisplayName);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log(changed == 0
                ? "【钟声】所有派生数值都已经等于公式结果，没有改动。"
                : "【钟声】共回填 " + changed + " 个角色的派生数值。");
        }

        private static int Count(ValidationReport report, ValidationSeverity severity)
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
