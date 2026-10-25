using System.IO;
using UnityEditor;
using UnityEngine;

namespace LightAndShadow.EditorTools
{
    /// <summary>
    /// Builds the player prefab from scratch so its composition lives in code
    /// rather than in a hand-assembled asset.
    /// </summary>
    /// <remarks>
    /// The point of generating rather than authoring: the player is going to
    /// grow a sword, a cape, a hitbox, an i-frame flasher and a trail. Every
    /// time that set changes, regenerating is one menu click and the result is
    /// identical on every machine. It also documents exactly what a player is.
    ///
    /// Regenerating REPLACES the prefab. Anything tweaked by hand in the
    /// inspector is lost, so the tool asks first when the file already exists.
    /// Once you start tuning values on the prefab itself, either fold those
    /// values back into this file or stop regenerating.
    /// </remarks>
    public static class PlayerPrefabBuilder
    {
        private const string PrefabFolder = "Assets/NeoNoir/Prefabs";
        private const string PrefabPath = PrefabFolder + "/Player.prefab";
        private const string PlayerMaterialName = "NN_PlayerWhite";

        [MenuItem("Tools/NeoNoir/Generate Player Prefab", priority = 0)]
        public static void GeneratePlayerPrefab()
        {
            if (File.Exists(PrefabPath) && !EditorUtility.DisplayDialog(
                    "Regenerate player prefab?",
                    $"{PrefabPath} already exists.\n\n" +
                    "Regenerating rebuilds it from code and discards any changes made " +
                    "to the prefab in the inspector. Prefab instances already in scenes " +
                    "keep their own overrides.",
                    "Regenerate",
                    "Cancel"))
            {
                return;
            }

            EnsureFolder(PrefabFolder);

            GameObject player = BuildPlayer();

            try
            {
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(player, PrefabPath, out bool success);

                if (!success)
                {
                    Debug.LogError($"[NeoNoir] Failed to save the player prefab to {PrefabPath}.");
                    return;
                }

                Selection.activeObject = saved;
                EditorGUIUtility.PingObject(saved);
                Debug.Log($"[NeoNoir] Generated player prefab at {PrefabPath}.", saved);
            }
            finally
            {
                // The source object only ever existed to be captured.
                Object.DestroyImmediate(player);
            }
        }

        private static GameObject BuildPlayer()
        {
            // The primitive brings the built-in capsule mesh, its renderer and a
            // matching CapsuleCollider, which is exactly the placeholder body.
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";

            ApplyMaterial(player);
            ConfigureRigidbody(player.AddComponent<Rigidbody>());

            // PlayerMotor reads input off this component, so it has to exist
            // first for the reference to resolve at runtime.
            player.AddComponent<PlayerInputReader>();
            player.AddComponent<PlayerMotor>();
            player.AddComponent<PlayerHealth>();

            return player;
        }

        private static void ConfigureRigidbody(Rigidbody body)
        {
            body.mass = 1f;
            body.useGravity = true;

            // The motor drives rotation directly, so physics must not spin the
            // capsule; a dash at 30 u/s needs continuous detection to avoid
            // tunnelling through thin colliders.
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        private static void ApplyMaterial(GameObject player)
        {
            Material material = FindMaterial(PlayerMaterialName);

            if (material == null)
            {
                Debug.LogWarning(
                    $"[NeoNoir] Could not find a material named {PlayerMaterialName}. " +
                    "The prefab will use the default material and will not read as white.");
                return;
            }

            player.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static Material FindMaterial(string materialName)
        {
            // Search by name rather than by a hard-coded path so the material
            // can be reorganised without breaking this tool.
            foreach (string guid in AssetDatabase.FindAssets($"t:Material {materialName}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (Path.GetFileNameWithoutExtension(path) == materialName)
                {
                    return AssetDatabase.LoadAssetAtPath<Material>(path);
                }
            }

            return null;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = Path.GetFileName(folder);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
