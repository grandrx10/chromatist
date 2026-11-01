using UnityEngine;
using UnityEngine.UI;

namespace LightAndShadow
{
    /// <summary>
    /// An enemy that can be hit forever: it takes every blow, so its damage
    /// numbers show, and is back to full health straight after. Never dies.
    /// Over its head it shows the damage dealt to it, which goes back to 0
    /// after <see cref="resetDelay"/> seconds without a hit.
    /// </summary>
    /// <remarks>
    /// Goes next to an <see cref="EnemyHealth"/> on an object with the art
    /// and a collider for the blades to hit. Nothing else is needed: the
    /// numbers come from <see cref="DamageNumbers"/>, which hears every
    /// enemy. Its health is raised high so no single blow is cut short by
    /// what is left of it.
    ///
    /// The total is drawn on an overlay canvas of its own, like the damage
    /// numbers, so it keeps its colour over the two-tone threshold.
    /// </remarks>
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class TrainingDummy : MonoBehaviour
    {
        private const float ReferenceHeight = 1080f;

        [SerializeField]
        [Tooltip("Health it is given, refilled after every blow. Only needs to be more than any single blow.")]
        [Min(1f)]
        private float health = 1000000f;

        [Header("Total")]
        [SerializeField]
        [Tooltip("Seconds without a hit before the total goes back to 0.")]
        [Min(0f)]
        private float resetDelay = 2f;

        [SerializeField]
        [Tooltip("Height of the total above the top of the body.")]
        private float heightAbove = 0.6f;

        [SerializeField]
        [Tooltip("Empty uses Unity's built-in font.")]
        private Font font;

        [SerializeField]
        [Min(8)]
        private int fontSize = 40;

        [SerializeField]
        private Color colour = Color.white;

        [SerializeField]
        private Color outlineColour = Color.black;

        [SerializeField]
        [Min(0f)]
        private float outlineWidth = 2.5f;

        [SerializeField]
        [Tooltip("Scale the total pops to on each hit, settling to 1.")]
        [Min(1f)]
        private float popScale = 1.25f;

        [SerializeField]
        [Min(0.01f)]
        private float popTime = 0.12f;

        private EnemyHealth enemyHealth;
        private float total;
        private float sinceHit;
        private float popAge = float.MaxValue;

        private GameObject canvasObject;
        private RectTransform canvasRect;
        private Text label;
        private Camera worldCamera;

        private void Awake()
        {
            enemyHealth = GetComponent<EnemyHealth>();
            enemyHealth.SetMaxHealth(health);
            enemyHealth.DestroyOnDeath = false;
            enemyHealth.CannotDie = true;
            BuildLabel();
        }

        private void OnEnable()
        {
            enemyHealth.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            enemyHealth.Damaged -= OnDamaged;

            if (canvasObject != null)
            {
                canvasObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (canvasObject != null)
            {
                Destroy(canvasObject);
            }
        }

        private void OnDamaged(EnemyHealth target, float amount)
        {
            target.Heal(target.MaxHealth);

            if (amount <= 0f)
            {
                return;
            }

            total += amount;
            sinceHit = 0f;
            popAge = 0f;
        }

        private void LateUpdate()
        {
            sinceHit += Time.deltaTime;
            popAge += Time.deltaTime;

            if (total > 0f && sinceHit >= resetDelay)
            {
                total = 0f;
            }

            Place();
        }

        /// <summary>Puts the total over the dummy's head on screen.</summary>
        private void Place()
        {
            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }

            Bounds body = enemyHealth.BodyBounds();
            Vector3 world = new Vector3(body.center.x, body.max.y + heightAbove, body.center.z);
            Vector3 screen = worldCamera != null ? worldCamera.WorldToScreenPoint(world) : Vector3.back;

            if (screen.z <= 0f)
            {
                canvasObject.SetActive(false);
                return;
            }

            canvasObject.SetActive(true);

            Vector2 canvasSize = canvasRect.rect.size;
            label.rectTransform.anchoredPosition = new Vector2(screen.x / Screen.width * canvasSize.x, screen.y / Screen.height * canvasSize.y);
            label.text = Mathf.RoundToInt(total).ToString();

            float pop = Mathf.Clamp01(popAge / popTime);
            label.rectTransform.localScale = Vector3.one * Mathf.Lerp(popScale, 1f, 1f - (1f - pop) * (1f - pop));
        }

        private void BuildLabel()
        {
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            // Not a child: the dummy may be scaled or moved, the canvas must not be.
            canvasObject = new GameObject(name + " Total");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Under the dialogue box (100), with the damage numbers (90).
            canvas.sortingOrder = 89;
            canvasRect = canvasObject.GetComponent<RectTransform>();

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceHeight * 16f / 9f, ReferenceHeight);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject made = new GameObject("Label", typeof(RectTransform));
            RectTransform rect = made.GetComponent<RectTransform>();
            rect.SetParent(canvasRect, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(fontSize * 8f, fontSize * 2f);

            label = made.AddComponent<Text>();
            label.font = font;
            label.fontSize = fontSize;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            label.color = colour;

            Outline outline = made.AddComponent<Outline>();
            outline.effectDistance = new Vector2(outlineWidth, -outlineWidth);
            outline.effectColor = outlineColour;
        }
    }
}
