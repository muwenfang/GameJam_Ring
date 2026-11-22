using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ring.UI
{
    /// <summary>
    /// 纯代码搭 uGUI 的小工具集。
    ///
    /// 为什么不用预制体：本轮没有美术资源，也不希望把 .unity/.prefab 手写成 YAML（编辑器里无法验证）。
    /// 运行时用代码搭出来，美术到位后把这里换成预制体即可——所有调用都收敛在本文件。
    /// </summary>
    public static class UiBuilder
    {
        private static Font cachedFont;

        /// <summary>面板用的深色底。</summary>
        public static readonly Color PanelColor = new Color(0.09f, 0.10f, 0.13f, 0.92f);

        /// <summary>按钮正常色。</summary>
        public static readonly Color ButtonColor = new Color(0.22f, 0.26f, 0.34f, 1f);

        /// <summary>高亮色（标题、强调）。</summary>
        public static readonly Color AccentColor = new Color(0.92f, 0.80f, 0.42f, 1f);

        /// <summary>创建一个全屏覆盖的 Canvas（含 EventSystem）。</summary>
        public static Canvas CreateCanvas(string name, int sortingOrder = 100)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();
            return canvas;
        }

        /// <summary>场景里没有 EventSystem 时补一个，否则按钮点不动。</summary>
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        /// <summary>铺满父级的背景面板。</summary>
        public static RectTransform CreateFullScreenPanel(Transform parent, string name, Color color)
        {
            RectTransform rect = CreateRect(parent, name);
            Stretch(rect);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            return rect;
        }

        /// <summary>创建并拉伸一个空 RectTransform。</summary>
        public static RectTransform CreateRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>按锚点与像素尺寸摆一个矩形（anchor 为 0~1 的相对位置）。</summary>
        public static RectTransform Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>居中大标题。</summary>
        public static Text CreateTitle(Transform parent, string text, int fontSize, Color color)
        {
            RectTransform rect = CreateRect(parent, "Title");
            Place(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 220f), new Vector2(1200f, 160f));
            return Configure(rect.gameObject.AddComponent<Text>(), text, fontSize, color, TextAnchor.MiddleCenter);
        }

        /// <summary>正文文本。</summary>
        public static Text CreateText(
            Transform parent,
            string name,
            string text,
            int fontSize,
            Color color,
            TextAnchor anchor,
            Vector2 anchorPoint,
            Vector2 position,
            Vector2 size)
        {
            RectTransform rect = CreateRect(parent, name);
            Place(rect, anchorPoint, new Vector2(0.5f, 0.5f), position, size);
            return Configure(rect.gameObject.AddComponent<Text>(), text, fontSize, color, anchor);
        }

        /// <summary>普通按钮。</summary>
        public static Button CreateButton(
            Transform parent,
            string label,
            Vector2 anchorPoint,
            Vector2 position,
            Vector2 size,
            Action onClick,
            int fontSize = 34)
        {
            RectTransform rect = CreateRect(parent, "Btn_" + label);
            Place(rect, anchorPoint, new Vector2(0.5f, 0.5f), position, size);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = ButtonColor;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.2f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.85f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;

            Text text = CreateText(
                rect, "Label", label, fontSize, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), Vector2.zero, size);
            Stretch((RectTransform)text.transform);

            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            return button;
        }

        /// <summary>横向进度条（血条用）。</summary>
        public static Image CreateBar(Transform parent, Vector2 anchorPoint, Vector2 position, Vector2 size, Color fillColor)
        {
            RectTransform background = CreateRect(parent, "Bar");
            Place(background, anchorPoint, new Vector2(0.5f, 0.5f), position, size);
            var backgroundImage = background.gameObject.AddComponent<Image>();
            backgroundImage.color = new Color(0f, 0f, 0f, 0.65f);

            RectTransform fill = CreateRect(background, "Fill");
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(1f, 1f);
            fill.offsetMin = new Vector2(2f, 2f);
            fill.offsetMax = new Vector2(-2f, -2f);
            fill.pivot = new Vector2(0f, 0.5f);

            var image = fill.gameObject.AddComponent<Image>();
            image.color = fillColor;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.fillAmount = 1f;
            return image;
        }

        private static Text Configure(Text text, string content, int fontSize, Color color, TextAnchor anchor)
        {
            text.text = content;
            text.font = GetFont();
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// 取一个能显示中文的字体：优先系统里的中文字体，取不到再退回 Unity 内置字体。
        /// （Unity 内置的 LegacyRuntime 不含中文字形，中文会显示成方框。）
        /// </summary>
        public static Font GetFont()
        {
            if (cachedFont != null)
            {
                return cachedFont;
            }

            string[] preferred = { "Microsoft YaHei", "微软雅黑", "SimHei", "黑体", "SimSun", "宋体", "Noto Sans CJK SC" };
            List<string> installed = null;

            try
            {
                installed = new List<string>(Font.GetOSInstalledFontNames());
            }
            catch (Exception)
            {
                installed = null;
            }

            if (installed != null)
            {
                for (int i = 0; i < preferred.Length; i++)
                {
                    if (installed.Contains(preferred[i]))
                    {
                        cachedFont = Font.CreateDynamicFontFromOSFont(preferred[i], 32);
                        if (cachedFont != null)
                        {
                            return cachedFont;
                        }
                    }
                }
            }

            cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (cachedFont == null)
            {
                cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            return cachedFont;
        }
    }
}
