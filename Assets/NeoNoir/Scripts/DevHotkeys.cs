using UnityEngine;
using UnityEngine.InputSystem;

namespace LightAndShadow
{
    /// <summary>
    /// Developer shortcuts. K skips straight to Luna's own fight in a level
    /// that has her, and otherwise lights every altar in the world. M kills
    /// the boss whose fight is on outright.
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
    ///
    /// The Luna skip is <see cref="LunaAwakening.SkipToFight"/>: the default
    /// loadout, less Luna, and her fight from the start, past the audience
    /// and every captive weapon.
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
                LunaAwakening luna = FindFirstObjectByType<LunaAwakening>();

                if (luna != null)
                {
                    SkipToLuna(luna);
                }
                else
                {
                    LightAllAltars();
                }
            }

            if (keyboard != null && keyboard.mKey.wasPressedThisFrame)
            {
                KillBoss();
            }
        }

        /// <summary>
        /// Kills every boss brought on by a Boss, Puppeteer or Chromatist
        /// Intro and still alive, past any phase or shield that would stop
        /// the blow, so it dies as if struck down.
        /// </summary>
        private static void KillBoss()
        {
            int killed = 0;

            foreach (BossIntro intro in FindObjectsByType<BossIntro>(FindObjectsSortMode.None))
            {
                killed += Kill(intro.Boss);
            }

            foreach (PuppeteerIntro intro in FindObjectsByType<PuppeteerIntro>(FindObjectsSortMode.None))
            {
                killed += Kill(intro.Boss);
            }

            foreach (ChromatistIntro intro in FindObjectsByType<ChromatistIntro>(FindObjectsSortMode.None))
            {
                killed += Kill(intro.Boss);
            }

            Debug.Log(killed > 0
                ? $"[DevHotkeys] Killed {killed} boss(es)."
                : "[DevHotkeys] No boss to kill: no boss fight has started.");
        }

        private static int Kill(Enemy boss)
        {
            if (boss == null || boss.Health == null || boss.Health.IsDead)
            {
                return 0;
            }

            EnemyHealth health = boss.Health;
            health.Invulnerable = false;
            health.CannotDie = false;
            health.MinimumHealth = 0f;

            // Linked damage reaches it even while intangible (the Puppeteer).
            health.TakeLinkedDamage(health.CurrentHealth);
            return health.IsDead ? 1 : 0;
        }

        private static void SkipToLuna(LunaAwakening luna)
        {
            if (luna.SkipToFight())
            {
                Debug.Log("[DevHotkeys] Skipped to Luna's fight.");
            }
            else
            {
                Debug.Log("[DevHotkeys] Can't skip to Luna's fight: she is already out, or Luna Awakening is missing its boss prefab, captives or player.");
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
