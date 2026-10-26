using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The damaging part of one cube in an <see cref="EarthRipple"/>. Hurts
    /// the player it touches, once, unless a <see cref="ParryBarrier"/>
    /// catches it first.
    /// </summary>
    /// <remarks>
    /// Added at spawn time, so the cube prefab stays pure art. Its size comes
    /// from the prefab's own meshes, measured in the cube's local space so
    /// turning it to face the wave does not inflate it.
    ///
    /// Only the part above the floor counts: a cube still buried hurts
    /// nobody, and one half risen only hits what that half touches. The test
    /// is a box overlap each frame rather than a trigger, the same reasoning
    /// as <see cref="EnemyProjectile"/>, so the cube needs no collider or
    /// rigidbody.
    ///
    /// The barrier is checked before the player, so a parried cube never
    /// hurts, even when both are touched in the same frame. The barrier is a
    /// sphere, so the cube is treated as the sphere inside its footprint.
    /// </remarks>
    public sealed class EarthRippleCube : MonoBehaviour
    {
        /// <summary>Height a cube must stand above the floor before it counts as out.</summary>
        private const float ExposedThreshold = 0.05f;

        private static readonly Collider[] overlaps = new Collider[16];

        private float damage;
        private float groundHeight;
        private Vector3 waveVelocity;
        private Action parried;

        private Vector3 localCentre;
        private Vector3 localHalfSize = Vector3.one * 0.5f;
        private Vector3 lastCentre;
        private bool hasHit;

        /// <summary>
        /// Starts testing for hits. <paramref name="onParried"/> is called if
        /// a barrier catches the cube, just before it removes itself.
        /// </summary>
        public void Arm(float hitDamage, float floorHeight, Vector3 velocity, Action onParried)
        {
            damage = hitDamage;
            groundHeight = floorHeight;
            waveVelocity = velocity;
            parried = onParried;

            MeasureLocalBounds();
            lastCentre = transform.TransformPoint(localCentre);
        }

        /// <summary>The prefab's meshes, combined, in this object's local space.</summary>
        private void MeasureLocalBounds()
        {
            bool found = false;
            Bounds combined = default;

            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;

                if (mesh == null)
                {
                    continue;
                }

                Bounds meshBounds = mesh.bounds;

                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = meshBounds.center + Vector3.Scale(meshBounds.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 local = transform.InverseTransformPoint(filter.transform.TransformPoint(corner));

                    if (!found)
                    {
                        combined = new Bounds(local, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        combined.Encapsulate(local);
                    }
                }
            }

            if (found)
            {
                localCentre = combined.center;
                localHalfSize = combined.extents;
            }
        }

        private void LateUpdate()
        {
            // After RisingCube has moved it this frame.
            Vector3 centre = transform.TransformPoint(localCentre);
            Vector3 from = lastCentre;
            lastCentre = centre;

            if (hasHit)
            {
                return;
            }

            Vector3 halfSize = Vector3.Scale(localHalfSize, transform.lossyScale);
            float top = centre.y + halfSize.y;
            float bottom = Mathf.Max(centre.y - halfSize.y, groundHeight);

            if (top - groundHeight < ExposedThreshold)
            {
                return;
            }

            // Just the part above the floor, as an upright box.
            float exposedHalf = (top - bottom) * 0.5f;
            Vector3 exposedCentre = new Vector3(centre.x, bottom + exposedHalf, centre.z);
            Vector3 exposedFrom = new Vector3(from.x, exposedCentre.y, from.z);

            if (ParryBarrier.TryCatch(exposedFrom, exposedCentre, Mathf.Min(halfSize.x, halfSize.z), waveVelocity))
            {
                parried?.Invoke();
                Destroy(gameObject);
                return;
            }

            Quaternion upright = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            int count = Physics.OverlapBoxNonAlloc(exposedCentre, new Vector3(halfSize.x, exposedHalf, halfSize.z),
                                                   overlaps, upright, Physics.AllLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                PlayerHealth health = overlaps[i].GetComponentInParent<PlayerHealth>();

                if (health != null)
                {
                    health.TakeDamage(damage, exposedCentre);
                    hasHit = true;
                    return;
                }
            }
        }
    }
}
