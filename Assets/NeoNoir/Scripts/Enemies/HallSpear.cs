using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// One thrown spear coming down into the floor: it flies in along its own
    /// shaft, sticks, stands a while, then sinks away.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The spear appears <see cref="Settings.flightDistance"/> back along
    ///      its shaft from where its tip will land, leaning
    ///      <see cref="Settings.leanAngle"/> from upright toward the direction
    ///      it was thrown from, and flies straight in over
    ///      <see cref="Settings.flightTime"/> seconds.
    ///   2. On landing it sinks <see cref="Settings.embedDepth"/> into the
    ///      floor, hurts the player if they are within
    ///      <see cref="Settings.damageRadius"/>, bursts a
    ///      <see cref="HammerImpact"/> ring and debris out of the floor, and
    ///      jolts the camera.
    ///   3. It stands for <see cref="Settings.lingerTime"/> seconds, then
    ///      slides <see cref="Settings.sinkDepth"/> down into the floor over
    ///      <see cref="Settings.sinkTime"/> seconds and is put away.
    ///
    /// The spear prefab is pure art: authored standing upright, tip down. Its
    /// pivot can be anywhere; the tip is found from the model, as the bottom
    /// middle of everything it draws. Spears are pooled per prefab, since a
    /// spear storm throws hundreds.
    ///
    /// Spears are copied from a plain template made once per prefab: the
    /// prefab with its meshes built and every script and collider stripped,
    /// hung under a new pivot at its tip.
    /// A ProBuilder model with no mesh saved rebuilds its whole mesh each
    /// time it is copied, so copying the prefab itself makes every spear
    /// cost a full rebuild; the template's copies share the one mesh.
    /// </remarks>
    public sealed class HallSpear : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Spear")]
            [Tooltip("Your spear model. Authored upright, tip down; the tip is found from the model, so the pivot can be anywhere. Needs no script or collider.")]
            public GameObject spearPrefab;

            [Tooltip("How far along its shaft the spear flies in from.")]
            [Min(0f)] public float flightDistance = 14f;

            [Tooltip("Seconds the spear takes to fly in.")]
            [Min(0.01f)] public float flightTime = 0.18f;

            [Tooltip("Degrees the shaft leans from upright, its butt toward where it was thrown from.")]
            [Range(0f, 80f)] public float leanAngle = 30f;

            [Tooltip("Up to this many degrees more or less lean, picked at random per spear.")]
            [Range(0f, 40f)] public float leanJitter = 8f;

            [Tooltip("Up to this many degrees the lean swings left or right, picked at random per spear.")]
            [Range(0f, 90f)] public float headingJitter = 20f;

            [Tooltip("How far the tip ends up buried in the floor.")]
            [Min(0f)] public float embedDepth = 0.3f;

            [Header("After landing")]
            [Tooltip("Seconds the spear stands in the floor before sinking.")]
            [Min(0f)] public float lingerTime = 2.5f;

            [Tooltip("Seconds the spear takes to sink away.")]
            [Min(0.01f)] public float sinkTime = 0.6f;

            [Tooltip("How far the spear sinks along its shaft before it is gone. Make it at least the spear's length.")]
            [Min(0f)] public float sinkDepth = 4f;

            [Header("Damage")]
            [Tooltip("Damage to the player if they are this close to the tip as it lands. 0 for none.")]
            [Min(0f)] public float damage = 1f;

            [Tooltip("How close, ignoring height, the player has to be to the tip to be hit.")]
            [Min(0f)] public float damageRadius = 0.7f;

            [Header("Impact")]
            [Tooltip("Material for the burst where the spear lands. Empty uses plain NeoNoir/Flat. Its own colour is ignored: Impact Colour is used instead.")]
            public Material impactMaterial;

            [Tooltip("Colour of the ring and debris. Keep it strongly saturated, or the threshold turns it white or black. Brightened by the burst's Ring and Debris Intensity.")]
            public Color impactColour = new Color(1f, 0.62f, 0.08f, 1f);

            [Tooltip("Width of the bands edging the ring on both sides. 0 for none.")]
            [Min(0f)] public float impactEdgeWidth = 0.12f;

            [Tooltip("Colour of the ring's edges. Above 1 it blooms.")]
            [ColorUsage(false, true)]
            public Color impactEdgeColour = new Color(2f, 2f, 2f, 1f);

            [Tooltip("The ring and debris burst where the spear lands. Ring Radius and Debris Count both 0 for none.")]
            public HammerImpact.Settings landingBurst = new HammerImpact.Settings
            {
                ringRadius = 2.2f,
                ringWidth = 0.45f,
                ringTime = 0.3f,
                debrisCount = 6,
                debrisSize = 0.3f,
                debrisUpSpeed = new Vector2(5f, 9f),
                debrisOutSpeed = new Vector2(2f, 5f),
                debrisTime = 0.6f,
            };

            [Tooltip("How far each landing jolts the camera. 0 for none. Many spears landing together read as a rumble.")]
            [Min(0f)] public float shakeStrength = 0.15f;

            [Tooltip("Seconds each landing's camera jolt takes to die away.")]
            [Min(0.01f)] public float shakeDuration = 0.15f;
        }

        private static readonly Dictionary<GameObject, Stack<HallSpear>> Pools = new Dictionary<GameObject, Stack<HallSpear>>();
        private static readonly Dictionary<GameObject, GameObject> Templates = new Dictionary<GameObject, GameObject>();

        private GameObject prefab;
        private Settings settings;
        private PlayerHealth target;
        private Vector3 tip;
        private Vector3 shaft;
        private float age;
        private bool landed;
        private bool quiet;

        // Tinted through property blocks, so a pooled spear thrown untinted
        // next time has them cleared.
        private static MaterialPropertyBlock tintBlock;
        private Renderer[] parts;
        private bool tinted;

        /// <summary>
        /// Throws a spear whose tip lands on <paramref name="floor"/>, a point
        /// on the floor, leaning back toward <paramref name="thrownFrom"/>, a
        /// flat direction. <paramref name="player"/> is hurt if it is standing
        /// there; null hurts no one. <paramref name="quiet"/> lands it with no
        /// burst and no camera jolt, for a caller that makes one for a whole
        /// cluster of spears. <paramref name="tint"/> recolours the whole
        /// spear, whose materials need NeoNoir/Flat's _BaseColor; null leaves
        /// it as modelled. Returns null with no spear prefab set.
        /// </summary>
        public static HallSpear Throw(Vector3 floor, Vector3 thrownFrom, Settings settings, PlayerHealth player,
                                      bool quiet = false, Color? tint = null)
        {
            if (settings.spearPrefab == null)
            {
                return null;
            }

            HallSpear spear = Take(settings.spearPrefab);
            spear.quiet = quiet;
            spear.Tint(tint);
            spear.Begin(floor, thrownFrom, settings, player);
            return spear;
        }

        private void Tint(Color? colour)
        {
            if (colour == null && !tinted)
            {
                return;
            }

            parts ??= GetComponentsInChildren<Renderer>(true);
            tintBlock ??= new MaterialPropertyBlock();

            foreach (Renderer part in parts)
            {
                if (colour.HasValue)
                {
                    FlatMaterial.Tint(part, colour.Value, tintBlock);
                }
                else
                {
                    part.SetPropertyBlock(null);
                }
            }

            tinted = colour.HasValue;
        }

        /// <summary>
        /// Makes <paramref name="count"/> spears of <paramref name="prefab"/>
        /// ready ahead of time, so a storm does not build them mid-fight.
        /// Spears already pooled count toward it.
        /// </summary>
        public static void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null)
            {
                return;
            }

            if (Pools.TryGetValue(prefab, out Stack<HallSpear> pool))
            {
                // A scene change destroys pooled spears with it.
                Stack<HallSpear> alive = new Stack<HallSpear>();

                foreach (HallSpear pooled in pool)
                {
                    if (pooled != null)
                    {
                        alive.Push(pooled);
                    }
                }

                Pools[prefab] = alive;
                pool = alive;
            }

            int have = pool != null ? pool.Count : 0;

            for (int i = have; i < count; i++)
            {
                Make(prefab).PutAway();
            }
        }

        private static HallSpear Take(GameObject prefab)
        {
            if (Pools.TryGetValue(prefab, out Stack<HallSpear> pool))
            {
                // A scene change destroys pooled spears with it.
                while (pool.Count > 0)
                {
                    HallSpear pooled = pool.Pop();

                    if (pooled != null)
                    {
                        pooled.gameObject.SetActive(true);
                        return pooled;
                    }
                }
            }

            return Make(prefab);
        }

        private static HallSpear Make(GameObject prefab)
        {
            GameObject made = Instantiate(TemplateOf(prefab));
            made.name = prefab.name;

            HallSpear spear = made.AddComponent<HallSpear>();
            spear.prefab = prefab;
            return spear;
        }

        /// <summary>
        /// The plain copy of <paramref name="prefab"/> spears are made from,
        /// made the first time it is wanted: an <see cref="ArtTemplate"/>
        /// hung under a new pivot at its tip.
        /// </summary>
        private static GameObject TemplateOf(GameObject prefab)
        {
            // A scene change destroys the template with it.
            if (Templates.TryGetValue(prefab, out GameObject template) && template != null)
            {
                return template;
            }

            // Made active once, so a ProBuilder model builds its meshes.
            GameObject model = Instantiate(prefab);
            model.name = prefab.name;
            ArtTemplate.Strip(model);

            // Hung under a new pivot at its tip -- the bottom middle of the
            // upright model -- whatever pivot it was built with.
            model.transform.position = Vector3.zero;
            bool found = false;
            Bounds extent = default;

            foreach (Renderer part in model.GetComponentsInChildren<Renderer>())
            {
                if (found)
                {
                    extent.Encapsulate(part.bounds);
                }
                else
                {
                    extent = part.bounds;
                    found = true;
                }
            }

            template = new GameObject(prefab.name + " (spear template)");
            model.transform.SetParent(template.transform, true);

            if (found)
            {
                model.transform.localPosition = -new Vector3(extent.center.x, extent.min.y, extent.center.z);
            }

            ArtTemplate.Hold(template);
            Templates[prefab] = template;
            return template;
        }

        private void PutAway()
        {
            gameObject.SetActive(false);

            if (!Pools.TryGetValue(prefab, out Stack<HallSpear> pool))
            {
                pool = new Stack<HallSpear>();
                Pools[prefab] = pool;
            }

            pool.Push(this);
        }

        private void Begin(Vector3 floor, Vector3 thrownFrom, Settings spearSettings, PlayerHealth player)
        {
            settings = spearSettings;
            target = player;
            age = 0f;
            landed = false;

            thrownFrom.y = 0f;

            if (thrownFrom.sqrMagnitude < 0.0001f)
            {
                thrownFrom = Vector3.forward;
            }

            float heading = UnityEngine.Random.Range(-settings.headingJitter, settings.headingJitter);
            Vector3 back = Quaternion.AngleAxis(heading, Vector3.up) * thrownFrom.normalized;

            float lean = Mathf.Clamp(settings.leanAngle + UnityEngine.Random.Range(-settings.leanJitter, settings.leanJitter), 0f, 85f) * Mathf.Deg2Rad;

            // Points from the tip up the shaft to the butt.
            shaft = Vector3.up * Mathf.Cos(lean) + back * Mathf.Sin(lean);
            tip = floor - shaft * settings.embedDepth;

            // Spun about its own shaft too, so a crowd of them is not all
            // showing the same side.
            transform.rotation = Quaternion.FromToRotation(Vector3.up, shaft)
                               * Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

            Place(0f);
        }

        private void Update()
        {
            age += Time.deltaTime;

            if (!landed)
            {
                Place(age / settings.flightTime);

                if (age >= settings.flightTime)
                {
                    Land();
                }

                return;
            }

            float sinking = age - settings.flightTime - settings.lingerTime;

            if (sinking <= 0f)
            {
                return;
            }

            float t = Mathf.Clamp01(sinking / settings.sinkTime);
            transform.position = tip - shaft * (settings.sinkDepth * t * t);

            if (t >= 1f)
            {
                PutAway();
            }
        }

        /// <summary>Puts the spear <paramref name="t"/> of the way along its flight, 1 being landed.</summary>
        private void Place(float t)
        {
            transform.position = tip + shaft * (settings.flightDistance * (1f - Mathf.Clamp01(t)));
        }

        private void Land()
        {
            landed = true;
            Place(1f);

            Vector3 floor = tip + shaft * settings.embedDepth;

            if (!quiet && (settings.landingBurst.ringRadius > 0f || settings.landingBurst.debrisCount > 0))
            {
                HammerImpact.Create(floor, settings.landingBurst, settings.impactMaterial,
                    settings.impactColour, settings.impactEdgeWidth, settings.impactEdgeColour);
            }

            if (!quiet && settings.shakeStrength > 0f)
            {
                TopDownCamera.Shake(settings.shakeStrength, settings.shakeDuration);
            }

            if (target == null || settings.damage <= 0f)
            {
                return;
            }

            Vector3 offset = target.transform.position - floor;
            offset.y = 0f;

            if (offset.sqrMagnitude <= settings.damageRadius * settings.damageRadius)
            {
                target.TakeDamage(settings.damage, floor);
            }
        }
    }
}
