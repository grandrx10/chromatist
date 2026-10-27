using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Takes aim at the player, fires a laser straight ahead and turns with
    /// it, sweeping it round and round the arena, while potions conjured
    /// from runes rain down on it.
    /// </summary>
    /// <remarks>
    /// The attack's own animation (WitchBlast) should loop: it plays for the
    /// whole attack. The enemy stands still, which a new asset's movement is
    /// set to. In order:
    ///   1. Aim: for <see cref="aimTime"/> seconds the enemy turns toward the
    ///      player as usual.
    ///   2. Lock: it stops turning and holds that direction for
    ///      <see cref="lockTime"/> seconds, the warning before it fires,
    ///      while a <see cref="HandRuneCircle"/> writes itself in, dimly,
    ///      upright in front of the hand.
    ///   3. Sweep: a <see cref="SweepBeam"/> fires from the object called
    ///      <see cref="originName"/> (or the muzzle), level, straight ahead,
    ///      and the enemy turns at a steady <see cref="turnSpeed"/> for
    ///      <see cref="sweepTime"/> seconds, the beam with it, round and
    ///      round. It cannot be parried, and burns the player standing in it.
    ///      The circle flares up as it fires, and stays lit in front of the
    ///      hand, the beam running through it.
    ///   4. The beam narrows away and the circle undraws; <see cref="recovery"/> seconds later the
    ///      attack is over.
    ///
    /// From the moment the beam fires until it stops, a
    /// <see cref="RunePotion"/> starts forming every
    /// <see cref="potionInterval"/> seconds at a random spot
    /// <see cref="potionHeight"/> above the floor, within
    /// <see cref="arenaRadius"/> of the enemy's <see cref="Enemy.ArenaCentre"/>
    /// (set by <see cref="BossIntro"/>; without one, round wherever the enemy
    /// stands) and no closer to her than <see cref="minDistanceFromEnemy"/>.
    /// Each forms out of runes, drops and shatters into a pool on its own,
    /// so those still forming when the attack ends still fall.
    ///
    /// The potions' own hit, the pools and the shatters all have their
    /// settings here, under Potion, Pool and Shatter, as for Potion Lob.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Witch Blast", fileName = "WitchBlast")]
    public sealed class WitchBlastPattern : AttackPattern
    {
        public enum TurnDirection
        {
            Random,
            Clockwise,
            Anticlockwise,
        }

        [Header("Aim")]
        [SerializeField]
        [Tooltip("Seconds the enemy turns toward the player before locking on.")]
        [Min(0f)]
        private float aimTime = 1f;

        [SerializeField]
        [Tooltip("Seconds the enemy holds still, aimed, before the beam fires.")]
        [Min(0f)]
        private float lockTime = 1f;

        [Header("Sweep")]
        [SerializeField]
        [Tooltip("Degrees per second the enemy, and the beam with it, turns. Slow enough to outrun.")]
        [Min(0f)]
        private float turnSpeed = 30f;

        [SerializeField]
        [Tooltip("Which way round the beam sweeps, as seen from above.")]
        private TurnDirection turnDirection = TurnDirection.Random;

        [SerializeField]
        [Tooltip("Seconds the beam sweeps for.")]
        [Min(0f)]
        private float sweepTime = 12f;

        [SerializeField]
        [Tooltip("Name of the object the beam fires from, somewhere under the enemy. Empty, or not found, fires from the muzzle.")]
        private string originName = "";

        [SerializeField]
        [Tooltip("Seconds after the beam stops before the attack counts as over.")]
        [Min(0f)]
        private float recovery = 0.5f;

        [SerializeField] private SweepBeam.Settings beam = new SweepBeam.Settings();

        [Header("Rune circle")]
        [SerializeField]
        [Tooltip("How far ahead of the hand the circle stands.")]
        private float circleAhead = 0.4f;

        [SerializeField]
        [Tooltip("Degrees per second the circle turns about the beam.")]
        private float circleSpin = 90f;

        [SerializeField]
        [Tooltip("Seconds the circle takes to undraw once the beam stops.")]
        [Min(0f)]
        private float circleEraseTime = 0.3f;

        [SerializeField]
        [Tooltip("Written in over the lock, dimly at Charging Brightness; Complete Flash is the flare as the beam fires.")]
        private RuneCircle.Settings circle = new RuneCircle.Settings
        {
            radius = 1.4f,
            runeCount = 6,
            runeSize = 0.4f,
            ringWidth = 0.05f,
            runeWidth = 0.04f,
            drawTime = 0.1f,
            chargingBrightness = 0.35f,
            chargeCatchUp = 0.1f,
            completeFlash = 3f,
            flashTime = 0.5f,
            spinSpeed = 0f,
            floorOffset = 0f,
        };

        [Header("Rune potions")]
        [SerializeField]
        [Tooltip("Art for each potion. Needs no scripts or colliders.")]
        private GameObject potionPrefab;

        [SerializeField]
        [Tooltip("Seconds between one potion starting to form and the next.")]
        [Min(0.05f)]
        private float potionInterval = 1.5f;

        [SerializeField]
        [Tooltip("How far from the arena's middle potions may form.")]
        [Min(0f)]
        private float arenaRadius = 18f;

        [SerializeField]
        [Tooltip("Potions never form closer to the enemy than this, measured flat.")]
        [Min(0f)]
        private float minDistanceFromEnemy = 3f;

        [SerializeField]
        [Tooltip("How high above the floor potions form.")]
        [Min(0f)]
        private float potionHeight = 8f;

        [SerializeField]
        [Tooltip("What counts as floor under a potion. Leave the Player and Enemy layers out.")]
        private LayerMask groundMask = 1;

        [SerializeField] private RunePotion.Settings runePotion = new RunePotion.Settings();

        [Header("Potion")]
        [SerializeField]
        private LobbedPotion.Settings potion = new LobbedPotion.Settings { arcHeight = 0f, flightTime = 0.6f, tumbleSpeed = 360f };

        [Header("Pool")]
        [SerializeField] private PotionPool.Settings pool = new PotionPool.Settings { maxRadius = 3f, damagePerSecond = 10f };

        [Header("Shatter")]
        [SerializeField] private PotionShatter.Settings shatter = new PotionShatter.Settings();

        public override IEnumerator Perform(Enemy enemy)
        {
            enemy.VelocityOverride = Vector3.zero;

            if (potionPrefab != null)
            {
                // Enough for every potion that could be forming or falling at once.
                float alive = runePotion.formTime + runePotion.hangTime + potion.flightTime;
                ArtPool.Prewarm(potionPrefab, Mathf.CeilToInt(alive / potionInterval) + 1);
            }

            if (aimTime > 0f)
            {
                yield return new WaitForSeconds(aimTime);
            }

            enemy.FacingOverride = Forward(enemy);

            Transform origin = FindOrigin(enemy);
            HandRuneCircle rune = HandRuneCircle.Create(origin, enemy, circle, circleAhead, circleSpin, false);

            for (float locked = 0f; locked < lockTime; locked += Time.deltaTime)
            {
                rune.Charge(locked / lockTime);
                yield return null;
            }

            rune.Ignite();
            SweepBeam laser = SweepBeam.Fire(origin.position, Forward(enemy), enemy.transform, beam);

            float sign = turnDirection == TurnDirection.Clockwise ? 1f
                       : turnDirection == TurnDirection.Anticlockwise ? -1f
                       : Random.value < 0.5f ? -1f : 1f;

            enemy.SpinOverride = turnSpeed * sign;

            Vector3 centre = enemy.ArenaCentre ?? enemy.transform.position;
            float clock = 0f;
            float nextPotion = 0f;

            while (clock < sweepTime && !enemy.Health.IsDead)
            {
                laser.Aim(origin.position, Forward(enemy));

                if (clock >= nextPotion)
                {
                    nextPotion += potionInterval;
                    ConjurePotion(enemy, centre);
                }

                yield return null;
                clock += Time.deltaTime;
            }

            laser.Stop();
            rune.Release(circleEraseTime);
            enemy.SpinOverride = null;
            enemy.FacingOverride = null;

            if (recovery > 0f)
            {
                yield return new WaitForSeconds(recovery);
            }
        }

        /// <summary>Which way the enemy's model faces, flat.</summary>
        private static Vector3 Forward(Enemy enemy)
        {
            Vector3 forward = enemy.Model.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        private void ConjurePotion(Enemy enemy, Vector3 centre)
        {
            if (potionPrefab == null)
            {
                Debug.LogWarning($"[{name}] No potion prefab assigned.", this);
                return;
            }

            Vector3 spot = centre;

            // A few tries at a spot clear of the enemy; the last is used regardless.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle * arenaRadius;
                spot = centre + new Vector3(offset.x, 0f, offset.y);

                Vector3 fromEnemy = spot - enemy.transform.position;
                fromEnemy.y = 0f;

                if (fromEnemy.sqrMagnitude >= minDistanceFromEnemy * minDistanceFromEnemy)
                {
                    break;
                }
            }

            Vector3 floor = FindFloor(spot);
            RunePotion.Conjure(potionPrefab, floor + Vector3.up * potionHeight, floor, enemy.transform,
                               runePotion, potion, pool, shatter);
        }

        private Transform FindOrigin(Enemy enemy)
        {
            if (string.IsNullOrEmpty(originName))
            {
                return enemy.Muzzle;
            }

            foreach (Transform child in enemy.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == originName)
                {
                    return child;
                }
            }

            Debug.LogWarning($"[{name}] No object called \"{originName}\" under {enemy.name}; firing from the muzzle.", this);
            return enemy.Muzzle;
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

        private void Reset()
        {
            Movement.type = MovementType.StandStill;
            potion.hitMask = LayerMask.GetMask("Default", "Player");
            beam.hitMask = LayerMask.GetMask("Default", "Player");
        }
    }
}
