using System;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Builds simple uGUI widgets from code. Art/UI are out of scope for this prototype, so screens are
    /// assembled procedurally instead of maintaining prefabs.
    /// </summary>
    public static class UiFactory
    {
        private static Font _font;

        public static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.12f, 0.92f);
        public static readonly Color ButtonColor = new Color(0.22f, 0.45f, 0.85f, 1f);
        public static readonly Color DangerColor = new Color(0.75f, 0.22f, 0.2f, 1f);

        private static Font Font => _font != null ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(this RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>Centered vertical panel with automatic layout.</summary>
        public static RectTransform CreatePanel(string name, Transform parent, Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.gameObject.AddComponent<Image>().color = PanelColor;
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 20, 20);
            layout.spacing = 10;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            return rect;
        }

        public static Text CreateText(Transform parent, string text, int size = 20, TextAnchor anchor = TextAnchor.MiddleLeft, float height = 30)
        {
            RectTransform rect = CreateRect("Text", parent);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = Font;
            label.fontSize = size;
            label.alignment = anchor;
            label.color = Color.white;
            label.text = text;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            AddLayoutHeight(rect, height);
            return label;
        }

        public static Button CreateButton(Transform parent, string caption, Action onClick, Color? color = null, float height = 40)
        {
            RectTransform rect = CreateRect("Button_" + caption, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color ?? ButtonColor;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
            AddLayoutHeight(rect, height);

            Text label = CreateText(rect, caption, 20, TextAnchor.MiddleCenter);
            label.rectTransform.Stretch();
            return button;
        }

        public static InputField CreateInput(Transform parent, string value, string placeholder)
        {
            RectTransform rect = CreateRect("Input", parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.12f);
            AddLayoutHeight(rect, 38);

            Text text = CreateText(rect, string.Empty, 20);
            text.rectTransform.Stretch();
            text.rectTransform.offsetMin = new Vector2(10, 0);
            text.supportRichText = false;

            Text hint = CreateText(rect, placeholder, 20);
            hint.rectTransform.Stretch();
            hint.rectTransform.offsetMin = new Vector2(10, 0);
            hint.color = new Color(1f, 1f, 1f, 0.35f);

            var input = rect.gameObject.AddComponent<InputField>();
            input.targetGraphic = image;
            input.textComponent = text;
            input.placeholder = hint;
            input.text = value;
            return input;
        }

        /// <summary>Horizontal row container.</summary>
        public static RectTransform CreateRow(Transform parent, float height = 36)
        {
            RectTransform rect = CreateRect("Row", parent);
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            AddLayoutHeight(rect, height);
            return rect;
        }

        public static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
            }
        }

        private static void AddLayoutHeight(RectTransform rect, float height)
        {
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
        }
    }
}
