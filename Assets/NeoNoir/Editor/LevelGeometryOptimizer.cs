using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LightAndShadow.EditorTools
{
    /// <summary>
    /// Cleans up the open scene's ProBuilder geometry without changing how it
    /// looks: Tools > NeoNoir > Optimize Level Geometry.
    /// </summary>
    /// <remarks>
    /// Duplicating a ProBuilder object copies its whole mesh into the scene
    /// twice over -- once as ProBuilder's editable data, once as the Unity
    /// mesh -- so a few hundred copies of one bookshelf weigh hundreds of MB
    /// and every copy is its own mesh on the GPU.
    ///
    /// 1. Every group of two or more objects whose rendered meshes are
    ///    identical is pointed at one shared mesh asset under
    ///    Assets/NeoNoir/Meshes/&lt;scene&gt;, and loses its ProBuilder
    ///    components. Those objects can no longer be edited in ProBuilder
    ///    (ProBuilderize gets editing back). One-off objects -- walls, stairs,
    ///    arches -- stay ProBuilder.
    /// 2. All ProBuilder-made geometry gets Batching, Occluder and Occludee
    ///    Static, except anything a script could move: objects with or under
    ///    a Rigidbody, Animator or Animation, objects with or under a script,
    ///    and anything a script in the scene references.
    ///
    /// Not undoable. The scene is saved first and copied to
    /// Library/SceneBackups, then saved again once done. Objects inside prefab
    /// instances are left alone.
    /// </remarks>
    public static class LevelGeometryOptimizer
    {
        private const string MenuPath = "Tools/NeoNoir/Optimize Level Geometry";
        private const string MeshRoot = "Assets/NeoNoir/Meshes";
        private const string BackupFolder = "Library/SceneBackups";

        private const StaticEditorFlags StaticFlags =
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;

        [MenuItem(MenuPath)]
        private static void Optimize()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("Optimize Level Geometry", "Save the scene first.", "OK");
                return;
            }

            if (scene.isDirty && !EditorSceneManager.SaveScene(scene))
            {
                return;
            }

            List<ProBuilderMesh> geometry = FindGeometry(scene);
            List<List<ProBuilderMesh>> duplicates = GroupDuplicates(geometry);
            List<GameObject> toMakeStatic = FindStaticCandidates(scene, geometry);

            int duplicateCount = 0;
            foreach (List<ProBuilderMesh> group in duplicates)
            {
                duplicateCount += group.Count;
            }

            string summary =
                $"{duplicateCount} objects share {duplicates.Count} identical meshes. They will use one mesh asset " +
                $"per group and stop being ProBuilder objects.\n\n" +
                $"{toMakeStatic.Count} of {geometry.Count} ProBuilder objects will be marked Static " +
                $"({geometry.Count - toMakeStatic.Count} skipped as scripted or moving).\n\n" +
                $"The scene is backed up to {BackupFolder} first. This cannot be undone with Ctrl+Z.";

            if (!EditorUtility.DisplayDialog("Optimize Level Geometry", summary, "Optimize", "Cancel"))
            {
                return;
            }

            string backup = Backup(scene);

            try
            {
                string folder = EnsureMeshFolder(scene.name);
                for (int i = 0; i < duplicates.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Optimize Level Geometry", "Baking shared meshes", (float)i / duplicates.Count);
                    Bake(duplicates[i], folder, scene.name);
                }

                foreach (GameObject go in toMakeStatic)
                {
                    GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(go) | StaticFlags);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[LevelGeometryOptimizer] {scene.name}: baked {duplicateCount} objects into {duplicates.Count} " +
                      $"shared meshes, marked {toMakeStatic.Count} objects Static. Backup: {backup}");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanOptimize()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static List<ProBuilderMesh> FindGeometry(Scene scene)
        {
            var geometry = new List<ProBuilderMesh>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (ProBuilderMesh pb in root.GetComponentsInChildren<ProBuilderMesh>(true))
                {
                    if (!PrefabUtility.IsPartOfPrefabInstance(pb)
                        && pb.TryGetComponent(out MeshFilter filter)
                        && filter.sharedMesh != null)
                    {
                        geometry.Add(pb);
                    }
                }
            }

            return geometry;
        }

        /// <summary>Groups of two or more objects whose rendered meshes match exactly.</summary>
        private static List<List<ProBuilderMesh>> GroupDuplicates(List<ProBuilderMesh> geometry)
        {
            var byFingerprint = new Dictionary<Hash128, List<ProBuilderMesh>>();
            foreach (ProBuilderMesh pb in geometry)
            {
                Hash128 key = Fingerprint(pb.GetComponent<MeshFilter>().sharedMesh);
                if (!byFingerprint.TryGetValue(key, out List<ProBuilderMesh> group))
                {
                    group = new List<ProBuilderMesh>();
                    byFingerprint.Add(key, group);
                }

                group.Add(pb);
            }

            var duplicates = new List<List<ProBuilderMesh>>();
            foreach (List<ProBuilderMesh> group in byFingerprint.Values)
            {
                if (group.Count >= 2)
                {
                    duplicates.Add(group);
                }
            }

            return duplicates;
        }

        private static Hash128 Fingerprint(Mesh mesh)
        {
            var hash = new Hash128();
            hash.Append(mesh.vertexCount);
            hash.Append(mesh.subMeshCount);
            hash.Append(mesh.vertices);
            hash.Append(mesh.normals);
            hash.Append(mesh.tangents);
            hash.Append(mesh.colors);

            var uvs = new List<Vector4>();
            for (int channel = 0; channel < 8; channel++)
            {
                mesh.GetUVs(channel, uvs);
                hash.Append(uvs.ToArray());
            }

            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                hash.Append((int)mesh.GetTopology(i));
                hash.Append(mesh.GetIndices(i));
            }

            return hash;
        }

        /// <summary>Points every object in the group at one saved copy of their mesh.</summary>
        private static void Bake(List<ProBuilderMesh> group, string folder, string sceneName)
        {
            Mesh source = group[0].GetComponent<MeshFilter>().sharedMesh;
            string baseName = Regex.Replace(group[0].name, @"\s*\(\d+\)$", "");

            Mesh shared = Object.Instantiate(source);
            shared.name = $"{sceneName}_{baseName}_{source.vertexCount}v";
            AssetDatabase.CreateAsset(shared, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{shared.name}.asset"));

            foreach (ProBuilderMesh pb in group)
            {
                Strip(pb, shared);
            }
        }

        private static void Strip(ProBuilderMesh pb, Mesh shared)
        {
            GameObject go = pb.gameObject;
            MeshFilter filter = go.GetComponent<MeshFilter>();
            Mesh old = filter.sharedMesh;

            // Otherwise ProBuilderMesh.OnDestroy destroys whatever mesh the
            // filter holds.
            pb.preserveMeshAssetOnDestroy = true;

            // ProBuilderShape, PolyShape and BezierShape need the
            // ProBuilderMesh, so they go first.
            foreach (MonoBehaviour behaviour in go.GetComponents<MonoBehaviour>())
            {
                if (behaviour != null && behaviour != pb && IsProBuilder(behaviour))
                {
                    Object.DestroyImmediate(behaviour);
                }
            }

            Object.DestroyImmediate(pb);

            filter.sharedMesh = shared;
            filter.hideFlags = HideFlags.None;

            if (go.TryGetComponent(out MeshCollider collider) && collider.sharedMesh == old)
            {
                collider.sharedMesh = shared;
            }

            if (old != null && old != shared && !EditorUtility.IsPersistent(old))
            {
                Object.DestroyImmediate(old);
            }
        }

        private static bool IsProBuilder(Object component)
        {
            string ns = component.GetType().Namespace;
            return ns != null && ns.StartsWith("UnityEngine.ProBuilder", StringComparison.Ordinal);
        }

        /// <summary>
        /// The geometry nothing could move at runtime. Static batching bakes
        /// positions in, so a scripted object marked Static would stop moving.
        /// </summary>
        private static List<GameObject> FindStaticCandidates(Scene scene, List<ProBuilderMesh> geometry)
        {
            HashSet<Transform> scripted = FindScriptedTransforms(scene);

            var candidates = new List<GameObject>();
            foreach (ProBuilderMesh pb in geometry)
            {
                if (pb.GetComponentInParent<Rigidbody>(true) == null
                    && pb.GetComponentInParent<Animator>(true) == null
                    && pb.GetComponentInParent<Animation>(true) == null
                    && !IsUnderAny(pb.transform, scripted))
                {
                    candidates.Add(pb.gameObject);
                }
            }

            return candidates;
        }

        /// <summary>Every transform holding a script, or referenced by one.</summary>
        private static HashSet<Transform> FindScriptedTransforms(Scene scene)
        {
            var scripted = new HashSet<Transform>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null || IsProBuilder(behaviour))
                    {
                        continue;
                    }

                    scripted.Add(behaviour.transform);

                    SerializedProperty property = new SerializedObject(behaviour).GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference)
                        {
                            continue;
                        }

                        switch (property.objectReferenceValue)
                        {
                            case GameObject referenced when referenced.scene == scene:
                                scripted.Add(referenced.transform);
                                break;
                            case Component referenced when referenced.gameObject.scene == scene:
                                scripted.Add(referenced.transform);
                                break;
                        }
                    }
                }
            }

            return scripted;
        }

        private static bool IsUnderAny(Transform transform, HashSet<Transform> set)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (set.Contains(current))
                {
                    return true;
                }
            }

            return false;
        }

        private static string EnsureMeshFolder(string sceneName)
        {
            if (!AssetDatabase.IsValidFolder(MeshRoot))
            {
                AssetDatabase.CreateFolder("Assets/NeoNoir", "Meshes");
            }

            string folder = $"{MeshRoot}/{sceneName}";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder(MeshRoot, sceneName);
            }

            return folder;
        }

        private static string Backup(Scene scene)
        {
            Directory.CreateDirectory(BackupFolder);
            string destination = Path.Combine(BackupFolder, $"{scene.name}_{DateTime.Now:yyyyMMdd_HHmmss}.unity");
            File.Copy(scene.path, destination);
            return Path.GetFullPath(destination);
        }
    }
}
