using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// An attack that does nothing: a breather between real attacks.
    /// </summary>
    /// <remarks>
    /// Lasts <see cref="duration"/> seconds, then the usual Delay After. The
    /// enemy follows this attack's movement the whole time, so set it to
    /// Idle to amble about or Stand Still to wait in place. A new asset
    /// starts on Idle with no delay after, so the duration alone decides how
    /// long the breather is.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Idle", fileName = "Idle")]
    public sealed class IdlePattern : AttackPattern
    {
        [SerializeField]
        [Tooltip("Seconds spent idling before the attack counts as over.")]
        [Min(0f)]
        private float duration = 5f;

        public override IEnumerator Perform(Enemy enemy)
        {
            if (duration > 0f)
            {
                yield return new WaitForSeconds(duration);
            }
        }

        private void Reset()
        {
            delayAfter = 0f;
            Movement.type = MovementType.Idle;
        }
    }
}
