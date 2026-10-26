using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// NeoNoir/Flat materials made at runtime, for effects that work with no
    /// material assigned.
    /// </summary>
    /// <remarks>
    /// One of each, shared by every effect. Effects colour them through a
    /// property block, never by editing them, so nothing can tint another
    /// effect's copy. Null if the shader is missing.
    /// </remarks>
    public static class FlatMaterial
    {
        public static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static readonly int CullId = Shader.PropertyToID("_Cull");

        private static Material shared;
        private static Material doubleSided;

        /// <summary>The plain material, culling back faces as usual.</summary>
        public static Material Shared
        {
            get
            {
                if (shared == null)
                {
                    shared = Make("Runtime Flat");
                }

                return shared;
            }
        }

        /// <summary>
        /// Draws both faces. For trails and other camera-facing strips, whose
        /// winding can come out either way.
        /// </summary>
        public static Material DoubleSided
        {
            get
            {
                if (doubleSided == null)
                {
                    doubleSided = Make("Runtime Flat (double sided)");

                    if (doubleSided != null && doubleSided.HasProperty(CullId))
                    {
                        doubleSided.SetFloat(CullId, 0f);
                    }
                }

                return doubleSided;
            }
        }

        /// <summary>Sets <paramref name="target"/>'s colour through its property block.</summary>
        public static void Tint(Renderer target, Color colour, MaterialPropertyBlock block)
        {
            target.GetPropertyBlock(block);
            block.SetColor(BaseColorId, colour);
            target.SetPropertyBlock(block);
        }

        private static Material Make(string materialName)
        {
            Shader flat = Shader.Find("NeoNoir/Flat");
            return flat != null ? new Material(flat) { name = materialName } : null;
        }
    }
}
