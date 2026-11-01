using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A sword thrown up out of the floor by the mace, the same sword as a
    /// <see cref="StoneSword"/>'s: held hovering by the attack that made it,
    /// lit in a bright colour of its own, then launched point first at the
    /// player, exploding the instant it lands.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. While held, the attack poses it every frame through
    ///      <see cref="Hold"/>. Over <see cref="Settings.tintTime"/> the blade
    ///      turns from black to its colour, as a pulled Stone Sword's does.
    ///   2. <see cref="Launch"/>ed, it flies straight on by itself, trailing
    ///      its colour, tested with a sphere cast from its tip each frame.
    ///   3. The moment it meets anything -- the player, the floor, a wall --
    ///      it explodes there, as a Stone Sword does at the end of its fuse:
    ///      the player within <see cref="Settings.blastRadius"/> takes
    ///      <see cref="Settings.blastDamage"/>. Out of
    ///      <see cref="Settings.lifetime"/>, it goes off in the air. The boss
    ///      and triggers are passed through.
    ///
    /// Or, instead of being launched, it can be <see cref="Lob"/>bed: thrown
    /// up in an arc onto a spot on the floor, as a <see cref="MaceDebris"/>
    /// chunk is. A <see cref="WarningCircle"/> of the blast's reach, in the
    /// sword's colour, marks the spot and fills as it falls. Its point
    /// follows the arc -- up as it leaves, over the top, down as it falls --
    /// rolling slowly about its length at <see cref="Settings.fallRoll"/>, so
    /// it always comes down point first. It stabs into the spot and explodes
    /// at once. Nothing on the way stops it, but a parry still catches it.
    ///
    /// Caught by a <see cref="ParryBarrier"/> in flight, it bursts harmlessly.
    /// <see cref="Dismiss"/>ed while held, as when the boss dies, it shrinks
    /// away. The trail, explosion and sparks all take the sword's colour.
    ///
    /// Its forward is the way its point faces. The art is a copy of
    /// <see cref="Settings.art"/>'s <see cref="ArtTemplate"/>, scripts and
    /// colliders dropped. Given the BladeInStone prefab -- a Stone Sword on
    /// its root -- only its Blade child is used, the rock left off. The art is
    /// turned by <see cref="Settings.artRotation"/> to point along forward. A
    /// Stone Sword's blade, built tip down its local -Y, is pointed along
    /// forward by itself, Art Rotation turning it further from there. Without
    /// art it is a plain long box. Its renderers are tinted
    /// through property blocks, so their materials want NeoNoir/Flat's
    /// _BaseColor.
    ///
    /// Launched during a run, give it the run's <see cref="ChaseFrame.Velocity"/>
    /// on top of its own, so it flies at the player as they are carried along.
    /// A lobbed one, its spot and its warning ride along with the run until
    /// it lands, as a chunk does, so it comes down where it was marked
    /// relative to the running player.
    /// </remarks>
    public sealed class FlyingSword : MonoBehaviour
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static readonly Collider[] Touching = new Collider[32];

        // Heights the warning circles take turns at, so no two share a
        // plane, as Stone Swords' do.
        private const int WarningLayers = 8;
        private const float WarningLayerStep = 0.012f;
        private static int nextWarningLayer;

        [Serializable]
        public sealed class Settings
        {
            [Header("Look")]
            [Tooltip("The sword's art: the BladeInStone prefab, whose Blade alone is used, or any sword model. Empty uses a plain long box.")]
            public GameObject art;

            [Tooltip("Multiple of the art's own scale, or the box's length.")]
            [Min(0.01f)] public float size = 1f;

            [Tooltip("Turns the art so its point faces along forward (local +Z). A Stone Sword's blade is pointed already; this turns it further.")]
            public Vector3 artRotation;

            [Tooltip("Seconds the blade takes to turn from black to its colour.")]
            [Min(0f)] public float tintTime = 0.2f;

            [Tooltip("Seconds of travel the trail covers.")]
            [Min(0.01f)] public float trailTime = 0.3f;

            [Tooltip("Width of the trail at the sword. It narrows to nothing at the tail.")]
            [Min(0.001f)] public float trailWidth = 0.35f;

            [Header("Flight")]
            [Tooltip("Speed it flies at once launched, on top of any run's.")]
            [Min(0f)] public float speed = 22f;

            [Tooltip("Size of the tip for hits and parries.")]
            [Min(0.01f)] public float radius = 0.25f;

            [Tooltip("Seconds it flies before going off in the air by itself.")]
            [Min(0.1f)] public float lifetime = 3f;

            [Tooltip("What sets it off. Leave the Enemy layer out. Triggers are always ignored.")]
            public LayerMask hitMask = ~0;

            [Header("Falling")]
            [Tooltip("Degrees per second a lobbed sword rolls about its length as it falls.")]
            public float fallRoll = 180f;

            [Tooltip("How deep a lobbed sword's point stabs into the floor.")]
            [Min(0f)] public float stabDepth = 0.4f;

            [Tooltip("Width of the outline of the warning circle a lobbed sword marks its spot with.")]
            [Min(0f)] public float warningOutline = 0.12f;

            [Header("Explosion")]
            [Tooltip("Reach of the explosion. A lobbed sword's is given by whoever lobs it instead.")]
            [Min(0.1f)] public float blastRadius = 3f;

            [Tooltip("Damage to the player within reach. A lobbed sword's is given by whoever lobs it instead.")]
            [Min(0f)] public float blastDamage = 15f;

            [Tooltip("How far the camera jolts on the explosion. 0 for none.")]
            [Min(0f)] public float blastShake = 0.3f;

            [Min(0.01f)] public float shakeDuration = 0.2f;

            [Tooltip("The ball of the explosion. Its colour is the sword's.")]
            public BlastSphere.Settings sphere = new BlastSphere.Settings();

            [Tooltip("Sparks of the explosion. Their colour is the sword's.")]
            public EnergyBurst.Settings burst = new EnergyBurst.Settings
            {
                flashSize = 2f,
                sparkCount = 18,
                sparkSize = 0.35f,
                sparkSpeed = new Vector2(6f, 14f),
                sparkLife = 0.45f,
            };

            [Tooltip("Sparks when a parry catches the sword. Their colour is the sword's.")]
            public EnergyBurst.Settings shatter = new EnergyBurst.Settings
            {
                flashSize = 0.8f,
                flashTime = 0.12f,
                sparkCount = 8,
                sparkSize = 0.2f,
                sparkSpeed = new Vector2(3f, 7f),
                sparkLife = 0.3f,
            };

            [Header("Dismissed")]
            [Tooltip("Seconds a dismissed sword takes to shrink away.")]
            [Min(0.01f)] public float shrinkTime = 0.25f;
        }

        private Settings settings;
        private Color colour;
        private Renderer[] parts;
        private MaterialPropertyBlock block;
        private TrailRenderer trail;
        private Vector3 velocity;
        private Vector3 baseScale;
        private float tipReach;
        private float hiltReach;
        private bool launched;
        private float age;
        private float flightAge;

        // Seconds into the shrink once dismissed, or negative.
        private float shrinkClock = -1f;

        // Lobbed: where from and to, how long and high, how far a run has
        // carried it since, and the warning on its spot.
        private bool lobbed;
        private Vector3 lobFrom;
        private Vector3 lobTo;
        private float lobTime;
        private float lobArc;
        private Vector3 drift;
        private Vector3 lobAcross;
        private float roll;
        private Vector3 warningLift;
        private WarningCircle warning;

        // Reach of this one's explosion: the settings', or a lob's own.
        private float blastRadius;
        private float blastDamage;

        /// <summary>True once launched.</summary>
        public bool Launched => launched;

        /// <summary>
        /// A sword at the given place, its point along <paramref name="rotation"/>'s
        /// forward, lighting up in <paramref name="swordColour"/>, held until launched.
        /// </summary>
        public static FlyingSword Create(Vector3 position, Quaternion rotation, Settings settings, Color swordColour)
        {
            GameObject host = new GameObject("FlyingSword");
            host.transform.SetPositionAndRotation(position, rotation);

            GameObject art = MakeArt(settings, out Quaternion artRotation);
            art.name = "Art";
            art.transform.SetParent(host.transform, false);
            art.transform.localPosition = Vector3.zero;
            art.transform.localRotation = artRotation;

            FlyingSword sword = host.AddComponent<FlyingSword>();
            sword.settings = settings;
            sword.colour = swordColour;
            sword.blastRadius = settings.blastRadius;
            sword.blastDamage = settings.blastDamage;
            sword.baseScale = host.transform.localScale;
            sword.parts = art.GetComponentsInChildren<Renderer>();
            sword.block = new MaterialPropertyBlock();
            sword.MeasureEnds();
            sword.Paint(Color.black);
            return sword;
        }

        /// <summary>
        /// The art, at its size: a Stone Sword's blade alone, any other model
        /// whole, or a box; and the turn that points it along forward.
        /// </summary>
        private static GameObject MakeArt(Settings settings, out Quaternion artRotation)
        {
            GameObject art;
            artRotation = Quaternion.Euler(settings.artRotation);

            if (settings.art == null)
            {
                art = GameObject.CreatePrimitive(PrimitiveType.Cube);
                art.transform.localScale = new Vector3(0.18f, 0.05f, 1.6f) * settings.size;

                MeshRenderer box = art.GetComponent<MeshRenderer>();
                box.sharedMaterial = FlatMaterial.Shared;
                box.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                // A box is built pointing along +Z already.
                artRotation = Quaternion.identity;
            }
            else
            {
                art = Instantiate(ArtTemplate.Of(settings.art));
                art.transform.localScale = settings.art.transform.localScale;

                Transform blade = settings.art.GetComponent<StoneSword>() != null ? art.transform.Find("Blade") : null;

                if (blade != null)
                {
                    // Keeps its size in the world as it leaves the rock behind.
                    blade.SetParent(null, true);
                    Destroy(art);
                    art = blade.gameObject;

                    // Built tip down its local -Y: turned to point along +Z.
                    artRotation = Quaternion.Euler(-90f, 0f, 0f) * artRotation;
                }

                art.transform.localScale *= settings.size;
            }

            foreach (Collider part in art.GetComponentsInChildren<Collider>())
            {
                Destroy(part);
            }

            return art;
        }

        /// <summary>Poses the held sword. Ignored once launched.</summary>
        public void Hold(Vector3 position, Quaternion rotation)
        {
            if (!launched && shrinkClock < 0f)
            {
                transform.SetPositionAndRotation(position, rotation);
            }
        }

        /// <summary>
        /// Sends it point first along <paramref name="direction"/> at its own
        /// speed, plus <paramref name="carried"/>, such as a run's.
        /// </summary>
        public void Launch(Vector3 direction, Vector3 carried)
        {
            if (launched || shrinkClock >= 0f)
            {
                return;
            }

            Vector3 heading = direction.sqrMagnitude > 0.000001f ? direction.normalized : transform.forward;
            velocity = heading * settings.speed + carried;
            launched = true;
            flightAge = 0f;

            transform.rotation = Quaternion.LookRotation(heading, Vector3.up);
            Paint(colour);
            BuildTrail();
        }

        /// <summary>
        /// Throws it, point leading, up in an arc <paramref name="arcHeight"/>
        /// above the straight line, to stab into <paramref name="floor"/>, a
        /// point on the floor, after <paramref name="seconds"/>, its spot
        /// marked meanwhile. It explodes <paramref name="reach"/> across for
        /// <paramref name="damage"/>, in place of the settings' Blast Radius
        /// and Blast Damage.
        /// </summary>
        public void Lob(Vector3 floor, float seconds, float arcHeight, float reach, float damage)
        {
            if (launched || shrinkClock >= 0f)
            {
                return;
            }

            launched = true;
            lobbed = true;
            lobFrom = Tip();
            lobTo = floor;
            lobTime = Mathf.Max(0.05f, seconds);
            lobArc = arcHeight;
            blastRadius = Mathf.Max(0.1f, reach);
            blastDamage = Mathf.Max(0f, damage);
            flightAge = 0f;
            roll = UnityEngine.Random.Range(0f, 360f);

            // What the point's up is turned from: across the way it travels.
            Vector3 travel = lobTo - lobFrom;
            travel.y = 0f;
            lobAcross = travel.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, travel.normalized) : Vector3.right;

            warningLift = Vector3.up * (0.02f + WarningLayerStep * (nextWarningLayer++ % WarningLayers));
            warning = WarningCircle.Create(floor + warningLift, blastRadius, settings.warningOutline, null, colour);

            Pose(0f);
            BuildTrail();
        }

        /// <summary>Shrinks a held sword away where it is. A flying one carries on.</summary>
        public void Dismiss()
        {
            if (!launched && shrinkClock < 0f)
            {
                shrinkClock = 0f;
            }
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            age += deltaTime;

            if (shrinkClock >= 0f)
            {
                Shrinking(deltaTime);
                return;
            }

            if (!launched || lobbed)
            {
                Paint(settings.tintTime > 0f ? Color.Lerp(Color.black, colour, age / settings.tintTime) : colour);
            }

            if (!launched)
            {
                return;
            }

            if (lobbed)
            {
                Falling(deltaTime);
                return;
            }

            Vector3 from = Tip();
            Vector3 step = velocity * deltaTime;

            if (ParryBarrier.TryCatch(from, from + step, settings.radius, velocity))
            {
                EnergyBurst.Create(from, Recolour(settings.shatter));
                Spend();
                return;
            }

            float distance = step.magnitude;

            if (distance > 0f && TryHit(from, step / distance, distance, out RaycastHit hit))
            {
                Explode(from + step / distance * hit.distance);
                return;
            }

            transform.position += step;
            flightAge += deltaTime;

            if (flightAge >= settings.lifetime)
            {
                Explode(Tip());
            }
        }

        /// <summary>One step of a lob: along the arc, riding a run, its warning filling; on landing, the stab and the blast.</summary>
        private void Falling(float deltaTime)
        {
            Vector3 from = Tip();

            flightAge += deltaTime;
            drift += ChaseFrame.Step(deltaTime);
            roll += settings.fallRoll * deltaTime;

            float t = Mathf.Clamp01(flightAge / lobTime);
            Pose(t);

            if (ParryBarrier.TryCatch(from, Tip(), settings.radius, (Tip() - from) / Mathf.Max(deltaTime, 0.0001f)))
            {
                EnergyBurst.Create(from, Recolour(settings.shatter));
                Spend();
                return;
            }

            if (warning != null)
            {
                warning.transform.position = lobTo + drift + warningLift;
                warning.Fill = t;
            }

            if (t >= 1f)
            {
                Explode(lobTo + drift);
            }
        }

        /// <summary>
        /// The lobbed sword <paramref name="t"/> of the way along its arc: its
        /// point on the arc, plus how far a run has carried it, leading along
        /// the arc's way, rolled about its length. As it lands the point
        /// sinks into the floor by the Stab Depth.
        /// </summary>
        private void Pose(float t)
        {
            Vector3 tip = Vector3.Lerp(lobFrom, lobTo, t) + Vector3.up * (lobArc * 4f * t * (1f - t)) + drift;
            Vector3 heading = (lobTo - lobFrom) + Vector3.up * (lobArc * 4f * (1f - 2f * t));
            heading = heading.sqrMagnitude > 0.0001f ? heading.normalized : Vector3.down;

            // Up kept square to the heading, from across the way it travels.
            Vector3 up = Vector3.Cross(heading, lobAcross);
            Quaternion rotation = Quaternion.AngleAxis(roll, heading) * Quaternion.LookRotation(heading, up);

            // Only sinks in over the very end, as it lands.
            tip += heading * (settings.stabDepth * Mathf.Clamp01((t - 0.95f) / 0.05f));
            transform.SetPositionAndRotation(tip - rotation * Vector3.forward * tipReach, rotation);
        }

        private void Explode(Vector3 point)
        {
            BlastSphere.Settings ball = settings.sphere;
            BlastSphere.Create(point, blastRadius, new BlastSphere.Settings
            {
                growTime = ball.growTime,
                fadeTime = ball.fadeTime,
                colour = colour,
                outlineWidth = ball.outlineWidth,
                outlineColour = ball.outlineColour,
                material = ball.material,
            });
            EnergyBurst.Create(point, Recolour(settings.burst));
            TopDownCamera.Shake(settings.blastShake, settings.shakeDuration);

            int count = Physics.OverlapSphereNonAlloc(point, blastRadius, Touching, ~0,
                                                      QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                PlayerHealth player = Touching[i].GetComponentInParent<PlayerHealth>();

                if (player != null)
                {
                    player.TakeDamage(blastDamage, point);
                    break;
                }
            }

            Spend();
        }

        private void Spend()
        {
            DropTrail();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (warning != null)
            {
                Destroy(warning.gameObject);
            }
        }

        /// <summary>The nearest thing on the path that is neither the boss nor a trigger.</summary>
        private bool TryHit(Vector3 from, Vector3 direction, float distance, out RaycastHit nearest)
        {
            int count = Physics.SphereCastNonAlloc(from, settings.radius, direction, Hits, distance,
                                                   settings.hitMask, QueryTriggerInteraction.Ignore);
            nearest = default;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Hits[i];

                if (hit.collider.GetComponentInParent<Enemy>() != null)
                {
                    continue;
                }

                if (!found || hit.distance < nearest.distance)
                {
                    nearest = hit;
                    found = true;
                }
            }

            return found;
        }

        private void Shrinking(float deltaTime)
        {
            shrinkClock += deltaTime;
            float left = 1f - shrinkClock / settings.shrinkTime;

            if (left <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            transform.localScale = baseScale * left;
        }

        private Vector3 Tip()
        {
            return transform.position + transform.forward * tipReach;
        }

        /// <summary>How far ahead of the pivot along forward the art reaches, to its tip, and how far behind, to its hilt.</summary>
        private void MeasureEnds()
        {
            float furthest = 0f;
            float nearest = 0f;

            foreach (MeshFilter part in GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = part.sharedMesh;

                if (mesh == null)
                {
                    continue;
                }

                Bounds bounds = mesh.bounds;

                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 local = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    float along = Vector3.Dot(part.transform.TransformPoint(local) - transform.position, transform.forward);
                    furthest = Mathf.Max(furthest, along);
                    nearest = Mathf.Min(nearest, along);
                }
            }

            tipReach = furthest;
            hiltReach = -nearest;
        }

        private void BuildTrail()
        {
            GameObject host = new GameObject("Trail");
            host.transform.SetParent(transform, false);
            // From the hilt, so the trail streams out behind the whole blade.
            host.transform.localPosition = Vector3.back * hiltReach;

            trail = host.AddComponent<TrailRenderer>();
            trail.time = settings.trailTime;
            trail.widthMultiplier = settings.trailWidth;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.minVertexDistance = 0.05f;
            trail.numCapVertices = 0;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = FlatMaterial.DoubleSided;

            if (trail.sharedMaterial != null)
            {
                FlatMaterial.Tint(trail, colour, new MaterialPropertyBlock());
            }
        }

        /// <summary>Leaves the trail in the world to shrink away.</summary>
        private void DropTrail()
        {
            if (trail == null)
            {
                return;
            }

            trail.transform.SetParent(null, true);
            trail.emitting = false;
            Destroy(trail.gameObject, trail.time);
            trail = null;
        }

        private EnergyBurst.Settings Recolour(EnergyBurst.Settings from)
        {
            return new EnergyBurst.Settings
            {
                flashSize = from.flashSize,
                flashTime = from.flashTime,
                sparkCount = from.sparkCount,
                sparkSize = from.sparkSize,
                sparkSpeed = from.sparkSpeed,
                sparkLife = from.sparkLife,
                colour = colour,
            };
        }

        private void Paint(Color tint)
        {
            foreach (Renderer part in parts)
            {
                if (part != null)
                {
                    FlatMaterial.Tint(part, tint, block);
                }
            }
        }
    }
}
