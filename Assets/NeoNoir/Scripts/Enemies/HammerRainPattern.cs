using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Raises the hammer to the sky and holds it there while hammers rain
    /// down on random spots around the arena, each bursting into projectiles
    /// when it lands.
    /// </summary>
    /// <remarks>
    /// The attack's own animation is the raised pose, blended into from idle
    /// over <see cref="raiseBlend"/> seconds, so no separate raising animation
    /// is needed. It is held for the whole attack, then blended back to idle
    /// over <see cref="lowerBlend"/>. The enemy should stand still, which a
    /// new asset's movement is set to.
    ///
    /// Timing counts from the start of the attack, raise included:
    ///   0 to <see cref="raiseTime"/>        the hammer goes up, nothing falls.
    ///   then every <see cref="interval"/>    a new spot is marked, and a
    ///                                       <see cref="HammerDrop"/> starts there.
    ///   <see cref="duration"/>               the attack is over.
    /// A spot is only marked if its hammer lands by the end, so every hammer
    /// is down when the attack ends. With the defaults -- 7 seconds, a 1
    /// second raise, one a second, each taking 1 second to land -- that is 6
    /// hammers, the last landing at 7 seconds.
    ///
    /// Spots are picked at random within <see cref="arenaRadius"/> of the
    /// enemy's <see cref="Enemy.ArenaCentre"/>, which <see cref="BossIntro"/>
    /// sets; without one, around wherever the enemy stands when the attack
    /// starts. Each drop lives on its own once started, so hammers already
    /// falling still land if the enemy dies.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Hammer Rain", fileName = "HammerRain")]
    public sealed class HammerRainPattern : AttackPattern
    {
        [Header("Raise")]
        [SerializeField]
        [Tooltip("Seconds to blend from idle into the raised pose (the Attack Animation above).")]
        [Min(0f)]
        private float raiseBlend = 0.6f;

        [SerializeField]
        [Tooltip("Seconds from the start of the attack before the first spot is marked.")]
        [Min(0f)]
        private float raiseTime = 1f;

        [SerializeField]
        [Tooltip("Seconds to blend from the raised pose back to idle once the attack ends.")]
        [Min(0f)]
        private float lowerBlend = 0.5f;

        [Header("Rain")]
        [SerializeField]
        [Tooltip("Seconds the whole attack lasts, raise included.")]
        [Min(0f)]
        private float duration = 7f;

        [SerializeField]
        [Tooltip("Seconds between one spot being marked and the next.")]
        [Min(0.05f)]
        private float interval = 1f;

        [SerializeField]
        [Tooltip("Spots are picked at random within this far of the arena's centre.")]
        [Min(0f)]
        private float arenaRadius = 12f;

        [SerializeField]
        [Tooltip("What counts as floor when finding the height a hammer lands at. Leave the Player and Enemy layers out.")]
        private LayerMask groundMask = 1;

        [SerializeField] private HammerDrop.Settings drop = new HammerDrop.Settings();

        public override float AttackAnimationBlend => raiseBlend;

        public override float IdleReturnBlend => lowerBlend;

        public override IEnumerator Perform(Enemy enemy)
        {
            Vector3 centre = enemy.ArenaCentre ?? enemy.transform.position;
            float toLand = HammerDrop.TimeToLand(drop);

            float elapsed = 0f;
            float nextDrop = raiseTime;

            while (elapsed < duration)
            {
                // Catches up if a long frame skipped a mark; stops marking
                // once a hammer could no longer land in time.
                while (nextDrop <= elapsed && nextDrop + toLand <= duration + 0.001f)
                {
                    HammerDrop.Create(RandomSpot(enemy, centre), drop);
                    nextDrop += interval;
                }

                yield return null;
                elapsed += Time.deltaTime;
            }
        }

        private Vector3 RandomSpot(Enemy enemy, Vector3 centre)
        {
            Vector2 offset = Random.insideUnitCircle * arenaRadius;
            Vector3 spot = new Vector3(centre.x + offset.x, centre.y, centre.z + offset.y);

            // From well above, so a raised floor still counts; the enemy's
            // own colliders are passed over in case it stands on the spot.
            float nearest = float.MaxValue;

            foreach (RaycastHit hit in Physics.RaycastAll(spot + Vector3.up * 20f, Vector3.down, 40f,
                                                          groundMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(enemy.transform) || hit.distance >= nearest)
                {
                    continue;
                }

                nearest = hit.distance;
                spot.y = hit.point.y;
            }

            return spot;
        }

        private void Reset()
        {
            delayAfter = 0f;
            Movement.type = MovementType.StandStill;
        }
    }
}
