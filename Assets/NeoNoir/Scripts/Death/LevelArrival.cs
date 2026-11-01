using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Where a player coming from the previous level's <see cref="LevelExit"/>
    /// is placed: they keep the same place relative to this as they had to
    /// that exit's anchor.
    /// </summary>
    /// <remarks>
    /// Put it on the platform the level starts on, at the spot matching the
    /// previous level's exit anchor, turned the same way relative to the
    /// platform. One per scene. Only used when the exit has an anchor.
    /// </remarks>
    public sealed class LevelArrival : MonoBehaviour
    {
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.6f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1.5f);
        }
    }
}
