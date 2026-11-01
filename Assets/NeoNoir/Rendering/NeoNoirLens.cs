using UnityEngine;

namespace LightAndShadow.Rendering
{
    /// <summary>
    /// The one lens bending the picture right now, if any: space warped
    /// round a point on the floor, as round a <see cref="WhiteHole"/>.
    /// <see cref="NeoNoirPostFeature"/> draws it after the edges.
    /// </summary>
    /// <remarks>
    /// Whatever shows a lens calls <see cref="Show"/> every frame it wants
    /// one, and <see cref="Hide"/> when done; a newer caller takes it over.
    /// Drawn only in game cameras, through Hidden/NeoNoir/Lens.
    /// </remarks>
    public static class NeoNoirLens
    {
        public struct Lens
        {
            /// <summary>Its middle, on the floor.</summary>
            public Vector3 centre;

            /// <summary>Radius out to where nothing is bent, in world units, measured on the floor.</summary>
            public float radius;

            /// <summary>Share of the radius drawn as the flat white middle.</summary>
            public float coreFraction;

            /// <summary>How much further out the world is shown at the white middle, as a share of the radius.</summary>
            public float pull;

            /// <summary>Degrees the world is turned round the middle at the white. Negative turns the other way.</summary>
            public float swirl;

            /// <summary>Share of the reach red and blue are split apart by, at the white.</summary>
            public float chroma;

            /// <summary>Degrees the ripples turn the world by.</summary>
            public float ripple;

            /// <summary>Ripples across the lens.</summary>
            public float ripples;

            /// <summary>How fast the ripples run in.</summary>
            public float rippleSpeed;

            /// <summary>Share the pull throbs by.</summary>
            public float pulse;

            /// <summary>Colour of the white middle. Above 1 blooms.</summary>
            public Color coreColour;
        }

        private static object owner;

        /// <summary>Whether a lens is showing.</summary>
        public static bool Active { get; private set; }

        public static Lens Current { get; private set; }

        /// <summary>Shows <paramref name="lens"/> for <paramref name="by"/>, until it hides it.</summary>
        public static void Show(object by, Lens lens)
        {
            owner = by;
            Current = lens;
            Active = true;
        }

        /// <summary>Hides the lens, if <paramref name="by"/> is the one showing it.</summary>
        public static void Hide(object by)
        {
            if (owner == by)
            {
                owner = null;
                Active = false;
            }
        }
    }
}
