using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LightAndShadow
{
    /// <summary>
    /// The screen swallowed by white: a white circle swells out from a point
    /// in the world until it covers everything, and stays until told to
    /// clear, here or in the next scene.
    /// </summary>
    /// <remarks>
    /// The circle starts the size something in the world is on screen, as
    /// the middle of a <see cref="WhiteHole"/>, and swells, faster and
    /// faster, until it covers the screen. <see cref="Covered"/> is true from
    /// then. <see cref="Clear"/> fades the white away;
    /// <see cref="LoadThenClear"/> loads a scene under it first.
    ///
    /// Drawn over everything, UI included, and kept across a load. Built at
    /// runtime, so it needs no prefab. Times are unscaled.
    /// </remarks>
    public sealed class WhiteOut : MonoBehaviour
    {
        private const int TextureSize = 256;

        private static Sprite disc;

        private RectTransform circle;
        private Image fill;
        private CanvasGroup group;

        /// <summary>Whether the white covers the whole screen.</summary>
        public bool Covered { get; private set; }

        /// <summary>
        /// Swells a white circle from <paramref name="centre"/>, starting as
        /// big on screen as <paramref name="radius"/> there, to cover the
        /// screen over <paramref name="time"/>.
        /// </summary>
        public static WhiteOut Cover(Vector3 centre, float radius, float time)
        {
            GameObject host = new GameObject("WhiteOut");
            DontDestroyOnLoad(host);

            WhiteOut white = host.AddComponent<WhiteOut>();
            white.Build();
            white.StartCoroutine(white.Swell(centre, radius, time));
            return white;
        }

        /// <summary>Fades the white away over <paramref name="fadeTime"/>, then goes.</summary>
        public void Clear(float fadeTime)
        {
            StartCoroutine(FadeOnceCovered(fadeTime));
        }

        /// <summary>
        /// Loads <paramref name="scene"/> under the white, then fades it away
        /// over <paramref name="fadeTime"/>. A scene missing from the build
        /// fades away where it is.
        /// </summary>
        public void LoadThenClear(string scene, float fadeTime)
        {
            StartCoroutine(Load(scene, fadeTime));
        }

        private IEnumerator Swell(Vector3 centre, float radius, float time)
        {
            for (float clock = 0f; clock < time; clock += Time.unscaledDeltaTime)
            {
                Vector2 point = OnScreen(centre, radius, out float start);
                float t = clock / time;
                Draw(point, Mathf.Lerp(start, Reach(point), t * t));
                yield return null;
            }

            circle.gameObject.SetActive(false);
            fill.enabled = true;
            Covered = true;
        }

        private IEnumerator Load(string scene, float fadeTime)
        {
            while (!Covered)
            {
                yield return null;
            }

            if (Application.CanStreamedLevelBeLoaded(scene))
            {
                LevelExit.ReleasePending();
                yield return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            }
            else
            {
                Debug.LogWarning($"[WhiteOut] Can't load scene \"{scene}\": it isn't in File > Build Profiles' scene list.");
            }

            // The new scene's player and camera settle in under the white.
            yield return null;
            yield return null;
            yield return FadeOnceCovered(fadeTime);
        }

        private IEnumerator FadeOnceCovered(float fadeTime)
        {
            while (!Covered)
            {
                yield return null;
            }

            for (float clock = 0f; clock < fadeTime; clock += Time.unscaledDeltaTime)
            {
                group.alpha = 1f - clock / fadeTime;
                yield return null;
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// Where <paramref name="centre"/> is on screen, and in
        /// <paramref name="size"/> how big <paramref name="radius"/> is there.
        /// The middle of the screen and nothing if it can't be seen.
        /// </summary>
        private static Vector2 OnScreen(Vector3 centre, float radius, out float size)
        {
            Vector2 middle = new Vector2(Screen.width, Screen.height) * 0.5f;
            Camera camera = Camera.main;
            size = 0f;

            if (camera == null)
            {
                return middle;
            }

            Vector3 point = camera.WorldToScreenPoint(centre);

            if (point.z <= 0f)
            {
                return middle;
            }

            Vector3 edge = camera.WorldToScreenPoint(centre + camera.transform.right * radius);
            size = ((Vector2)edge - (Vector2)point).magnitude;
            return point;
        }

        /// <summary>The radius that covers the whole screen from <paramref name="centre"/>, soft edge and all.</summary>
        private static float Reach(Vector2 centre)
        {
            float x = Mathf.Max(centre.x, Screen.width - centre.x);
            float y = Mathf.Max(centre.y, Screen.height - centre.y);

            // The disc's soft edge is the outer tenth of its radius.
            return new Vector2(x, y).magnitude * 1.15f;
        }

        private void Draw(Vector2 centre, float radius)
        {
            circle.anchoredPosition = centre - Vector2.one * radius;
            circle.sizeDelta = Vector2.one * (radius * 2f);
        }

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            circle = MakeImage("Circle", Disc).rectTransform;

            // Placed in pixels from the bottom left corner.
            circle.anchorMin = Vector2.zero;
            circle.anchorMax = Vector2.zero;
            circle.pivot = Vector2.zero;
            Draw(Vector2.zero, 0f);

            fill = MakeImage("Fill", null);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;
            fill.enabled = false;
        }

        private Image MakeImage(string imageName, Sprite sprite)
        {
            GameObject host = new GameObject(imageName, typeof(RectTransform));
            host.transform.SetParent(transform, false);

            Image image = host.AddComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A white disc with a soft edge, see-through round it.</summary>
        private static Sprite Disc
        {
            get
            {
                if (disc != null)
                {
                    return disc;
                }

                Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
                {
                    name = "WhiteOutDisc",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };

                Color32[] pixels = new Color32[TextureSize * TextureSize];
                float half = TextureSize * 0.5f;

                for (int y = 0; y < TextureSize; y++)
                {
                    for (int x = 0; x < TextureSize; x++)
                    {
                        float distance = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude / half;
                        float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.9f, 1f, distance));
                        pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                    }
                }

                texture.SetPixels32(pixels);
                texture.Apply(false, true);

                disc = Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize), new Vector2(0.5f, 0.5f));
                return disc;
            }
        }
    }
}
