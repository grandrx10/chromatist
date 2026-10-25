using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightAndShadow
{
    /// <summary>
    /// Dissolves scenery that stands between the camera and its target, and
    /// brings it back once it is out of the way.
    /// </summary>
    /// <remarks>
    /// The dissolve is a true crossfade, not a stipple. A stipple punches
    /// single-pixel holes the outline pass then traces, which flashes the wall
    /// white. Instead, while a renderer is fading it is tagged with
    /// <see cref="RenderingLayer"/> and its NeoNoir/Flat material drops out of
    /// the main frame altogether; NeoNoirPostFeature draws it into a layer of
    /// its own, and the edge pass blends that layer, outlines and all, over the
    /// scene behind it by <c>_OcclusionFade</c>. The shadow caster ignores the
    /// fade, so a dissolved wall keeps throwing its shadow and the lighting
    /// never changes as the camera moves. A material without
    /// <c>_OcclusionFade</c> cannot dissolve, so it snaps to ShadowsOnly at the
    /// halfway point instead.
    ///
    /// Occluders are found by sweeping a sphere from the camera to several
    /// points up the target's body -- feet, middle, head -- so a low table in
    /// front of the legs counts as much as a wall across the face. Each sweep
    /// stops one radius short of its point, which keeps walls the target is
    /// merely standing in front of out of it.
    ///
    /// Owned by <see cref="TopDownCamera"/> rather than being a component of
    /// its own, so the camera needs no extra setup.
    /// </remarks>
    [Serializable]
    public sealed class OccluderFader
    {
        /// <summary>
        /// Rendering layer bit marking a renderer as mid-fade. NeoNoirPostFeature
        /// draws exactly these into the occluder layer.
        /// </summary>
        public const uint RenderingLayer = 1u << 31;

        private static readonly int OcclusionFadeId = Shader.PropertyToID("_OcclusionFade");

        [SerializeField]
        [Tooltip("Dissolves scenery between the camera and the target so it never blocks the view.")]
        private bool enabled = true;

        [SerializeField] private LayerMask layers = ~0;

        [SerializeField]
        [Tooltip("Never dissolved, whatever else is set: enemies, the player, and their projectiles. Checked on the collider and on every renderer it would hide.")]
        private LayerMask protectedLayers = (1 << 6) | (1 << 7); // Enemy, Player

        [SerializeField]
        [Tooltip("Radius of each sweep. Wider clears more of the surroundings.")]
        private float radius = 0.6f;

        [SerializeField]
        [Tooltip("Heights above the target's pivot that must stay visible.")]
        private float[] sampleHeights = { 0.3f, 1f, 1.7f };

        [SerializeField]
        [Tooltip("Seconds to dissolve fully out, or back in.")]
        private float fadeDuration = 0.2f;

        private sealed class Occluder
        {
            public float Fade = 1f;
            public bool Dithered;
            public ShadowCastingMode ShadowMode;
            public bool Occluding;
        }

        private readonly RaycastHit[] hits = new RaycastHit[64];
        private readonly Dictionary<Renderer, Occluder> occluders = new Dictionary<Renderer, Occluder>();
        private readonly List<Renderer> rendererScratch = new List<Renderer>();
        private readonly List<Renderer> finishedScratch = new List<Renderer>();
        private MaterialPropertyBlock block;

        public void Update(Vector3 cameraPosition, Transform target, float deltaTime)
        {
            foreach (Occluder occluder in occluders.Values)
            {
                occluder.Occluding = false;
            }

            if (enabled && target != null)
            {
                FindOccluders(cameraPosition, target);
            }

            float step = fadeDuration > 0f ? deltaTime / fadeDuration : 1f;
            finishedScratch.Clear();

            foreach (KeyValuePair<Renderer, Occluder> entry in occluders)
            {
                Renderer renderer = entry.Key;
                Occluder occluder = entry.Value;

                if (renderer == null)
                {
                    finishedScratch.Add(renderer);
                    continue;
                }

                occluder.Fade = Mathf.MoveTowards(occluder.Fade, occluder.Occluding ? 0f : 1f, step);
                Apply(renderer, occluder);

                if (occluder.Fade >= 1f)
                {
                    finishedScratch.Add(renderer);
                }
            }

            foreach (Renderer renderer in finishedScratch)
            {
                occluders.Remove(renderer);
            }
        }

        /// <summary>Puts every occluder back exactly as it was, immediately.</summary>
        public void RestoreAll()
        {
            foreach (KeyValuePair<Renderer, Occluder> entry in occluders)
            {
                if (entry.Key != null)
                {
                    entry.Value.Fade = 1f;
                    Apply(entry.Key, entry.Value);
                }
            }

            occluders.Clear();
        }

        public void Validate()
        {
            radius = Mathf.Max(0.01f, radius);
            fadeDuration = Mathf.Max(0f, fadeDuration);
        }

        private void FindOccluders(Vector3 cameraPosition, Transform target)
        {
            foreach (float height in sampleHeights)
            {
                Vector3 toPoint = target.position + Vector3.up * height - cameraPosition;
                float length = toPoint.magnitude - radius;

                if (length <= 0f)
                {
                    continue;
                }

                // Colliders the sphere already overlaps at the camera come back
                // with distance 0, which is exactly the wall the camera is in.
                int count = Physics.SphereCastNonAlloc(
                    cameraPosition, radius, toPoint.normalized, hits, length, layers, QueryTriggerInteraction.Ignore);

                for (int i = 0; i < count; i++)
                {
                    Collider hit = hits[i].collider;

                    // Rigidbodies are the player, enemies and projectiles, none
                    // of which should ever vanish. Anything entirely below the
                    // target's feet is the floor it stands on.
                    if (hit.attachedRigidbody != null
                        || IsProtected(hit.gameObject)
                        || hit.transform.IsChildOf(target)
                        || hit.bounds.max.y <= target.position.y + 0.05f)
                    {
                        continue;
                    }

                    // Renderers on the collider itself, or, for a collider that
                    // has none, on its children. Never upward: a parent may own
                    // the whole building.
                    hit.GetComponents(rendererScratch);
                    if (rendererScratch.Count == 0)
                    {
                        hit.GetComponentsInChildren(rendererScratch);
                    }

                    foreach (Renderer renderer in rendererScratch)
                    {
                        // A child can sit on a protected layer under an
                        // unprotected collider: a projectile's mesh under its
                        // hitbox, say.
                        if (IsProtected(renderer.gameObject))
                        {
                            continue;
                        }

                        if (!occluders.TryGetValue(renderer, out Occluder occluder))
                        {
                            Material material = renderer.sharedMaterial;
                            occluder = new Occluder
                            {
                                Dithered = material != null && material.HasProperty(OcclusionFadeId),
                                ShadowMode = renderer.shadowCastingMode
                            };
                            occluders.Add(renderer, occluder);
                        }

                        occluder.Occluding = true;
                    }
                }
            }
        }

        private bool IsProtected(GameObject candidate)
        {
            return (protectedLayers.value & (1 << candidate.layer)) != 0;
        }

        private void Apply(Renderer renderer, Occluder occluder)
        {
            if (!occluder.Dithered)
            {
                renderer.shadowCastingMode = occluder.Fade < 0.5f ? ShadowCastingMode.ShadowsOnly : occluder.ShadowMode;
                return;
            }

            // Clear the block entirely when fully visible: a renderer with a
            // property block drops out of SRP batching.
            if (occluder.Fade >= 1f)
            {
                renderer.renderingLayerMask &= ~RenderingLayer;
                renderer.SetPropertyBlock(null);
                return;
            }

            renderer.renderingLayerMask |= RenderingLayer;

            block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetFloat(OcclusionFadeId, occluder.Fade);
            renderer.SetPropertyBlock(block);
        }
    }
}
