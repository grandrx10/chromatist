using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LightAndShadow.EditorTools
{
    /// <summary>
    /// Moves a skinned mesh onto another rig: right-click a Skinned Mesh
    /// Renderer, set its Root Bone to a bone in the new rig first, then pick
    /// Rebind Bones To Root Bone's Rig.
    /// </summary>
    /// <remarks>
    /// A skinned mesh is pulled about by its Bones list, not by where it sits
    /// in the hierarchy or by its Root Bone, which only sets the bounds. So
    /// changing the Root Bone alone leaves the mesh following the old rig.
    /// This swaps every bone in the list for the bone of the same name in the
    /// new rig.
    ///
    /// The new rig is the Root Bone's parent and everything under it, so
    /// bones that are siblings of the root, like the legs, are found too. A
    /// bone the new rig lacks falls back to its nearest ancestor that it does
    /// have, and the console lists each one, so nothing is left pointing at
    /// the old rig. Bones that are already missing (the old rig was deleted)
    /// are looked up by name in the model the mesh was imported from.
    ///
    /// Undoable. Only names are matched, so the two rigs should come from the
    /// same skeleton; the mesh's bind poses are left alone, so it sits on the
    /// new bones the way it sat on the old.
    /// </remarks>
    public static class SkinnedMeshRebinder
    {
        private const string MenuPath = "CONTEXT/SkinnedMeshRenderer/Rebind Bones To Root Bone's Rig";

        [MenuItem(MenuPath)]
        private static void Rebind(MenuCommand command)
        {
            SkinnedMeshRenderer skin = (SkinnedMeshRenderer)command.context;
            Transform root = skin.rootBone;

            if (root == null)
            {
                Debug.LogWarning($"[Rebind] Set {skin.name}'s Root Bone to a bone in the rig you want first.", skin);
                return;
            }

            Transform rig = root.parent != null ? root.parent : root;
            Dictionary<string, Transform> byName = new Dictionary<string, Transform>();

            foreach (Transform bone in rig.GetComponentsInChildren<Transform>(true))
            {
                if (!byName.ContainsKey(bone.name))
                {
                    byName.Add(bone.name, bone);
                }
            }

            Transform[] bones = skin.bones;
            Transform[] sourceBones = FindSourceBones(skin);
            int moved = 0;

            for (int i = 0; i < bones.Length; i++)
            {
                Transform old = bones[i];

                if (old == null && sourceBones != null && i < sourceBones.Length)
                {
                    old = sourceBones[i];
                }

                if (old == null || old.IsChildOf(rig))
                {
                    continue;
                }

                Transform match = null;

                for (Transform search = old; search != null && match == null; search = search.parent)
                {
                    byName.TryGetValue(search.name, out match);
                }

                if (match == null)
                {
                    Debug.LogWarning($"[Rebind] No bone in {rig.name} for {old.name} or any of its parents; left as it was.", skin);
                    continue;
                }

                if (match.name != old.name)
                {
                    Debug.Log($"[Rebind] {rig.name} has no {old.name}; using {match.name} instead.", skin);
                }

                bones[i] = match;
                moved++;
            }

            Undo.RecordObject(skin, "Rebind Bones");
            skin.bones = bones;
            EditorUtility.SetDirty(skin);

            Debug.Log($"[Rebind] Moved {moved} of {bones.Length} bones on {skin.name} onto {rig.name}.", skin);
        }

        /// <summary>
        /// The bones the mesh was skinned to in the model it was imported
        /// from, in Bones-list order, or null if it can't be found. Lets a
        /// mesh whose old rig has been deleted still be matched by name.
        /// </summary>
        private static Transform[] FindSourceBones(SkinnedMeshRenderer skin)
        {
            if (skin.sharedMesh == null)
            {
                return null;
            }

            string path = AssetDatabase.GetAssetPath(skin.sharedMesh);
            GameObject model = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (model == null)
            {
                return null;
            }

            foreach (SkinnedMeshRenderer source in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (source.sharedMesh == skin.sharedMesh)
                {
                    return source.bones;
                }
            }

            return null;
        }
    }
}
