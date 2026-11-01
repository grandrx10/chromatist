using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Holds an enemy's body a little off the ground, as if hung from
    /// strings, with an optional gentle bob. Height only: it never touches
    /// how the enemy moves, turns or attacks.
    /// </summary>
    /// <remarks>
    /// Lifts the body <see cref="hover"/> above where it would otherwise be,
    /// and bobs it up from there by at most <see cref="bob"/>, never sinking
    /// below. Keep both low, or blows pass under it.
    ///
    /// Never moves the root, so its Rigidbody and colliders stay where they
    /// are. What it lifts is the Enemy's Model if that is a separate child,
    /// otherwise every child of the root -- the meshes and the skeleton.
    ///
    /// Animation stays in charge, and the lift can never build up: each frame
    /// the lift is taken back off before animation runs and put back on
    /// after it, so the body always hangs exactly that far above wherever
    /// the animation puts it.
    ///
    /// Goes on the enemy's root, beside <see cref="Enemy"/>. Runs after
    /// animation.
    /// </remarks>
    [RequireComponent(typeof(Enemy))]
    public sealed class PuppetFloat : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("How far off the ground the body hangs, in world units.")]
        [Min(0f)]
        private float hover = 0.5f;

        [SerializeField]
        [Tooltip("How far it bobs up and down on top of that, in world units. 0 holds it still.")]
        [Min(0f)]
        private float bob = 0.15f;

        [SerializeField]
        [Tooltip("Bobs up and down this many times a second.")]
        [Min(0f)]
        private float rate = 1f;

        private sealed class Part
        {
            public Transform transform;

            // The lift added last frame, in the parent's space, taken back
            // off before animation runs.
            public Vector3 added;
        }

        private readonly List<Part> parts = new List<Part>();

        private Enemy enemy;
        private float phase;

        private void Awake()
        {
            enemy = GetComponent<Enemy>();
        }

        private void OnEnable()
        {
            parts.Clear();
            Transform model = enemy.Model;

            if (model != transform)
            {
                Add(model);
            }
            else
            {
                foreach (Transform child in transform)
                {
                    Add(child);
                }
            }
        }

        private void Add(Transform part)
        {
            parts.Add(new Part { transform = part });
        }

        private void OnDisable()
        {
            TakeBack();
            parts.Clear();
        }

        // Before animation, which runs after every Update: the parts go back
        // to exactly where they were before last frame's lift, so the lift
        // can never build up, whatever the animation does or does not move.
        private void Update()
        {
            TakeBack();
        }

        private void LateUpdate()
        {
            phase += rate * Time.deltaTime;

            // Up from the hover and back, never below it.
            float rise = 0.5f - 0.5f * Mathf.Cos(phase * 2f * Mathf.PI);
            float lift = hover + bob * rise;

            foreach (Part part in parts)
            {
                Transform piece = part.transform;

                if (piece == null)
                {
                    continue;
                }

                // In world units, whatever the parent's scale.
                Transform parent = piece.parent;
                part.added = parent != null ? parent.InverseTransformVector(Vector3.up * lift) : Vector3.up * lift;
                piece.localPosition += part.added;
            }
        }

        private void TakeBack()
        {
            foreach (Part part in parts)
            {
                if (part.transform != null)
                {
                    part.transform.localPosition -= part.added;
                }

                part.added = Vector3.zero;
            }
        }
    }
}
