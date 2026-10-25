using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightAndShadow.EditorTools
{
    /// <summary>
    /// Builds a glowing floor line for every prerequisite in the open scene's
    /// skill tree: one <see cref="SkillTreeLink"/> per blade it requires.
    /// </summary>
    /// <remarks>
    /// The blade prefabs list their prerequisites as the Luna PREFAB, not the
    /// Luna in the scene, so each prerequisite is matched to the scene blade
    /// with the same <see cref="BladeDefinition"/>. Links point at scene
    /// blades, so they lay out between the blades where they actually stand.
    ///
    /// Rerunning replaces the links it made before (by name, under the
    /// "Skill Tree Links" object) rather than stacking duplicates. Undoable.
    /// </remarks>
    public static class SkillTreeLinkBuilder
    {
        private const string ParentName = "Skill Tree Links";
        private const string MaterialPath = "Assets/NeoNoir/Materials/NN_White.mat";
        private const float LineWidth = 0.15f;

        [MenuItem("Tools/NeoNoir/Build Skill Tree Links", priority = 20)]
        public static void Build()
        {
            PurchasableBlade[] blades = Object.FindObjectsByType<PurchasableBlade>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (blades.Length == 0)
            {
                EditorUtility.DisplayDialog("Build Skill Tree Links", "There are no Purchasable Blades in the open scene.", "OK");
                return;
            }

            Dictionary<BladeDefinition, PurchasableBlade> byBlade = new Dictionary<BladeDefinition, PurchasableBlade>();

            foreach (PurchasableBlade blade in blades)
            {
                if (blade.Blade != null)
                {
                    byBlade[blade.Blade] = blade;
                }
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Transform parent = FindOrCreateParent();
            int built = 0;

            foreach (PurchasableBlade to in blades)
            {
                foreach (PurchasableBlade prerequisite in to.Requires)
                {
                    if (prerequisite == null || prerequisite.Blade == null
                        || !byBlade.TryGetValue(prerequisite.Blade, out PurchasableBlade from))
                    {
                        Debug.LogWarning($"[SkillTreeLinkBuilder] {to.name} requires a blade that is not in the scene. Skipped.", to);
                        continue;
                    }

                    BuildLink(parent, from, to, material);
                    built++;
                }
            }

            EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);
            Selection.activeTransform = parent;
            Debug.Log($"[SkillTreeLinkBuilder] Built {built} skill tree link(s) under \"{ParentName}\".", parent);
        }

        private static Transform FindOrCreateParent()
        {
            GameObject existing = GameObject.Find(ParentName);

            if (existing != null)
            {
                return existing.transform;
            }

            GameObject parent = new GameObject(ParentName);
            Undo.RegisterCreatedObjectUndo(parent, "Build Skill Tree Links");
            return parent.transform;
        }

        private static void BuildLink(Transform parent, PurchasableBlade from, PurchasableBlade to, Material material)
        {
            string name = $"Link {from.name} -> {to.name}";
            Transform old = parent.Find(name);

            if (old != null)
            {
                Undo.DestroyObjectImmediate(old.gameObject);
            }

            GameObject link = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(link, "Build Skill Tree Links");
            link.transform.SetParent(parent, false);

            // The blade roots stand on the floor, so the line runs at their height.
            float floor = (from.transform.position.y + to.transform.position.y) * 0.5f;
            link.transform.position = new Vector3(0f, floor, 0f);

            LineRenderer line = link.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.widthMultiplier = LineWidth;
            line.numCapVertices = 4;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;

            SkillTreeLink component = link.AddComponent<SkillTreeLink>();
            SerializedObject serialized = new SerializedObject(component);
            serialized.FindProperty("from").objectReferenceValue = from;
            serialized.FindProperty("to").objectReferenceValue = to;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            component.LayLine();
        }
    }
}
