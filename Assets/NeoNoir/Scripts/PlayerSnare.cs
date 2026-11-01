using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Holds the player fast while strings have them: no moving, dashing,
    /// attacking or abilities, for <see cref="duration"/> seconds.
    /// </summary>
    /// <remarks>
    /// Being snared again while snared starts the timer over,
    /// and the strings that held them before are let go, through the release
    /// callback given to <see cref="Snare"/>, for the new ones to take over.
    ///
    /// Controls are taken through <see cref="PlayerInputReader.Locked"/>,
    /// and the body is held still against anything that would slide it.
    ///
    /// Added to the player by whatever first snares them (see
    /// <see cref="For"/>), so it needs no setup. Put it on the player by hand
    /// to choose <see cref="snarePoints"/>, the bones strings tie on to;
    /// left empty, it looks for the Raven rig's limbs by name.
    /// </remarks>
    public sealed class PlayerSnare : MonoBehaviour
    {
        private static readonly string[] DefaultPointNames =
        {
            "Forearm L", "Forearm R", "Foot L", "Foot R", "Leg L", "Leg R", "Arm L", "Arm R",
        };

        [SerializeField]
        [Tooltip("Bones the strings tie on to. Empty finds the Raven rig's arms and legs by name.")]
        private Transform[] snarePoints = new Transform[0];

        [SerializeField]
        [Tooltip("Seconds a snare holds the player.")]
        [Min(0f)]
        private float duration = 1f;

        private readonly List<Transform> points = new List<Transform>();
        private readonly HashSet<object> immunities = new HashSet<object>();

        private PlayerInputReader input;
        private PlayerMotor motor;
        private PlayerHealth health;
        private Rigidbody body;

        private Action released;
        private bool lockedInput;
        private float elapsed;

        /// <summary>True while the player is held.</summary>
        public bool IsSnared { get; private set; }

        /// <summary>
        /// True while something, such as a dash that cuts through strings,
        /// has said strings cannot take the player. See <see cref="AddImmunity"/>.
        /// </summary>
        public bool IsImmune => immunities.Count > 0;

        /// <summary>Bones strings tie on to. Never empty: the player's own root at worst.</summary>
        public IReadOnlyList<Transform> Points => points;

        /// <summary>How far through the snare the player is, 0 to 1. 0 when not snared.</summary>
        public float Progress => IsSnared ? (duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f) : 0f;

        /// <summary>The snare on <paramref name="player"/>'s object, added if it has none.</summary>
        public static PlayerSnare For(Component player)
        {
            PlayerSnare snare = player.GetComponentInParent<PlayerSnare>();

            if (snare == null)
            {
                PlayerMotor motor = player.GetComponentInParent<PlayerMotor>();
                GameObject host = motor != null ? motor.gameObject : player.gameObject;
                snare = host.AddComponent<PlayerSnare>();
            }

            return snare;
        }

        /// <summary>From now until <see cref="RemoveImmunity"/>, strings cannot take the player.</summary>
        public void AddImmunity(object source)
        {
            immunities.Add(source);
        }

        public void RemoveImmunity(object source)
        {
            immunities.Remove(source);
        }

        private void Awake()
        {
            input = GetComponent<PlayerInputReader>();
            motor = GetComponent<PlayerMotor>();
            health = GetComponent<PlayerHealth>();
            body = GetComponent<Rigidbody>();
            FindPoints();
        }

        private void OnEnable()
        {
            if (health != null)
            {
                health.Died += OnDied;
            }
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.Died -= OnDied;
            }

            Free();
        }

        private void FindPoints()
        {
            points.Clear();

            foreach (Transform point in snarePoints)
            {
                if (point != null)
                {
                    points.Add(point);
                }
            }

            if (points.Count == 0)
            {
                foreach (Transform part in GetComponentsInChildren<Transform>())
                {
                    if (Array.IndexOf(DefaultPointNames, part.name) >= 0)
                    {
                        points.Add(part);
                    }
                }
            }

            if (points.Count == 0)
            {
                points.Add(transform);
            }
        }

        /// <summary>
        /// Holds the player, starting the snare over if already held.
        /// <paramref name="onReleased"/> is called once, when this snare ends:
        /// worn off, or taken over by another snare.
        /// </summary>
        public void Snare(Action onReleased)
        {
            Action previous = released;
            released = onReleased;
            elapsed = 0f;

            if (!IsSnared)
            {
                IsSnared = true;

                if (input != null && !input.Locked)
                {
                    input.Locked = true;
                    lockedInput = true;
                }

                if (motor != null)
                {
                    motor.CancelDash();
                }
            }

            HoldStill();

            // Last, so strings let go only once the new ones have the player.
            previous?.Invoke();
        }

        private void OnDied(PlayerHealth died)
        {
            Free();
        }

        private void Update()
        {
            if (!IsSnared)
            {
                return;
            }

            elapsed += Time.deltaTime;

            if (elapsed >= duration)
            {
                Free();
            }
        }

        private void FixedUpdate()
        {
            if (IsSnared)
            {
                HoldStill();
            }
        }

        /// <summary>Stops the body sliding, leaving gravity its say.</summary>
        private void HoldStill()
        {
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = new Vector3(0f, Mathf.Min(0f, body.linearVelocity.y), 0f);
            }
        }

        /// <summary>Ends the snare, if there is one, and lets go of its strings.</summary>
        private void Free()
        {
            if (!IsSnared)
            {
                return;
            }

            IsSnared = false;

            if (lockedInput && input != null)
            {
                input.Locked = false;
            }

            lockedInput = false;

            Action letGo = released;
            released = null;
            letGo?.Invoke();
        }
    }
}
