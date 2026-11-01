using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LightAndShadow
{
    /// <summary>
    /// The door out of the Afterworld: walk up and press E, and white swells
    /// out of the door to fill the screen (<see cref="WhiteOut"/>), the level saved in <see cref="RespawnSave"/> loads,
    /// and the white fades away with the player at its respawn point (the
    /// level's door, a return plane, or the boss spawn).
    /// </summary>
    /// <remarks>
    /// With nothing saved yet, it goes to <see cref="firstLevel"/> as a fresh
    /// start instead, where the level's opening plays (see
    /// <see cref="LevelCheckpoints"/>).
    /// </remarks>
    public sealed class AfterworldDoor : MonoBehaviour
    {
        [Header("Interaction")]
        [SerializeField]
        [Tooltip("Where the prompt shows and distance is measured from. Empty uses this object.")]
        private Transform promptPoint;

        [SerializeField]
        [Tooltip("How close (in metres, ignoring height) the player must be to interact.")]
        [Min(0f)]
        private float interactRadius = 2.5f;

        [SerializeField] private string promptText = "E  Return";

        [SerializeField]
        [Tooltip("Where the prompt shows, relative to the prompt point.")]
        private Vector3 promptOffset = new Vector3(0f, 2.5f, 0f);

        [Header("Going back")]
        [SerializeField]
        [Tooltip("Scene gone to when nothing has been saved yet.")]
        private string firstLevel = "L1";

        [SerializeField]
        [Tooltip("Seconds the white takes to swell out of the door and fill the screen.")]
        [Min(0.01f)]
        private float fillTime = 0.8f;

        [SerializeField]
        [Tooltip("Seconds the screen stays white once the level has loaded.")]
        [Min(0f)]
        private float whiteHold = 0.3f;

        [SerializeField]
        [Tooltip("Seconds the white takes to fade away at the respawn point.")]
        [Min(0f)]
        private float fadeFromWhiteTime = 1f;

        private Transform player;
        private PlayerInputReader input;
        private bool busy;
        private bool showingPrompt;

        private Transform Point => promptPoint != null ? promptPoint : transform;

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

            if (!FindPlayer() || DialogueBox.Instance.IsOpen || PlanarDistance() > interactRadius)
            {
                HidePrompt();
                return;
            }

            showingPrompt = true;
            DialogueBox.Instance.ShowPrompt(Point.position + promptOffset, promptText);

            if (input != null && input.InteractPressed)
            {
                Interact();
            }
        }

        /// <summary>Goes back to the saved level, as pressing E in range does.</summary>
        public void Interact()
        {
            if (busy)
            {
                return;
            }

            bool saved = RespawnSave.HasSave;
            string scene = saved ? RespawnSave.Scene : firstLevel;

            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Debug.LogWarning($"[AfterworldDoor] Can't load scene \"{scene}\": it isn't in File > Build Profiles' scene list.", this);
                return;
            }

            busy = true;
            HidePrompt();

            if (input != null)
            {
                input.Locked = true;
            }

            // White swells out of the door to fill the screen, and outlives this scene.
            Bounds door = DoorBounds;
            float size = Mathf.Max(door.extents.x, door.extents.y, door.extents.z);
            WhiteOut white = WhiteOut.Cover(door.center, size, fillTime);
            white.StartCoroutine(GoBack(white, scene, saved, whiteHold, fadeFromWhiteTime));
        }

        private static IEnumerator GoBack(WhiteOut white, string scene, bool respawn, float hold, float fadeOut)
        {
            while (!white.Covered)
            {
                yield return null;
            }

            if (respawn)
            {
                RespawnSave.BeginRespawn();
            }

            LevelExit.ReleasePending();
            yield return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);

            // The level's player and camera settle in at the spawn under the white.
            yield return null;
            yield return null;

            for (float clock = 0f; clock < hold; clock += Time.unscaledDeltaTime)
            {
                yield return null;
            }

            white.Clear(fadeOut);
        }

        /// <summary>World bounds of the door's renderers, or a point at the prompt point without any.</summary>
        private Bounds DoorBounds
        {
            get
            {
                Bounds bounds = new Bounds(Point.position, Vector3.zero);
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
            Vector3 offset = player.position - Point.position;
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

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(Point.position, interactRadius);
        }
    }
}
