using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LightAndShadow
{
    /// <summary>
    /// The player's death, from the blackout in the level to rising in the
    /// Afterworld. Started by <see cref="PlayerDeath"/>.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The player freezes and their controls lock. The camera draws only
    ///      the player on black, and every canvas is hidden.
    ///   2. Blood bursts out all round them, and their animator plays its
    ///      death state.
    ///   3. After the fall time, a white circle opens on the floor under
    ///      them, they sink into it, and it closes. The screen is black.
    ///   4. The Afterworld loads under the black. Its player starts hidden
    ///      below the floor, drawn alone on black as in the level.
    ///   5. A white circle opens under them, they rise out of it, it closes,
    ///      and the Afterworld is shown and their controls unlock.
    ///
    /// Steps 4 and 5 also play on their own: <see cref="Arrive"/> after a
    /// <see cref="ReturnCircle"/> fills the screen with white (entering the
    /// Afterworld is always the rise), and <see cref="Rise"/> where the
    /// player stands, for a level's opening.
    ///
    /// The blackout works by moving the player onto an unused layer and
    /// having the camera draw only that layer, so nothing else needs hiding
    /// and anything spawned meanwhile stays unseen. The sunk player is kept
    /// out of sight by the circle, which is drawn solid: wide enough, it
    /// hides what is below it from the camera's angle.
    ///
    /// Kept across the load, and destroyed at the end. Times are unscaled,
    /// so hit stop on the killing blow does not stall it.
    /// </remarks>
    public sealed class DeathSequence : MonoBehaviour
    {
        private const float CircleLift = 0.02f;
        private const int CircleSegments = 64;

        private PlayerDeath.Settings settings;
        private int layer;

        // The player being worked on, in whichever scene.
        private Transform player;
        private PlayerMotor motor;
        private PlayerInputReader input;
        private Rigidbody body;
        private bool wasKinematic;
        private readonly List<KeyValuePair<GameObject, int>> originalLayers = new List<KeyValuePair<GameObject, int>>();
        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();

        // The camera and canvases changed for the blackout.
        private Camera view;
        private int viewMask;
        private CameraClearFlags viewClear;
        private Color viewBackground;
        private TopDownCamera follow;
        private Transform followAnchor;
        private readonly List<Canvas> hiddenCanvases = new List<Canvas>();

        /// <summary>Starts the death of the player <paramref name="death"/> is on.</summary>
        public static void Play(PlayerDeath death, PlayerDeath.Settings settings)
        {
            GameObject host = new GameObject("DeathSequence");
            DontDestroyOnLoad(host);

            DeathSequence sequence = host.AddComponent<DeathSequence>();
            sequence.settings = settings;
            sequence.layer = FreeLayer(death.gameObject.layer);

            PlayerMotor root = death.GetComponentInParent<PlayerMotor>();
            sequence.StartCoroutine(sequence.Run(root != null ? root.transform : death.transform));
        }

        /// <summary>
        /// Once <paramref name="white"/> covers the screen, loads
        /// <paramref name="scene"/> under it, fades it away over
        /// <paramref name="clearTime"/> onto black, and has the player rise
        /// there as after dying.
        /// </summary>
        public static void Arrive(string scene, WhiteOut white, float clearTime, PlayerDeath.Settings settings)
        {
            DeathSequence sequence = Make(settings);
            sequence.StartCoroutine(sequence.ArriveThroughWhite(scene, white, clearTime));
        }

        /// <summary>Has <paramref name="target"/> rise out of a white circle where it stands, on black, as in the Afterworld.</summary>
        public static void Rise(Transform target, PlayerDeath.Settings settings)
        {
            DeathSequence sequence = Make(settings);
            sequence.StartCoroutine(sequence.RiseHere(target));
        }

        private static DeathSequence Make(PlayerDeath.Settings settings)
        {
            GameObject host = new GameObject("DeathSequence");
            DontDestroyOnLoad(host);

            DeathSequence sequence = host.AddComponent<DeathSequence>();
            sequence.settings = settings ?? new PlayerDeath.Settings();
            sequence.layer = FreeLayer(0);
            return sequence;
        }

        private IEnumerator ArriveThroughWhite(string scene, WhiteOut white, float clearTime)
        {
            while (white != null && !white.Covered)
            {
                yield return null;
            }

            yield return LoadAndRise(scene, () => FadeAway(white, clearTime));
        }

        private static IEnumerator FadeAway(WhiteOut white, float time)
        {
            if (white == null)
            {
                yield break;
            }

            white.Clear(time);

            while (white != null)
            {
                yield return null;
            }
        }

        private IEnumerator RiseHere(Transform target)
        {
            Take(target);
            Isolate();
            Vector3 standing = player.position;
            yield return HideBelow(standing);
            yield return RiseFrom(standing);
        }

        private IEnumerator Run(Transform dying)
        {
            // 1. Freeze, and black out all but the player.
            Take(dying);
            Isolate();

            // 2. Blood, and the fall.
            SprayBlood();
            PlayDeathAnimation();

            yield return Wait(settings.fallTime);

            // 3. Sink into the white.
            Vector3 floor = RuneCircle.FloorBelow(player.position + Vector3.up * 0.5f, player);
            Transform circle = MakeCircle(floor);

            HoldCamera();
            yield return Scale(circle, 0f, 1f, settings.circleOpenTime, true);

            Vector3 standing = player.position;
            Vector3 sunk = standing - Vector3.up * settings.sinkDepth;

            for (float clock = 0f; clock < settings.sinkTime; clock += Time.unscaledDeltaTime)
            {
                float t = clock / settings.sinkTime;
                Move(Vector3.Lerp(standing, sunk, t * t));
                yield return null;
            }

            Move(sunk);
            SetRenderers(false);

            yield return Scale(circle, 1f, 0f, settings.circleCloseTime, false);
            Destroy(circle.gameObject);

            yield return Wait(settings.blackBeforeLoad);

            // 4. Load the Afterworld under a black cover, so its first frame
            // is never seen whole.
            ScreenFade cover = ScreenFade.Create(Color.black, 1f);
            yield return LoadAndRise(settings.afterworldScene, () => RemoveCover(cover));
        }

        private static IEnumerator RemoveCover(ScreenFade cover)
        {
            Destroy(cover.gameObject);
            yield break;
        }

        /// <summary>
        /// Loads <paramref name="scene"/> under whatever covers the screen,
        /// hides its player below the floor on black, takes the cover away
        /// with <paramref name="uncover"/>, and has the player rise.
        /// </summary>
        private IEnumerator LoadAndRise(string scene, Func<IEnumerator> uncover)
        {
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Debug.LogWarning($"[DeathSequence] Can't load scene \"{scene}\": it isn't in File > Build Profiles' scene list.");
                yield return uncover();
                Destroy(gameObject);
                yield break;
            }

            LevelExit.ReleasePending();
            yield return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            yield return null;

            // What was changed in the last scene went with it.
            originalLayers.Clear();
            hiddenRenderers.Clear();
            hiddenCanvases.Clear();
            view = null;
            follow = null;
            input = null;

            PlayerMotor arrived = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

            if (arrived == null)
            {
                yield return uncover();
                Destroy(gameObject);
                yield break;
            }

            Take(arrived.transform);
            Isolate();

            Vector3 standing = player.position;
            yield return HideBelow(standing);
            yield return uncover();
            yield return RiseFrom(standing);
        }

        /// <summary>Sinks the player out of sight below <paramref name="standing"/> and frames it with the camera.</summary>
        private IEnumerator HideBelow(Vector3 standing)
        {
            Move(standing - Vector3.up * settings.sinkDepth);
            SetRenderers(false);

            follow = FindFirstObjectByType<TopDownCamera>();
            HoldCamera(standing);

            if (follow != null)
            {
                follow.SnapToTarget();
            }

            yield return null;
        }

        /// <summary>5. Opens a white circle at <paramref name="standing"/>, raises the player out of it, closes it, and hands back control.</summary>
        private IEnumerator RiseFrom(Vector3 standing)
        {
            Vector3 floor = RuneCircle.FloorBelow(standing + Vector3.up * 0.5f, player);

            yield return Wait(settings.blackAfterLoad);

            Transform circle = MakeCircle(floor);
            yield return Scale(circle, 0f, 1f, settings.circleOpenTime, true);

            SetRenderers(true);
            Vector3 sunk = player.position;

            for (float clock = 0f; clock < settings.riseTime; clock += Time.unscaledDeltaTime)
            {
                float t = clock / settings.riseTime;
                Move(Vector3.Lerp(sunk, standing, 1f - (1f - t) * (1f - t)));
                yield return null;
            }

            Move(standing);

            // The world spreads out from the circle, which closes meanwhile.
            // The black goes up the same frame the world comes back, so
            // nothing outside the circle is seen early.
            ScreenIris.Reveal(floor, settings.circleRadius, settings.revealTime);
            Release();

            // The circle is on the blackout layer, which the restored
            // camera may not draw; keep it drawn until it has closed.
            Camera camera = view;

            if (camera != null)
            {
                camera.cullingMask = viewMask | (1 << layer);
            }

            yield return Scale(circle, 1f, 0f, settings.circleCloseTime, false);
            Destroy(circle.gameObject);

            if (camera != null)
            {
                camera.cullingMask = viewMask;
            }

            Destroy(gameObject);
        }

        /// <summary>Freezes <paramref name="target"/> and moves it onto the blackout layer.</summary>
        private void Take(Transform target)
        {
            player = target;
            motor = target.GetComponent<PlayerMotor>();
            input = target.GetComponent<PlayerInputReader>();
            body = target.GetComponent<Rigidbody>();

            if (motor != null)
            {
                motor.CancelDash();
                motor.enabled = false;
            }

            if (body != null)
            {
                wasKinematic = body.isKinematic;

                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.isKinematic = true;
                }
            }

            LockInput();

            foreach (Transform part in target.GetComponentsInChildren<Transform>(true))
            {
                originalLayers.Add(new KeyValuePair<GameObject, int>(part.gameObject, part.gameObject.layer));
                part.gameObject.layer = layer;
            }
        }

        /// <summary>Has the camera draw only the blackout layer on black, and hides every canvas but the screen fades.</summary>
        private void Isolate()
        {
            view = Camera.main;

            if (view != null)
            {
                viewMask = view.cullingMask;
                viewClear = view.clearFlags;
                viewBackground = view.backgroundColor;

                view.cullingMask = 1 << layer;
                view.clearFlags = CameraClearFlags.SolidColor;
                view.backgroundColor = Color.black;
            }

            foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                bool fade = canvas.GetComponent<ScreenFade>() != null || canvas.GetComponent<WhiteOut>() != null;

                if (!fade && canvas.enabled && canvas.isRootCanvas)
                {
                    canvas.enabled = false;
                    hiddenCanvases.Add(canvas);
                }
            }
        }

        /// <summary>Undoes the blackout and hands the player back their controls.</summary>
        private void Release()
        {
            foreach (KeyValuePair<GameObject, int> entry in originalLayers)
            {
                if (entry.Key != null)
                {
                    entry.Key.layer = entry.Value;
                }
            }

            if (view != null)
            {
                view.cullingMask = viewMask;
                view.clearFlags = viewClear;
                view.backgroundColor = viewBackground;
            }

            foreach (Canvas canvas in hiddenCanvases)
            {
                if (canvas != null)
                {
                    canvas.enabled = true;
                }
            }

            if (follow != null)
            {
                follow.Target = player;
            }

            if (followAnchor != null)
            {
                Destroy(followAnchor.gameObject);
            }

            if (body != null)
            {
                body.isKinematic = wasKinematic;
            }

            if (motor != null)
            {
                motor.Teleport(player.position);
                motor.enabled = true;
            }

            if (input != null)
            {
                input.Locked = false;
                input = null;
            }
        }

        private void SprayBlood()
        {
            ScarlettBlood.Settings source = settings.blood;

            if (source == null || source.droplets <= 0)
            {
                return;
            }

            // All the way round, rather than fanned away from a blow.
            ScarlettBlood.Settings round = new ScarlettBlood.Settings
            {
                droplets = source.droplets,
                spread = 360f,
                minSpeed = source.minSpeed,
                maxSpeed = source.maxSpeed,
                drag = source.drag,
                lifetime = source.lifetime,
                size = source.size,
                stretch = source.stretch,
                outlineWidth = source.outlineWidth,
                lift = source.lift,
            };

            Material material = settings.bloodMaterial != null ? settings.bloodMaterial : FlatMaterial.DoubleSided;
            ScarlettBlood blood = ScarlettBlood.Spawn(player.position + Vector3.up * settings.bloodHeight, player.forward, round, material);

            if (blood == null)
            {
                return;
            }

            blood.gameObject.layer = layer;

            if (settings.bloodMaterial == null && blood.TryGetComponent(out MeshRenderer bloodRenderer))
            {
                FlatMaterial.Tint(bloodRenderer, settings.bloodColour, new MaterialPropertyBlock());
            }
        }

        private void PlayDeathAnimation()
        {
            if (string.IsNullOrEmpty(settings.deathState))
            {
                return;
            }

            Animator animator = player.GetComponentInChildren<Animator>();

            if (animator == null || animator.runtimeAnimatorController == null)
            {
                Debug.LogWarning("[DeathSequence] The player has no animator with a controller, so no death animation plays.", player);
                return;
            }

            int state = Animator.StringToHash(settings.deathState);

            if (!animator.HasState(0, state))
            {
                Debug.LogWarning($"[DeathSequence] The player's Animator Controller has no state called \"{settings.deathState}\" on its base layer.", animator);
                return;
            }

            animator.Play(state, 0, 0f);
        }

        /// <summary>A flat white disc of radius 1 lying on <paramref name="floor"/>, on the blackout layer, scaled to nothing.</summary>
        private Transform MakeCircle(Vector3 floor)
        {
            GameObject disc = new GameObject("DeathCircle") { layer = layer };
            disc.transform.position = floor + Vector3.up * CircleLift;
            disc.transform.localScale = Vector3.zero;

            Vector3[] vertices = new Vector3[CircleSegments + 1];
            Vector3[] normals = new Vector3[CircleSegments + 1];
            int[] triangles = new int[CircleSegments * 3];

            normals[0] = Vector3.up;

            for (int i = 0; i < CircleSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / CircleSegments;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                normals[i + 1] = Vector3.up;

                // Clockwise seen from above, so the face points up.
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % CircleSegments + 1;
                triangles[i * 3 + 2] = i + 1;
            }

            Mesh mesh = new Mesh { name = "DeathCircle", vertices = vertices, normals = normals, triangles = triangles };
            mesh.RecalculateBounds();
            disc.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer meshRenderer = disc.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            if (settings.circleMaterial != null)
            {
                meshRenderer.sharedMaterial = settings.circleMaterial;
            }
            else
            {
                meshRenderer.sharedMaterial = FlatMaterial.DoubleSided;
                FlatMaterial.Tint(meshRenderer, settings.circleColour, new MaterialPropertyBlock());
            }

            return disc.transform;
        }

        /// <summary>Grows or shrinks the circle between fractions of its full radius.</summary>
        private IEnumerator Scale(Transform circle, float from, float to, float time, bool easeOut)
        {
            for (float clock = 0f; clock < time; clock += Time.unscaledDeltaTime)
            {
                float t = clock / time;
                t = easeOut ? 1f - (1f - t) * (1f - t) : t * t;
                SetRadius(circle, Mathf.Lerp(from, to, t));
                yield return null;
            }

            SetRadius(circle, to);
        }

        private void SetRadius(Transform circle, float fraction)
        {
            float radius = Mathf.Max(0f, fraction) * settings.circleRadius;
            circle.localScale = new Vector3(radius, 1f, radius);
        }

        /// <summary>Keeps the camera where it is while the player sinks, rather than following them down.</summary>
        private void HoldCamera()
        {
            if (follow == null)
            {
                follow = FindFirstObjectByType<TopDownCamera>();
            }

            HoldCamera(player.position);
        }

        /// <summary>Has the camera frame <paramref name="point"/> instead of the player.</summary>
        private void HoldCamera(Vector3 point)
        {
            if (follow == null)
            {
                return;
            }

            if (followAnchor == null)
            {
                followAnchor = new GameObject("DeathCameraAnchor").transform;
            }

            followAnchor.position = point;
            follow.Target = followAnchor;
        }

        private void Move(Vector3 position)
        {
            if (body != null)
            {
                body.position = position;
            }

            player.position = position;
        }

        private void SetRenderers(bool visible)
        {
            if (!visible)
            {
                hiddenRenderers.Clear();

                foreach (Renderer part in player.GetComponentsInChildren<Renderer>())
                {
                    if (part.enabled)
                    {
                        part.enabled = false;
                        hiddenRenderers.Add(part);
                    }
                }

                return;
            }

            foreach (Renderer part in hiddenRenderers)
            {
                if (part != null)
                {
                    part.enabled = true;
                }
            }

            hiddenRenderers.Clear();
        }

        /// <summary>Waits <paramref name="seconds"/> of unscaled time.</summary>
        private IEnumerator Wait(float seconds)
        {
            for (float clock = 0f; clock < seconds; clock += Time.unscaledDeltaTime)
            {
                yield return null;
            }
        }

        private void LockInput()
        {
            if (input != null)
            {
                input.Locked = true;
            }
        }

        private void LateUpdate()
        {
            // Something else, such as a boss intro finishing, may hand the
            // controls back mid-death.
            LockInput();
        }

        /// <summary>A layer no one has named, for the blackout; <paramref name="fallback"/> if every one is taken.</summary>
        private static int FreeLayer(int fallback)
        {
            for (int i = 31; i >= 8; i--)
            {
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                {
                    return i;
                }
            }

            return fallback;
        }
    }
}
