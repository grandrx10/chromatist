using UnityEngine;
using UnityEngine.InputSystem;

namespace LightAndShadow
{
    /// <summary>
    /// Developer shortcuts. K lights every altar in the world.
    /// </summary>
    /// <remarks>
    /// Makes itself on load, in the editor and development builds only, so it
    /// needs no object in any scene and never ships in a release build.
    ///
    /// An altar is lit by draining its health, as if the player had struck it
    /// down, so its rune circle, damage numbers and any
    /// <see cref="SummoningAltar"/> watching it all react as they would in
    /// play. One whose health will not take the blow, being invulnerable, is
    /// lit directly instead.
    /// </remarks>
    public sealed class DevHotkeys : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            GameObject host = new GameObject("DevHotkeys");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            host.AddComponent<DevHotkeys>();
        }
#endif

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.kKey.wasPressedThisFrame)
            {
                LightAllAltars();
            }
        }

        private static void LightAllAltars()
        {
            Altar[] altars = FindObjectsByType<Altar>(FindObjectsSortMode.None);
            int lit = 0;

            foreach (Altar altar in altars)
            {
                if (altar.IsLit)
                {
                    continue;
                }

                // Exactly what is left, undoing any vulnerability, so the
                // damage number shows a sensible amount.
                EnemyHealth health = altar.Health;
                health.TakeDamage(health.CurrentHealth / Mathf.Max(0.0001f, health.DamageTakenMultiplier));

                if (!altar.IsLit)
                {
                    altar.Light();
                }

                lit++;
            }

            Debug.Log($"[DevHotkeys] Lit {lit} altar(s) of {altars.Length}.");
        }
    }
}
