using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A sphere of runes around a boss, fed by the altars' generator cores:
    /// while any core stands, no blow can kill the boss.
    /// </summary>
    /// <remarks>
    /// Raised by a <see cref="SummoningAltar"/> on its boss when the fight
    /// starts. It turns on <see cref="EnemyHealth.CannotDie"/> whenever at
    /// least one of the generators' cores is armed and standing, and off
    /// when none are, whatever the boss is doing, so the boss can be worn
    /// down to its last sliver of health but not finished until every core
    /// is broken.
    ///
    /// The protection is there from the start, but the sphere stays unseen
    /// until it is first needed: it forms the first time it stops a killing
    /// blow, and from then on shows whenever any core stands.
    /// <see cref="Hold"/> shows it for a while even before that, as the
    /// <see cref="WitchHealPattern"/> does while she heals.
    ///
    /// Each generator supplies an equal share of the sphere's runes, spread
    /// evenly over it, so breaking a core thins the whole sphere: that
    /// share's runes are flung outward and dissolve. Restoring the core
    /// writes them back in. Runes stream through the air from every
    /// standing core into the sphere, to show what feeds it. A blow the
    /// shield stops makes the sphere flare.
    ///
    /// Built entirely in code. Follows the boss without being parented to
    /// it, and destroys itself when the boss dies or goes away. Runes face
    /// the camera and fade by dithered dissolve.
    /// </remarks>
    public sealed class RunicShield : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Runes each generator supplies to the sphere.")]
            [Range(1, 60)] public int runesPerGenerator = 14;

            [Tooltip("Gap between the boss's body and the sphere. Fitted round its colliders.")]
            [Min(0f)] public float padding = 0.6f;

            [Tooltip("Height of each rune on the sphere, in metres.")]
            [Min(0.05f)] public float runeSize = 0.5f;

            [Tooltip("Width of the runes' lines, in metres.")]
            [Min(0.01f)] public float lineWidth = 0.05f;

            [Tooltip("HDR: above 1 blooms, which is what makes it glow.")]
            [ColorUsage(false, true)] public Color colour = new Color(4f, 0.35f, 0.3f, 1f);

            [Tooltip("Material to draw with. Empty uses a shared NeoNoir/Flat one. Needs a _Fade property to dissolve.")]
            public Material material;

            [Tooltip("Degrees a second the sphere turns about the vertical.")]
            public float spinSpeed = 40f;

            [Tooltip("Degrees the sphere's axis wobbles off the vertical.")]
            [Range(0f, 45f)] public float wobble = 12f;

            [Tooltip("Seconds each rune takes to be written in.")]
            [Min(0.01f)] public float writeTime = 0.35f;

            [Tooltip("Seconds a rune takes to dissolve when its generator breaks.")]
            [Min(0.01f)] public float breakTime = 0.6f;

            [Tooltip("Metres a rune is flung outward as it dissolves.")]
            [Min(0f)] public float breakDistance = 2.5f;

            [Header("Flare")]
            [Tooltip("How much brighter the sphere flares when it stops a killing blow.")]
            [Min(0f)] public float flareBrightness = 2.5f;

            [Tooltip("How much bigger the sphere swells when it stops a killing blow, as a fraction.")]
            [Min(0f)] public float flareSwell = 0.15f;

            [Tooltip("Seconds the flare takes to fade.")]
            [Min(0.01f)] public float flareTime = 0.35f;

            [Header("Feed")]
            [Tooltip("Runes a second that stream from each standing core into the sphere. 0 sends none.")]
            [Min(0f)] public float feedRate = 3f;

            [Tooltip("Seconds a fed rune takes to fly from its core to the sphere.")]
            [Min(0.05f)] public float feedTime = 1.2f;

            [Tooltip("Metres the fed runes' path bows upward at its middle.")]
            [Min(0f)] public float feedArc = 4f;

            [Tooltip("Height of the fed runes, in metres.")]
            [Min(0.05f)] public float feedRuneSize = 0.35f;
        }

        private sealed class Rune
        {
            public Transform transform;
            public RuneDrawing drawing;
            public Vector3 direction;
            public float roll;
            public int generator;

            // Seconds since it started being written, or since it started
            // breaking; which, by whether it is supplied.
            public float written;
            public float broken = -1f;
        }

        private sealed class Mote
        {
            public Transform transform;
            public RuneDrawing drawing;
            public Vector3 from;
            public Vector3 direction;
            public float roll;
            public float age;
        }

        private static readonly List<RunicShield> all = new List<RunicShield>();

        private readonly List<Rune> runes = new List<Rune>();
        private readonly List<Mote> motes = new List<Mote>();

        private Settings settings;
        private EnemyHealth guarded;
        private Altar[] generators;
        private bool[] standing;
        private float[] owed;
        private Vector3 offset;
        private float radius;
        private float spin;
        private float age;
        private float flare = 1f;
        private int runeIndex;
        private int holds;

        /// <summary>True while any generator stands, and so the boss cannot die.</summary>
        public bool IsUp { get; private set; }

        /// <summary>True once the shield has stopped a killing blow, after which it shows whenever it is up.</summary>
        public bool IsFormed { get; private set; }

        /// <summary>Whether the sphere is to be seen: formed, or held.</summary>
        private bool Showing => IsFormed || holds > 0;

        /// <summary>The shield guarding <paramref name="health"/>, or null if none is.</summary>
        public static RunicShield For(EnemyHealth health)
        {
            foreach (RunicShield shield in all)
            {
                if (shield != null && shield.guarded == health)
                {
                    return shield;
                }
            }

            return null;
        }

        /// <summary>
        /// Shows the sphere, formed or not, until a matching
        /// <see cref="Unhold"/>. Only the runes of standing cores show.
        /// </summary>
        public void Hold()
        {
            holds++;
        }

        /// <summary>Ends a <see cref="Hold"/>. The sphere dissolves again if it has not formed.</summary>
        public void Unhold()
        {
            holds = Mathf.Max(0, holds - 1);
        }

        /// <summary>
        /// Raises the shield round <paramref name="boss"/>, fed by the cores
        /// of <paramref name="altars"/>.
        /// </summary>
        public static RunicShield Raise(EnemyHealth boss, IEnumerable<Altar> altars, Settings settings)
        {
            GameObject host = new GameObject("RunicShield");
            RunicShield shield = host.AddComponent<RunicShield>();
            shield.Begin(boss, altars, settings);
            return shield;
        }

        private void Begin(EnemyHealth boss, IEnumerable<Altar> altars, Settings shieldSettings)
        {
            settings = shieldSettings;
            guarded = boss;

            List<Altar> found = new List<Altar>();

            foreach (Altar altar in altars)
            {
                if (altar != null)
                {
                    found.Add(altar);
                }
            }

            generators = found.ToArray();
            standing = new bool[generators.Length];
            owed = new float[generators.Length];

            Bounds body = boss.BodyBounds();
            offset = body.center - boss.transform.position;
            radius = Mathf.Max(body.extents.x, body.extents.y, body.extents.z) + settings.padding;
            transform.position = body.center;

            // Spread over the sphere by the golden angle, dealt out to the
            // generators in turn, so each one's share covers all of it.
            int total = generators.Length * settings.runesPerGenerator;
            Material material = settings.material != null ? settings.material : FlatMaterial.Shared;

            for (int i = 0; i < total; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / total;
                float ring = Mathf.Sqrt(1f - y * y);
                float angle = i * 2.39996323f;

                GameObject child = new GameObject("Rune");
                child.transform.SetParent(transform, false);

                RuneDrawing drawing = new RuneDrawing(child, material);
                drawing.AddRune(RuneGlyphs.Rune(i), Vector3.zero, Vector3.forward, settings.runeSize,
                                settings.lineWidth, 0f, 1f);
                drawing.Progress = 0f;
                drawing.SetColour(settings.colour);

                runes.Add(new Rune
                {
                    transform = child.transform,
                    drawing = drawing,
                    direction = new Vector3(Mathf.Cos(angle) * ring, y, Mathf.Sin(angle) * ring),
                    roll = UnityEngine.Random.Range(-30f, 30f),
                    generator = i % generators.Length,
                    // Stagger the first writing, so it sweeps in.
                    written = -UnityEngine.Random.Range(0f, 0.5f),
                });
            }

            // Unseen until formed or held: as if already broken and gone.
            foreach (Rune rune in runes)
            {
                rune.broken = settings.breakTime;
            }

            guarded.DeathPrevented += OnDeathPrevented;
            all.Add(this);
            Refresh();
        }

        private void LateUpdate()
        {
            if (settings == null || guarded == null || guarded.IsDead || !guarded.isActiveAndEnabled)
            {
                Destroy(gameObject);
                return;
            }

            float deltaTime = Time.deltaTime;
            age += deltaTime;
            spin += settings.spinSpeed * deltaTime;
            flare = Mathf.MoveTowards(flare, 1f, deltaTime / settings.flareTime);

            Refresh();

            transform.position = guarded.transform.position + offset;

            Quaternion tilt = Quaternion.AngleAxis(settings.wobble * Mathf.Sin(age * 0.7f), Vector3.right)
                              * Quaternion.AngleAxis(settings.wobble * Mathf.Cos(age * 0.5f), Vector3.forward);
            Quaternion turn = tilt * Quaternion.AngleAxis(spin, Vector3.up);
            Quaternion facing = RuneDrawing.FacingCamera();

            // Swells with the flare, eased so it snaps out and settles.
            float flareAmount = flare - 1f;
            float swell = 1f + settings.flareSwell * flareAmount * flareAmount;
            Color colour = settings.colour * (1f + settings.flareBrightness * flareAmount * flareAmount);

            UpdateRunes(deltaTime, turn, facing, swell, colour);
            UpdateMotes(deltaTime, turn, facing);
            Feed(deltaTime);
        }

        /// <summary>Reads which generators stand, and lets the boss die or not by it.</summary>
        private void Refresh()
        {
            IsUp = false;

            for (int i = 0; i < generators.Length; i++)
            {
                Altar generator = generators[i];
                standing[i] = generator != null && generator.CoreHealth != null && !generator.IsCoreBroken;
                IsUp |= standing[i];
            }

            guarded.CannotDie = IsUp;
        }

        private void UpdateRunes(float deltaTime, Quaternion turn, Quaternion facing, float swell, Color colour)
        {
            foreach (Rune rune in runes)
            {
                bool supplied = standing[rune.generator] && Showing;

                if (supplied && rune.broken >= 0f)
                {
                    // Restored: write it in again from nothing.
                    rune.broken = -1f;
                    rune.written = -UnityEngine.Random.Range(0f, 0.3f);
                }
                else if (!supplied && rune.broken < 0f)
                {
                    rune.broken = 0f;
                }

                float distance = radius * swell;
                float fade = 1f;

                if (rune.broken >= 0f)
                {
                    rune.broken += deltaTime;
                    float t = Mathf.Clamp01(rune.broken / settings.breakTime);
                    distance += settings.breakDistance * (1f - (1f - t) * (1f - t));
                    fade = 1f - t;
                }
                else
                {
                    rune.written += deltaTime;
                    rune.drawing.Progress = rune.written / settings.writeTime;
                }

                bool visible = fade > 0f && rune.drawing.Progress > 0f;
                rune.transform.gameObject.SetActive(visible);

                if (!visible)
                {
                    continue;
                }

                rune.transform.SetPositionAndRotation(transform.position + turn * rune.direction * distance,
                                                      facing * Quaternion.AngleAxis(rune.roll, Vector3.up));
                rune.drawing.SetFade(fade);
                rune.drawing.SetColour(colour);
            }
        }

        private void Feed(float deltaTime)
        {
            if (settings.feedRate <= 0f || !Showing)
            {
                return;
            }

            for (int i = 0; i < generators.Length; i++)
            {
                if (!standing[i])
                {
                    owed[i] = 0f;
                    continue;
                }

                owed[i] += settings.feedRate * deltaTime;

                while (owed[i] >= 1f)
                {
                    owed[i] -= 1f;
                    SendMote(generators[i].CorePoint);
                }
            }
        }

        private void SendMote(Vector3 from)
        {
            GameObject child = new GameObject("FedRune");
            child.transform.SetParent(transform, false);
            child.transform.position = from;

            RuneDrawing drawing = new RuneDrawing(child, settings.material != null ? settings.material : FlatMaterial.Shared);
            drawing.AddRune(RuneGlyphs.Rune(runeIndex++), Vector3.zero, Vector3.forward, settings.feedRuneSize,
                            settings.lineWidth, 0f, 1f);
            drawing.Progress = 0f;
            drawing.SetColour(settings.colour);

            motes.Add(new Mote
            {
                transform = child.transform,
                drawing = drawing,
                from = from,
                direction = UnityEngine.Random.onUnitSphere,
                roll = UnityEngine.Random.Range(-30f, 30f),
            });
        }

        private void UpdateMotes(float deltaTime, Quaternion turn, Quaternion facing)
        {
            for (int i = motes.Count - 1; i >= 0; i--)
            {
                Mote mote = motes[i];
                mote.age += deltaTime;
                float t = mote.age / settings.feedTime;

                if (t >= 1f)
                {
                    mote.drawing.Release();
                    Destroy(mote.transform.gameObject);
                    motes.RemoveAt(i);
                    continue;
                }

                // Into a spot on the sphere that turns with it, bowing up
                // on the way, quickening as it nears.
                Vector3 to = transform.position + turn * mote.direction * radius;
                float eased = t * t;
                Vector3 position = Vector3.Lerp(mote.from, to, eased)
                                   + Vector3.up * (settings.feedArc * 4f * eased * (1f - eased));

                mote.transform.SetPositionAndRotation(position, facing * Quaternion.AngleAxis(mote.roll, Vector3.up));
                mote.drawing.Progress = mote.age / settings.writeTime;
                mote.drawing.SetFade(1f - Mathf.InverseLerp(0.8f, 1f, t));
            }
        }

        private void OnDeathPrevented(EnemyHealth health)
        {
            IsFormed = true;
            flare = 2f;
        }

        private void OnDestroy()
        {
            all.Remove(this);

            if (guarded != null)
            {
                guarded.DeathPrevented -= OnDeathPrevented;
                guarded.CannotDie = false;
            }

            foreach (Rune rune in runes)
            {
                rune.drawing.Release();
            }

            foreach (Mote mote in motes)
            {
                mote.drawing.Release();
            }
        }
    }
}
