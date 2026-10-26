using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LightAndShadow
{
    /// <summary>
    /// Pops a number off an enemy every time it takes damage, at a random
    /// spot round its body, rising and fading away.
    /// </summary>
    /// <remarks>
    /// Hears every enemy through <see cref="EnemyHealth.AnyDamaged"/>, so
    /// enemies need nothing added to them.
    ///
    /// Each number belongs to a point in the world, not to the enemy: it
    /// rises from where the hit landed and stays there if the enemy walks off
    /// or dies. It is projected onto the screen every frame, so it stays put
    /// on the floor as the camera moves.
    ///
    /// Drawn on an overlay canvas of its own, built in code, which comes after
    /// the two-tone threshold: the numbers keep their colour, and the black
    /// outline keeps them readable over white and black alike. Labels are
    /// pooled; past <see cref="maxNumbers"/> on screen, the oldest is reused.
    ///
    /// Installs itself in every scene that does not already have one, so it
    /// works with no setup. Place one by hand to change its settings.
    /// </remarks>
    public sealed class DamageNumbers : MonoBehaviour
    {
        // Canvas units the layout is designed at, scaled to the real screen.
        private const float ReferenceHeight = 1080f;

        [Header("Look")]
        [SerializeField]
        [Tooltip("Empty uses Unity's built-in font.")]
        private Font font;

        [SerializeField]
        [Min(8)]
        private int fontSize = 30;

        [SerializeField]
        private Color colour = Color.white;

        [SerializeField]
        private Color outlineColour = Color.black;

        [SerializeField]
        [Tooltip("Thickness of the outline, in canvas pixels.")]
        [Min(0f)]
        private float outlineWidth = 2f;

        [Header("Placement")]
        [SerializeField]
        [Tooltip("Farthest a number starts from the middle of the body, sideways, as a fraction of the body's width.")]
        [Min(0f)]
        private float spread = 0.8f;

        [SerializeField]
        [Tooltip("How far up the body numbers start, 0 the middle and 1 the top. Each is placed at random up to this.")]
        [Range(0f, 1.5f)]
        private float heightRange = 1f;

        [Header("Motion")]
        [SerializeField]
        [Tooltip("World units a number rises over its life.")]
        [Min(0f)]
        private float rise = 1.2f;

        [SerializeField]
        [Tooltip("Seconds a number lasts.")]
        [Min(0.1f)]
        private float lifetime = 0.8f;

        [SerializeField]
        [Tooltip("Last part of its life, as a fraction, that a number spends fading out.")]
        [Range(0f, 1f)]
        private float fadePortion = 0.4f;

        [SerializeField]
        [Tooltip("Scale a number pops in at, settling to 1.")]
        [Min(1f)]
        private float popScale = 1.5f;

        [SerializeField]
        [Tooltip("Seconds the pop takes to settle.")]
        [Min(0.01f)]
        private float popTime = 0.12f;

        [SerializeField]
        [Tooltip("Most numbers on screen at once. Past it the oldest is reused.")]
        [Min(1)]
        private int maxNumbers = 48;

        private sealed class Number
        {
            public Text Label;
            public Outline Outline;
            public Vector3 Start;
            public float Age;
            public bool Live;
        }

        private readonly List<Number> numbers = new List<Number>();

        private RectTransform canvasRect;
        private Camera worldCamera;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => EnsureOne();
            EnsureOne();
        }

        private static void EnsureOne()
        {
            if (FindFirstObjectByType<DamageNumbers>(FindObjectsInactive.Include) == null)
            {
                new GameObject("DamageNumbers").AddComponent<DamageNumbers>();
            }
        }

        private void Awake()
        {
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Under the dialogue box, which sits at 100.
            canvas.sortingOrder = 90;
            canvasRect = GetComponent<RectTransform>();

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceHeight * 16f / 9f, ReferenceHeight);
            scaler.matchWidthOrHeight = 0.5f;
        }

        private void OnEnable()
        {
            EnemyHealth.AnyDamaged += OnDamaged;
        }

        private void OnDisable()
        {
            EnemyHealth.AnyDamaged -= OnDamaged;

            foreach (Number number in numbers)
            {
                number.Live = false;
                number.Label.gameObject.SetActive(false);
            }
        }

        private void OnDamaged(EnemyHealth enemy, float amount)
        {
            int shown = Mathf.RoundToInt(amount);

            if (shown <= 0 || enemy == null)
            {
                return;
            }

            Bounds body = enemy.BodyBounds();
            Vector2 side = Random.insideUnitCircle * (Mathf.Max(body.extents.x, body.extents.z) * spread);
            float up = Random.Range(0f, heightRange) * body.extents.y;

            Number number = Take();
            number.Start = body.center + new Vector3(side.x, up, side.y);
            number.Age = 0f;
            number.Live = true;
            number.Label.text = shown.ToString();

            // Newest on top, so a fresh hit is never hidden under an old one.
            number.Label.transform.SetAsLastSibling();
            Place(number);
        }

        /// <summary>A free label, a new one while under the cap, or else the oldest on screen.</summary>
        private Number Take()
        {
            Number oldest = null;

            foreach (Number number in numbers)
            {
                if (!number.Live)
                {
                    return number;
                }

                if (oldest == null || number.Age > oldest.Age)
                {
                    oldest = number;
                }
            }

            return numbers.Count < maxNumbers ? Make() : oldest;
        }

        private Number Make()
        {
            GameObject made = new GameObject("Number", typeof(RectTransform));
            RectTransform rect = made.GetComponent<RectTransform>();
            rect.SetParent(canvasRect, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(fontSize * 6f, fontSize * 2f);

            Text label = made.AddComponent<Text>();
            label.font = font;
            label.fontSize = fontSize;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;

            Outline outline = made.AddComponent<Outline>();
            outline.effectDistance = new Vector2(outlineWidth, -outlineWidth);

            Number number = new Number { Label = label, Outline = outline };
            numbers.Add(number);
            return number;
        }

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;

            foreach (Number number in numbers)
            {
                if (!number.Live)
                {
                    continue;
                }

                number.Age += deltaTime;

                if (number.Age >= lifetime)
                {
                    number.Live = false;
                    number.Label.gameObject.SetActive(false);
                    continue;
                }

                Place(number);
            }
        }

        /// <summary>Moves a number to where its point in the world is on screen now, risen, popped and faded for its age.</summary>
        private void Place(Number number)
        {
            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }

            float t = Mathf.Clamp01(number.Age / lifetime);

            // Eased out: leaps up off the hit, then drifts.
            float risen = rise * (1f - (1f - t) * (1f - t));
            Vector3 world = number.Start + Vector3.up * risen;

            Vector3 screen = worldCamera != null ? worldCamera.WorldToScreenPoint(world) : Vector3.back;

            // Behind the camera, or no camera at all: nothing sensible to show.
            if (screen.z <= 0f)
            {
                number.Label.gameObject.SetActive(false);
                return;
            }

            number.Label.gameObject.SetActive(true);

            // Screen pixels into canvas units, which the scaler has resized.
            Vector2 canvasSize = canvasRect.rect.size;
            Vector2 position = new Vector2(screen.x / Screen.width * canvasSize.x, screen.y / Screen.height * canvasSize.y);
            number.Label.rectTransform.anchoredPosition = position;

            float pop = Mathf.Clamp01(number.Age / popTime);
            number.Label.rectTransform.localScale = Vector3.one * Mathf.Lerp(popScale, 1f, 1f - (1f - pop) * (1f - pop));

            float fadeStart = 1f - fadePortion;
            float alpha = fadePortion > 0f ? 1f - Mathf.Clamp01((t - fadeStart) / fadePortion) : 1f;

            Color fill = colour;
            fill.a *= alpha;
            number.Label.color = fill;

            Color edge = outlineColour;
            edge.a *= alpha;
            number.Outline.effectColor = edge;
        }
    }
}
