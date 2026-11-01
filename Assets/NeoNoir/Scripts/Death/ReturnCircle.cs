using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A white plane on a level's floor that takes the player back to the
    /// Afterworld to spend Chroma. Coming back through the
    /// <see cref="AfterworldDoor"/> puts them back on it.
    /// </summary>
    /// <remarks>
    /// Goes on the plane. Come close and press E: white swells out from the
    /// plane until it fills the screen (<see cref="WhiteOut"/>), the
    /// Afterworld loads under it, the white fades away onto black, and the
    /// player rises out of a white circle, as after dying (the circle and
    /// rise timings are the player's <see cref="PlayerDeath"/> settings).
    ///
    /// Using it saves this plane as the respawn point (see
    /// <see cref="RespawnSave"/>), so dying afterwards also comes back here,
    /// until a boss fight starts and saves its own spawn.
    ///
    /// Needs the scene's <see cref="LevelCheckpoints"/>, which does the
    /// placing on the way back. Each one in a scene needs its own
    /// <see cref="Id"/>; the default is the object's name.
    ///
    /// On the way back, everything in <see cref="skippedMinions"/> is switched
    /// off, for minions between the level start and one placed before a boss.
    /// Until every one of them is dead or destroyed, the plane can't be used
    /// and shows no prompt.
    /// </remarks>
    public sealed class ReturnCircle : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Tells this one apart from the others in the scene. Empty uses the object's name.")]
        private string id;

        [SerializeField]
        [Tooltip("Where the player stands on coming back. Empty: the middle of the plane.")]
        private Transform spawnPoint;

        [SerializeField]
        [Tooltip("Switched off when coming back here, so they are not fought again. The plane can't be used until all of them are dead or destroyed.")]
        private List<GameObject> skippedMinions = new List<GameObject>();

        [Header("Interaction")]
        [SerializeField]
        [Tooltip("How close (in metres, ignoring height) the player must come to the middle of the plane to use it.")]
        [Min(0.1f)]
        private float radius = 2f;

        [SerializeField] private string promptText = "E  Return";

        [SerializeField]
        [Tooltip("Where the prompt shows, relative to the middle of the plane.")]
        private Vector3 promptOffset = new Vector3(0f, 2f, 0f);

        [Header("Going")]
        [SerializeField] private string afterworldScene = "Afterworld";

        [SerializeField]
        [Tooltip("Seconds the white takes to swell out from the plane and fill the screen.")]
        [Min(0.01f)]
        private float fillTime = 0.8f;

        [SerializeField]
        [Tooltip("Seconds the white takes to fade away in the Afterworld, before the player rises.")]
        [Min(0f)]
        private float fadeFromWhiteTime = 1f;

        private Transform player;
        private PlayerInputReader input;
        private bool busy;
        private bool showingPrompt;

        public string Id => string.IsNullOrEmpty(id) ? name : id;

        public Vector3 SpawnPosition => spawnPoint != null ? spawnPoint.position : transform.position;

        public IReadOnlyList<GameObject> SkippedMinions => skippedMinions;

        /// <summary>The circle in the loaded scenes with <paramref name="circleId"/>, or null.</summary>
        public static ReturnCircle Find(string circleId)
        {
            if (string.IsNullOrEmpty(circleId))
            {
                return null;
            }

            foreach (ReturnCircle circle in FindObjectsByType<ReturnCircle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (circle.Id == circleId)
                {
                    return circle;
                }
            }

            return null;
        }

        private void OnDisable()
        {
            HidePrompt();
        }

        private void Update()
        {
            if (busy)
            {
                return;
            }

            if (!FindPlayer() || DialogueBox.Instance.IsOpen || PlanarDistance() > radius || !MinionsCleared)
            {
                HidePrompt();
                return;
            }

            showingPrompt = true;
            DialogueBox.Instance.ShowPrompt(transform.position + promptOffset, promptText);

            if (input != null && input.InteractPressed)
            {
                Use();
            }
        }

        /// <summary>Saves this circle and goes to the Afterworld, as pressing E in it does.</summary>
        public void Use()
        {
            if (busy || !MinionsCleared)
            {
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(afterworldScene))
            {
                Debug.LogWarning($"[ReturnCircle] Can't load scene \"{afterworldScene}\": it isn't in File > Build Profiles' scene list.", this);
                return;
            }

            busy = true;
            HidePrompt();

            if (input != null)
            {
                input.Locked = true;
            }

            string scene = gameObject.scene.name;
            bool atBoss = RespawnSave.Scene == scene && RespawnSave.AtBoss;
            RespawnSave.SetCircle(scene, atBoss, Id);

            // The white starts as big as the plane and outlives this scene.
            Bounds plane = PlaneBounds;
            float size = Mathf.Max(plane.extents.x, plane.extents.z);
            WhiteOut white = WhiteOut.Cover(plane.center, size, fillTime);
            DeathSequence.Arrive(afterworldScene, white, fadeFromWhiteTime, PlayerDeath.SettingsOf(player));
        }

        /// <summary>
        /// True once every object in <see cref="skippedMinions"/> is
        /// destroyed, switched off by a respawn, or has all its enemies dead.
        /// </summary>
        public bool MinionsCleared
        {
            get
            {
                foreach (GameObject minion in skippedMinions)
                {
                    if (minion == null || LevelCheckpoints.WasSkipped(minion))
                    {
                        continue;
                    }

                    foreach (EnemyHealth health in minion.GetComponentsInChildren<EnemyHealth>(true))
                    {
                        if (!health.IsDead)
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
        }

        /// <summary>World bounds of the plane's renderers, or a point at this object without any.</summary>
        private Bounds PlaneBounds
        {
            get
            {
                Bounds bounds = new Bounds(transform.position, Vector3.zero);
                bool any = false;

                foreach (Renderer target in GetComponentsInChildren<Renderer>())
                {
                    if (any)
                    {
                        bounds.Encapsulate(target.bounds);
                    }
                    else
                    {
                        bounds = target.bounds;
                        any = true;
                    }
                }

                return bounds;
            }
        }

        private bool FindPlayer()
        {
            if (player == null)
            {
                PlayerMotor motor = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

                if (motor == null)
                {
                    return false;
                }

                player = motor.transform;
                input = motor.GetComponent<PlayerInputReader>();
            }

            return true;
        }

        private float PlanarDistance()
        {
            Vector3 offset = player.position - transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        private void HidePrompt()
        {
            if (!showingPrompt)
            {
                return;
            }

            showingPrompt = false;

            // The box may already be gone when the scene is unloading.
            DialogueBox box = FindFirstObjectByType<DialogueBox>(FindObjectsInactive.Include);

            if (box != null)
            {
                box.HidePrompt();
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(transform.position, radius);

            if (spawnPoint != null)
            {
                Gizmos.DrawLine(transform.position, spawnPoint.position);
            }
        }
    }
}
