using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Holds a <see cref="RuneCircle"/> upright just ahead of a hand, facing
    /// the way the enemy faces, so a punch is thrown through it.
    /// </summary>
    /// <remarks>
    /// Placed in LateUpdate, after the animator has posed the hand, so it
    /// never trails a frame behind the fist. Spins slowly about the punch's
    /// line. Created unlit, it is written in bit by bit with
    /// <see cref="Charge"/> and lit with <see cref="Ignite"/>.
    /// <see cref="Release"/> undraws it, and it undraws itself the same
    /// way if the hand or the enemy goes away, dies or is disabled.
    /// </remarks>
    public sealed class HandRuneCircle : MonoBehaviour
    {
        private RuneCircle circle;
        private Transform hand;
        private Enemy owner;
        private float offset;
        private float spinSpeed;
        private float spin;
        private Vector3 forward = Vector3.forward;
        private bool released;

        /// <summary>
        /// Writes a circle at <paramref name="handPoint"/>,
        /// <paramref name="ahead"/> in front of it, turning at
        /// <paramref name="degreesPerSecond"/>. Unless
        /// <paramref name="ignite"/>, it waits to be charged and lit.
        /// </summary>
        public static HandRuneCircle Create(Transform handPoint, Enemy enemy, RuneCircle.Settings settings,
                                            float ahead, float degreesPerSecond, bool ignite = true)
        {
            RuneCircle circle = RuneCircle.Create(handPoint.position, settings, ignite);

            HandRuneCircle holder = circle.gameObject.AddComponent<HandRuneCircle>();
            holder.circle = circle;
            holder.hand = handPoint;
            holder.owner = enemy;
            holder.offset = ahead;
            holder.spinSpeed = degreesPerSecond;
            holder.spin = Random.Range(0f, 360f);
            holder.LateUpdate();
            return holder;
        }

        /// <summary>Writes the circle in, dimly, up to <paramref name="fraction"/> of the way.</summary>
        public void Charge(float fraction)
        {
            circle.Charge(fraction);
        }

        /// <summary>Finishes the circle and lights it, with its flash.</summary>
        public void Ignite()
        {
            circle.Ignite();
        }

        /// <summary>Undraws the circle over <paramref name="duration"/> seconds, then it is gone.</summary>
        public void Release(float duration)
        {
            if (released)
            {
                return;
            }

            released = true;
            circle.Erase(duration);
        }

        private void LateUpdate()
        {
            if (!released && (hand == null || owner == null || owner.Health.IsDead || !owner.isActiveAndEnabled))
            {
                Release(0.15f);
            }

            if (owner != null)
            {
                Vector3 facing = owner.Model.forward;
                facing.y = 0f;

                if (facing.sqrMagnitude > 0.0001f)
                {
                    forward = facing.normalized;
                }
            }

            spin += spinSpeed * Time.deltaTime;

            Vector3 position = hand != null ? hand.position + forward * offset : transform.position;

            // The circle is drawn in its local XZ plane: its local up along
            // the punch stands it upright across it.
            transform.SetPositionAndRotation(position,
                                             Quaternion.LookRotation(Vector3.up, forward)
                                             * Quaternion.AngleAxis(spin, Vector3.up));
        }
    }
}
