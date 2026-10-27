using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>Which fist a punch is thrown with.</summary>
    public enum PunchHand
    {
        Right,
        Left,
    }

    /// <summary>
    /// A combo of lunging punches, each dashing at the player and firing a
    /// beam of light straight out of the fist on impact.
    /// </summary>
    /// <remarks>
    /// The punches in <see cref="combo"/> follow one another -- by default
    /// right, right, right, left, the rights landing two seconds in and the
    /// left one second in. For each, in order:
    ///   1. Its animation (<see cref="rightAnimation"/> or
    ///      <see cref="leftAnimation"/>) plays from the start, and a
    ///      <see cref="HandRuneCircle"/> forms just ahead of the fist that
    ///      throws it. The enemy stands until the punch's dash start --
    ///      by default none, so the dash begins with the animation.
    ///   2. From then until the punch's impact time the enemy dashes. It sets off
    ///      straight at the player at <see cref="dashSpeed"/>, then homes the
    ///      way the hammer whirl does: it only ever accelerates toward them,
    ///      at <see cref="dashAcceleration"/>, never faster than the dash
    ///      speed, so it swings round in a curve rather than turning sharp
    ///      after a player who sidesteps. At the punch's homing end it stops
    ///      homing and runs dead straight on to the impact, so the last
    ///      stretch can be dodged. It leaves a
    ///      <see cref="DustTrail"/> behind it, and a <see cref="RuneTrail"/>
    ///      of runes that hurt the player who touches them, and brakes once within
    ///      <see cref="stopDistance"/> of the player.
    ///   3. On impact it stops dead, a <see cref="LightningBeam"/> fires from
    ///      the fist the way the model faces, the camera shakes, and the
    ///      circle undraws. It
    ///      stands for <see cref="pauseAfter"/> seconds -- never less than
    ///      the beam lasts, so it is gone -- before the next punch begins.
    /// The model turns toward the player, at the enemy's own turn speed, only
    /// from the start of each punch until its homing ends. From then until
    /// the next punch begins it holds still, so the straight run, the beam
    /// and the pause after all go the way it was facing. After the last punch it
    /// goes to its idle animation, and <see cref="recovery"/> seconds pass
    /// before the attack is over.
    ///
    /// The fists are the objects called <see cref="rightHandName"/> and
    /// <see cref="leftHandName"/> under the enemy; one not found falls back
    /// to the enemy's muzzle. If the enemy dies, the combo stops outright.
    /// Beams already fired finish.
    ///
    /// Leave the Attack Animation above empty: each punch plays its own.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Lightning Punch", fileName = "LightningPunch")]
    public sealed class LightningPunchPattern : AttackPattern
    {
        [Serializable]
        public sealed class Punch
        {
            public PunchHand hand = PunchHand.Right;

            [Tooltip("Seconds into the punch's animation the dash begins. Until then the enemy stands.")]
            [Min(0f)] public float dashStart;

            [Tooltip("Seconds into the punch's animation the dash stops homing. From then to the impact it runs dead straight.")]
            [Min(0f)] public float homingEnds = 1.7f;

            [Tooltip("Seconds into the punch's animation the fist lands: the dash ends and the beam fires.")]
            [Min(0f)] public float impactTime = 2f;
        }

        [Header("Combo")]
        [SerializeField]
        [Tooltip("The punches, in order, each starting once the one before it has landed and paused.")]
        private List<Punch> combo = new List<Punch>
        {
            new Punch { hand = PunchHand.Right, homingEnds = 1.7f, impactTime = 2f },
            new Punch { hand = PunchHand.Right, homingEnds = 1.7f, impactTime = 2f },
            new Punch { hand = PunchHand.Right, homingEnds = 1.7f, impactTime = 2f },
            new Punch { hand = PunchHand.Left, homingEnds = 0.7f, impactTime = 1f },
        };

        [SerializeField]
        [Tooltip("Seconds standing after each punch lands before the next begins. Never shorter than the beam lasts, so one is gone before the next fires.")]
        [Min(0f)]
        private float pauseAfter = 1.5f;

        [SerializeField]
        [Tooltip("Seconds standing in the idle animation after the last punch before the attack counts as over.")]
        [Min(0f)]
        private float recovery = 0.6f;

        [Header("Animations")]
        [SerializeField] private string rightAnimation = "LightningPunchRight";

        [SerializeField] private string leftAnimation = "LightningPunchLeft";

        [Header("Dash")]
        [SerializeField]
        [Tooltip("Speed each dash sets off at, straight at the player, and never goes faster than.")]
        [Min(0f)]
        private float dashSpeed = 14f;

        [SerializeField]
        [Tooltip("Speed gained toward the player every second while dashing. Lower swings wider round a player who sidesteps.")]
        [Min(0f)]
        private float dashAcceleration = 20f;

        [SerializeField]
        [Tooltip("The dash brakes once this close to the player, measured flat.")]
        [Min(0f)]
        private float stopDistance = 2f;

        [SerializeField] private DustTrail.Settings dust = new DustTrail.Settings();

        [Header("Rune trail")]
        [SerializeField]
        [Tooltip("Runes left hanging behind each dash, each hurting the player who touches it.")]
        private RuneTrail.Settings runeTrail = new RuneTrail.Settings();

        [Header("Fists")]
        [SerializeField]
        [Tooltip("Name of the object at the right fist, somewhere under the enemy.")]
        private string rightHandName = "RightHandPoint";

        [SerializeField]
        [Tooltip("Name of the object at the left fist, somewhere under the enemy.")]
        private string leftHandName = "LeftHandPoint";

        [Header("Rune circle")]
        [SerializeField]
        [Tooltip("How far ahead of the fist the circle stands.")]
        private float circleAhead = 0.25f;

        [SerializeField]
        [Tooltip("Degrees per second the circle turns about the punch.")]
        private float circleSpin = 180f;

        [SerializeField]
        [Tooltip("Seconds the circle takes to undraw after the impact.")]
        [Min(0.01f)]
        private float circleEraseTime = 0.15f;

        [SerializeField]
        [Tooltip("Keep Draw Time about the impact time, so it is whole as the punch lands.")]
        private RuneCircle.Settings circle = new RuneCircle.Settings
        {
            radius = 0.45f,
            runeCount = 4,
            runeSize = 0.18f,
            ringWidth = 0.03f,
            runeWidth = 0.025f,
            drawTime = 0.3f,
            flashTime = 0.2f,
            spinSpeed = 0f,
            floorOffset = 0f,
        };

        [Header("Beam")]
        [SerializeField] private LightningBeam.Settings beam = new LightningBeam.Settings();

        [SerializeField]
        [Tooltip("How far the camera shakes as each punch lands. 0 disables it.")]
        [Min(0f)]
        private float shakeStrength = 0.25f;

        [SerializeField]
        [Tooltip("Seconds the camera shake takes to die away.")]
        [Min(0.01f)]
        private float shakeDuration = 0.25f;

        public override IEnumerator Perform(Enemy enemy)
        {
            Transform right = FindPoint(enemy, rightHandName);
            Transform left = FindPoint(enemy, leftHandName);
            DustTrail trail = DustTrail.On(enemy.gameObject, dust);
            RuneTrail runes = RuneTrail.Create(enemy, runeTrail);

            foreach (Punch punch in combo)
            {
                // Free to turn toward the player again, until this punch's
                // homing ends.
                enemy.FacingOverride = null;

                bool isRight = punch.hand == PunchHand.Right;
                Transform hand = isRight ? right : left;
                enemy.RestartAnimation(isRight ? rightAnimation : leftAnimation);

                HandRuneCircle rune = HandRuneCircle.Create(hand, enemy, circle, circleAhead, circleSpin);
                Vector3 direction = Facing(enemy);
                enemy.VelocityOverride = Vector3.zero;

                float impact = Mathf.Max(punch.impactTime, 0f);
                float dashFrom = Mathf.Min(punch.dashStart, impact);
                float elapsed = 0f;

                // The charge-up, standing.
                while (elapsed < dashFrom)
                {
                    yield return null;
                    elapsed += Time.deltaTime;

                    if (enemy.Health.IsDead)
                    {
                        yield break;
                    }
                }

                if (enemy.HasTarget)
                {
                    Vector3 toPlayer = Flat(enemy.TargetPoint - enemy.transform.position);

                    if (toPlayer.sqrMagnitude > 0.0001f)
                    {
                        direction = toPlayer.normalized;
                    }
                }

                Vector3 velocity = direction * dashSpeed;

                // Gone by itself if every rune of the last dash was spent
                // and it sat idle through the pause.
                if (runes == null)
                {
                    runes = RuneTrail.Create(enemy, runeTrail);
                }

                bool locked = false;

                while (elapsed < impact)
                {
                    float deltaTime = Time.deltaTime;

                    // Once homing ends the punch is committed: the model holds
                    // the way it faces now until the next punch begins.
                    if (!locked && elapsed >= punch.homingEnds)
                    {
                        enemy.FacingOverride = Facing(enemy);
                        locked = true;
                    }

                    // Past the homing end it holds its course, straight on.
                    if (enemy.HasTarget && elapsed < punch.homingEnds)
                    {
                        Vector3 toPlayer = Flat(enemy.TargetPoint - enemy.transform.position);

                        if (toPlayer.sqrMagnitude > stopDistance * stopDistance)
                        {
                            // Pulled toward them, never pointed at them: a
                            // sidestep is followed round in a curve.
                            velocity += toPlayer.normalized * (dashAcceleration * deltaTime);
                            velocity = Vector3.ClampMagnitude(velocity, dashSpeed);
                        }
                        else
                        {
                            velocity = Vector3.MoveTowards(velocity, Vector3.zero, dashSpeed * 4f * deltaTime);
                        }
                    }

                    enemy.VelocityOverride = velocity;
                    trail.KeepEmitting();
                    runes.KeepEmitting();

                    yield return null;
                    elapsed += Time.deltaTime;

                    if (enemy.Health.IsDead)
                    {
                        yield break;
                    }
                }

                enemy.VelocityOverride = Vector3.zero;

                if (!locked)
                {
                    enemy.FacingOverride = Facing(enemy);
                }

                // Straight out the way she is facing.
                Vector3 origin = hand != null ? hand.position : enemy.Muzzle.position;
                LightningBeam.Fire(origin, Facing(enemy), enemy.transform, beam);

                if (shakeStrength > 0f)
                {
                    TopDownCamera.Shake(shakeStrength, shakeDuration);
                }
                rune.Release(circleEraseTime);

                // Standing, facing where it punched, as the animation follows through.
                float paused = 0f;
                float pause = Mathf.Max(pauseAfter, beam.growTime + beam.holdTime + beam.shrinkTime);

                while (paused < pause)
                {
                    yield return null;
                    paused += Time.deltaTime;

                    if (enemy.Health.IsDead)
                    {
                        yield break;
                    }
                }
            }

            enemy.VelocityOverride = Vector3.zero;
            enemy.PlayIdleAnimation();

            if (recovery > 0f)
            {
                yield return new WaitForSeconds(recovery);
            }

            enemy.VelocityOverride = null;
            enemy.FacingOverride = null;
        }

        private static Vector3 Facing(Enemy enemy)
        {
            Vector3 forward = Flat(enemy.Model.forward);
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }

        private Transform FindPoint(Enemy enemy, string pointName)
        {
            if (!string.IsNullOrEmpty(pointName))
            {
                foreach (Transform child in enemy.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == pointName)
                    {
                        return child;
                    }
                }
            }

            Debug.LogWarning($"[{name}] No object called \"{pointName}\" under {enemy.name}; using the muzzle.", this);
            return enemy.Muzzle;
        }

        private void Reset()
        {
            delayAfter = 2f;
            beam.wallMask = LayerMask.GetMask("Default");
            beam.hitMask = LayerMask.GetMask("Default", "Player");
        }
    }
}
