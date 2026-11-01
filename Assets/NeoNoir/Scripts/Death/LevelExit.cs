using System.Collections;
using LightAndShadow.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LightAndShadow
{
    /// <summary>
    /// The way on to the next level once the boss is beaten: the moment the
    /// boss dies, <see cref="nextScene"/> starts loading in the background;
    /// <see cref="delay"/> seconds later a white circle grows under the
    /// player, the room goes dark round them, the Chroma earned flies into
    /// them, and the next level is switched to under the dark. There the
    /// player is standing exactly where they were in this level, facing the
    /// same way, and the circle shrinks away as the room comes back.
    /// </summary>
    /// <remarks>
    /// Chroma is earned here too: <see cref="chromaForReaching"/> when the
    /// boss fight is triggered, <see cref="chromaForBeating"/> when the boss
    /// dies, each once ever (see <see cref="ChromaRewards"/>).
    ///
    /// The respawn save moves to the start of the next level's minion part
    /// the moment the boss dies (see <see cref="RespawnSave"/>), so dying
    /// from then on goes back to that level, not this one.
    ///
    /// Without an <see cref="exitAnchor"/>, the player keeps the same world
    /// coordinates, so the next level must line up with this one. With one,
    /// the player keeps their place relative to it instead: put the anchor
    /// on the platform the level ends on, and a <see cref="LevelArrival"/>
    /// on the matching spot of the matching platform in the next level.
    ///
    /// Unity holds every other scene load back until a background load
    /// is switched to, so anything else that loads a scene (the death
    /// sequence, doors, return circles) calls <see cref="ReleasePending"/>
    /// first. If the player dies while waiting, the next level is never
    /// switched to under them; the death sequence takes over.
    /// </remarks>
    public sealed class LevelExit : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("A Boss Intro, Puppeteer Intro or Chromatist Intro (or the object holding one). When its boss dies, the next level follows.")]
        private MonoBehaviour bossFight;

        [SerializeField]
        [Tooltip("The scene to go on to. It must be in File > Build Profiles' scene list.")]
        private string nextScene = "L2";

        [SerializeField]
        [Tooltip("Optional. A point on the platform this level ends on. The player keeps their place relative to it, measured from the Level Arrival in the next level. Empty: the same world coordinates.")]
        private Transform exitAnchor;

        [SerializeField]
        [Tooltip("Seconds after the boss dies before the circle grows under the player.")]
        [Min(0f)]
        private float delay = 3f;

        [Header("Chroma")]
        [SerializeField]
        [Tooltip("Chroma earned when the boss fight is triggered. Once ever.")]
        [Min(0)]
        private int chromaForReaching = 1;

        [SerializeField]
        [Tooltip("Chroma earned for beating the boss. Once ever.")]
        [Min(0)]
        private int chromaForBeating = 3;

        [SerializeField] private Transition transition = new Transition();

        /// <summary>The look and timing of the circle and the dark between levels.</summary>
        [System.Serializable]
        private sealed class Transition
        {
            [Tooltip("Radius the circle grows to under the player.")]
            [Min(0.1f)] public float circleRadius = 1.6f;

            [Tooltip("Seconds the circle takes to grow.")]
            [Min(0.01f)] public float growTime = 0.5f;

            [Tooltip("Seconds the room takes to go dark round the player and circle.")]
            [Min(0.01f)] public float darkenTime = 0.6f;

            [Tooltip("Seconds of dark before the switch, after any Chroma has flown in.")]
            [Min(0f)] public float holdDark = 0.3f;

            [Tooltip("Seconds of dark in the next level before it comes back.")]
            [Min(0f)] public float holdArrived = 0.2f;

            [Tooltip("Seconds the room takes to come back as the circle shrinks away.")]
            [Min(0.01f)] public float revealTime = 0.9f;
        }

        private BossIntro bossIntro;
        private PuppeteerIntro puppeteerIntro;
        private ChromatistIntro chromatistIntro;
        private bool leaving;

        private static Handoff pending;

        /// <summary>
        /// Gives up a level exit still loading in the background, so another
        /// scene load is not held back behind it. Call before loading a scene.
        /// </summary>
        public static void ReleasePending()
        {
            if (pending != null)
            {
                pending.Abandon();
            }
        }

        // With domain reload off, statics carry over between play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            pending = null;
        }

        private void Awake()
        {
            // Dragging in the object picks its first script, which may be
            // something beside the intro (L2's Summoning Altar), so the
            // intro is looked for on the same object too.
            GameObject host = bossFight != null ? bossFight.gameObject : null;
            bossIntro = bossFight as BossIntro ?? (host != null ? host.GetComponent<BossIntro>() : null);
            puppeteerIntro = bossFight as PuppeteerIntro ?? (host != null ? host.GetComponent<PuppeteerIntro>() : null);
            chromatistIntro = bossFight as ChromatistIntro ?? (host != null ? host.GetComponent<ChromatistIntro>() : null);

            if (bossIntro == null && puppeteerIntro == null && chromatistIntro == null)
            {
                Debug.LogWarning($"[LevelExit] {name} needs a Boss Intro, Puppeteer Intro or Chromatist Intro, or the next level never loads.", this);
            }
        }

        private void OnEnable()
        {
            if (bossIntro != null) { bossIntro.Defeated += OnDefeated; bossIntro.Triggered += OnTriggered; }
            if (puppeteerIntro != null) { puppeteerIntro.Defeated += OnDefeated; puppeteerIntro.Triggered += OnTriggered; }
            if (chromatistIntro != null) { chromatistIntro.Defeated += OnDefeated; chromatistIntro.Triggered += OnTriggered; }
        }

        private void OnDisable()
        {
            if (bossIntro != null) { bossIntro.Defeated -= OnDefeated; bossIntro.Triggered -= OnTriggered; }
            if (puppeteerIntro != null) { puppeteerIntro.Defeated -= OnDefeated; puppeteerIntro.Triggered -= OnTriggered; }
            if (chromatistIntro != null) { chromatistIntro.Defeated -= OnDefeated; chromatistIntro.Triggered -= OnTriggered; }
        }

        private void OnDefeated(BossIntro _) => Leave();
        private void OnDefeated(PuppeteerIntro _) => Leave();
        private void OnDefeated(ChromatistIntro _) => Leave();

        private void OnTriggered(BossIntro _) => Reached();
        private void OnTriggered(PuppeteerIntro _) => Reached();
        private void OnTriggered(ChromatistIntro _) => Reached();

        /// <summary>The boss fight is reached: its Chroma, the first time, flown into the player.</summary>
        private void Reached()
        {
            int earned = ChromaRewards.Give(gameObject.scene.name + "/reach", chromaForReaching);
            PlayerMotor player = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

            if (earned > 0 && player != null)
            {
                ChromaGain.Play(player.transform, earned, false);
            }
        }

        private void Leave()
        {
            if (leaving)
            {
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(nextScene))
            {
                Debug.LogWarning($"[LevelExit] Can't load scene \"{nextScene}\": it isn't in File > Build Profiles' scene list.", this);
                return;
            }

            PlayerMotor player = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

            if (player == null)
            {
                return;
            }

            leaving = true;
            Debug.Log($"[LevelExit] Boss beaten: loading {nextScene} in the background, switching in {delay} s.", this);
            RespawnSave.Set(nextScene, false);
            int earned = ChromaRewards.Give(gameObject.scene.name + "/beat", chromaForBeating);
            pending = Handoff.Begin(player, nextScene, exitAnchor, delay, earned, transition);
        }

        /// <summary>
        /// Outlives the load: loads the next level in the background, follows
        /// the player until the switch, then puts the new scene's player on
        /// the last spot, before the new scene draws its first frame.
        /// </summary>
        private sealed class Handoff : MonoBehaviour
        {
            private PlayerMotor oldPlayer;
            private PlayerHealth oldHealth;
            private Transform anchor;
            private bool anchored;
            private string scene;
            private AsyncOperation load;
            private bool abandoned;
            private Vector3 position;
            private Vector3 forward;
            private int earned;
            private Transition look;
            private TransitionCircle circle;

            public static Handoff Begin(PlayerMotor player, string scene, Transform anchor, float delay, int earned, Transition look)
            {
                GameObject host = new GameObject("LevelExit Handoff");
                DontDestroyOnLoad(host);

                Handoff handoff = host.AddComponent<Handoff>();
                handoff.oldPlayer = player;
                handoff.oldHealth = player.GetComponent<PlayerHealth>();
                handoff.scene = scene;
                handoff.anchor = anchor;
                handoff.anchored = anchor != null;
                handoff.earned = earned;
                handoff.look = look;
                handoff.Track();

                SceneManager.sceneLoaded += handoff.OnSceneLoaded;
                handoff.load = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
                handoff.load.allowSceneActivation = false;
                handoff.StartCoroutine(handoff.SwitchLater(delay));
                return handoff;
            }

            private IEnumerator SwitchLater(float delay)
            {
                for (float clock = 0f; clock < delay; clock += Time.deltaTime)
                {
                    yield return null;
                }

                // Dead, or gone: the death sequence has the screen, and will
                // release the load when it needs to load its own scene.
                if (!abandoned && (oldPlayer == null || (oldHealth != null && oldHealth.IsDead)))
                {
                    Debug.Log($"[LevelExit] Not switching to {scene} yet: the player is {(oldPlayer == null ? "gone" : "dead")}.");
                }

                while (!abandoned && (oldPlayer == null || (oldHealth != null && oldHealth.IsDead)))
                {
                    yield return null;
                }

                if (abandoned)
                {
                    yield break;
                }

                yield return Darken();

                Debug.Log($"[LevelExit] Switching to {scene} (background load at {load.progress:P0}).");
                Track();
                load.allowSceneActivation = true;
            }

            /// <summary>The circle grows under the player, the room goes dark round them, and the Chroma earned flies in.</summary>
            private IEnumerator Darken()
            {
                PlayerInputReader input = oldPlayer.GetComponent<PlayerInputReader>();

                if (input != null)
                {
                    input.Locked = true;
                }

                NeoNoirDarkness.Keep(oldPlayer.gameObject);
                circle = TransitionCircle.Create(transform);
                circle.Player = oldPlayer.transform;

                for (float clock = 0f; clock < look.growTime; clock += Time.deltaTime)
                {
                    float t = clock / look.growTime;
                    circle.Radius = look.circleRadius * (1f - (1f - t) * (1f - t));
                    yield return null;
                }

                circle.Radius = look.circleRadius;

                for (float clock = 0f; clock < look.darkenTime; clock += Time.deltaTime)
                {
                    NeoNoirDarkness.Amount = clock / look.darkenTime;
                    yield return null;
                }

                NeoNoirDarkness.Amount = 1f;

                if (earned > 0 && oldPlayer != null)
                {
                    ChromaGain gain = ChromaGain.Play(oldPlayer.transform, earned, true);

                    while (gain != null && !gain.Done)
                    {
                        yield return null;
                    }
                }

                for (float clock = 0f; clock < look.holdDark; clock += Time.deltaTime)
                {
                    yield return null;
                }
            }

            /// <summary>In the next level: a moment of dark, then the room comes back as the circle shrinks away.</summary>
            private IEnumerator Reveal(PlayerMotor player)
            {
                PlayerInputReader input = player != null ? player.GetComponent<PlayerInputReader>() : null;

                if (input != null)
                {
                    input.Locked = true;
                }

                for (float clock = 0f; clock < look.holdArrived; clock += Time.deltaTime)
                {
                    yield return null;
                }

                for (float clock = 0f; clock < look.revealTime; clock += Time.deltaTime)
                {
                    float t = clock / look.revealTime;
                    float eased = t * t * (3f - 2f * t);
                    NeoNoirDarkness.Amount = 1f - eased;
                    circle.Radius = look.circleRadius * (1f - eased);
                    yield return null;
                }

                NeoNoirDarkness.Amount = 0f;

                if (input != null)
                {
                    input.Locked = false;
                }

                if (player != null)
                {
                    NeoNoirDarkness.Keep(player.gameObject, false);
                }

                Destroy(gameObject);
            }

            /// <summary>Lets the load finish without placing anyone, so the next load can go ahead.</summary>
            public void Abandon()
            {
                if (abandoned)
                {
                    return;
                }

                abandoned = true;
                Debug.Log($"[LevelExit] Another scene is loading, so {scene} is let go without placing the player.");
                StopAllCoroutines();

                if (pending == this)
                {
                    pending = null;
                }

                load.allowSceneActivation = true;
            }

            // The player still moves while the next level loads in the
            // background, so the spot is taken right up to the switch.
            private void Update()
            {
                if (!abandoned)
                {
                    Track();
                }
            }

            private void Track()
            {
                if (oldPlayer == null)
                {
                    return;
                }

                position = oldPlayer.transform.position;
                forward = oldPlayer.transform.forward;

                // Kept relative to the anchor, while it is still there.
                if (anchor != null)
                {
                    Quaternion undo = Quaternion.Inverse(anchor.rotation);
                    position = undo * (position - anchor.position);
                    forward = undo * forward;
                }
            }

            // Runs after the new scene's Awake and OnEnable, before any Start
            // or the first frame drawn.
            private void OnSceneLoaded(Scene loaded, LoadSceneMode mode)
            {
                if (loaded.name != scene)
                {
                    return;
                }

                SceneManager.sceneLoaded -= OnSceneLoaded;
                PlayerMotor player = abandoned ? null : FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

                if (player != null)
                {
                    Place(player);
                }

                if (player == null || circle == null)
                {
                    Destroy(gameObject);
                    return;
                }

                // Still dark: the new player is kept lit, on the circle, from
                // the first frame drawn.
                NeoNoirDarkness.Keep(player.gameObject);
                circle.Player = player.transform;
                StartCoroutine(Reveal(player));
            }

            private void Place(PlayerMotor player)
            {
                if (anchored)
                {
                    LevelArrival arrival = FindFirstObjectByType<LevelArrival>(FindObjectsInactive.Exclude);

                    if (arrival != null)
                    {
                        Transform point = arrival.transform;
                        position = point.position + point.rotation * position;
                        forward = point.rotation * forward;
                    }
                    else
                    {
                        Debug.LogWarning($"[LevelExit] {scene} has no Level Arrival to match the exit anchor, so the player is placed relative to the world origin instead.");
                    }
                }

                player.Teleport(position, forward);
                Debug.Log($"[LevelExit] Arrived in {scene}; player placed at {position}.");

                TopDownCamera view = FindFirstObjectByType<TopDownCamera>();

                if (view != null)
                {
                    view.SnapToTarget();
                }
            }

            private void OnDestroy()
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;

                // Let go part way through, by another load: the dark goes with it.
                if (circle != null)
                {
                    NeoNoirDarkness.Amount = 0f;
                }

                if (pending == this)
                {
                    pending = null;
                }
            }
        }
    }
}
