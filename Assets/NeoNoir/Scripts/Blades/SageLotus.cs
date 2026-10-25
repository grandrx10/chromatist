using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LightAndShadow
{
    /// <summary>
    /// One of Sage's lotuses. Planted on the floor as a bud, blooms open, and
    /// then bursts: a ring flares out across the floor, petals scatter, and
    /// enemies inside the burst take damage.
    /// </summary>
    /// <remarks>
    /// Two ways to burst, picked by <see cref="Settings.burstOnBloom"/>. The
    /// scepter's primary bursts the moment it is fully open. The secondary's
    /// trail waits, open, to be stepped on: an enemy setting it off is hurt
    /// by the burst, and the player setting it off is healed instead. A
    /// waiting lotus nobody steps on wilts away after
    /// <see cref="Settings.lifetime"/>.
    ///
    /// Stepping is found by overlapping a short upright capsule at the
    /// lotus, so anything standing on it counts whatever its pivot height.
    /// Only solid colliders count -- triggers are ignored, as for damage.
    ///
    /// The burst reuses <see cref="BlueBurst"/> for the floor ring, with no
    /// notes, and <see cref="RosePetal"/> for the petals. Its ring starts
    /// small and flares out to exactly the burst radius, so what it shows is
    /// what it hit.
    ///
    /// Spawned at runtime and builds its own mesh, so it needs no prefab. A
    /// model can be given instead. It is never instantiated per lotus: the
    /// first time it is used, it is built once and baked into a single plain
    /// mesh, one submesh per material, which every lotus after shares. So a
    /// model made of many parts, or one still carrying ProBuilder data that
    /// would rebuild its mesh on every spawn, costs no more than the built-in
    /// lotus. Its colliders are left behind in the bake.
    /// </remarks>
    public sealed class SageLotus : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Bloom")]
            [Tooltip("Width of the lotus at full bloom.")]
            [Min(0.05f)] public float size = 0.9f;

            [Tooltip("Scale the bud starts at, as a fraction of full bloom.")]
            [Range(0.01f, 1f)] public float startScale = 0.1f;

            [Tooltip("Seconds from bud to full bloom.")]
            [Min(0.01f)] public float bloomTime = 0.5f;

            [Tooltip("Height above the floor the lotus sits at.")]
            [Range(0f, 1f)] public float floorOffset = 0.05f;

            [Header("Trigger")]
            [Tooltip("Burst the moment it is fully open. Off waits, open, for the player or an enemy to step on it.")]
            public bool burstOnBloom = true;

            [Tooltip("How close the player or an enemy must come to the lotus to step on it.")]
            [Min(0.05f)] public float stepRadius = 0.5f;

            [Tooltip("Seconds an open lotus waits to be stepped on before wilting away without bursting. 0 waits forever.")]
            [Min(0f)] public float lifetime = 12f;

            [Tooltip("Seconds wilting takes.")]
            [Min(0.01f)] public float wiltTime = 0.3f;

            [Header("Burst")]
            [Tooltip("Damage to every enemy inside the burst.")]
            [Min(0f)] public float damage = 12f;

            [Tooltip("Enemies within this distance of the lotus when it bursts take the damage.")]
            [Min(0.1f)] public float burstRadius = 1.6f;

            [Tooltip("Health given to the player when they step on it.")]
            [Min(0f)] public float heal;

            [Tooltip("Whether the burst still hurts nearby enemies when it was the player who stepped on it.")]
            public bool damageWhenPlayerSteps;

            [Tooltip("Show a slash on each enemy the burst hits.")]
            public bool slashEffect = true;

            [Tooltip("Petals the burst scatters.")]
            [Range(0, 64)] public int petals = 14;

            [Tooltip("How the burst's petals fly. Carry speed is how fast they are flung outward.")]
            public RosePetal.Settings petal = new RosePetal.Settings
            {
                size = new Vector2(0.14f, 0.26f),
                scatter = 0.2f,
                burstSpeed = 2.5f,
                carrySpeed = 3f,
                drag = 4f,
                fallSpeed = 0.3f,
                floatTime = 0.4f,
                floatJitter = 0.4f,
                fadeTime = 0.6f,
            };

            [Tooltip("The ring that flares out across the floor. Its notes are ignored. It grows to the burst radius.")]
            public BlueBurst.Settings ring = new BlueBurst.Settings
            {
                notes = 0,
                lifetime = 0.05f,
                ringTime = 0.3f,
                ringGrowth = 2.5f,
                ringThickness = 0.1f,
                ringIntensity = 4f,
            };
        }

        private enum Phase { Blooming, Waiting, Wilting }

        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        // Upright reach of the step and burst checks above the floor:
        // enough to catch any body standing on the lotus.
        private const float CheckHeight = 1.5f;

        private static readonly Collider[] overlaps = new Collider[32];
        private static Mesh sharedMesh;

        private sealed class BakedModel
        {
            public Mesh Mesh;
            public Material[] Materials;
        }

        // One bake per model, shared by every lotus that shows it. A null
        // entry is a model with nothing to bake, so it falls back to
        // instantiating rather than trying again every lotus.
        private static readonly Dictionary<GameObject, BakedModel> bakedModels = new Dictionary<GameObject, BakedModel>();

        // Only bodies can step on a lotus. Leaving the floor and walls out
        // keeps every waiting lotus from finding them, every frame.
        private static int stepLayers;

        private readonly HashSet<EnemyHealth> hit = new HashSet<EnemyHealth>();

        private Settings settings;
        private Material material;
        private Transform player;
        private Color slashTint;
        private Blade source;

        private Transform bloom;
        private Renderer[] renderers;
        private MaterialPropertyBlock fadeBlock;
        private bool hasFade;

        private Phase phase;
        private float age;

        /// <summary>
        /// Plants a bud at <paramref name="floorPoint"/>. <paramref name="player"/>
        /// is who can step on it to be healed, and is never hurt by it.
        /// <paramref name="model"/>, if given, is shown instead of the built
        /// lotus; it should be one unit across, facing up.
        /// <paramref name="source"/> is the blade that planted it, told about
        /// any kill its burst makes.
        /// </summary>
        public static SageLotus Plant(Vector3 floorPoint, Settings settings, Material material, GameObject model,
                                      Transform player, Color slashTint, Blade source = null)
        {
            GameObject host = new GameObject("SageLotus");
            SageLotus lotus = host.AddComponent<SageLotus>();
            lotus.Begin(floorPoint, settings, material, model, player, slashTint, source);
            return lotus;
        }

        private void Begin(Vector3 floorPoint, Settings plantSettings, Material plantMaterial, GameObject model,
                           Transform plantPlayer, Color tint, Blade plantSource)
        {
            settings = plantSettings;
            material = plantMaterial;
            player = plantPlayer;
            slashTint = tint;
            source = plantSource;

            transform.SetPositionAndRotation(floorPoint, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            BakedModel baked = model != null ? Bake(model) : null;

            if (baked != null)
            {
                bloom = AddBloom(baked.Mesh, baked.Materials);
            }
            else if (model != null)
            {
                // Nothing in the model could be baked: show it as it is.
                GameObject made = Instantiate(model, transform, false);

                foreach (Collider part in made.GetComponentsInChildren<Collider>())
                {
                    part.enabled = false;
                }

                bloom = made.transform;
            }
            else
            {
                bloom = AddBloom(LotusMesh(), new[] { material });
            }

            bloom.localPosition = Vector3.up * settings.floorOffset;

            renderers = bloom.GetComponentsInChildren<Renderer>();
            fadeBlock = new MaterialPropertyBlock();
            hasFade = material != null && material.HasProperty(FadeId);

            phase = Phase.Blooming;
            SetBloom(settings.startScale);
        }

        /// <summary>Bakes <paramref name="model"/> now rather than on the first lotus, so that lotus has no hitch.</summary>
        public static void Prewarm(GameObject model)
        {
            if (model != null)
            {
                Bake(model);
            }
        }

        private Transform AddBloom(Mesh mesh, Material[] materials)
        {
            GameObject made = new GameObject("Bloom");
            made.transform.SetParent(transform, false);
            made.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer meshRenderer = made.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.sharedMaterials = materials;

            return made.transform;
        }

        /// <summary>
        /// Builds <paramref name="model"/> once, off to the side, and merges
        /// every mesh in it into one, grouped into a submesh per material.
        /// Laid out as an instance of the model would sit under a lotus:
        /// turned as its root is, but not moved or scaled, since the lotus
        /// sets those itself.
        /// </summary>
        private static BakedModel Bake(GameObject model)
        {
            if (bakedModels.TryGetValue(model, out BakedModel cached) && (cached == null || cached.Mesh != null))
            {
                return cached;
            }

            // Built live, so a model that makes its mesh as it wakes --
            // ProBuilder does -- has made it by the time it is read.
            GameObject template = Instantiate(model);
            Transform root = template.transform;
            Matrix4x4 toLotus = Matrix4x4.Rotate(root.rotation) * root.worldToLocalMatrix;

            List<Material> materials = new List<Material>();
            List<List<CombineInstance>> groups = new List<List<CombineInstance>>();

            foreach (MeshRenderer part in template.GetComponentsInChildren<MeshRenderer>())
            {
                MeshFilter filter = part.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;

                if (!part.enabled || mesh == null)
                {
                    continue;
                }

                Material[] partMaterials = part.sharedMaterials;
                Matrix4x4 matrix = toLotus * part.transform.localToWorldMatrix;

                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    Material partMaterial = partMaterials.Length > 0
                        ? partMaterials[Mathf.Min(submesh, partMaterials.Length - 1)]
                        : null;

                    int group = materials.IndexOf(partMaterial);
                    if (group < 0)
                    {
                        group = materials.Count;
                        materials.Add(partMaterial);
                        groups.Add(new List<CombineInstance>());
                    }

                    groups[group].Add(new CombineInstance { mesh = mesh, subMeshIndex = submesh, transform = matrix });
                }
            }

            BakedModel baked = null;

            if (groups.Count > 0)
            {
                // Each material's parts merged into one piece, then the
                // pieces stacked as the submeshes of the final mesh.
                CombineInstance[] pieces = new CombineInstance[groups.Count];
                List<Mesh> scratch = new List<Mesh>();

                for (int i = 0; i < groups.Count; i++)
                {
                    Mesh piece = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    piece.CombineMeshes(groups[i].ToArray(), true, true);
                    scratch.Add(piece);
                    pieces[i] = new CombineInstance { mesh = piece, transform = Matrix4x4.identity };
                }

                Mesh combined = new Mesh { name = model.name + " (baked)", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                combined.CombineMeshes(pieces, false, false);
                combined.RecalculateBounds();

                foreach (Mesh piece in scratch)
                {
                    Destroy(piece);
                }

                baked = new BakedModel { Mesh = combined, Materials = materials.ToArray() };
            }
            else
            {
                Debug.LogWarning($"[SageLotus] {model.name} has no mesh to bake, so every lotus will instantiate it -- slowly. Give it a plain MeshFilter and MeshRenderer.", model);
            }

            // Hidden at once, then removed: the bake is a copy, so it
            // outlives the template and whatever mesh the template made.
            template.SetActive(false);
            Destroy(template);

            bakedModels[model] = baked;
            return baked;
        }

        private void Update()
        {
            age += Time.deltaTime;

            switch (phase)
            {
                case Phase.Blooming:
                    float t = Mathf.Clamp01(age / settings.bloomTime);

                    // Ease-out: unfurls quickly, then settles open.
                    float eased = 1f - (1f - t) * (1f - t);
                    SetBloom(Mathf.Lerp(settings.startScale, 1f, eased));

                    if (t < 1f)
                    {
                        break;
                    }

                    if (settings.burstOnBloom)
                    {
                        Burst(false);
                        return;
                    }

                    phase = Phase.Waiting;
                    age = 0f;
                    break;

                case Phase.Waiting:
                    if (settings.lifetime > 0f && age >= settings.lifetime)
                    {
                        phase = Phase.Wilting;
                        age = 0f;
                        break;
                    }

                    CheckStep();
                    break;

                case Phase.Wilting:
                    float left = 1f - Mathf.Clamp01(age / settings.wiltTime);

                    if (left <= 0f)
                    {
                        Destroy(gameObject);
                        return;
                    }

                    SetVisibility(left);
                    break;
            }
        }

        private void SetBloom(float fraction)
        {
            bloom.localScale = Vector3.one * (settings.size * fraction);
        }

        /// <summary>Dissolves through _Fade where the material has it, and shrinks away where it does not.</summary>
        private void SetVisibility(float visibility)
        {
            if (!hasFade)
            {
                SetBloom(visibility);
                return;
            }

            fadeBlock.SetFloat(FadeId, visibility);

            foreach (Renderer part in renderers)
            {
                if (part != null)
                {
                    part.SetPropertyBlock(fadeBlock);
                }
            }
        }

        // ---- stepping -------------------------------------------------------

        private void CheckStep()
        {
            if (stepLayers == 0)
            {
                stepLayers = StepLayers();
            }

            Vector3 floor = transform.position;
            int count = Physics.OverlapCapsuleNonAlloc(floor, floor + Vector3.up * CheckHeight, settings.stepRadius,
                                                       overlaps, stepLayers, QueryTriggerInteraction.Ignore);

            bool enemy = false;

            for (int i = 0; i < count; i++)
            {
                Transform other = overlaps[i].transform;

                // The player first: one standing on it alongside an enemy is
                // still healed.
                if (player != null && other.IsChildOf(player))
                {
                    Burst(true);
                    return;
                }

                EnemyHealth health = overlaps[i].GetComponentInParent<EnemyHealth>();
                enemy |= health != null && !health.IsDead;
            }

            if (enemy)
            {
                Burst(false);
            }
        }

        /// <summary>
        /// The player's layer and the layers of every live enemy. Falls back
        /// to everything if neither can be found, so stepping still works,
        /// only slower.
        /// </summary>
        private int StepLayers()
        {
            int layers = 0;

            if (player != null)
            {
                foreach (Collider part in player.GetComponentsInChildren<Collider>())
                {
                    layers |= 1 << part.gameObject.layer;
                }
            }

            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer >= 0)
            {
                layers |= 1 << enemyLayer;
            }

            foreach (EnemyHealth enemy in EnemyHealth.Active)
            {
                if (enemy != null)
                {
                    layers |= 1 << enemy.gameObject.layer;
                }
            }

            return layers != 0 ? layers : ~0;
        }

        // ---- burst ----------------------------------------------------------

        private void Burst(bool byPlayer)
        {
            Vector3 floor = transform.position;

            if (byPlayer && settings.heal > 0f && player != null)
            {
                PlayerHealth health = player.GetComponent<PlayerHealth>();

                if (health != null)
                {
                    health.Heal(settings.heal);
                }
            }

            if ((!byPlayer || settings.damageWhenPlayerSteps) && settings.damage > 0f)
            {
                BladeDamage.Sweep(floor, floor + Vector3.up * CheckHeight, settings.burstRadius, settings.damage,
                                  Vector3.zero, hit, OnHit, slashTint, false, source: source);
            }

            // Starts small and flares out to the burst radius.
            float ringStart = settings.burstRadius / Mathf.Max(1f, settings.ring.ringGrowth);
            Vector3 centre = floor + Vector3.up * (settings.size * 0.3f);
            BlueBurst.Play(centre, floor + Vector3.up * settings.floorOffset, ringStart, settings.ring, material, null);

            float offset = Random.Range(0f, 360f);

            for (int i = 0; i < settings.petals; i++)
            {
                float yaw = offset + 360f * i / settings.petals + Random.Range(-15f, 15f);
                Vector3 outward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                RosePetal.Spawn(centre, outward, settings.petal, material);
            }

            Destroy(gameObject);
        }

        private void OnHit(EnemyHealth enemy, Vector3 from)
        {
            if (!settings.slashEffect || SlashEffect.Current == null || enemy == null)
            {
                return;
            }

            Vector3 at = enemy.BodyBounds().center;
            Vector3 direction = at - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = transform.forward;
            }

            SlashEffect.Current.TriggerSlash(at, direction, 1f, slashTint);
        }

        // ---- mesh -----------------------------------------------------------

        /// <summary>
        /// A lotus one unit across, facing up, built once and shared: three
        /// rings of pointed, cupped petals, each ring shorter, steeper and
        /// turned half a petal from the one outside it.
        /// </summary>
        private static Mesh LotusMesh()
        {
            if (sharedMesh != null)
            {
                return sharedMesh;
            }

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            AddRing(vertices, triangles, 8, 0.5f, 0.26f, 12f, 0f);
            AddRing(vertices, triangles, 8, 0.38f, 0.24f, 38f, 22.5f);
            AddRing(vertices, triangles, 6, 0.24f, 0.2f, 66f, 0f);

            sharedMesh = new Mesh { name = "Sage lotus" };
            sharedMesh.SetVertices(vertices);
            sharedMesh.SetTriangles(triangles, 0);
            sharedMesh.RecalculateNormals();
            sharedMesh.RecalculateBounds();
            return sharedMesh;
        }

        private static void AddRing(List<Vector3> vertices, List<int> triangles, int count, float length, float width,
                                    float pitch, float yawOffset)
        {
            for (int i = 0; i < count; i++)
            {
                Quaternion turn = Quaternion.Euler(0f, yawOffset + 360f * i / count, 0f) * Quaternion.Euler(-pitch, 0f, 0f);
                AddPetal(vertices, triangles, turn, length, width);
            }
        }

        /// <summary>
        /// One petal from the centre outward along <paramref name="turn"/>'s
        /// forward: pointed at both ends, widest a little before halfway,
        /// cupped across its width and curling up toward its tip. Double
        /// sided, each face with its own vertices so they keep their own normals.
        /// </summary>
        private static void AddPetal(List<Vector3> vertices, List<int> triangles, Quaternion turn, float length, float width)
        {
            const int rows = 8;
            const float cup = 0.05f;
            const float curl = 0.08f;

            for (int face = 0; face < 2; face++)
            {
                int first = vertices.Count;

                for (int row = 0; row < rows; row++)
                {
                    float t = row / (rows - 1f);
                    // Clamped: sin(PI) in floats comes out a hair below zero.
                    float half = width * 0.5f * Mathf.Sqrt(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.8f))));
                    float z = t * length;
                    float lift = curl * t * t;

                    vertices.Add(turn * new Vector3(-half, lift + cup, z));
                    vertices.Add(turn * new Vector3(0f, lift, z));
                    vertices.Add(turn * new Vector3(half, lift + cup, z));
                }

                for (int row = 0; row < rows - 1; row++)
                {
                    for (int column = 0; column < 2; column++)
                    {
                        int a = first + row * 3 + column;
                        int b = a + 1;
                        int c = a + 3;
                        int d = a + 4;

                        if (face == 0)
                        {
                            triangles.AddRange(new[] { a, c, b, b, c, d });
                        }
                        else
                        {
                            triangles.AddRange(new[] { a, b, c, b, d, c });
                        }
                    }
                }
            }
        }
    }
}
