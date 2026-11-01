using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightAndShadow
{
    /// <summary>
    /// Scarlett the Scythe's fight, all one attack: she leaps to the middle
    /// of the arena and buries herself head first, then, again and again
    /// until she is beaten, tears a rift across the whole arena that hurts
    /// the player, heals her, and lets enemies climb out of it.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The drop: she appears <see cref="dropHeight"/> straight above the
    ///      enemy's <see cref="Enemy.ArenaCentre"/>, side on, turned
    ///      <see cref="leapSpin"/> degrees edge first, and drops onto it over
    ///      <see cref="dropTime"/> (see <see cref="BladeDrop"/>), while a
    ///      <see cref="WarningCircle"/> of <see cref="landingRadius"/>
    ///      fills where she will land. She comes down head first, sunk
    ///      <see cref="landingSink"/> into the floor, with a heavy impact --
    ///      a <see cref="HammerImpact"/>, a <see cref="BlastSphere"/> and a
    ///      jolt of the camera -- and <see cref="landingDamage"/> to the
    ///      player inside the circle. Her colliders are off in the air. Without
    ///      an arena centre she drops where she is. She stays
    ///      buried there, unmoving, for the rest of the fight.
    ///   2. Rifts: straight away, then every <see cref="riftInterval"/>
    ///      seconds, a <see cref="ScarlettClaw"/> tears open through the arena
    ///      centre from end to end, running from the back (the
    ///      <see cref="Enemy.Summoner"/>, Luna) to the front. Its length is
    ///      measured across the enemy's <see cref="Enemy.Arena"/>, or is
    ///      <see cref="fallbackLength"/> without one; the settings' Span is
    ///      ignored. It cuts only the player, once, for its Damage, and each
    ///      time it does, she heals <see cref="riftHeal"/>. Enemies it passes
    ///      by.
    ///   3. Out of each rift, <see cref="spawnCount"/> enemies, picked from
    ///      <see cref="units"/> by weight, rise at random spots along it, each
    ///      with <see cref="spawnHealth"/> of their usual health,
    ///      as the tear opens past it, over <see cref="riseTime"/> (see
    ///      <see cref="RiftRise"/>). They can be hit while rising, and wake
    ///      once up. There is no cap.
    ///
    /// When she dies, every enemy her rifts brought in still alive fades away
    /// over <see cref="dismissFadeTime"/>.
    ///
    /// The rift's look comes from Scarlett's own blade: its material, tinted
    /// in its slash, overshadow and blood colours, from the
    /// <see cref="ScarlettScythe"/> on <see cref="scarlett"/>'s prefab.
    ///
    /// The attack never ends by itself, so give the enemy only this pattern.
    /// Her art is posed tip down local -Y, flat facing local +Z, as the
    /// player's blades are. Her Rigidbody, if she has one, is kinematic
    /// throughout.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Blade Attacks/Scarlett Rift", fileName = "ScarlettRift")]
    public sealed class ScarlettRiftPattern : AttackPattern
    {
        /// <summary>Gap left under a new unit's body, so it never stands sunk into the floor.</summary>
        private const float FloorLift = 0.05f;

        /// <summary>Spots tried along the rift for each unit before giving it up.</summary>
        private const int SpotTries = 12;

        /// <summary>Step taken outward from the centre when measuring the arena along the rift.</summary>
        private const float MeasureStep = 0.25f;

        /// <summary>Furthest out from the centre the arena is measured.</summary>
        private const float MeasureReach = 200f;

        private static readonly Collider[] overlaps = new Collider[16];

        [Header("Drop to the middle")]
        [SerializeField]
        [Tooltip("How far above where she lands she appears, to drop straight down.")]
        [FormerlySerializedAs("leapHeight")]
        [Min(0f)]
        private float dropHeight = 8f;

        [SerializeField]
        [Tooltip("Seconds the drop takes. She gathers speed all the way down.")]
        [FormerlySerializedAs("leapTime")]
        [Min(0.01f)]
        private float dropTime = 0.9f;

        [SerializeField]
        [Tooltip("Degrees she is turned edge first from side on as she drops. Whole turns bring her down head down.")]
        private float leapSpin = 360f;

        [SerializeField]
        [Tooltip("How far her head sinks into the floor as she lands.")]
        [Min(0f)]
        private float landingSink = 0.3f;

        [SerializeField]
        [Tooltip("Radius of the warning circle where she lands, and of the landing's damage.")]
        [Min(0f)]
        private float landingRadius = 4f;

        [SerializeField]
        [Tooltip("Width of the warning circle's outline.")]
        [Min(0f)]
        private float landingOutline = 0.12f;

        [SerializeField]
        [Tooltip("Damage to the player inside the circle as she lands.")]
        [Min(0f)]
        private float landingDamage = 25f;

        [SerializeField]
        private Color warningColour = new Color(1f, 0.1f, 0.15f);

        [SerializeField]
        [Tooltip("Height above the floor the warning circle is drawn at, to clear a rug or carpet.")]
        [Range(0f, 1f)]
        private float warningHeight = 0.12f;

        [SerializeField]
        [Tooltip("What counts as floor. Leave the Player and Enemy layers out.")]
        private LayerMask groundMask = 1;

        [Header("Heavy impact")]
        [SerializeField]
        [Tooltip("Material for the landing burst, e.g. NeoNoir/Flat in the colour you want. Empty draws no burst.")]
        private Material landingMaterial;

        [SerializeField]
        private HammerImpact.Settings landing = new HammerImpact.Settings
        {
            ringRadius = 6f,
            ringWidth = 1f,
            ringTime = 0.45f,
            debrisCount = 14,
            debrisSize = 0.5f,
            debrisUpSpeed = new Vector2(6f, 10f),
            debrisOutSpeed = new Vector2(3f, 7f),
            debrisTime = 0.8f,
        };

        [SerializeField]
        [Tooltip("Radius of the explosion sphere the landing bursts into. 0 for none.")]
        [Min(0f)]
        private float blastRadius = 3.5f;

        [SerializeField]
        private BlastSphere.Settings blast = new BlastSphere.Settings
        {
            growTime = 0.12f,
            fadeTime = 0.35f,
            colour = new Color(3f, 0.2f, 0.3f, 1f),
            outlineWidth = 0.15f,
            outlineColour = new Color(2f, 2f, 2f, 1f),
        };

        [SerializeField]
        [Tooltip("How far the landing jolts the camera. 0 for none.")]
        [Min(0f)]
        private float landingShake = 0.8f;

        [SerializeField]
        [Min(0.01f)]
        private float landingShakeDuration = 0.4f;

        [Header("Rift")]
        [SerializeField]
        [Tooltip("Scarlett's blade. The rift takes its material and colours from the ScarlettScythe on its prefab.")]
        private BladeDefinition scarlett;

        [SerializeField]
        [Tooltip("Seconds from one rift to the next. The first opens as soon as she lands.")]
        [Min(0.1f)]
        private float riftInterval = 5f;

        [SerializeField]
        [Tooltip("Health she regains each time a rift cuts the player.")]
        [Min(0f)]
        private float riftHeal = 100f;

        [SerializeField]
        [Tooltip("Height above the floor the rift is torn at.")]
        private float riftHeight = 1f;

        [SerializeField]
        [Tooltip("Length of the rift when the enemy has no arena to measure.")]
        [Min(0.1f)]
        private float fallbackLength = 40f;

        [SerializeField]
        [Tooltip("The tear. Damage is what it does to the player; Thickness is its width, as drawn and as hit. Span is ignored: it reaches end to end of the arena.")]
        private ScarlettClaw.Settings rift = new ScarlettClaw.Settings
        {
            damage = 20f,
            span = 40f,
            speed = 60f,
            thickness = 5f,
            bow = 0.5f,
            kinks = 4,
            kinkSize = 0.6f,
            raggedness = 0.6f,
            splinters = 16,
            splinterLength = 1.8f,
            hold = 0.3f,
            fadeTime = 0.7f,
            lift = 4f,
        };

        [Header("Enemies from the rift")]
        [SerializeField]
        [Tooltip("Enemies rising out of each rift.")]
        [Min(0)]
        private int spawnCount = 3;

        [SerializeField]
        [Tooltip("Enemies to pick from, with how likely each is.")]
        private List<HammerSummonPattern.SummonOption> units = new List<HammerSummonPattern.SummonOption>();

        [SerializeField]
        [Tooltip("Share of its prefab's health each enemy rises with.")]
        [Range(0.01f, 1f)]
        private float spawnHealth = 0.3f;

        [SerializeField]
        [Tooltip("Seconds an enemy takes to rise out of the floor. It can be hit meanwhile, and wakes once up.")]
        [Min(0.01f)]
        private float riseTime = 1f;

        [SerializeField]
        [Tooltip("Room kept all round a rising enemy's body, clear of walls, Scarlett, the player and other enemies.")]
        [Min(0f)]
        private float spawnClearance = 0.5f;

        [SerializeField]
        [Tooltip("Seconds the enemies still alive take to fade away once Scarlett dies.")]
        [Min(0.01f)]
        private float dismissFadeTime = 1f;

        // Tinted copy of Scarlett's swipe material, shared by every boss.
        [NonSerialized] private Material riftMaterial;
        [NonSerialized] private ScarlettScythe riftMaterialFrom;

        public override IEnumerator Perform(Enemy enemy)
        {
            enemy.VelocityOverride = Vector3.zero;

            // Posed by hand for the whole fight: the enemy's own turning
            // would right her while she is buried.
            enemy.PhysicsDriven = true;

            Rigidbody body = enemy.GetComponent<Rigidbody>();

            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                }

                body.isKinematic = true;
            }

            List<Enemy> risen = new List<Enemy>();

            enemy.Health.Died += died =>
            {
                foreach (Enemy unit in risen)
                {
                    RiftRise.Dismiss(unit, dismissFadeTime);
                }

                risen.Clear();
            };

            Vector3 centre = enemy.ArenaCentre ?? enemy.transform.position;

            foreach (object frame in Leap(enemy, centre))
            {
                yield return frame;
            }

            ScarlettScythe scythe = scarlett != null && scarlett.Prefab != null
                ? scarlett.Prefab.GetComponent<ScarlettScythe>()
                : null;

            if (scythe == null)
            {
                Debug.LogWarning($"[ScarlettRiftPattern] {name} has no Scarlett blade with a ScarlettScythe, so the rift is drawn without her material.", this);
            }

            Material material = RiftMaterial(scythe);
            Color colour = scythe != null ? scythe.SwipeColour : warningColour;
            Vector3 floor = FindFloor(centre);

            // Until she dies, which stops this from outside.
            while (true)
            {
                risen.RemoveAll(unit => unit == null || unit.Health.IsDead);

                RiftLine(enemy, floor, out Vector3 start, out Vector3 across, out float length);
                Vector3 middle = start + across * (length * 0.5f) + Vector3.up * riftHeight;
                Vector3 bow = Vector3.Cross(Vector3.up, across);
                PlayerHealth player = enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null;

                ScarlettClaw.SpawnAgainstPlayer(middle, across, bow, length, rift, material, colour, player, lost =>
                {
                    if (lost > 0f && enemy != null)
                    {
                        enemy.Health.Heal(riftHeal);
                    }
                });

                // Where along the rift each enemy rises, nearest the start first.
                List<float> spots = new List<float>();

                for (int i = 0; i < spawnCount; i++)
                {
                    spots.Add(UnityEngine.Random.Range(0f, length));
                }

                spots.Sort();

                float clock = 0f;
                int next = 0;

                do
                {
                    // Each rises as the tear's head passes it.
                    while (next < spots.Count && spots[next] <= clock * rift.speed)
                    {
                        Enemy unit = Raise(enemy, start, across, spots[next], length);

                        if (unit != null)
                        {
                            risen.Add(unit);
                        }

                        next++;
                    }

                    yield return null;
                    clock += Time.deltaTime;
                }
                while (clock < riftInterval);
            }
        }

        /// <summary>
        /// The rift's line on the floor: from the back end of the arena,
        /// through <paramref name="centre"/>, to the front end, along
        /// <paramref name="across"/>.
        /// </summary>
        private void RiftLine(Enemy enemy, Vector3 centre, out Vector3 start, out Vector3 across, out float length)
        {
            Vector3 back = enemy.Summoner != null ? enemy.Summoner.position
                         : enemy.HasTarget ? centre - Flat(enemy.TargetPoint - centre)
                         : centre - Vector3.forward;

            across = Flat(centre - back);
            across = across.sqrMagnitude > 0.0001f ? across.normalized : Vector3.forward;

            float behind;
            float ahead;

            if (enemy.Arena != null)
            {
                behind = Reach(enemy.Arena, centre, -across);
                ahead = Reach(enemy.Arena, centre, across);
            }
            else
            {
                behind = fallbackLength * 0.5f;
                ahead = fallbackLength * 0.5f;
            }

            start = centre - across * behind;
            length = Mathf.Max(0.1f, behind + ahead);
        }

        /// <summary>How far from <paramref name="centre"/> along <paramref name="direction"/> the arena reaches.</summary>
        private static float Reach(ArenaBounds arena, Vector3 centre, Vector3 direction)
        {
            float reach = 0f;

            while (reach < MeasureReach && arena.Contains(centre + direction * (reach + MeasureStep)))
            {
                reach += MeasureStep;
            }

            return reach;
        }

        /// <summary>
        /// One enemy, rising out of the rift near <paramref name="distance"/>
        /// along it. Null if none could be picked or no spot nearby would do.
        /// </summary>
        private Enemy Raise(Enemy enemy, Vector3 start, Vector3 across, float distance, float length)
        {
            Enemy prefab = HammerSummonPattern.Pick(units);

            if (prefab == null)
            {
                return null;
            }

            // Made first, to measure its body; moved to its spot before
            // physics ever steps, or destroyed before it is ever drawn.
            Enemy unit = Instantiate(prefab, enemy.transform.position, prefab.transform.rotation);
            unit.name = prefab.name;
            unit.Health.SetMaxHealth(unit.Health.MaxHealth * spawnHealth);
            Physics.SyncTransforms();

            Bounds unitBody = unit.Health.BodyBounds();
            Vector3 centreOffset = unitBody.center - unit.transform.position;
            float bottomOffset = unitBody.min.y - unit.transform.position.y;
            Vector3 halfSize = unitBody.extents + Vector3.one * spawnClearance;
            halfSize.y = unitBody.extents.y + Mathf.Min(spawnClearance, FloorLift * 0.5f);

            for (int attempt = 0; attempt < SpotTries; attempt++)
            {
                // The first try where the tear's head is; the rest anywhere along it.
                float along = attempt == 0 ? distance : UnityEngine.Random.Range(0f, length);
                Vector3 flat = start + across * along;

                if (!TryFloor(enemy, unit, flat, out float floor))
                {
                    continue;
                }

                Vector3 standing = new Vector3(flat.x, floor + FloorLift - bottomOffset, flat.z);

                if (Blocked(unit, standing + centreOffset, halfSize))
                {
                    continue;
                }

                unit.transform.position = standing;
                Physics.SyncTransforms();
                unit.TurnModelTowardTarget(360f);
                RiftRise.Begin(unit, standing, riseTime);
                return unit;
            }

            Destroy(unit.gameObject);
            return null;
        }

        /// <summary>Height of the arena floor at <paramref name="spot"/>, ignoring the boss and the unit.</summary>
        private bool TryFloor(Enemy enemy, Enemy unit, Vector3 spot, out float floor)
        {
            if (enemy.Arena != null)
            {
                floor = spot.y;
                return enemy.Arena.Contains(spot) && enemy.Arena.FloorAt(spot, unit.transform, out floor);
            }

            float nearest = float.MaxValue;
            floor = spot.y;

            foreach (RaycastHit hit in Physics.RaycastAll(spot + Vector3.up * 20f, Vector3.down, 40f,
                                                          groundMask, QueryTriggerInteraction.Ignore))
            {
                Transform part = hit.collider.transform;

                if (part.IsChildOf(enemy.transform) || part.IsChildOf(unit.transform) || hit.distance >= nearest)
                {
                    continue;
                }

                nearest = hit.distance;
                floor = hit.point.y;
            }

            return nearest < float.MaxValue;
        }

        /// <summary>Whether anything solid but the unit itself is inside the box.</summary>
        private static bool Blocked(Enemy unit, Vector3 centre, Vector3 halfSize)
        {
            int count = Physics.OverlapBoxNonAlloc(centre, halfSize, overlaps, Quaternion.identity,
                                                   Physics.AllLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (!overlaps[i].transform.IsChildOf(unit.transform))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A drop straight down onto <paramref name="centre"/>, to land head
        /// first in the floor there with a heavy impact, where she stays.
        /// </summary>
        private IEnumerable Leap(Enemy enemy, Vector3 centre)
        {
            Transform root = enemy.transform;
            Transform model = enemy.Model;

            List<Collider> switchedOff = new List<Collider>();

            foreach (Collider part in enemy.GetComponentsInChildren<Collider>())
            {
                if (part.enabled)
                {
                    part.enabled = false;
                    switchedOff.Add(part);
                }
            }

            // Measured with her colliders off, so the ray never finds her.
            Vector3 floor = FindFloor(centre);
            Vector3 start = root.position;

            Vector3 heading = Flat(floor - start);
            heading = heading.sqrMagnitude > 0.0001f ? heading.normalized : Flat(model.forward);
            heading = heading.sqrMagnitude > 0.0001f ? heading.normalized : Vector3.forward;
            Vector3 tumbleAxis = Vector3.Cross(Vector3.up, heading);

            // Side on, her flat square to the way she came, posed head first
            // for the whole drop.
            Quaternion sideOn = Quaternion.LookRotation(tumbleAxis, Vector3.up);
            Quaternion headFirst = Quaternion.AngleAxis(leapSpin, tumbleAxis) * sideOn;
            Vector3 buried = AmberTrianglePattern.Buried(root, model, floor, headFirst, floor.y - landingSink);

            WarningCircle warning = WarningCircle.Create(floor + Vector3.up * warningHeight, landingRadius,
                                                         landingOutline, null, warningColour);
            enemy.FacingOverride = heading;
            model.rotation = headFirst;

            foreach (object frame in BladeDrop.Fall(root, buried, dropHeight, dropTime, warning))
            {
                yield return frame;
            }

            Destroy(warning.gameObject);

            HeavyImpact(floor);
            Hurt(enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null, floor, landingRadius, landingDamage);

            foreach (Collider part in switchedOff)
            {
                if (part != null)
                {
                    part.enabled = true;
                }
            }

            enemy.FacingOverride = null;
        }

        /// <summary>The heavy landing: dust and debris, an explosion sphere, and a hard jolt.</summary>
        private void HeavyImpact(Vector3 floor)
        {
            if (landingMaterial != null)
            {
                HammerImpact.Create(floor, landing, landingMaterial);
            }

            if (blastRadius > 0f)
            {
                BlastSphere.Create(floor, blastRadius, blast);
            }

            if (landingShake > 0f)
            {
                TopDownCamera.Shake(landingShake, landingShakeDuration);
            }
        }

        /// <summary>Hurts <paramref name="player"/> if within <paramref name="radius"/> of <paramref name="centre"/>, measured flat.</summary>
        private static void Hurt(PlayerHealth player, Vector3 centre, float radius, float damage)
        {
            if (player == null || player.IsDead || damage <= 0f)
            {
                return;
            }

            Vector3 offset = Flat(player.transform.position - centre);

            if (offset.sqrMagnitude <= radius * radius)
            {
                player.TakeDamage(damage, centre);
            }
        }

        /// <summary>Scarlett's swipe material, tinted in her colours, made once and shared.</summary>
        private Material RiftMaterial(ScarlettScythe scythe)
        {
            if (scythe == null)
            {
                return null;
            }

            if (riftMaterial == null || riftMaterialFrom != scythe)
            {
                riftMaterial = scythe.CreateSwipeMaterial();
                riftMaterialFrom = scythe;
            }

            return riftMaterial;
        }

        /// <summary>The floor under <paramref name="point"/>, or the point itself if there is none.</summary>
        private Vector3 FindFloor(Vector3 point)
        {
            // From well above, so a raised floor still counts.
            if (Physics.Raycast(point + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 40f,
                                groundMask, QueryTriggerInteraction.Ignore))
            {
                point.y = hit.point.y;
            }

            return point;
        }

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }

        private void Reset()
        {
            delayAfter = 0f;
            Movement.type = MovementType.StandStill;
            groundMask = LayerMask.GetMask("Default");
        }
    }
}
