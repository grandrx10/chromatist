using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Gathers power at the tip of a sceptre, then releases it in a burst
    /// that sends homing missiles after the player.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The attack's own animation (SceptorAttack) plays, and a
    ///      <see cref="SceptorCharge"/> gathers at the sceptre point: streaks
    ///      rushing in and a red glow swelling there, building over
    ///      <see cref="castTime"/> seconds. <see cref="SceptorRunes"/> spew
    ///      out of it all the while, thicker as it builds, and dissolve.
    ///   2. At the end of the cast the glow goes in an
    ///      <see cref="EnergyBurst"/> with a last burst of runes, the camera
    ///      shakes, and
    ///      <see cref="missileCount"/> <see cref="HomingMissile"/>s fly out of
    ///      it, each in its own direction, before they turn on the player.
    ///   3. <see cref="recovery"/> seconds later, for the animation to finish,
    ///      the attack is over.
    ///
    /// The scatter directions are spread evenly round the compass from a
    /// random start, each nudged by up to <see cref="scatterJitter"/>
    /// degrees and tilted up by up to <see cref="scatterLift"/>, so the
    /// missiles never bunch up and each swings in from its own side.
    ///
    /// The sceptre point is the object called <see cref="sceptorPointName"/>
    /// under the enemy; without one, the enemy's muzzle. If the enemy dies
    /// during the cast, the gathering goes with it and nothing is fired.
    /// Missiles already flying keep going.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Sceptor Attack", fileName = "SceptorAttack")]
    public sealed class SceptorAttackPattern : AttackPattern
    {
        [Header("Timing")]
        [SerializeField]
        [Tooltip("Seconds from the start of the attack animation to the release.")]
        [Min(0f)]
        private float castTime = 2f;

        [SerializeField]
        [Tooltip("Seconds after the release before the attack counts as over. Lets the animation finish.")]
        [Min(0f)]
        private float recovery = 0.5f;

        [Header("Sceptre")]
        [SerializeField]
        [Tooltip("Name of the object at the sceptre's tip, somewhere under the enemy. Not found, the enemy's muzzle is used.")]
        private string sceptorPointName = "SceptorPoint";

        [SerializeField] private SceptorCharge.Settings charge = new SceptorCharge.Settings();

        [SerializeField] private SceptorRunes.Settings runes = new SceptorRunes.Settings();

        [Header("Release")]
        [SerializeField]
        private EnergyBurst.Settings release = new EnergyBurst.Settings
        {
            flashSize = 1.4f,
            flashTime = 0.2f,
            sparkCount = 14,
            sparkSize = 0.35f,
            sparkSpeed = new Vector2(5f, 10f),
            sparkLife = 0.4f,
        };

        [SerializeField]
        [Tooltip("How far the camera shakes on the release. 0 disables it.")]
        [Min(0f)]
        private float shakeStrength = 0.25f;

        [SerializeField]
        [Tooltip("Seconds the camera shake takes to die away.")]
        [Min(0.01f)]
        private float shakeDuration = 0.2f;

        [Header("Missiles")]
        [SerializeField]
        [Tooltip("Art for each missile. Needs no scripts or colliders. Empty draws a glowing ball.")]
        private GameObject missilePrefab;

        [SerializeField]
        [Tooltip("Missiles fired on the release.")]
        [Range(1, 16)]
        private int missileCount = 3;

        [SerializeField]
        [Tooltip("Degrees each scatter direction may stray from an even spread.")]
        [Range(0f, 90f)]
        private float scatterJitter = 30f;

        [SerializeField]
        [Tooltip("Most degrees a missile may be tilted upward as it scatters.")]
        [Range(0f, 80f)]
        private float scatterLift = 25f;

        [SerializeField] private HomingMissile.Settings missile = new HomingMissile.Settings();

        public override IEnumerator Perform(Enemy enemy)
        {
            Transform point = FindPoint(enemy);
            SceptorCharge gathering = SceptorCharge.Create(point, enemy, castTime, charge);
            SceptorRunes spew = SceptorRunes.Create(point, enemy, castTime, runes);

            if (castTime > 0f)
            {
                yield return new WaitForSeconds(castTime);
            }

            if (gathering != null)
            {
                Destroy(gathering.gameObject);
            }

            // A dead enemy's spew stops by itself, without the last burst.
            if (enemy.Health.IsDead || point == null)
            {
                yield break;
            }

            if (spew != null)
            {
                spew.Release();
            }

            Release(enemy, point.position);

            if (recovery > 0f)
            {
                yield return new WaitForSeconds(recovery);
            }
        }

        private void Release(Enemy enemy, Vector3 origin)
        {
            EnergyBurst.Create(origin, release);

            if (shakeStrength > 0f)
            {
                TopDownCamera.Shake(shakeStrength, shakeDuration);
            }

            Transform target = enemy.HasTarget ? enemy.Target : null;
            float start = Random.Range(0f, 360f);

            for (int i = 0; i < missileCount; i++)
            {
                float heading = start + 360f * i / missileCount + Random.Range(-scatterJitter, scatterJitter);
                float lift = Random.Range(0f, scatterLift);
                Vector3 direction = Quaternion.Euler(-lift, heading, 0f) * Vector3.forward;

                GameObject art = missilePrefab != null ? Instantiate(missilePrefab) : null;
                HomingMissile.Launch(art, origin, direction, target, enemy.transform, missile);
            }
        }

        private Transform FindPoint(Enemy enemy)
        {
            if (!string.IsNullOrEmpty(sceptorPointName))
            {
                foreach (Transform child in enemy.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == sceptorPointName)
                    {
                        return child;
                    }
                }
            }

            Debug.LogWarning($"[{name}] No object called \"{sceptorPointName}\" under {enemy.name}; using the muzzle.", this);
            return enemy.Muzzle;
        }

        private void Reset()
        {
            delayAfter = 2f;
            missile.hitMask = LayerMask.GetMask("Default", "Player");
        }
    }
}
