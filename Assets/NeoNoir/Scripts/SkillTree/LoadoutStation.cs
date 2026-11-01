using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LightAndShadow
{
    /// <summary>
    /// A circle in the Afterworld where the player picks their setup. Inside
    /// it, every owned blade appears ahead of the player: the primary up top
    /// in the middle, the secondaries in a line below it in rotation order.
    /// </summary>
    /// <remarks>
    /// Left click and drag a blade onto another to swap the two; let go
    /// anywhere else and it goes back. The order is saved to
    /// <see cref="ChosenLoadout"/> at once. The blades fade in on stepping
    /// into the circle and out on leaving it.
    ///
    /// "Ahead" is away from the camera, so the blades stand beyond the
    /// player on screen. They are laid out from the middle of the circle,
    /// not from the player, so they hold still while the player moves.
    ///
    /// The blades are plain copies of each blade's prefab,
    /// <see cref="Blade.Seized"/> so they never act. They are picked by how
    /// near they are to the pointer on screen, so they need no colliders.
    /// </remarks>
    public sealed class LoadoutStation : AfterworldCircle
    {
        [Header("Layout")]
        [SerializeField]
        [Tooltip("How far ahead of the middle of the circle the line of blades stands, away from the camera.")]
        private float distance = 1.8f;

        [SerializeField]
        [Tooltip("Height of the line of secondaries above the floor.")]
        private float height = 1f;

        [SerializeField]
        [Tooltip("How far above the line the primary sits.")]
        private float primaryRaise = 1.3f;

        [SerializeField]
        [Tooltip("Space between neighbouring secondaries.")]
        [Min(0.1f)]
        private float spacing = 1f;

        [SerializeField]
        [Tooltip("How far each blade bobs.")]
        [Min(0f)]
        private float bob = 0.06f;

        [Header("Feel")]
        [SerializeField]
        [Tooltip("Seconds the blades take to fade in and out.")]
        [Min(0.01f)]
        private float fadeTime = 0.35f;

        [SerializeField]
        [Tooltip("How quickly blades slide to their places. Higher is snappier.")]
        [Min(0.1f)]
        private float settle = 12f;

        [SerializeField]
        [Tooltip("How near the pointer must be to a blade on screen to pick it up, in pixels at 1080p.")]
        [Min(1f)]
        private float pickRadius = 80f;

        [SerializeField]
        [Tooltip("How much bigger the blade being dragged, and the one it would swap with, are drawn.")]
        [Min(1f)]
        private float highlightScale = 1.2f;

        private sealed class Shown
        {
            public BladeDefinition definition;
            public Blade blade;
            public Vector3 baseScale;
            public float scale = 1f;
            public float phase;
        }

        private readonly List<Shown> shown = new List<Shown>();
        private float visibility;
        private int dragging = -1;
        private int hovered = -1;
        private Vector3 dragPoint;

        protected override void Awake()
        {
            base.Awake();
            BladeUnlocks.BladeAcquired += OnBladeAcquired;
        }

        private void Start()
        {
            Rebuild();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            BladeUnlocks.BladeAcquired -= OnBladeAcquired;
        }

        private void OnBladeAcquired(BladeDefinition blade)
        {
            Rebuild();
        }

        protected override void Entered()
        {
            // Bought something since: show it.
            Rebuild();
        }

        protected override void Left()
        {
            Drop(false);
        }

        /// <summary>Makes a copy of every owned blade, in the chosen order.</summary>
        private void Rebuild()
        {
            Drop(false);

            foreach (Shown entry in shown)
            {
                if (entry.blade != null)
                {
                    Destroy(entry.blade.gameObject);
                }
            }

            shown.Clear();
            ChosenLoadout.Resolve(ChosenLoadout.OwnedInScene());

            for (int i = 0; i < ChosenLoadout.Order.Count; i++)
            {
                BladeDefinition definition = ChosenLoadout.Order[i];

                if (definition.Prefab == null)
                {
                    continue;
                }

                Pose pose = Slot(i, ChosenLoadout.Order.Count);
                Blade blade = Instantiate(definition.Prefab, pose.position, pose.rotation);
                blade.name = definition.DisplayName + " (Setup)";
                blade.Seized = true;
                blade.Visibility = visibility;

                shown.Add(new Shown
                {
                    definition = definition,
                    blade = blade,
                    baseScale = blade.transform.localScale,
                    phase = i * 1.3f,
                });
            }
        }

        protected override void Update()
        {
            base.Update();

            visibility = Mathf.MoveTowards(visibility, PlayerInside ? 1f : 0f, Time.deltaTime / fadeTime);

            bool interactive = PlayerInside && visibility > 0.5f && !DialogueBox.IsTalking && !SettingsMenu.IsOpen;
            UpdateDrag(interactive);

            float ease = 1f - Mathf.Exp(-settle * Time.deltaTime);

            for (int i = 0; i < shown.Count; i++)
            {
                Shown entry = shown[i];

                if (entry.blade == null)
                {
                    continue;
                }

                Pose pose = Slot(i, shown.Count);
                Vector3 target = i == dragging ? dragPoint : pose.position + Vector3.up * (Mathf.Sin(Time.time * 2f + entry.phase) * bob);

                Transform blade = entry.blade.transform;
                blade.position = Vector3.Lerp(blade.position, target, i == dragging ? 1f - Mathf.Exp(-settle * 2f * Time.deltaTime) : ease);
                blade.rotation = Quaternion.Slerp(blade.rotation, pose.rotation, ease);

                bool highlighted = i == dragging || i == hovered;
                entry.scale = Mathf.Lerp(entry.scale, highlighted ? highlightScale : 1f, ease);
                blade.localScale = entry.baseScale * entry.scale;

                entry.blade.Visibility = visibility;
            }
        }

        private void UpdateDrag(bool interactive)
        {
            Mouse mouse = Mouse.current;
            Camera view = Camera.main;

            if (!interactive || mouse == null || view == null)
            {
                Drop(false);
                return;
            }

            Vector2 pointer = mouse.position.ReadValue();

            if (dragging < 0)
            {
                hovered = -1;

                if (mouse.leftButton.wasPressedThisFrame)
                {
                    dragging = Nearest(pointer, view, -1);

                    if (dragging >= 0)
                    {
                        dragPoint = shown[dragging].blade.transform.position;
                    }
                }

                return;
            }

            // Follows the pointer across the upright plane the blades stand in.
            Pose plane = Slot(0, shown.Count);
            Vector3 normal = Ahead(view);
            Ray ray = view.ScreenPointToRay(pointer);

            if (new Plane(normal, plane.position).Raycast(ray, out float along))
            {
                dragPoint = ray.GetPoint(along);
            }

            hovered = Nearest(pointer, view, dragging);

            if (!mouse.leftButton.isPressed)
            {
                Drop(true);
            }
        }

        /// <summary>Lets go of the dragged blade, swapping it with the one under it if <paramref name="swap"/>.</summary>
        private void Drop(bool swap)
        {
            if (swap && dragging >= 0 && hovered >= 0 && hovered != dragging)
            {
                ChosenLoadout.Swap(dragging, hovered);
                (shown[dragging], shown[hovered]) = (shown[hovered], shown[dragging]);
            }

            dragging = -1;
            hovered = -1;
        }

        /// <summary>The blade nearest the pointer on screen, within reach, other than <paramref name="except"/>. -1 if none.</summary>
        private int Nearest(Vector2 pointer, Camera view, int except)
        {
            float reach = pickRadius * Screen.height / 1080f;
            float best = reach * reach;
            int found = -1;

            for (int i = 0; i < shown.Count; i++)
            {
                if (i == except || shown[i].blade == null)
                {
                    continue;
                }

                // Measured to where the blade belongs, not where it is, so a
                // blade sliding past does not steal the drop.
                Vector3 screen = view.WorldToScreenPoint(SlotCentre(i));

                if (screen.z <= 0f)
                {
                    continue;
                }

                float distanceSquared = ((Vector2)screen - pointer).sqrMagnitude;

                if (distanceSquared < best)
                {
                    best = distanceSquared;
                    found = i;
                }
            }

            return found;
        }

        /// <summary>The middle of the blade that sits in slot <paramref name="index"/>, for picking.</summary>
        private Vector3 SlotCentre(int index)
        {
            Blade blade = shown[index].blade;
            Renderer[] renderers = blade.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
            {
                return Slot(index, shown.Count).position;
            }

            Bounds bounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            // Where that middle will be once the blade has settled.
            return bounds.center - blade.transform.position + Slot(index, shown.Count).position;
        }

        /// <summary>Where the blade in place <paramref name="index"/> of <paramref name="count"/> stands: 0 the primary.</summary>
        private Pose Slot(int index, int count)
        {
            Camera view = Camera.main;
            Vector3 ahead = Ahead(view);
            Vector3 right = Vector3.Cross(Vector3.up, ahead);

            Vector3 row = transform.position + ahead * distance + Vector3.up * height;
            Vector3 position;

            if (index == 0)
            {
                position = row + Vector3.up * primaryRaise;
            }
            else
            {
                int secondaries = Mathf.Max(1, count - 1);
                position = row + right * ((index - 1 - (secondaries - 1) * 0.5f) * spacing);
            }

            // Flat side to the camera, tip down, as they hang in orbit.
            return new Pose(position, Quaternion.LookRotation(-ahead, Vector3.up));
        }

        /// <summary>Away from the camera, along the floor.</summary>
        private static Vector3 Ahead(Camera view)
        {
            if (view == null)
            {
                return Vector3.forward;
            }

            Vector3 ahead = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up);

            if (ahead.sqrMagnitude < 0.0001f)
            {
                ahead = Vector3.ProjectOnPlane(view.transform.up, Vector3.up);
            }

            return ahead.normalized;
        }
    }
}
