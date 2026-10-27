using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Reusable copies of a piece of art, such as a thrown potion, baked down
    /// to plain meshes so taking one costs next to nothing.
    /// </summary>
    /// <remarks>
    /// The first time a prefab is asked for, it is spawned once and baked: a
    /// copy of its hierarchy with nothing but transforms, mesh filters and
    /// renderers, each mesh copied out so it outlives the original. Every
    /// other component goes -- above all ProBuilder's, which otherwise
    /// rebuild their meshes from scratch on every single copy, the hitch a
    /// ring of potions spawned in one frame used to cause. Colliders go too.
    ///
    /// Copies handed back with <see cref="Return"/> are switched off and kept
    /// for the next <see cref="Take"/>, which only makes a new one when none
    /// is waiting. <see cref="Prewarm"/> makes them ahead of time. The baked
    /// template and the waiting copies live under one hidden object that
    /// survives scene loads; copies out in the world belong to their scene.
    /// </remarks>
    public static class ArtPool
    {
        private sealed class Entry
        {
            public GameObject template;
            public readonly Stack<GameObject> waiting = new Stack<GameObject>();
        }

        private sealed class Member : MonoBehaviour
        {
            public Entry entry;
        }

        private static readonly Dictionary<GameObject, Entry> entries = new Dictionary<GameObject, Entry>();

        private static Transform root;

        /// <summary>A copy of <paramref name="prefab"/>'s art, switched on, at the given place.</summary>
        public static GameObject Take(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            Entry entry = EntryFor(prefab);
            GameObject art = null;

            while (art == null && entry.waiting.Count > 0)
            {
                art = entry.waiting.Pop();
            }

            if (art == null)
            {
                art = MakeCopy(entry);
            }

            art.transform.SetParent(null, false);
            art.transform.SetPositionAndRotation(position, rotation);
            art.SetActive(true);
            return art;
        }

        /// <summary>Makes sure at least <paramref name="count"/> copies of <paramref name="prefab"/> are waiting.</summary>
        public static void Prewarm(GameObject prefab, int count)
        {
            Entry entry = EntryFor(prefab);

            while (entry.waiting.Count < count)
            {
                entry.waiting.Push(MakeCopy(entry));
            }
        }

        /// <summary>Puts a copy from <see cref="Take"/> away for reuse. Anything else is simply destroyed.</summary>
        public static void Return(GameObject art)
        {
            Member member = art.GetComponent<Member>();

            if (member == null || member.entry.template == null)
            {
                Object.Destroy(art);
                return;
            }

            art.SetActive(false);
            art.transform.SetParent(Root(), false);
            member.entry.waiting.Push(art);
        }

        private static Entry EntryFor(GameObject prefab)
        {
            if (entries.TryGetValue(prefab, out Entry entry) && entry.template != null)
            {
                return entry;
            }

            entry = new Entry { template = Bake(prefab) };
            entries[prefab] = entry;
            return entry;
        }

        private static GameObject MakeCopy(Entry entry)
        {
            GameObject copy = Object.Instantiate(entry.template, Root());
            copy.name = entry.template.name;
            copy.AddComponent<Member>().entry = entry;
            return copy;
        }

        private static Transform Root()
        {
            if (root == null)
            {
                GameObject host = new GameObject("ArtPool");
                host.SetActive(false);
                Object.DontDestroyOnLoad(host);
                root = host.transform;
            }

            return root;
        }

        /// <summary>A plain-mesh copy of <paramref name="prefab"/>, kept switched off under the root.</summary>
        private static GameObject Bake(GameObject prefab)
        {
            // Spawned under the switched-off root, so nothing on it ever
            // wakes; the meshes are built here, as the editor saved them or
            // by ProBuilder on the spot.
            GameObject source = Object.Instantiate(prefab, Root());
            GameObject baked = CopyNode(source.transform, Root()).gameObject;
            baked.name = prefab.name;
            baked.transform.localPosition = Vector3.zero;
            baked.transform.localRotation = Quaternion.identity;

            Object.Destroy(source);
            return baked;
        }

        private static Transform CopyNode(Transform from, Transform parent)
        {
            GameObject node = new GameObject(from.name);
            node.layer = from.gameObject.layer;
            node.SetActive(from.gameObject.activeSelf);

            Transform to = node.transform;
            to.SetParent(parent, false);
            to.localPosition = from.localPosition;
            to.localRotation = from.localRotation;
            to.localScale = from.localScale;

            MeshFilter filter = from.GetComponent<MeshFilter>();
            MeshRenderer renderer = from.GetComponent<MeshRenderer>();

            if (filter != null && renderer != null)
            {
                Mesh mesh = MeshOf(from, filter);

                if (mesh != null)
                {
                    node.AddComponent<MeshFilter>().sharedMesh = mesh;

                    MeshRenderer copy = node.AddComponent<MeshRenderer>();
                    copy.sharedMaterials = renderer.sharedMaterials;
                    copy.shadowCastingMode = renderer.shadowCastingMode;
                    copy.receiveShadows = renderer.receiveShadows;
                    copy.enabled = renderer.enabled;
                }
            }

            foreach (Transform child in from)
            {
                CopyNode(child, to);
            }

            return to;
        }

        /// <summary>The node's mesh, copied so it survives the original being destroyed.</summary>
        private static Mesh MeshOf(Transform node, MeshFilter filter)
        {
            if (filter.sharedMesh == null || filter.sharedMesh.vertexCount == 0)
            {
                // ProBuilder builds its mesh in Awake, which a switched-off root never runs.
                RefreshProBuilder(node);
            }

            Mesh mesh = filter.sharedMesh;

            if (mesh == null || mesh.vertexCount == 0)
            {
                return null;
            }

            Mesh copy = Object.Instantiate(mesh);
            copy.name = mesh.name;
            return copy;
        }

        /// <summary>Asks a ProBuilder mesh on <paramref name="node"/>, if any, to build its mesh now.</summary>
        private static void RefreshProBuilder(Transform node)
        {
            UnityEngine.ProBuilder.ProBuilderMesh shape = node.GetComponent<UnityEngine.ProBuilder.ProBuilderMesh>();

            if (shape != null)
            {
                shape.ToMesh();
                shape.Refresh();
            }
        }
    }
}
