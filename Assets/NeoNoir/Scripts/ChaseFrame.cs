using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The moving frame of a forced run, such as <see cref="SpearHallIntro"/>'s
    /// chase: how fast the player is being carried along, so the player's
    /// attacks can happen relative to them rather than to the floor.
    /// </summary>
    /// <remarks>
    /// Zero whenever no run is on, so everything reading it behaves exactly
    /// as normal. While a run is on:
    ///
    ///   riding      what the player leaves where they are -- a lotus, a
    ///               ring on the floor, a claw mark -- moves on by
    ///               <see cref="Step"/> every frame, keeping up with them.
    ///   inheriting  what the player launches -- an arrow, a note, a slash --
    ///               takes <see cref="Velocity"/> as it leaves and keeps it,
    ///               the way a thrown thing keeps its thrower's speed.
    ///
    /// Riding things stop where they are the moment the run ends; launched
    /// things keep their speed until they land.
    /// </remarks>
    public static class ChaseFrame
    {
        /// <summary>How fast the frame is moving, flat. Zero with no run on.</summary>
        public static Vector3 Velocity { get; private set; }

        /// <summary>True while a run is on.</summary>
        public static bool Active => Velocity.sqrMagnitude > 0f;

        /// <summary>Down the run, flat and unit length. Zero with no run on.</summary>
        public static Vector3 Forward => Active ? Velocity.normalized : Vector3.zero;

        /// <summary>How far the frame moves in <paramref name="deltaTime"/>: what a riding thing adds to its position.</summary>
        public static Vector3 Step(float deltaTime) => Velocity * deltaTime;

        /// <summary>Starts a run moving at <paramref name="velocity"/>. Only the flat part counts.</summary>
        public static void Begin(Vector3 velocity)
        {
            Velocity = new Vector3(velocity.x, 0f, velocity.z);
        }

        /// <summary>Ends the run: everything is back to the floor's frame.</summary>
        public static void End()
        {
            Velocity = Vector3.zero;
        }

        /// <summary>
        /// <paramref name="vector"/> with its part along the run taken out,
        /// leaving only across it. Unchanged with no run on.
        /// </summary>
        public static Vector3 Across(Vector3 vector)
        {
            Vector3 forward = Forward;
            return vector - forward * Vector3.Dot(vector, forward);
        }

        // Static state survives leaving play mode when domain reload is off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            Velocity = Vector3.zero;
        }
    }
}
