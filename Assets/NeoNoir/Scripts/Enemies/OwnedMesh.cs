using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Destroys a mesh made at runtime along with the object showing it, so
    /// cut pieces and the like leave nothing behind.
    /// </summary>
    public sealed class OwnedMesh : MonoBehaviour
    {
        public Mesh Mesh { get; set; }

        private void OnDestroy()
        {
            if (Mesh != null)
            {
                Destroy(Mesh);
            }
        }
    }
}
