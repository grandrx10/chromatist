using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Raises a freshly summoned enemy up out of the floor, fading in as it
    /// comes, then lets it loose. Used by <see cref="ScarlettRiftPattern"/>
    /// for the enemies that climb out of her rifts.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="SummonFadeIn"/>, the enemy can be hit, and killed,
    /// the whole way up; it is only held dormant, so it neither moves nor
    /// attacks until it stands on the floor. Then it is woken straight away
    /// and this component removes itself.
    ///
    /// It starts sunk its own height below where it will stand. Its
    /// Rigidbody, if it has one, is kinematic on the way up and put back as
    /// it was after.
    ///
    /// Fades through _Fade, the same dither as a dying body; a part whose
    /// material lacks it simply switches on at the halfway point.
    /// </remarks>
    public sealed class RiftRise : MonoBehaviour
    {
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private readonly List<Renderer> renderers = new List<Renderer>();
        private readonly List<bool> canFade = new List<bool>();

        private Enemy enemy;
        private Rigidbody body;
        private bool wasKinematic;
        private MaterialPropertyBlock block;
        private Vector3 from;
        private Vector3 to;
        private float duration;
        private float age;

        /// <summary>
        /// Sinks <paramref name="unit"/> below <paramref name="standing"/>,
        /// where its pivot will end up, and raises it there over
        /// <paramref name="riseTime"/> seconds.
        /// </summary>
        public static RiftRise Begin(Enemy unit, Vector3 standing, float riseTime)
        {
            RiftRise rise = unit.gameObject.AddComponent<RiftRise>();
            rise.Setup(unit, standing, riseTime);
            return rise;
        }

        /// <summary>
        /// Stops <paramref name="unit"/> for good and dissolves it away over
        /// <paramref name="fadeTime"/> seconds, whether still rising or
        /// fighting: it stops acting, can no longer be hit, and is removed
        /// once gone.
        /// </summary>
        public static void Dismiss(Enemy unit, float fadeTime)
        {
            if (unit == null || unit.Health.IsDead)
            {
                return;
            }

            RiftRise rising = unit.GetComponent<RiftRise>();

            if (rising != null)
            {
                Destroy(rising);
            }

            unit.Health.Invulnerable = true;
            unit.enabled = false;
            unit.Health.enabled = false;

            foreach (Collider part in unit.GetComponentsInChildren<Collider>())
            {
                part.enabled = false;
            }

            Rigidbody unitBody = unit.GetComponent<Rigidbody>();

            if (unitBody != null)
            {
                if (!unitBody.isKinematic)
                {
                    unitBody.linearVelocity = Vector3.zero;
                }

                unitBody.isKinematic = true;
            }

            unit.gameObject.AddComponent<RagdollFade>().Begin(0f, fadeTime);
        }

        private void Setup(Enemy unit, Vector3 standing, float riseTime)
        {
            enemy = unit;
            duration = Mathf.Max(0.01f, riseTime);
            age = 0f;
            block = new MaterialPropertyBlock();

            enemy.StayDormant = true;

            Bounds bodyBounds = enemy.Health.BodyBounds();
            to = standing;
            from = standing + Vector3.down * Mathf.Max(0.1f, bodyBounds.size.y);

            body = enemy.GetComponent<Rigidbody>();

            if (body != null)
            {
                wasKinematic = body.isKinematic;
                body.isKinematic = true;
            }

            foreach (Renderer part in GetComponentsInChildren<Renderer>())
            {
                if (!part.enabled)
                {
                    continue;
                }

                bool fades = true;

                foreach (Material material in part.sharedMaterials)
                {
                    if (material == null || !material.HasProperty(FadeId))
                    {
                        fades = false;
                        break;
                    }
                }

                renderers.Add(part);
                canFade.Add(fades);
            }

            // Before it is ever drawn, so it never flashes in whole.
            Place(0f);
        }

        private void Update()
        {
            if (enemy == null || enemy.Health.IsDead)
            {
                Destroy(this);
                return;
            }

            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / duration);
            Place(t);

            if (t >= 1f)
            {
                if (body != null)
                {
                    body.isKinematic = wasKinematic;
                }

                enemy.StayDormant = false;
                enemy.Wake();
                Destroy(this);
            }
        }

        private void Place(float t)
        {
            // Quick out of the floor, easing to a stop.
            float eased = 1f - (1f - t) * (1f - t);
            Vector3 position = Vector3.Lerp(from, to, eased);
            transform.position = position;

            if (body != null)
            {
                body.position = position;
            }

            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer part = renderers[i];

                if (part == null)
                {
                    continue;
                }

                if (!canFade[i])
                {
                    part.enabled = t >= 0.5f;
                    continue;
                }

                part.GetPropertyBlock(block);
                block.SetFloat(FadeId, t);
                part.SetPropertyBlock(block);
            }
        }
    }
}
