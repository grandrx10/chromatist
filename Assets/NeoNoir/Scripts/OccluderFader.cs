using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightAndShadow
{
    /// <summary>
    /// Cuts a hole through scenery that stands between the camera and its
    /// target, and closes it again once it is out of the way.
    /// </summary>
    /// <remarks>
    /// Only a round patch around the target, as the camera sees it, is cut
    /// away -- <see cref="holeRadius"/> wide at the target's distance -- and
    /// only where the occluder is nearer the camera than the target. The rest
    /// of the wall stays solid, so the room still reads.
    ///
    /// The hole is a true crossfade, not a stipple. A stipple punches
    /// single-pixel holes the outline pass then traces, which flashes the wall
    /// white. Instead, while a renderer is fading it is tagged with
    /// <see cref="RenderingLayer"/> and the part of its NeoNoir/Flat material
    /// inside the hole drops out of the main frame; NeoNoirPostFeature draws
    /// that part into a layer of its own, and the edge pass blends that layer,
    /// outlines and all, over the scene behind it. The hole opens as
    /// <c>_OcclusionFade</c> falls. The shadow caster ignores the fade, so a
    /// cut wall keeps throwing its whole shadow and the lighting never changes
    /// as the camera moves. A material without <c>_OcclusionFade</c> cannot be
    /// cut, so the whole renderer snaps to ShadowsOnly at the halfway point
    /// instead.
    ///
    /// Anything on the <see cref="NonOccludeLayerName"/> layer is never cut.
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

        /// <summary>Layer whose objects are never cut, on top of <see cref="protectedLayers"/>.</summary>
        public const string NonOccludeLayerName = "NonOcclude";

        private static readonly int OcclusionFadeId = Shader.PropertyToID("_OcclusionFade");
        private static readonly int OcclusionTargetId = Shader.PropertyToID("_NeoNoirOcclusionTarget");
        private static readonly int OcclusionSoftnessId = Shader.PropertyToID("_NeoNoirOcclusionSoftness");

        [SerializeField]
        [Tooltip("Dissolves scenery between the camera and the target so it never blocks the view.")]
        private bool enabled = true;

        [SerializeField] private LayerMask layers = ~0;

        [SerializeField]
        [Tooltip("Never dissolved, whatever else is set: enemies, the player, and their projectiles. Checked on the collider and on every renderer it would hide.")]
        private LayerMask protectedLayers = (1 << 6) | (1 << 7); // Enemy, Player

        [SerializeField]
        [Tooltip("How far short of the target each sweep stops, and its narrowest width. The sweep is never narrower than the hole.")]
        private float radius = 0.6f;

        [SerializeField]
        [Tooltip("Heights above the target's pivot that must stay visible.")]
        private float[] sampleHeights = { 0.3f, 1f, 1.7f };

        [SerializeField]
        [Tooltip("Seconds to dissolve fully out, or back in.")]
        private float fadeDuration = 0.2f;

        [SerializeField]
        [Tooltip("Radius of the hole cut around the target, in world units at the target's distance from the camera.")]
        private float holeRadius = 2.5f;

        [SerializeField]
        [Tooltip("Width of the soft rim of the hole, in the same units. 0 is a hard cut.")]
        private float holeSoftness = 0.4f;

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
        private int nonOccludeLayer = -1;

        public void Update(Vector3 cameraPosition, Transform target, float deltaTime)
        {
            foreach (Occluder occluder in occluders.Values)
            {
                occluder.Occluding = false;
            }

            // Looked up each frame rather than once, so a layer added while
            // playing takes effect. -1 while the layer does not exist.
            nonOccludeLayer = LayerMask.NameToLayer(NonOccludeLayerName);

            if (enabled && target != null)
            {
                FindOccluders(cameraPosition, target);
                PublishHole(target);
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
            holeRadius = Mathf.Max(0.01f, holeRadius);
            holeSoftness = Mathf.Max(0f, holeSoftness);
        }

        /// <summary>
        /// Centres the hole on the middle of the sampled body, for every
        /// NeoNoir/Flat surface to cut against.
        /// </summary>
        private void PublishHole(Transform target)
        {
            float low = float.MaxValue;
            float high = float.MinValue;

            foreach (float height in sampleHeights)
            {
                low = Mathf.Min(low, height);
                high = Mathf.Max(high, height);
            }

            float middle = sampleHeights.Length > 0 ? (low + high) * 0.5f : 0f;
            Vector3 centre = target.position + Vector3.up * middle;

            Shader.SetGlobalVector(OcclusionTargetId, new Vector4(centre.x, centre.y, centre.z, holeRadius));
            Shader.SetGlobalFloat(OcclusionSoftnessId, holeSoftness);
        }

        private void FindOccluders(Vector3 cameraPosition, Transform target)
        {
            // At least as wide as the hole, so a wall that only reaches into
            // its edge is still found and cut, rather than the hole stopping
            // flat at it. Too wide is harmless: the shader only ever cuts
            // what is in front of the target.
            float sweepRadius = Mathf.Max(radius, holeRadius);

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
                    cameraPosition, sweepRadius, toPoint.normalized, hits, length, layers, QueryTriggerInteraction.Ignore);

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
            return (protectedLayers.value & (1 << candidate.layer)) != 0
                || candidate.layer == nonOccludeLayer;
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
