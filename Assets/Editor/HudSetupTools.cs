using Ring.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Ring.EditorTools
{
    /// <summary>
    /// 把战斗 HUD 落成场景里的真实物体，方便在 Hierarchy / Inspector 里自由改。
    ///
    /// 为什么要这个菜单：运行时用代码搭 UI 只解决了「有界面」，改位置配色还得回来改代码；
    /// 生成一次之后，左右两块、中间倒计时、特性栏都是普通 GameObject + RectTransform，
    /// 拖动、改颜色、换字体、加图标都能直接在编辑器里做，代码只在运行时负责填数值。
    ///
    /// 圆角与虚线描边是运行时按实际像素尺寸生成的（<see cref="HudSprites"/>），
    /// 所以编辑器里看到的是直角矩形，按 Play 才会变成设计稿那样；自己给 Image 指定贴图就不会被覆盖。
    /// </summary>
    public static class HudSetupTools
    {
        private const string HudCanvasName = "HudCanvas";
        private const string HudRootName = "BattleHud";

        /// <summary>HUD 画布的层级：压住 battleCanvas（0），但低于 RingCanvas 的结算面板（100）与设置面板（300）。</summary>
        private const int HudSortingOrder = 90;

        [MenuItem("钟声/界面/生成战斗 HUD（场景）", false, 40)]
        private static void CreateHudInScene()
        {
            BattleHud existing = Object.FindObjectOfType<BattleHud>(true);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorUtility.DisplayDialog(
                    "已经有一套 HUD",
                    "场景里已经存在 BattleHud（" + existing.name + "）。\n"
                    + "重复生成会出现两套血条，所以这次没有新建。要重做请先用「删除战斗 HUD（场景）」。",
                    "知道了");
                return;
            }

            Canvas canvas = CreateCanvas();
            Undo.RegisterCreatedObjectUndo(canvas.gameObject, "生成战斗 HUD");

            BattleHud hud = BattleHudBuilder.Build(canvas.transform, HudRootName);
            hud.gameObject.SetActive(true);

            WireController(hud);

            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Selection.activeGameObject = hud.gameObject;

            Debug.Log(
                "[HUD] 已在场景里生成 " + HudCanvasName + " / " + HudRootName
                + "（Sorting Order " + HudSortingOrder + "）。"
                + "左右两块与中间的引用已经自动填好，之后可以直接改位置/配色/字号。\n"
                + "圆角与虚线框在按 Play 时生成；想换成自己的美术图，给对应 Image 指定 Sprite 即可（不会被覆盖）。",
                hud);
        }

        [MenuItem("钟声/界面/删除战斗 HUD（场景）", false, 41)]
        private static void RemoveHudFromScene()
        {
            BattleHud hud = Object.FindObjectOfType<BattleHud>(true);
            if (hud == null)
            {
                Debug.Log("[HUD] 场景里没有 BattleHud，无需删除。");
                return;
            }

            // HUD 一般挂在自己生成的画布下；连着画布一起删，免得留一个空 Canvas。
            Canvas canvas = hud.GetComponentInParent<Canvas>();
            bool deleteWholeCanvas = canvas != null && canvas.name == HudCanvasName;
            GameObject target = deleteWholeCanvas ? canvas.gameObject : hud.gameObject;

            Undo.DestroyObjectImmediate(target);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[HUD] 已删除场景里的战斗 HUD。运行时仍会自动兜底搭一套默认布局。");
        }

        [MenuItem("钟声/界面/校验 HUD 接线", false, 42)]
        private static void ValidateHud()
        {
            BattleHud hud = Object.FindObjectOfType<BattleHud>(true);
            if (hud == null)
            {
                Debug.LogWarning(
                    "[HUD] 场景里没有 BattleHud。运行时会自动搭一套兜底布局；"
                    + "想手工调界面就用菜单「钟声 / 界面 / 生成战斗 HUD（场景）」。");
                return;
            }

            Undo.RecordObject(hud, "校验 HUD 接线");
            var missing = new System.Collections.Generic.List<string>();

            SerializedObject so = new SerializedObject(hud);
            CheckProperty(so, "playerName", missing);
            CheckProperty(so, "playerBarFill", missing);
            CheckProperty(so, "playerTraitRow", missing);
            CheckProperty(so, "opponentName", missing);
            CheckProperty(so, "opponentBarFill", missing);
            CheckProperty(so, "opponentTraitRow", missing);
            CheckProperty(so, "bellValue", missing);
            CheckProperty(so, "roundValue", missing);

            if (missing.Count == 0)
            {
                Debug.Log("[HUD] 接线完整：玩家/对手血条、特性栏、倒计时与回合数都已指定。", hud);
                return;
            }

            Debug.LogWarning(
                "[HUD] 以下字段为空（留空时运行时会按名字在子物体里找，找不到就不显示）："
                + string.Join("、", missing), hud);
        }

        private static void CheckProperty(SerializedObject so, string name, System.Collections.Generic.List<string> missing)
        {
            SerializedProperty property = so.FindProperty(name);
            if (property == null || property.objectReferenceValue == null)
            {
                missing.Add(name);
            }
        }

        // ---------- 内部 ----------

        private static Canvas CreateCanvas()
        {
            var go = new GameObject(
                HudCanvasName,
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = HudSortingOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        /// <summary>把 GameUiController 的 battleHud 引用填上，免得它再去场景里找。</summary>
        private static void WireController(BattleHud hud)
        {
            GameUiController controller = Object.FindObjectOfType<GameUiController>(true);
            if (controller == null)
            {
                return;
            }

            SerializedObject so = new SerializedObject(controller);
            SerializedProperty property = so.FindProperty("battleHud");
            if (property == null || property.objectReferenceValue != null)
            {
                return;
            }

            property.objectReferenceValue = hud;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
        }
    }
}
