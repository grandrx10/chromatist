using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LightAndShadow
{
    /// <summary>
    /// A scene change through a closing circle: the screen goes black from
    /// the edges in, round the player, the next scene loads, and the circle
    /// opens again round the player there.
    /// </summary>
    /// <remarks>
    /// Drawn over everything, UI included, and kept across the load. The
    /// circle is a soft-edged hole in a black square, with black bars filling
    /// the rest of the screen, so it needs no shader. Times are unscaled.
    /// </remarks>
    public sealed class ScreenIris : MonoBehaviour
    {
        private const int TextureSize = 256;

        private static Sprite hole;

        private RectTransform holeRect;
        private RectTransform[] bars;

        /// <summary>
        /// Closes the screen round <paramref name="focus"/> over
        /// <paramref name="closeTime"/>, loads <paramref name="scene"/>, then
        /// opens it round the player there over <paramref name="openTime"/>.
        /// </summary>
        public static void CloseThenLoad(string scene, Transform focus, float closeTime, float openTime)
        {
            GameObject host = new GameObject("ScreenIris");
            DontDestroyOnLoad(host);

            ScreenIris iris = host.AddComponent<ScreenIris>();
            iris.Build();
            iris.StartCoroutine(iris.Play(scene, focus, closeTime, openTime));
        }

        /// <summary>
        /// Covers the screen in black but for a hole round
        /// <paramref name="centre"/>, as big on screen as
        /// <paramref name="radius"/> there, and opens the hole to the whole
        /// screen over <paramref name="time"/>. Drawn from the frame it is
        /// made, so whatever the camera shows that frame is already hidden.
        /// </summary>
        public static void Reveal(Vector3 centre, float radius, float time)
        {
            GameObject host = new GameObject("ScreenIris");
            DontDestroyOnLoad(host);

            ScreenIris iris = host.AddComponent<ScreenIris>();
            iris.Build();

            Vector2 point = OnScreen(centre, radius, out float start);
            iris.Draw(point, start);
            iris.StartCoroutine(iris.Widen(centre, radius, time));
        }

        private IEnumerator Widen(Vector3 centre, float radius, float time)
        {
            for (float clock = 0f; clock < time; clock += Time.unscaledDeltaTime)
            {
                Vector2 point = OnScreen(centre, radius, out float start);
                float t = clock / time;
                Draw(point, Mathf.Lerp(start, Open(point), t * t * (3f - 2f * t)));
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

        private IEnumerator Play(string scene, Transform focus, float closeTime, float openTime)
        {
            Vector2 centre = Centre(focus, new Vector2(Screen.width, Screen.height) * 0.5f);

            for (float clock = 0f; clock < closeTime; clock += Time.unscaledDeltaTime)
            {
                centre = Centre(focus, centre);
                float t = clock / closeTime;
                Draw(centre, Open(centre) * (1f - t * t * (3f - 2f * t)));
                yield return null;
            }

            Draw(centre, 0f);

            // The new scene's player and camera settle in under the black.
            // A scene missing from the build opens back up where it was.
            if (Application.CanStreamedLevelBeLoaded(scene))
            {
                LevelExit.ReleasePending();
                yield return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            }
            else
            {
                Debug.LogWarning($"[ScreenIris] Can't load scene \"{scene}\": it isn't in File > Build Profiles' scene list.");
            }

            yield return null;
            yield return null;

            PlayerMotor player = FindAnyObjectByType<PlayerMotor>();
            focus = player != null ? player.transform : null;
            centre = Centre(focus, new Vector2(Screen.width, Screen.height) * 0.5f);

            for (float clock = 0f; clock < openTime; clock += Time.unscaledDeltaTime)
            {
                centre = Centre(focus, centre);
                float t = clock / openTime;
                Draw(centre, Open(centre) * (t * t * (3f - 2f * t)));
                yield return null;
            }

            Destroy(gameObject);
        }

        /// <summary>Where <paramref name="focus"/> is on screen, or <paramref name="fallback"/> if it can't be seen.</summary>
        private static Vector2 Centre(Transform focus, Vector2 fallback)
        {
            Camera camera = Camera.main;

            if (focus == null || camera == null)
            {
                return fallback;
            }

            Vector3 point = camera.WorldToScreenPoint(focus.position);
            return point.z > 0f ? (Vector2)point : fallback;
        }

        /// <summary>The radius that clears the whole screen from <paramref name="centre"/>, soft edge and all.</summary>
        private static float Open(Vector2 centre)
        {
            float x = Mathf.Max(centre.x, Screen.width - centre.x);
            float y = Mathf.Max(centre.y, Screen.height - centre.y);

            // The hole's soft edge is the outer tenth of its radius.
            return new Vector2(x, y).magnitude * 1.15f;
        }

        private void Draw(Vector2 centre, float radius)
        {
            float size = radius * 2f;
            holeRect.anchoredPosition = centre - Vector2.one * radius;
            holeRect.sizeDelta = new Vector2(size, size);

            float left = centre.x - radius;
            float right = centre.x + radius;
            float bottom = centre.y - radius;
            float top = centre.y + radius;
            const float Overlap = 1f;

            // Left and right full height; above and below between them.
            Place(bars[0], -1f, -1f, left + Overlap, Screen.height + 2f);
            Place(bars[1], right - Overlap, -1f, Screen.width - right + Overlap + 1f, Screen.height + 2f);
            Place(bars[2], left, -1f, size, bottom + Overlap + 1f);
            Place(bars[3], left, top - Overlap, size, Screen.height - top + Overlap + 1f);
        }

        private static void Place(RectTransform bar, float x, float y, float width, float height)
        {
            bar.anchoredPosition = new Vector2(x, y);
            bar.sizeDelta = new Vector2(Mathf.Max(0f, width), Mathf.Max(0f, height));
        }

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            holeRect = MakeImage("Hole", Hole);
            bars = new RectTransform[4];

            for (int i = 0; i < bars.Length; i++)
            {
                bars[i] = MakeImage("Bar", null);
            }

            Draw(new Vector2(Screen.width, Screen.height) * 0.5f, Open(new Vector2(Screen.width, Screen.height) * 0.5f));
        }

        private RectTransform MakeImage(string imageName, Sprite sprite)
        {
            GameObject host = new GameObject(imageName, typeof(RectTransform));
            host.transform.SetParent(transform, false);

            Image image = host.AddComponent<Image>();
            image.sprite = sprite;
            image.color = Color.black;
            image.raycastTarget = false;

            // Placed in pixels from the bottom left corner.
            RectTransform rect = (RectTransform)host.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            return rect;
        }

        /// <summary>A white square, see-through in a soft-edged circle in its middle.</summary>
        private static Sprite Hole
        {
            get
            {
                if (hole != null)
                {
                    return hole;
                }

                Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
                {
                    name = "ScreenIrisHole",
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
                        float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.9f, 1f, distance));
                        pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                    }
                }

                texture.SetPixels32(pixels);
                texture.Apply(false, true);

                hole = Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize), new Vector2(0.5f, 0.5f));
                return hole;
            }
        }
    }
}
