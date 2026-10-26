using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LightAndShadow
{
    /// <summary>
    /// A visual novel style dialogue box along the bottom of the screen, with
    /// an optional pair of choices, and the "E" prompt shown over things the
    /// player can interact with.
    /// </summary>
    /// <remarks>
    /// Text types itself out. Left click while it is typing shows the rest at
    /// once; left click once it is all shown moves on. While a choice is up,
    /// only a click on one of the choices does anything.
    ///
    /// It knows nothing about blades or purchases. Callers drive it one line
    /// at a time with <see cref="Say"/> and <see cref="Ask"/>, and are called
    /// back when the player moves on or picks a choice. Locking the player's
    /// controls is the caller's business, too.
    ///
    /// Builds its own overlay canvas, so nothing has to be placed in the
    /// scene: <see cref="Instance"/> makes one on first use. To restyle it,
    /// put this component on an object in the scene and change the fields --
    /// that one is used instead.
    ///
    /// The canvas is screen space overlay, drawn after the two-tone
    /// threshold like the health bar, so its colours reach the screen as set.
    ///
    /// Clicks are hit tested against the choice rectangles directly, the same
    /// way the rest of the project polls devices, so no EventSystem is
    /// needed. Time is unscaled, so hit stop never stalls the typing.
    /// </remarks>
    public sealed class DialogueBox : MonoBehaviour
    {
        [Header("Text")]
        [SerializeField]
        [Tooltip("Empty uses Unity's built-in font.")]
        private Font font;

        [SerializeField] [Min(8)] private int fontSize = 34;
        [SerializeField] [Min(8)] private int speakerFontSize = 42;

        [SerializeField]
        [Tooltip("Typing speed, for lines that do not set their own.")]
        [Min(1f)]
        private float charactersPerSecond = 40f;

        [Header("Layout (pixels at 1920x1080)")]
        [SerializeField] [Min(0f)] private float screenMargin = 60f;
        [SerializeField] [Min(40f)] private float boxHeight = 240f;
        [SerializeField] [Min(0f)] private float padding = 32f;
        [SerializeField] [Min(0f)] private float frameWidth = 3f;
        [SerializeField] private Vector2 choiceSize = new Vector2(280f, 72f);
        [SerializeField] [Min(0f)] private float choiceGap = 48f;

        [SerializeField]
        [Tooltip("Space between the speaker's name and the edges of its nameplate: across, then up and down.")]
        private Vector2 speakerPadding = new Vector2(24f, 10f);

        [Header("Colours")]
        [SerializeField] private Color background = Color.black;
        [SerializeField] private Color foreground = Color.white;

        [Header("Interact prompt")]
        [SerializeField]
        [Tooltip("Pixels the prompt sits above the point it is shown for.")]
        private float promptRaise = 60f;

        [SerializeField] [Min(8)] private int promptFontSize = 26;

        private const float ReferenceHeight = 1080f;
        private const string HiddenColour = "<color=#00000000>";

        private static DialogueBox instance;

        private Canvas canvas;
        private RectTransform canvasRect;
        private RectTransform box;
        private RectTransform speakerPlate;
        private Text speakerLabel;
        private Text body;
        private RectTransform bodyRect;

        private RectTransform choiceRow;
        private readonly Choice[] choices = new Choice[2];

        private RectTransform prompt;
        private Text promptLabel;
        private Vector3 promptWorldPosition;

        private string fullText = string.Empty;
        private int visibleCharacters;
        private int totalCharacters;
        private float typed;
        private float lineSpeed;

        private Action onAdvance;
        private Action<int> onChosen;
        private bool asking;
        private int hovered = -1;

        private sealed class Choice
        {
            public RectTransform Rect;
            public Image Fill;
            public Text Label;
        }

        /// <summary>The dialogue box in the scene, made on first use if there is none.</summary>
        public static DialogueBox Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<DialogueBox>(FindObjectsInactive.Include);
                }

                if (instance == null)
                {
                    instance = new GameObject("DialogueBox").AddComponent<DialogueBox>();
                }

                return instance;
            }
        }

        /// <summary>True from the first line until <see cref="Close"/>.</summary>
        public bool IsOpen => box != null && box.gameObject.activeSelf;

        /// <summary>
        /// True while dialogue is open or the interact prompt is showing.
        /// Static, and never makes a box, so other systems can ask cheaply.
        /// </summary>
        public static bool IsEngaged => instance != null
                                        && (instance.IsOpen || (instance.prompt != null && instance.prompt.gameObject.activeSelf));

        /// <summary>True while the current line is still typing itself out.</summary>
        public bool IsTyping => IsOpen && visibleCharacters < totalCharacters;

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
            }
        }

        /// <summary>
        /// Shows <paramref name="line"/>. <paramref name="advanced"/> is called
        /// when the player clicks on past it.
        /// </summary>
        public void Say(DialogueLine line, Action advanced)
        {
            Begin(line, false);
            onAdvance = advanced;
        }

        /// <summary>
        /// Shows <paramref name="line"/>, then once it is typed out, two
        /// choices. <paramref name="chosen"/> is given 0 for the first, 1 for
        /// the second.
        /// </summary>
        public void Ask(DialogueLine line, string first, string second, Action<int> chosen)
        {
            choices[0].Label.text = first;
            choices[1].Label.text = second;
            Begin(line, true);
            onChosen = chosen;
        }

        /// <summary>Hides the box and drops any pending callbacks.</summary>
        public void Close()
        {
            onAdvance = null;
            onChosen = null;
            asking = false;
            box.gameObject.SetActive(false);
            choiceRow.gameObject.SetActive(false);
        }

        /// <summary>Shows the interact prompt over <paramref name="worldPosition"/>.</summary>
        public void ShowPrompt(Vector3 worldPosition, string text)
        {
            promptWorldPosition = worldPosition;

            if (promptLabel.text != text)
            {
                promptLabel.text = text;
                float width = promptLabel.preferredWidth + padding;
                prompt.sizeDelta = new Vector2(width, promptFontSize + padding * 0.75f);
            }

            prompt.gameObject.SetActive(true);
            PlacePrompt();
        }

        public void HidePrompt()
        {
            prompt.gameObject.SetActive(false);
        }

        private void Begin(DialogueLine line, bool ask)
        {
            onAdvance = null;
            onChosen = null;
            asking = ask;
            hovered = -1;

            string speaker = line != null ? line.speaker : null;
            bool hasSpeaker = !string.IsNullOrWhiteSpace(speaker);
            speakerLabel.text = hasSpeaker ? speaker : string.Empty;
            speakerPlate.gameObject.SetActive(hasSpeaker);

            // The nameplate is as wide as the name needs.
            if (hasSpeaker)
            {
                speakerPlate.sizeDelta = new Vector2(speakerLabel.preferredWidth + speakerPadding.x * 2f,
                                                     speakerFontSize * 1.2f + speakerPadding.y * 2f);
            }

            fullText = line != null && line.text != null ? line.text : string.Empty;
            totalCharacters = CountVisible(fullText);
            visibleCharacters = 0;
            typed = 0f;
            lineSpeed = line != null && line.charactersPerSecond > 0f ? line.charactersPerSecond : charactersPerSecond;

            box.gameObject.SetActive(true);
            choiceRow.gameObject.SetActive(false);
            Render();

            // An empty line has nothing to type.
            if (!IsTyping)
            {
                Finished();
            }
        }

        private void Update()
        {
            if (prompt.gameObject.activeSelf)
            {
                PlacePrompt();
            }

            if (!IsOpen)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;

            if (IsTyping)
            {
                if (clicked)
                {
                    visibleCharacters = totalCharacters;
                }
                else
                {
                    typed += lineSpeed * Time.unscaledDeltaTime;
                    int whole = Mathf.Min(totalCharacters, Mathf.FloorToInt(typed));

                    if (whole == visibleCharacters)
                    {
                        return;
                    }

                    visibleCharacters = whole;
                }

                Render();

                if (!IsTyping)
                {
                    Finished();
                }

                // The click that finished the text is spent; the next one
                // moves on.
                return;
            }

            if (asking)
            {
                UpdateChoices(mouse, clicked);
                return;
            }

            if (clicked)
            {
                Action advanced = onAdvance;
                onAdvance = null;
                advanced?.Invoke();
            }
        }

        private void Finished()
        {
            if (asking)
            {
                choiceRow.gameObject.SetActive(true);
                SetHovered(-1);
            }
        }

        private void UpdateChoices(Mouse mouse, bool clicked)
        {
            int over = -1;

            if (mouse != null)
            {
                Vector2 pointer = mouse.position.ReadValue();

                for (int i = 0; i < choices.Length; i++)
                {
                    if (RectTransformUtility.RectangleContainsScreenPoint(choices[i].Rect, pointer, null))
                    {
                        over = i;
                    }
                }
            }

            SetHovered(over);

            if (clicked && over >= 0)
            {
                Action<int> chosen = onChosen;
                onChosen = null;
                asking = false;
                choiceRow.gameObject.SetActive(false);
                chosen?.Invoke(over);
            }
        }

        private void SetHovered(int index)
        {
            hovered = index;

            // Hovered choices invert: filled with the text colour, text in
            // the background colour.
            for (int i = 0; i < choices.Length; i++)
            {
                bool on = i == hovered;
                choices[i].Fill.color = on ? foreground : background;
                choices[i].Label.color = on ? background : foreground;
            }
        }

        private void PlacePrompt()
        {
            Camera camera = Camera.main;

            if (camera == null)
            {
                return;
            }

            Vector3 screen = camera.WorldToScreenPoint(promptWorldPosition);

            // Behind the camera: nowhere sensible to put it.
            if (screen.z < 0f)
            {
                prompt.gameObject.SetActive(false);
                return;
            }

            float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            prompt.anchoredPosition = new Vector2(screen.x / scale, screen.y / scale + promptRaise);
        }

        /// <summary>
        /// Writes the typed part of the line, with the rest present but
        /// invisible. Laying out the whole line from the start keeps a word
        /// from jumping to the next row halfway through being typed.
        /// </summary>
        private void Render()
        {
            if (visibleCharacters >= totalCharacters)
            {
                body.text = fullText;
                return;
            }

            StringBuilder builder = new StringBuilder(fullText.Length + 32);
            Stack<string> open = new Stack<string>();
            int shown = 0;
            int i = 0;

            // The typed part, tags and all. Tags cost no typing time.
            while (i < fullText.Length)
            {
                if (TryReadTag(fullText, i, out int tagEnd, out string name, out bool closing))
                {
                    builder.Append(fullText, i, tagEnd - i);

                    if (closing)
                    {
                        if (open.Count > 0)
                        {
                            open.Pop();
                        }
                    }
                    else
                    {
                        open.Push(name);
                    }

                    i = tagEnd;
                    continue;
                }

                if (shown == visibleCharacters)
                {
                    break;
                }

                builder.Append(fullText[i]);
                shown++;
                i++;
            }

            // Close what the typed part left open, so the hidden part below
            // inherits none of it.
            while (open.Count > 0)
            {
                builder.Append("</").Append(open.Pop()).Append('>');
            }

            // The rest without its tags, wrapped in fully transparent colour.
            builder.Append(HiddenColour);

            while (i < fullText.Length)
            {
                if (TryReadTag(fullText, i, out int tagEnd, out _, out _))
                {
                    i = tagEnd;
                    continue;
                }

                builder.Append(fullText[i]);
                i++;
            }

            builder.Append("</color>");
            body.text = builder.ToString();
        }

        private static int CountVisible(string text)
        {
            int count = 0;
            int i = 0;

            while (i < text.Length)
            {
                if (TryReadTag(text, i, out int tagEnd, out _, out _))
                {
                    i = tagEnd;
                    continue;
                }

                count++;
                i++;
            }

            return count;
        }

        /// <summary>
        /// Reads a rich text tag starting at <paramref name="start"/>, such as
        /// &lt;b&gt;, &lt;/b&gt; or &lt;color=#fff&gt;. A '&lt;' that is not
        /// the start of a tag Unity knows is ordinary text.
        /// </summary>
        private static bool TryReadTag(string text, int start, out int end, out string name, out bool closing)
        {
            end = start;
            name = null;
            closing = false;

            if (text[start] != '<')
            {
                return false;
            }

            int close = text.IndexOf('>', start + 1);

            if (close < 0)
            {
                return false;
            }

            string inner = text.Substring(start + 1, close - start - 1);
            closing = inner.StartsWith("/");

            if (closing)
            {
                inner = inner.Substring(1);
            }

            int equals = inner.IndexOf('=');
            name = (equals >= 0 ? inner.Substring(0, equals) : inner).Trim().ToLowerInvariant();

            if (name != "b" && name != "i" && name != "color" && name != "size")
            {
                return false;
            }

            end = close + 1;
            return true;
        }

        private void Build()
        {
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasRect = GetComponent<RectTransform>();

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceHeight * 16f / 9f, ReferenceHeight);
            scaler.matchWidthOrHeight = 0.5f;

            // The box: a frame-coloured panel with a background panel inset
            // by the frame width, the same hard-edged look as the health bar.
            box = Panel("Box", canvasRect, out _);
            box.anchorMin = new Vector2(0f, 0f);
            box.anchorMax = new Vector2(1f, 0f);
            box.pivot = new Vector2(0.5f, 0f);
            box.offsetMin = new Vector2(screenMargin, screenMargin);
            box.offsetMax = new Vector2(-screenMargin, screenMargin + boxHeight);

            // The speaker's nameplate: a box of its own sitting on the main
            // box's top edge at the left, its bottom frame lying over the
            // box's top frame so the two borders read as one.
            speakerPlate = Panel("Speaker", box, out _);
            speakerPlate.anchorMin = speakerPlate.anchorMax = new Vector2(0f, 1f);
            speakerPlate.pivot = new Vector2(0f, 0f);
            speakerPlate.anchoredPosition = new Vector2(padding, -frameWidth);

            speakerLabel = Label("Name", speakerPlate, speakerFontSize, TextAnchor.MiddleCenter);
            speakerLabel.fontStyle = FontStyle.Bold;
            speakerLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            RectTransform speakerRect = speakerLabel.rectTransform;
            speakerRect.anchorMin = Vector2.zero;
            speakerRect.anchorMax = Vector2.one;
            speakerRect.offsetMin = Vector2.zero;
            speakerRect.offsetMax = Vector2.zero;

            body = Label("Text", box, fontSize, TextAnchor.UpperLeft);
            body.supportRichText = true;
            body.lineSpacing = 1.1f;
            bodyRect = body.rectTransform;
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = new Vector2(padding, padding);
            bodyRect.offsetMax = new Vector2(-padding, -padding);

            // The choices sit centred just above the box.
            choiceRow = new GameObject("Choices", typeof(RectTransform)).GetComponent<RectTransform>();
            choiceRow.SetParent(canvasRect, false);
            choiceRow.anchorMin = choiceRow.anchorMax = new Vector2(0.5f, 0f);
            choiceRow.pivot = new Vector2(0.5f, 0f);
            choiceRow.sizeDelta = new Vector2(choiceSize.x * 2f + choiceGap, choiceSize.y);
            choiceRow.anchoredPosition = new Vector2(0f, screenMargin + boxHeight + choiceGap * 0.5f);

            for (int i = 0; i < choices.Length; i++)
            {
                RectTransform rect = Panel("Choice " + (i + 1), choiceRow, out Image fill);
                rect.anchorMin = rect.anchorMax = new Vector2(i == 0 ? 0f : 1f, 0.5f);
                rect.pivot = new Vector2(i == 0 ? 0f : 1f, 0.5f);
                rect.sizeDelta = choiceSize;
                rect.anchoredPosition = Vector2.zero;

                Text label = Label("Label", rect, fontSize, TextAnchor.MiddleCenter);
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = Vector2.zero;
                label.rectTransform.offsetMax = Vector2.zero;

                choices[i] = new Choice { Rect = rect, Fill = fill, Label = label };
            }

            prompt = Panel("Prompt", canvasRect, out _);
            prompt.anchorMin = prompt.anchorMax = Vector2.zero;
            prompt.pivot = new Vector2(0.5f, 0f);
            promptLabel = Label("Label", prompt, promptFontSize, TextAnchor.MiddleCenter);
            promptLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            promptLabel.rectTransform.anchorMin = Vector2.zero;
            promptLabel.rectTransform.anchorMax = Vector2.one;
            promptLabel.rectTransform.offsetMin = Vector2.zero;
            promptLabel.rectTransform.offsetMax = Vector2.zero;

            box.gameObject.SetActive(false);
            choiceRow.gameObject.SetActive(false);
            prompt.gameObject.SetActive(false);
        }

        /// <summary>A frame-coloured rectangle with a background-coloured one inset inside it. Returns the outer one.</summary>
        private RectTransform Panel(string name, RectTransform parent, out Image fill)
        {
            Image frame = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            frame.color = foreground;
            frame.raycastTarget = false;
            RectTransform rect = frame.rectTransform;
            rect.SetParent(parent, false);

            fill = new GameObject("Fill", typeof(RectTransform)).AddComponent<Image>();
            fill.color = background;
            fill.raycastTarget = false;
            RectTransform fillRect = fill.rectTransform;
            fillRect.SetParent(rect, false);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(frameWidth, frameWidth);
            fillRect.offsetMax = new Vector2(-frameWidth, -frameWidth);

            return rect;
        }

        private Text Label(string name, RectTransform parent, int size, TextAnchor alignment)
        {
            Text text = new GameObject(name, typeof(RectTransform)).AddComponent<Text>();
            text.rectTransform.SetParent(parent, false);
            text.font = font;
            text.fontSize = size;
            text.color = foreground;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.raycastTarget = false;
            return text;
        }
    }
}
