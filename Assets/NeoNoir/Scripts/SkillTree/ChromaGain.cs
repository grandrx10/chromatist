using System.Collections;
using LightAndShadow.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightAndShadow
{
    /// <summary>
    /// Chroma being earned: glowing orbs, one per point, swell in round the
    /// player and spiral into them, each landing in a flash.
    /// </summary>
    /// <remarks>
    /// Purely for show: the points themselves are given by
    /// <see cref="ChromaRewards"/>. The orbs look like the Afterworld's
    /// <see cref="ChromaOrbs"/>. With keepLit, they and their flashes stay
    /// lit through <see cref="NeoNoirDarkness"/>. Built from primitives, so
    /// it needs no prefab.
    /// </remarks>
    public sealed class ChromaGain : MonoBehaviour
    {
        private const float OrbSize = 0.16f;
        private const float StartRadius = 2.4f;
        private const float SwellTime = 0.3f;
        private const float FlyTime = 0.9f;
        private const float Stagger = 0.18f;
        private const float Turn = 200f;
        private static readonly Color Colour = new Color(4f, 4f, 4f, 1f);

        private static readonly EnergyBurst.Settings Burst = new EnergyBurst.Settings
        {
            flashSize = 0.9f,
            flashTime = 0.25f,
            sparkCount = 14,
            sparkSize = 0.3f,
            sparkSpeed = new Vector2(3f, 7f),
            sparkLife = 0.4f,
            colour = new Color(4f, 4f, 4f, 1f),
        };

        private Transform target;
        private bool keepLit;
        private int flying;

        /// <summary>True once every orb has landed.</summary>
        public bool Done { get; private set; }

        /// <summary>Flies <paramref name="count"/> orbs into <paramref name="player"/>.</summary>
        public static ChromaGain Play(Transform player, int count, bool keepLit)
        {
            ChromaGain gain = new GameObject("ChromaGain").AddComponent<ChromaGain>();
            gain.target = player;
            gain.keepLit = keepLit;
            gain.StartCoroutine(gain.Run(Mathf.Max(0, count)));
            return gain;
        }

        private IEnumerator Run(int count)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            float offset = Random.value * 360f;

            for (int i = 0; i < count; i++)
            {
                flying++;
                StartCoroutine(Fly(MakeOrb(block), offset + i * 360f / count));

                for (float clock = 0f; clock < Stagger; clock += Time.deltaTime)
                {
                    yield return null;
                }
            }

            while (flying > 0)
            {
                yield return null;
            }

            Done = true;
            Destroy(gameObject);
        }

        private IEnumerator Fly(Transform orb, float angle)
        {
            Vector3 centre = target != null ? target.position : transform.position;

            for (float clock = 0f; clock < SwellTime + FlyTime; clock += Time.deltaTime)
            {
                if (target != null)
                {
                    centre = target.position;
                }

                float swell = Mathf.Clamp01(clock / SwellTime);
                float t = Mathf.Clamp01((clock - SwellTime) / FlyTime);

                // Spirals in, faster and faster, turning as it closes.
                float closing = t * t;
                float turned = angle + Turn * closing;
                Vector3 way = Quaternion.Euler(0f, turned, 0f) * Vector3.forward;
                float rise = Mathf.Sin(t * Mathf.PI) * 0.5f;

                orb.position = centre + way * (StartRadius * (1f - closing)) + Vector3.up * rise;
                orb.localScale = Vector3.one * (OrbSize * (1f - (1f - swell) * (1f - swell)));
                yield return null;
            }

            EnergyBurst burst = EnergyBurst.Create(centre, Burst);

            if (keepLit)
            {
                NeoNoirDarkness.Keep(burst.gameObject);
            }

            Destroy(orb.gameObject);
            flying--;
        }

        private Transform MakeOrb(MaterialPropertyBlock block)
        {
            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(orb.GetComponent<Collider>());
            orb.name = "Chroma";
            orb.transform.SetParent(transform, false);
            orb.transform.localScale = Vector3.zero;

            MeshRenderer renderer = orb.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = FlatMaterial.Shared;
            FlatMaterial.Tint(renderer, Colour, block);

            TrailRenderer trail = orb.AddComponent<TrailRenderer>();
            trail.time = 0.18f;
            trail.minVertexDistance = 0.03f;
            trail.widthMultiplier = OrbSize * 0.7f;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = FlatMaterial.DoubleSided;
            FlatMaterial.Tint(trail, Colour, block);

            if (keepLit)
            {
                NeoNoirDarkness.Keep(orb);
            }

            return orb.transform;
        }
    }
}
