using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace LightAndShadow
{
    /// <summary>
    /// A flat colour over the whole screen, UI included, that fades in and
    /// out and is kept across scene loads.
    /// </summary>
    /// <remarks>
    /// Built at runtime, so it needs no prefab. Times are unscaled. Destroy
    /// its <see cref="Component.gameObject"/> when done with it.
    /// </remarks>
    public sealed class ScreenFade : MonoBehaviour
    {
        private CanvasGroup group;

        /// <summary>The canvas it draws on, for code that hides every other canvas.</summary>
        public Canvas Canvas { get; private set; }

        public float Alpha
        {
            get => group.alpha;
            set => group.alpha = Mathf.Clamp01(value);
        }

        /// <summary>Makes a full-screen <paramref name="colour"/> at <paramref name="alpha"/>.</summary>
        public static ScreenFade Create(Color colour, float alpha)
        {
            GameObject host = new GameObject("ScreenFade");
            DontDestroyOnLoad(host);

            ScreenFade fade = host.AddComponent<ScreenFade>();
            fade.Build(colour);
            fade.Alpha = alpha;
            return fade;
        }

        /// <summary>Fades to <paramref name="alpha"/> over <paramref name="time"/> seconds.</summary>
        public IEnumerator FadeTo(float alpha, float time)
        {
            float from = Alpha;

            for (float clock = 0f; clock < time; clock += Time.unscaledDeltaTime)
            {
                Alpha = Mathf.Lerp(from, alpha, clock / time);
                yield return null;
            }

            Alpha = alpha;
        }

        private void Build(Color colour)
        {
            Canvas = gameObject.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 1000;

            group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            Image fill = new GameObject("Fill", typeof(RectTransform)).AddComponent<Image>();
            fill.color = colour;
            fill.raycastTarget = false;

            RectTransform rect = fill.rectTransform;
            rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
