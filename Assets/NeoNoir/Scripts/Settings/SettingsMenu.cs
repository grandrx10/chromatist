using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LightAndShadow
{
    /// <summary>
    /// The settings menu, opened and closed with Escape: Audio, Display and
    /// Other tabs, over a dimmed, paused game.
    /// </summary>
    /// <remarks>
    /// Made at startup and kept across scene loads, so it is in every scene
    /// with nothing placed. The game is paused (time scale 0) while it is
    /// open, and the player's input and the dialogue box ignore everything.
    ///
    /// Each setting is a row with arrows either side of its value; clicking
    /// an arrow steps it. Everything applies and saves at once, so there is
    /// no Apply button: Close (or Escape) is all there is.
    ///
    /// Drawn and hit tested the same way as the <see cref="DialogueBox"/>:
    /// its own overlay canvas, clicks tested against the rectangles directly,
    /// so no EventSystem is needed.
    /// </remarks>
    public sealed class SettingsMenu : MonoBehaviour
    {
        private const float ReferenceHeight = 1080f;
        private const float FrameWidth = 3f;
        private const float VolumeStep = 0.05f;
        private const float TextSpeedStep = 10f;

        private static readonly Color Background = Color.black;
        private static readonly Color Foreground = Color.white;

        private static readonly FullScreenMode[] Modes =
        {
            FullScreenMode.ExclusiveFullScreen,
            FullScreenMode.FullScreenWindow,
            FullScreenMode.Windowed,
        };

        private static readonly string[] ModeNames = { "Fullscreen", "Borderless", "Windowed" };

        private static SettingsMenu instance;

        private sealed class MenuButton
        {
            public RectTransform Rect;
            public Image Fill;
            public Text Label;
            public Action Clicked;
        }

        private sealed class Row
        {
            public Text Value;
            public Func<string> Read;
        }

        private readonly List<MenuButton> buttons = new List<MenuButton>();
        private readonly List<Row> rows = new List<Row>();
        private readonly List<MenuButton> tabs = new List<MenuButton>();
        private readonly List<GameObject> pages = new List<GameObject>();
        private readonly List<Vector2Int> resolutions = new List<Vector2Int>();

        private Font font;
        private RectTransform root;
        private int page;
        private float timeScaleBefore = 1f;

        /// <summary>True while the menu is up. Never makes a menu.</summary>
        public static bool IsOpen => instance != null && instance.root != null && instance.root.gameObject.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (instance != null)
            {
                return;
            }

            GameObject host = new GameObject("SettingsMenu");
            DontDestroyOnLoad(host);
            host.AddComponent<SettingsMenu>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            Build();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;

                if (root != null && root.gameObject.activeSelf)
                {
                    Time.timeScale = timeScaleBefore;
                }
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (IsOpen)
                {
                    Close();
                }
                else
                {
                    Open();
                }

                return;
            }

            if (!IsOpen)
            {
                return;
            }

            Mouse mouse = Mouse.current;

            if (mouse == null)
            {
                return;
            }

            Vector2 pointer = mouse.position.ReadValue();
            bool clicked = mouse.leftButton.wasPressedThisFrame;
            MenuButton over = null;

            foreach (MenuButton button in buttons)
            {
                bool hovered = button.Rect.gameObject.activeInHierarchy
                               && RectTransformUtility.RectangleContainsScreenPoint(button.Rect, pointer, null);

                if (hovered)
                {
                    over = button;
                }

                Paint(button, hovered || IsCurrentTab(button));
            }

            if (clicked && over != null)
            {
                over.Clicked?.Invoke();
            }

            // Every frame: a new resolution only shows on the frame after it is set.
            Refresh();
        }

        public void Open()
        {
            if (IsOpen)
            {
                return;
            }

            timeScaleBefore = Time.timeScale;
            Time.timeScale = 0f;
            root.gameObject.SetActive(true);
            ShowPage(page);
            Refresh();
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            root.gameObject.SetActive(false);
            Time.timeScale = timeScaleBefore;
        }

        private void ShowPage(int index)
        {
            page = index;

            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].SetActive(i == page);
            }
        }

        private bool IsCurrentTab(MenuButton button)
        {
            int index = tabs.IndexOf(button);
            return index >= 0 && index == page;
        }

        private void Refresh()
        {
            foreach (Row row in rows)
            {
                row.Value.text = row.Read();
            }
        }

        private static void Paint(MenuButton button, bool on)
        {
            // Hovered (and the open tab) invert, as the dialogue choices do.
            button.Fill.color = on ? Foreground : Background;
            button.Label.color = on ? Background : Foreground;
        }

        // ---------------------------------------------------------------
        //  Settings
        // ---------------------------------------------------------------

        private void StepVolume(int direction)
        {
            GameSettings.Volume = Mathf.Round((GameSettings.Volume + direction * VolumeStep) / VolumeStep) * VolumeStep;
        }

        private void StepTextSpeed(int direction)
        {
            GameSettings.TextSpeed += direction * TextSpeedStep;
        }

        private void StepMode(int direction)
        {
            int index = Mathf.Max(0, Array.IndexOf(Modes, GameSettings.WindowMode));
            index = (index + direction + Modes.Length) % Modes.Length;
            GameSettings.WindowMode = Modes[index];
        }

        private string ModeName()
        {
            int index = Array.IndexOf(Modes, GameSettings.WindowMode);
            return index >= 0 ? ModeNames[index] : GameSettings.WindowMode.ToString();
        }

        private void StepResolution(int direction)
        {
            FindResolutions();

            if (resolutions.Count == 0)
            {
                return;
            }

            int index = resolutions.IndexOf(new Vector2Int(Screen.width, Screen.height));

            // Not a listed size (a dragged window): step from the nearest below.
            if (index < 0)
            {
                index = 0;

                for (int i = 0; i < resolutions.Count; i++)
                {
                    if (resolutions[i].x * resolutions[i].y <= Screen.width * Screen.height)
                    {
                        index = i;
                    }
                }
            }

            index = Mathf.Clamp(index + direction, 0, resolutions.Count - 1);
            GameSettings.SetResolution(resolutions[index].x, resolutions[index].y);
        }

        /// <summary>The screen's sizes, smallest first, each once whatever its refresh rates.</summary>
        private void FindResolutions()
        {
            resolutions.Clear();

            foreach (Resolution resolution in Screen.resolutions)
            {
                Vector2Int size = new Vector2Int(resolution.width, resolution.height);

                if (!resolutions.Contains(size))
                {
                    resolutions.Add(size);
                }
            }

            resolutions.Sort((a, b) => (a.x * a.y).CompareTo(b.x * b.y));
        }

        // ---------------------------------------------------------------
        //  Building
        // ---------------------------------------------------------------

        private void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceHeight * 16f / 9f, ReferenceHeight);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform canvasRect = GetComponent<RectTransform>();

            // Dims the paused game behind.
            Image dim = new GameObject("Menu", typeof(RectTransform)).AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.7f);
            dim.raycastTarget = false;
            root = dim.rectTransform;
            root.SetParent(canvasRect, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            RectTransform panel = Panel("Panel", root, out _);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(960f, 620f);

            Text title = Label("Title", panel, 48, TextAnchor.MiddleCenter);
            title.text = "Settings";
            title.fontStyle = FontStyle.Bold;
            Place(title.rectTransform, new Vector2(0f, 250f), new Vector2(600f, 70f));

            string[] names = { "Audio", "Display", "Other" };

            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                MenuButton tab = MakeButton(names[i], panel, new Vector2((i - 1) * 240f, 165f), new Vector2(220f, 64f), () => ShowPage(index));
                tabs.Add(tab);
            }

            RectTransform audioPage = Page("Audio", panel);
            AddRow(audioPage, 0, "Master Volume", () => Mathf.RoundToInt(GameSettings.Volume * 100f) + "%",
                   () => StepVolume(-1), () => StepVolume(1));

            RectTransform displayPage = Page("Display", panel);
            AddRow(displayPage, 0, "Window Mode", ModeName, () => StepMode(-1), () => StepMode(1));
            AddRow(displayPage, 1, "Resolution", () => Screen.width + " x " + Screen.height,
                   () => StepResolution(-1), () => StepResolution(1));

            RectTransform otherPage = Page("Other", panel);
            AddRow(otherPage, 0, "Text Speed", () => Mathf.RoundToInt(GameSettings.TextSpeed).ToString(),
                   () => StepTextSpeed(-1), () => StepTextSpeed(1));

            MakeButton("Close", panel, new Vector2(0f, -240f), new Vector2(240f, 64f), Close);

            ShowPage(0);
            root.gameObject.SetActive(false);
        }

        private RectTransform Page(string pageName, RectTransform parent)
        {
            RectTransform rect = new GameObject(pageName, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            pages.Add(rect.gameObject);
            return rect;
        }

        /// <summary>A setting: its name on the left, then "&lt;", its value and "&gt;".</summary>
        private void AddRow(RectTransform pageRect, int index, string label, Func<string> read, Action down, Action up)
        {
            float y = 50f - index * 100f;

            Text name = Label(label, pageRect, 34, TextAnchor.MiddleLeft);
            name.text = label;
            Place(name.rectTransform, new Vector2(-220f, y), new Vector2(400f, 64f));

            MakeButton("<", pageRect, new Vector2(70f, y), new Vector2(64f, 64f), down);

            Text value = Label("Value", pageRect, 34, TextAnchor.MiddleCenter);
            Place(value.rectTransform, new Vector2(230f, y), new Vector2(240f, 64f));

            MakeButton(">", pageRect, new Vector2(390f, y), new Vector2(64f, 64f), up);

            rows.Add(new Row { Value = value, Read = read });
        }

        private MenuButton MakeButton(string text, RectTransform parent, Vector2 position, Vector2 size, Action clicked)
        {
            RectTransform rect = Panel(text, parent, out Image fill);
            Place(rect, position, size);

            Text label = Label("Label", rect, 34, TextAnchor.MiddleCenter);
            label.text = text;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;

            MenuButton button = new MenuButton { Rect = rect, Fill = fill, Label = label, Clicked = clicked };
            buttons.Add(button);
            return button;
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        /// <summary>A frame-coloured rectangle with a background-coloured one inset inside it. Returns the outer one.</summary>
        private static RectTransform Panel(string panelName, RectTransform parent, out Image fill)
        {
            Image frame = new GameObject(panelName, typeof(RectTransform)).AddComponent<Image>();
            frame.color = Foreground;
            frame.raycastTarget = false;
            RectTransform rect = frame.rectTransform;
            rect.SetParent(parent, false);

            fill = new GameObject("Fill", typeof(RectTransform)).AddComponent<Image>();
            fill.color = Background;
            fill.raycastTarget = false;
            RectTransform fillRect = fill.rectTransform;
            fillRect.SetParent(rect, false);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(FrameWidth, FrameWidth);
            fillRect.offsetMax = new Vector2(-FrameWidth, -FrameWidth);

            return rect;
        }

        private Text Label(string labelName, RectTransform parent, int size, TextAnchor alignment)
        {
            Text text = new GameObject(labelName, typeof(RectTransform)).AddComponent<Text>();
            text.rectTransform.SetParent(parent, false);
            text.font = font;
            text.fontSize = size;
            text.color = Foreground;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }

        // With domain reload off, statics carry over between play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            instance = null;
        }
    }
}
