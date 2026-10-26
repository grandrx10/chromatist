using UnityEngine;
using UnityEngine.UI;

namespace LightAndShadow
{
    /// <summary>
    /// The boss's health bar: a long bar across the top of the screen with
    /// the boss's name written inside it, in the same look as the player's --
    /// a white fill in a white frame, black behind, and a grey drain bar that
    /// shows the health just lost before easing down to meet the fill.
    /// </summary>
    /// <remarks>
    /// The name is black where the white fill is behind it and white where
    /// the fill has drawn back past it, so it always reads, and a letter the
    /// fill's edge crosses is split between the two. It is two copies of the
    /// same label: a white one sitting on the background, and a black one
    /// inside the fill, clipped to it by a RectMask2D.
    ///
    /// Hidden until <see cref="Show"/>, and hides itself when the boss dies
    /// or is destroyed.
    ///
    /// Builds its own overlay canvas, so nothing has to be placed in the
    /// scene. To restyle it, put this component on an object in the scene and
    /// change the fields -- that one is used instead. Drawn just under the
    /// dialogue box.
    ///
    /// Read every frame rather than from the health's events, like the
    /// player's bar, so it is never stale.
    /// </remarks>
    public sealed class BossHealthBar : MonoBehaviour
    {
        [Header("Layout (pixels at 1920x1080)")]
        [SerializeField] private Vector2 barSize = new Vector2(1000f, 52f);
        [SerializeField] [Min(0f)] private float topMargin = 70f;
        [SerializeField] [Min(0f)] private float frameWidth = 3f;
        [SerializeField] [Min(0f)] private float fillInset = 3f;

        [Header("Name")]
        [SerializeField]
        [Tooltip("Empty uses Unity's built-in font.")]
        private Font font;

        [SerializeField] [Min(8)] private int fontSize = 30;

        [SerializeField]
        [Tooltip("Where in the bar the name sits.")]
        private TextAnchor nameAlignment = TextAnchor.MiddleCenter;

        [SerializeField]
        [Tooltip("Gap between the name and the ends of the bar.")]
        [Min(0f)]
        private float namePadding = 16f;

        [Header("Colours")]
        [SerializeField] private Color background = Color.black;
        [SerializeField] private Color foreground = Color.white;
        [SerializeField] private Color drainColour = new Color(0.5f, 0.5f, 0.5f, 1f);

        [Header("Drain")]
        [SerializeField]
        [Tooltip("Seconds the grey holds after a hit before it starts draining. Each new hit restarts it.")]
        [Min(0f)]
        private float drainDelay = 0.4f;

        [SerializeField]
        [Tooltip("How quickly the grey eases down to the fill once it starts. Higher is faster.")]
        [Min(0.01f)]
        private float drainSpeed = 5f;

        private const float ReferenceHeight = 1080f;

        private static BossHealthBar instance;

        private RectTransform root;
        private RectTransform fill;
        private RectTransform drain;
        private Text nameLabel;
        private Text nameOnFill;

        private EnemyHealth health;
        private float shown;
        private float drained;
        private float holdRemaining;

        private static BossHealthBar Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<BossHealthBar>(FindObjectsInactive.Include);
                }

                if (instance == null)
                {
                    instance = new GameObject("BossHealthBar").AddComponent<BossHealthBar>();
                }

