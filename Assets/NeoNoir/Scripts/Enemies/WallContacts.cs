using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Remembers the last wall an enemy's body actually touched, for attacks
    /// that bounce it off walls.
    /// </summary>
    /// <remarks>
    /// A cast ahead only sees walls the size of the cast. The body's own
    /// collider can be much wider, and at a shallow angle it reaches the wall
    /// first; physics then just cancels the push into the wall and the enemy
    /// slides along it. This hears the real contacts instead, so whatever
    /// the body touches counts.
    ///
    /// Added at runtime by the attack that needs it, onto the object with the
    /// enemy's Rigidbody, since collision messages only go there. Contacts
    /// facing upward are floor and are ignored, as are the player and
    /// anything off the mask.
    /// </remarks>
    public sealed class WallContacts : MonoBehaviour
    {
        /// <summary>A contact older than this, in seconds, no longer counts as touching.</summary>
        private const float FreshFor = 0.05f;

        private LayerMask mask = ~0;
        private Transform ignore;
        private Vector3 normal;
        private Vector3 point;
        private float touchedAt = float.NegativeInfinity;

        /// <summary>Finds or adds the recorder on <paramref name="enemy"/>, set to hear walls on <paramref name="wallMask"/>.</summary>
        public static WallContacts On(Enemy enemy, LayerMask wallMask)
        {
            WallContacts contacts = enemy.GetComponent<WallContacts>();

            if (contacts == null)
            {
                contacts = enemy.gameObject.AddComponent<WallContacts>();
            }

            contacts.mask = wallMask;
            contacts.ignore = enemy.Target;
            contacts.touchedAt = float.NegativeInfinity;
            return contacts;
        }

        /// <summary>
        /// The last wall touched within the last couple of physics steps: its
        /// flat surface normal, facing back toward the enemy, and where it was
        /// touched. False if nothing has been touched lately.
        /// </summary>
        public bool TryGetWall(out Vector3 wallNormal, out Vector3 contactPoint)
        {
            wallNormal = normal;
            contactPoint = point;
            return Time.time - touchedAt <= Mathf.Max(FreshFor, Time.fixedDeltaTime * 2f);
        }

        /// <summary>Forgets the last wall, once it has been bounced off.</summary>
        public void Clear()
        {
            touchedAt = float.NegativeInfinity;
        }

        private void OnCollisionEnter(Collision collision)
        {
            Record(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            Record(collision);
        }

        private void Record(Collision collision)
        {
            Transform other = collision.transform;

            if ((mask.value & (1 << collision.gameObject.layer)) == 0
                || (ignore != null && other.IsChildOf(ignore)))
            {
                return;
            }

            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                Vector3 flat = contact.normal;

                // Floor, or a slope gentle enough to walk on.
                if (flat.y > 0.7f)
                {
                    continue;
                }

                flat.y = 0f;

                if (flat.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                normal = flat.normalized;
                point = contact.point;
                touchedAt = Time.time;
                return;
            }
        }
    }
}
