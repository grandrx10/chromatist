using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Plain copies of art prefabs, for effects that make many of one:
    /// meshes built once, every script and collider stripped, kept out of
    /// sight. Copies of a template cost no more than the objects in it.
    /// </summary>
    /// <remarks>
    /// A ProBuilder model with no mesh saved rebuilds its whole mesh each
    /// time it is copied, so copying the prefab itself makes every copy cost
    /// a full rebuild. A template is the prefab copied once, its mesh built,
    /// then stripped; its own copies share that mesh.
    ///
    /// Templates are parked under one switched-off holder, so they never
    /// show, yet each stays switched on itself: a copy comes out switched on,
    /// with no parent. A scene change destroys them with the holder; they
    /// are made again the next time they are wanted.
    /// </remarks>
    public static class ArtTemplate
    {
        private static readonly Dictionary<GameObject, GameObject> Templates = new Dictionary<GameObject, GameObject>();
        private static Transform holder;

        /// <summary>The plain template for <paramref name="prefab"/>, made the first time it is wanted. Null for no prefab.</summary>
        public static GameObject Of(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }

            if (Templates.TryGetValue(prefab, out GameObject template) && template != null)
            {
                return template;
            }

            // Made active once, so a ProBuilder model builds its meshes.
            template = Object.Instantiate(prefab);
            template.name = prefab.name + " (template)";
            Strip(template);
            Hold(template);

            Templates[prefab] = template;
            return template;
        }

        /// <summary>Removes every script and collider from <paramref name="art"/> and its children, leaving pure looks.</summary>
        public static void Strip(GameObject art)
        {
            foreach (Transform part in art.GetComponentsInChildren<Transform>(true))
            {
                // Last added first: a script another needs goes after it.
                Component[] components = part.GetComponents<Component>();

                for (int i = components.Length - 1; i >= 0; i--)
                {
                    if (components[i] is MonoBehaviour || components[i] is Collider)
                    {
                        Object.DestroyImmediate(components[i]);
                    }
                }
            }
        }

        /// <summary>Parks <paramref name="template"/> out of sight, where its copies still come out switched on.</summary>
        public static void Hold(GameObject template)
        {
            if (holder == null)
            {
                GameObject host = new GameObject("Art Templates");
                host.SetActive(false);
                holder = host.transform;
            }

            template.transform.SetParent(holder, true);
        }
    }
}
