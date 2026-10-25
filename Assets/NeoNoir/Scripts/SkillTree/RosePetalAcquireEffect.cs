using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Rose's acquire effect: a swirl of her petals bursting up around the
    /// rapier.
    /// </summary>
    /// <remarks>
    /// The same <see cref="RosePetal"/>s her dash sheds. They are released
    /// from a ring round the blade over <see cref="emitTime"/>, each carried
    /// on round the ring, so together they read as one swirl.
    /// </remarks>
    public sealed class RosePetalAcquireEffect : BladeAcquireEffect
    {
        [SerializeField]
        [Tooltip("Material for the petals. Empty uses the blade's own, like Rose's dash does.")]
        private Material petalMaterial;

        [SerializeField] [Min(1)] private int count = 60;

        [SerializeField]
        [Tooltip("Seconds over which the petals are released.")]
        [Min(0f)]
        private float emitTime = 0.8f;

        [SerializeField]
        [Tooltip("Distance from the blade the petals are released at.")]
        [Min(0f)]
        private float radius = 0.9f;

        [SerializeField]
        private RosePetal.Settings petals = new RosePetal.Settings
        {
            size = new Vector2(0.14f, 0.26f),
            scatter = 0.25f,
            burstSpeed = 1.2f,
            carrySpeed = 3f,
            drag = 2f,
            fallSpeed = 0.15f,
            floatTime = 1f,
            floatJitter = 0.8f,
            fadeTime = 0.8f,
        };

        public override void Play(PurchasableBlade blade)
        {
            StartCoroutine(Emit(blade));
        }

        private IEnumerator Emit(PurchasableBlade blade)
        {
            Material material = petalMaterial != null ? petalMaterial : blade.BladeMaterial;
            Bounds bounds = blade.BladeBounds;
            float released = 0f;
            float clock = 0f;

            while (released < count)
            {
                clock += Time.deltaTime;
                float due = emitTime > 0f ? Mathf.Min(count, count * clock / emitTime) : count;

                while (released < due)
                {
                    released++;

                    float angle = Random.value * Mathf.PI * 2f;
                    Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                    // Carried round the ring rather than off it: a swirl.
                    Vector3 around = Vector3.Cross(Vector3.up, outward);

                    Vector3 origin = bounds.center + outward * radius
                                     + Vector3.up * (Random.Range(-1f, 1f) * bounds.extents.y);

                    RosePetal.Spawn(origin, around, petals, material);
                }

                yield return null;
            }
        }
    }
}
