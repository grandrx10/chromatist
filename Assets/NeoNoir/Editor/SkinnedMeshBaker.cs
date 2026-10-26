using UnityEditor;
using UnityEngine;

namespace LightAndShadow.EditorTools
{
    /// <summary>
    /// Turns a skinned mesh into a plain one stuck to a bone: right-click a
    /// Skinned Mesh Renderer and pick Bake To Static Mesh On Bone.
    /// </summary>
    /// <remarks>
    /// For rigid props like hats. The mesh is frozen exactly as it looks right
    /// now, saved under Assets/NeoNoir/Meshes/&lt;scene&gt;, and the Skinned
    /// Mesh Renderer is swapped for a Mesh Filter and Mesh Renderer with the
    /// same materials. The object is then parented, without moving, to the
    /// bone that pulls on the most vertices, so it still follows that bone
    /// but can be moved and scaled like any other object.
    ///
    /// The object's scale is reset to 1 first, since a skinned mesh ignores
    /// it anyway. The component swap and reparent are undoable; the saved mesh asset is
    /// not removed by undo.
    /// </remarks>
    public static class SkinnedMeshBaker
    {
        private const string MenuPath = "CONTEXT/SkinnedMeshRenderer/Bake To Static Mesh On Bone";
        private const string MeshRoot = "Assets/NeoNoir/Meshes";

        [MenuItem(MenuPath)]
        private static void Bake(MenuCommand command)
        {
            SkinnedMeshRenderer skin = (SkinnedMeshRenderer)command.context;

            if (skin.sharedMesh == null)
            {
                Debug.LogWarning($"[Bake] {skin.name} has no mesh.", skin);
                return;
            }

            Transform bone = FindMainBone(skin);

            if (bone == null)
            {
                Debug.LogWarning($"[Bake] {skin.name} has no bones to follow; rebind it to a rig first.", skin);
                return;
            }

            // A skinned mesh ignores its own scale, so it is easy to have
            // scaled it to nothing without noticing; baking would bake that in.
            Undo.RecordObject(skin.transform, "Bake To Static Mesh");
            skin.transform.localScale = Vector3.one;

            Mesh baked = new Mesh { name = skin.sharedMesh.name + "_baked" };
            skin.BakeMesh(baked, true);
            baked.RecalculateBounds();

            string folder = MeshRoot + "/" + skin.gameObject.scene.name;

            if (!AssetDatabase.IsValidFolder(MeshRoot))
            {
                AssetDatabase.CreateFolder("Assets/NeoNoir", "Meshes");
            }

            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder(MeshRoot, skin.gameObject.scene.name);
            }

            AssetDatabase.CreateAsset(baked, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + skin.name + ".asset"));

            GameObject go = skin.gameObject;
            Material[] materials = skin.sharedMaterials;
            UnityEngine.Rendering.ShadowCastingMode shadows = skin.shadowCastingMode;
            bool receiveShadows = skin.receiveShadows;

            Undo.DestroyObjectImmediate(skin);

            MeshFilter filter = Undo.AddComponent<MeshFilter>(go);
            filter.sharedMesh = baked;

            MeshRenderer renderer = Undo.AddComponent<MeshRenderer>(go);
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = shadows;
            renderer.receiveShadows = receiveShadows;

            Undo.SetTransformParent(go.transform, bone, "Bake To Static Mesh");

            Debug.Log($"[Bake] {go.name} is now a static mesh on {bone.name}.", go);
        }

        /// <summary>The bone with the most skin weight across all vertices.</summary>
        private static Transform FindMainBone(SkinnedMeshRenderer skin)
        {
            Transform[] bones = skin.bones;
            float[] totals = new float[bones.Length];

            foreach (BoneWeight1 weight in skin.sharedMesh.GetAllBoneWeights())
            {
                if (weight.boneIndex < totals.Length)
                {
                    totals[weight.boneIndex] += weight.weight;
                }
            }

            Transform best = null;
            float bestTotal = 0f;

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null && totals[i] > bestTotal)
                {
                    best = bones[i];
                    bestTotal = totals[i];
                }
            }

            return best != null ? best : skin.rootBone;
        }
    }
}
