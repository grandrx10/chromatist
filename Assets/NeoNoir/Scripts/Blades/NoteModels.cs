using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Cheap copies of Blue's note prefabs, for spawning many at once.
    /// </summary>
    /// <remarks>
    /// The note prefabs are ProBuilder objects, which store no finished
    /// mesh: every instance rebuilds its meshes from ProBuilder's own data,
    /// and cooks a mesh collider for each part, the moment it is spawned.
    /// A burst spawning ten at once stalls the frame badly.
    ///
    /// So each prefab is spawned once, the first time it is asked for (or in
    /// <see cref="Warm"/>), and copied into a bare template: the same
    /// hierarchy and transforms, with only a mesh filter holding a copy of
    /// the built mesh and a renderer with the same materials. No ProBuilder,
    /// no colliders, no scripts. The original is then thrown away, and every
    /// spawn after that clones the template.
    ///
    /// Templates live under one inactive, hidden object that survives scene
    /// loads.
    /// </remarks>
    public static class NoteModels
    {
        private static readonly Dictionary<GameObject, GameObject> templates = new Dictionary<GameObject, GameObject>();

        private static GameObject holder;

        /// <summary>Builds the templates for <paramref name="prefabs"/> now, so the first spawn of each is as cheap as the rest.</summary>
        public static void Warm(IReadOnlyList<GameObject> prefabs)
        {
            if (prefabs == null)
            {
                return;
            }

            foreach (GameObject prefab in prefabs)
            {
                if (prefab != null)
                {
                    Template(prefab);
                }
            }
        }

        /// <summary>A fresh, active, collider-free copy of <paramref name="prefab"/>'s look, at the origin.</summary>
        public static GameObject Spawn(GameObject prefab)
        {
            GameObject made = Object.Instantiate(Template(prefab));
            made.SetActive(true);
            return made;
        }

        private static GameObject Template(GameObject prefab)
        {
            if (templates.TryGetValue(prefab, out GameObject template) && template != null)
            {
                return template;
            }

            if (holder == null)
            {
                holder = new GameObject("NoteModels") { hideFlags = HideFlags.HideInHierarchy };
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
            }

            // Spawned active, so ProBuilder builds its meshes, then copied.
            GameObject source = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            template = Copy(source.transform, holder.transform).gameObject;
            template.name = prefab.name;
            Object.Destroy(source);

            templates[prefab] = template;
            return template;
        }

        private static Transform Copy(Transform source, Transform parent)
        {
            GameObject copy = new GameObject(source.name) { layer = source.gameObject.layer };

            Transform target = copy.transform;
            target.SetParent(parent, false);
            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;

            MeshFilter filter = source.GetComponent<MeshFilter>();
            MeshRenderer sourceRenderer = source.GetComponent<MeshRenderer>();

            if (filter != null && filter.sharedMesh != null && sourceRenderer != null)
            {
                // A copy: the original's mesh belongs to its ProBuilder
                // component and is destroyed along with it.
                copy.AddComponent<MeshFilter>().sharedMesh = Object.Instantiate(filter.sharedMesh);

                MeshRenderer copyRenderer = copy.AddComponent<MeshRenderer>();
                copyRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
                copyRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
                copyRenderer.receiveShadows = sourceRenderer.receiveShadows;
            }

            copy.SetActive(source.gameObject.activeSelf);

            foreach (Transform child in source)
            {
                Copy(child, target);
            }

            return target;
        }
    }
}