                return instance;
            }
        }

        /// <summary>Shows the bar for <paramref name="bossHealth"/>, under <paramref name="bossName"/>.</summary>
        public static void Show(EnemyHealth bossHealth, string bossName)
        {
            BossHealthBar bar = Instance;
            bar.health = bossHealth;
            bar.nameLabel.text = bossName;
            bar.nameOnFill.text = bossName;

            float fraction = bar.Fraction();
            bar.shown = fraction;
            bar.drained = fraction;
            bar.holdRemaining = 0f;
            SetWidth(bar.fill, fraction);
            SetWidth(bar.drain, fraction);

            bar.root.gameObject.SetActive(true);
        }

        /// <summary>Hides the bar, if there is one.</summary>
        public static void Hide()
        {
            if (instance != null)
            {
                instance.health = null;
                instance.root.gameObject.SetActive(false);
            }
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
            }
        }

        private float Fraction()
        {
            return health != null ? Mathf.Clamp01(health.CurrentHealth / health.MaxHealth) : 0f;
        }

        private void LateUpdate()
        {
            if (!root.gameObject.activeSelf)
            {
                return;
            }

            // Destroyed counts as dead: a boss with Destroy On Death is gone
            // the frame it dies.
            if (health == null || health.IsDead)
            {
                Hide();
                return;
            }

            float fraction = Fraction();

            if (fraction < shown)
            {
                holdRemaining = drainDelay;
            }

            if (!Mathf.Approximately(fraction, shown))
            {
                shown = fraction;
                SetWidth(fill, fraction);
            }

            if (fraction >= drained)
            {
                drained = fraction;
            }
            else if (holdRemaining > 0f)
            {
                holdRemaining -= Time.deltaTime;
            }
            else
            {
                drained = Mathf.Lerp(drained, fraction, 1f - Mathf.Exp(-drainSpeed * Time.deltaTime));

                // An ease never quite arrives on its own; snap the last sliver.
                if (drained - fraction < 0.001f)
                {
                    drained = fraction;
                }
            }

            SetWidth(drain, drained);
        }

        private static void SetWidth(RectTransform bar, float fraction)
        {
            bar.anchorMax = new Vector2(fraction, bar.anchorMax.y);

            // A zero-width image still draws a sliver on some resolutions.
            bar.gameObject.SetActive(fraction > 0f);
        }

        private void Build()
        {
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;
            RectTransform canvasRect = GetComponent<RectTransform>();

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceHeight * 16f / 9f, ReferenceHeight);
            scaler.matchWidthOrHeight = 0.5f;

            root = new GameObject("Boss", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(canvasRect, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.sizeDelta = barSize;
            root.anchoredPosition = new Vector2(0f, -topMargin);

            // Frame, with the background inset by the frame width, then the
            // drain and fill inset a little further inside that. The white
            // name goes between the drain and the fill, so the fill covers it.
            RectTransform frame = Block("Frame", root, foreground, 0f);
            RectTransform back = Block("Background", frame, background, frameWidth);
            drain = Block("Drain", back, drainColour, fillInset);

            Vector2 inner = barSize - Vector2.one * (2f * (frameWidth + fillInset));
            nameLabel = NameLabel("Name", back, foreground, inner);
            nameLabel.rectTransform.anchoredPosition = new Vector2(fillInset + namePadding, 0f);

            fill = Block("Fill", back, foreground, fillInset);
            fill.gameObject.AddComponent<RectMask2D>();

            // Pinned to the fill's left edge, which never moves, at the full
            // width of the bar: the fill shrinking only uncovers it.
            nameOnFill = NameLabel("Name", fill, background, inner);
            nameOnFill.rectTransform.anchoredPosition = new Vector2(namePadding, 0f);

            root.gameObject.SetActive(false);
        }

        /// <summary>
        /// A name label the size of the inside of the bar less its padding,
        /// pinned by its left edge to the middle of <paramref name="parent"/>'s
        /// left side. The caller sets how far in.
        /// </summary>
        private Text NameLabel(string name, RectTransform parent, Color colour, Vector2 size)
        {
            Text label = new GameObject(name, typeof(RectTransform)).AddComponent<Text>();
            RectTransform rect = label.rectTransform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(size.x - 2f * namePadding, size.y);

            label.font = font;
            label.fontSize = fontSize;
            label.fontStyle = FontStyle.Bold;
            label.color = colour;
            label.alignment = nameAlignment;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>A plain rectangle filling <paramref name="parent"/>, inset by <paramref name="inset"/> on every side.</summary>
        private static RectTransform Block(string name, RectTransform parent, Color colour, float inset)
        {
            Image image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;

            RectTransform rect = image.rectTransform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            return rect;
        }
    }
}
