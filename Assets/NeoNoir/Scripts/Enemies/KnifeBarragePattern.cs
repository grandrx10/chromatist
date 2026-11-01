using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The assassin's attack: dashes in, locks its facing and slides to a
    /// stop while winding up, then throws a stream of knives scattered at
    /// random across a frontal arc.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. It dashes at the player, always for at least
    ///      <see cref="minDashTime"/>, then on until within
    ///      <see cref="throwRange"/>, however far the player runs. The dash
    ///      homes the way the witch's punch does: it sets off straight at the
    ///      player at <see cref="dashSpeed"/>, then only ever accelerates
    ///      toward them, at <see cref="dashAcceleration"/>, so it swings round
    ///      in a curve after a player who sidesteps. It brakes once within
    ///      <see cref="stopDistance"/>, so a dash begun up close never runs
    ///      through them.
    ///   2. Once the dash is over,
    ///      <see cref="throwAnimation"/> starts, its charge-up included. For the
    ///      first <see cref="trackTime"/> seconds the model still turns toward
    ///      the player, but only at <see cref="trackTurnSpeed"/>, slower than
    ///      the enemy's own turn speed; then it holds the way it is facing.
    ///      The dash's momentum carries on: over the
    ///      <see cref="windup"/> it slides on the way it was going, slowing
    ///      evenly to a stop, and brakes hard once within
    ///      <see cref="stopDistance"/> of the player.
    ///   3. At the end of the windup -- when the animation reaches its throw --
    ///      it throws a knife every <see cref="throwInterval"/> for
    ///      <see cref="barrageDuration"/>, each along the held facing turned by
    ///      a random angle within <see cref="spread"/>. A player who sidesteps
    ///      during the charge can slip out of the arc.
    /// <see cref="dashAnimation"/> plays through the dash, and a
    /// <see cref="DustTrail"/> follows it and the slide. The facing is let go
    /// once the last knife is thrown, so the enemy tracks the player again
    /// while it waits out Delay After.
    ///
    /// Leave the Attack Animation and Range Limit above empty: the attack
    /// plays its own animations and does its own closing in. A range limit
    /// would have the enemy walk into range before the attack ever starts.
    ///
    /// Knives fly flat at the muzzle's height. Their speed, damage and how
    /// far they go -- speed times lifetime -- belong to the knife prefab.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Knife Barrage", fileName = "KnifeBarrage")]
    public sealed class KnifeBarragePattern : AttackPattern
    {
        [SerializeField]
        private EnemyProjectile knifePrefab;

        [Header("Dash")]
        [SerializeField]
        [Tooltip("Dashes in until this close to the player, measured flat, then starts the windup.")]
        [Min(0f)]
        private float throwRange = 8f;

        [SerializeField]
        [Tooltip("Seconds the dash always lasts at least, even when it starts in range.")]
        [Min(0f)]
        private float minDashTime = 0.4f;

        [SerializeField]
        [Tooltip("Speed the dash sets off at, straight at the player, and never goes faster than.")]
        [Min(0f)]
        private float dashSpeed = 14f;

        [SerializeField]
        [Tooltip("Speed gained toward the player every second while dashing in. Lower swings wider round a player who sidesteps.")]
        [Min(0f)]
        private float dashAcceleration = 30f;

        [SerializeField]
        [Tooltip("The dash, and the slide during the windup, brake hard once this close to the player, measured flat.")]
        [Min(0f)]
        private float stopDistance = 2.5f;

        [SerializeField] private DustTrail.Settings dust = new DustTrail.Settings();

        [Header("Barrage")]
        [SerializeField]
        [Tooltip("Total degrees the knives scatter across, centred on the locked facing. Each knife picks a random angle within it.")]
        [Range(0f, 360f)]
        private float spread = 30f;

        [SerializeField]
        [Tooltip("Seconds between knives.")]
        [Min(0.01f)]
        private float throwInterval = 0.1f;

        [SerializeField]
        [Tooltip("Seconds the barrage lasts. One knife is thrown at its start and one every interval after, while time remains.")]
        [Min(0f)]
        private float barrageDuration = 0.7f;

        [SerializeField]
        [Tooltip("Seconds from the start of the throw animation to the first knife, sliding to a stop with the facing locked. Match it to where the animation throws.")]
        [Min(0f)]
        private float windup = 0.7f;

        [SerializeField]
        [Tooltip("Seconds into the windup the model keeps turning toward the player before its facing locks.")]
        [Min(0f)]
        private float trackTime = 0.4f;

        [SerializeField]
        [Tooltip("Degrees per second it turns toward the player while tracking. Keep it under the enemy's own turn speed.")]
        [Min(0f)]
        private float trackTurnSpeed = 120f;

        [Header("Animations")]
        [SerializeField]
        [Tooltip("Played through the dash in.")]
        private string dashAnimation = "AssassinCharge";

        [SerializeField]
        [Tooltip("Played from the moment it is in range: the charge-up, then the throw at Windup seconds in.")]
        private string throwAnimation = "AssassinAttack";

        public override IEnumerator Perform(Enemy enemy)
        {
            DustTrail trail = DustTrail.On(enemy.gameObject, dust);
            Vector3 velocity = enemy.HasTarget ? FlatToTarget(enemy).normalized * dashSpeed : Vector3.zero;
            float dashed = 0f;

            enemy.PlayAnimation(dashAnimation);

            while (enemy.HasTarget)
            {
                Vector3 toPlayer = FlatToTarget(enemy);

                if (dashed >= minDashTime && toPlayer.sqrMagnitude <= throwRange * throwRange)
                {
                    break;
                }

                if (toPlayer.sqrMagnitude > stopDistance * stopDistance)
                {
                    // Pulled toward them, never pointed at them: a sidestep
                    // is followed round in a curve.
                    velocity += toPlayer.normalized * (dashAcceleration * Time.deltaTime);
                    velocity = Vector3.ClampMagnitude(velocity, dashSpeed);
                }
                else
                {
                    velocity = Vector3.MoveTowards(velocity, Vector3.zero, dashSpeed * 4f * Time.deltaTime);
                }

                enemy.VelocityOverride = velocity;
                trail.KeepEmitting();

                yield return null;
                dashed += Time.deltaTime;

                if (enemy.Health.IsDead)
                {
                    yield break;
                }
            }

            // Held on the model's own facing so the enemy's usual, faster
            // turning stays out of it; the tracking below turns it instead.
            enemy.FacingOverride = Facing(enemy);
            enemy.PlayAnimation(throwAnimation);

            // The dash's momentum, bled off evenly so it is gone by the throw.
            float speed = velocity.magnitude;
            float slowing = windup > 0f ? speed / windup : float.PositiveInfinity;
            float elapsed = 0f;

            while (elapsed < windup)
            {
                bool close = enemy.HasTarget && FlatToTarget(enemy).sqrMagnitude <= stopDistance * stopDistance;
                velocity = Vector3.MoveTowards(velocity, Vector3.zero, slowing * (close ? 4f : 1f) * Time.deltaTime);

                enemy.VelocityOverride = velocity;
                trail.KeepEmitting();

                if (elapsed < trackTime && enemy.HasTarget)
                {
                    enemy.TurnModelTowardTarget(trackTurnSpeed * Time.deltaTime);
                    enemy.FacingOverride = Facing(enemy);
                }

                yield return null;
                elapsed += Time.deltaTime;

                if (enemy.Health.IsDead)
                {
                    yield break;
                }
            }

            enemy.VelocityOverride = Vector3.zero;
            Vector3 facing = Facing(enemy);

            int knives = Mathf.FloorToInt(barrageDuration / throwInterval + 0.001f);

            for (int i = 0; i < knives; i++)
            {
                Throw(enemy, facing);
                yield return new WaitForSeconds(throwInterval);
            }

            enemy.PlayIdleAnimation();
            enemy.VelocityOverride = null;
            enemy.FacingOverride = null;
        }

        private void Throw(Enemy enemy, Vector3 facing)
        {
            if (knifePrefab == null)
            {
                Debug.LogWarning($"[{name}] No knife prefab assigned.", this);
                return;
            }

            float halfSpread = spread * 0.5f;
            Vector3 direction = Quaternion.AngleAxis(Random.Range(-halfSpread, halfSpread), Vector3.up) * facing;

            EnemyProjectile knife = Instantiate(knifePrefab, enemy.Muzzle.position, Quaternion.LookRotation(direction, Vector3.up));
            knife.Launch(direction);
        }

        private static Vector3 Facing(Enemy enemy)
        {
            Vector3 forward = Flat(enemy.Model.forward);
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        private static Vector3 FlatToTarget(Enemy enemy)
        {
            return Flat(enemy.TargetPoint - enemy.transform.position);
        }

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }

        private void Reset()
        {
            delayAfter = 1f;
            Movement.type = MovementType.StandStill;
        }
    }
}
