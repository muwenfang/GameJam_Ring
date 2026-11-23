using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Ring.Abilities;
using Ring.Bells;
using Ring.Characters;
using Ring.Core;
using Ring.Flow;
using Ring.Inputs;
using Ring.UI;

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

        /// <summary>
        /// 校验 30 条技能数值（六人设 × 特质/普攻/本我/自我/超我），
        /// 并把技能总表里的普攻数值与角色资产里那份旧抄录做一次交叉提示。
        /// </summary>
        [MenuItem("钟声/校验/校验技能总表", false, 3)]
        public static void ValidateAbilities()
        {
            var report = new ValidationReport();
            AbilityCatalog.CollectIssues(report);

            CharacterRoster roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(CharacterRosterPath);
            if (roster != null)
            {
                for (int i = 0; i < roster.Characters.Count; i++)
                {
                    CharacterBaseStats stats = roster.Characters[i];
                    if (stats == null)
                    {
                        continue;
                    }

                    AbilityDefinition basic = AbilityCatalog.Get(stats.Persona, AbilitySlot.BasicAttack);
                    if (basic == null)
                    {
                        report.Error(stats.DisplayName, "技能总表里没有这个人的普攻。");
                        continue;
                    }

                    string context = stats.DisplayName + "·普攻";
                    if (stats.BasicAttack == null)
                    {
                        report.Warn(context, "角色资产里没有填普攻；战斗层以技能总表为准。");
                    }
                    else if (!Mathf.Approximately(stats.BasicAttack.Damage, basic.Damage))
                    {
                        report.Info(context, "角色资产写 " + stats.BasicAttack.Damage + " 伤害，技能总表写 "
                            + basic.Damage + "；以技能总表为准（资产是旧版抄录）。");
                    }

                    report.Info(context, basic.AbilityName + "：" + basic.ToSummary());
                }
            }

            Debug.Log("【钟声】技能总表校验\n" + report.ToText());
            Debug.Log("【钟声】技能总览\n" + AbilityCatalog.ToOverview());

            string headline = report.IsClean
                ? "技能总表全部通过（" + report.Count + " 条提示）"
                : "错误 " + Count(report, ValidationSeverity.Error)
                  + " 条，警告 " + Count(report, ValidationSeverity.Warning)
                  + " 条，详见 Console。";
            EditorUtility.DisplayDialog("钟声 · 技能总表", headline, "好");
        }

        /// <summary>
        /// 检查当前打开的场景里主菜单接不接得上：Canvas、四个按钮、设置面板、流程组件。
        /// 只看不改 —— 界面是手工搭的，这里只负责把「点了没反应」的原因提前说出来。
        /// </summary>
        [MenuItem("钟声/场景/校验主菜单界面", false, 40)]
        public static void ValidateSceneUi()
        {
            var report = new ValidationReport();

            Canvas menu = FindRootCanvas("MainMenu");
            if (menu == null)
            {
                report.Error("主菜单", "当前场景里找不到名为 MainMenu 的根 Canvas。请先打开 SampleScene 再跑这个检查。");
            }
            else
            {
                CheckButton(report, menu.transform, "Start", "开始游戏");
                CheckButton(report, menu.transform, "Setting", "设置");
                CheckButton(report, menu.transform, "Exit", "退出游戏");

                report.Info("主菜单", "Canvas 的 Sorting Order = " + menu.sortingOrder
                    + "；运行时会新建 HUD Canvas（100），设置面板打开时抬到 300。");

                GameObject settingsPanel = FindGameObject(menu.transform, "SettingPanel");
                if (settingsPanel == null)
                {
                    report.Error("设置面板", "找不到 SettingPanel：设置 / 全屏都不可用。");
                }
                else
                {
                    if (settingsPanel.activeSelf)
                    {
                        report.Info("设置面板", "场景里是开着存的；GameUiController 会在运行时先收起来。");
                    }

                    CheckButton(report, settingsPanel.transform, "FullScreen", "全屏");
                    CheckButton(report, settingsPanel.transform, "CloseSetting", "关闭设置");

                    if (FindComponent<Slider>(settingsPanel.transform, "AudioSlider") == null)
                    {
                        report.Warn("设置面板", "找不到 AudioSlider：音量滑条不会生效。");
                    }

                    Text fullScreenLabel = FindComponent<Text>(settingsPanel.transform, "Label");
                    if (fullScreenLabel != null && fullScreenLabel.text != "全屏")
                    {
                        report.Info("设置面板", "全屏按钮现在写的是「" + fullScreenLabel.text + "」，运行时会按全屏状态改成「全屏 / 退出全屏」。");
                    }
                }

                CheckBackdrop(report, menu.transform, "MenuImage", "它铺满全屏");
                CheckBackdrop(report, menu.transform, "SettingImage", "它的绘制顺序在全屏按钮之前");
            }

            if (Object.FindObjectOfType<GameFlow>(true) == null)
            {
                report.Error("流程", "场景里没有 GameFlow，界面不会响应任何操作。");
            }

            MatchSpawner spawner = Object.FindObjectOfType<MatchSpawner>(true);
            if (spawner == null)
            {
                report.Warn("流程", "场景里没有 MatchSpawner：点「开始游戏」会失败（编辑器里 GameUiController 会临时补一个）。");
            }
            else if (spawner.Roster == null)
            {
                report.Error("流程", "MatchSpawner 没有指定角色总表（Roster 为空）。");
            }

            if (Object.FindObjectOfType<BellSystem>(true) == null)
            {
                report.Warn("流程", "场景里没有 BellSystem：开局会因为缺少钟声系统失败。");
            }

            if (Object.FindObjectOfType<EventSystem>(true) == null)
            {
                report.Error("交互", "场景里没有 EventSystem：所有按钮都点不动。");
            }

            if (Object.FindObjectOfType<GameInput>(true) == null)
            {
                report.Warn("输入", "场景里没有 GameInput：设置面板会退回用 Input.GetKeyDown(Escape) 判断 Esc。");
            }

            GameUiController ui = Object.FindObjectOfType<GameUiController>(true);
            if (ui == null)
            {
                report.Error("界面组件", "场景里没有 GameUiController：主菜单按钮不会被接线。");
            }
            else
            {
                CheckWiring(report, ui);
            }

            Debug.Log("【钟声】主菜单界面校验\n" + report.ToText());

            string headline = report.IsClean
                ? "界面接线没问题（" + report.Count + " 条提示）"
                : "错误 " + Count(report, ValidationSeverity.Error)
                  + " 条，警告 " + Count(report, ValidationSeverity.Warning)
                  + " 条，详见 Console。";
            EditorUtility.DisplayDialog("钟声 · 主菜单界面", headline, "好");
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

        private static void CheckButton(ValidationReport report, Transform root, string name, string label)
        {
            Button button = FindComponent<Button>(root, name);
            if (button == null)
            {
                report.Error("按钮", "找不到「" + label + "」按钮（物体名 " + name + "）。");
                return;
            }

            if (button.GetComponentInChildren<Text>(true) == null)
            {
                report.Warn("按钮", "「" + label + "」按钮上没有文字。");
            }
        }

        /// <summary>无贴图的装饰 Image 只要勾着 Raycast Target 就会挡住下面的按钮；不透明的话连按钮画面一起盖住。</summary>
        private static void CheckBackdrop(ValidationReport report, Transform root, string name, string why)
        {
            Image image = FindComponent<Image>(root, name);
            if (image == null)
            {
                return;
            }

            if (!image.enabled)
            {
                report.Info(name + " 遮挡", "Image 关着，不会挡东西。");
                return;
            }

            bool empty = image.sprite == null;
            bool opaque = image.color.a >= 0.99f;

            if (empty && opaque)
            {
                report.Warn(name + " 遮挡", "它没有贴图、颜色不透明，" + why
                    + "。没贴图的 Image 画出来就是一整块纯色，按钮会被盖住（看不见但仍能点）。"
                    + "运行时会先把它关掉并打警告；正式做法是给它指定贴图，或把它排到按钮前面。");
            }
            else if (empty)
            {
                report.Info(name + " 遮挡", "没有贴图但半透明，看起来是压暗层；记得它排在按钮后面就会盖住按钮画面。");
            }
            else
            {
                report.Info(name + " 遮挡", "有贴图，按美术排的层级显示。");
            }

            if (image.raycastTarget)
            {
                report.Warn(name + " 点击", "它勾着 Raycast Target，" + why
                    + "，会挡住按钮点击。建议在场景里取消勾选（GameUiController 的 fixBlockingRaycasts 也会在运行时关掉它）。");
            }
            else
            {
                report.Info(name + " 点击", "Raycast Target 已关，不会挡按钮点击。");
            }
        }

        private static void CheckWiring(ValidationReport report, GameUiController ui)
        {
            var serialized = new SerializedObject(ui);
            CheckReference(report, serialized, "mainMenuCanvas", "主菜单 Canvas", "MainMenu");
            CheckReference(report, serialized, "settingsPanel", "设置面板", "SettingPanel");
            CheckReference(report, serialized, "startButton", "开始游戏按钮", "Start");
            CheckReference(report, serialized, "settingsButton", "设置按钮", "Setting");
            CheckReference(report, serialized, "exitButton", "退出游戏按钮", "Exit");
            CheckReference(report, serialized, "fullScreenButton", "全屏按钮", "FullScreen");
            CheckReference(report, serialized, "closeSettingsButton", "关闭设置按钮", "CloseSetting");
            CheckReference(report, serialized, "fullScreenLabel", "全屏按钮文字", "FullScreen/Label");
            CheckReference(report, serialized, "volumeSlider", "音量滑条", "AudioSlider");
        }

        private static void CheckReference(
            ValidationReport report, SerializedObject serialized, string property, string label, string autoName)
        {
            SerializedProperty found = serialized.FindProperty(property);
            if (found == null)
            {
                return;
            }

            report.Info("界面接线", found.objectReferenceValue != null
                ? label + "已手动指定。"
                : label + "留空，运行时会按名字「" + autoName + "」自动找。");
        }

        private static Canvas FindRootCanvas(string name)
        {
            Canvas[] canvases = Object.FindObjectsOfType<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas != null && canvas.name == name && canvas.rootCanvas == canvas)
                {
                    return canvas;
                }
            }

            return null;
        }

        private static GameObject FindGameObject(Transform root, string name)
        {
            Transform found = FindTransform(root, name);
            return found != null ? found.gameObject : null;
        }

        private static T FindComponent<T>(Transform root, string name) where T : Component
        {
            Transform found = FindTransform(root, name);
            return found != null ? found.GetComponent<T>() : null;
        }

        private static Transform FindTransform(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindTransform(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
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
