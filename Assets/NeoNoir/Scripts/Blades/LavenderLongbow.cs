using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Lavender the Longbow. As a main blade, looses an arrow at the cursor
    /// whenever the attack is held and the bow is ready, marking enemies it
    /// hits with a vulnerability. As a secondary blade, fires a fanned volley
    /// that marks harder and kicks the player back.
    /// </summary>
    /// <remarks>
    /// Reference points, all children of the bow: the two ends of the string,
    /// the string's resting middle, and the point it is drawn back to. The
    /// string is drawn top, nock, bottom, with the nock sliding from rest to
    /// draw. Arrows fly the way the string pushes them: from the draw point
    /// toward the rest point.
    ///
    /// Drawing is tied to the cooldown, not the click. As the cooldown runs
    /// out, the bow draws itself over its last <see cref="drawTime"/>
    /// seconds while an arrow materialises on the string, so a ready bow is
    /// always fully drawn and nocked -- even resting in orbit -- and a click
    /// looses at once.
    ///
    /// As a main blade the bow never orbits: it is always held upright in
    /// front of the player, string toward them and arrow pointing at the
    /// cursor, turning only about the vertical. As a secondary it orbits like
    /// any blade until its volley, which holds it the same way. The
    /// arrow prefab is expected to point along its local +Z; if it does not,
    /// <see cref="arrowRotation"/> turns it. Its colliders are switched off
    /// and any rigidbody made kinematic, since arrows find what they hit by
    /// sweeping (see <see cref="LavenderArrow"/>), not by physics.
    ///
    /// Secondary. The bow is snapped to the cursor and fully drawn, then
    /// <see cref="volleyArrows"/> arrows leave at once, fanned evenly across
    /// <see cref="volleySpread"/> degrees, and the shot shoves the player
    /// back the opposite way. The ability counts as finished after a short
    /// recovery.
    ///
    /// The string, bubbles and stand-in arrow are drawn with a tinted copy
    /// of <see cref="effectMaterial"/>, expected to be NeoNoir/Tear: its
    /// outline branch for the string and the bubbles' glint, and its solid
    /// branch for the bubbles themselves.
    /// </remarks>
    public sealed class LavenderLongbow : Blade
    {
        [Header("String points")]
        [SerializeField]
        [Tooltip("Top end of the string, where it meets the bow.")]
        private Transform topString;

        [SerializeField]
        [Tooltip("Bottom end of the string, where it meets the bow.")]
        private Transform bottomString;

        [SerializeField]
        [Tooltip("Middle of the string at rest, undrawn.")]
        private Transform restPoint;

        [SerializeField]
        [Tooltip("Where the middle of the string is pulled to at full draw.")]
        private Transform drawPoint;

        [SerializeField]
        [Tooltip("Thickness of the string.")]
        [Min(0.001f)]
        private float stringWidth = 0.02f;

        [Header("Primary: arrow")]
        [SerializeField]
        [Tooltip("Arrows per second.")]
        [Range(0.2f, 10f)]
        private float attacksPerSecond = 1.5f;

        [SerializeField]
        [Tooltip("Seconds before the bow is ready that it starts drawing and the next arrow starts to materialise. Capped at the time between shots.")]
        [Min(0f)]
        private float drawTime = 0.4f;

        [SerializeField] private LavenderArrow.Settings arrow = new LavenderArrow.Settings();

        [SerializeField]
        [Tooltip("Height arrows fly at, relative to the middle of the player's collider.")]
        private float shotHeightOffset;

        [SerializeField]
        [Tooltip("Layers that stop an arrow. Enemies always stop it, whatever their layer.")]
        private LayerMask obstacleLayers = 1;

        [Header("Arrow model")]
        [SerializeField]
        [Tooltip("The arrow. Should point along its local +Z. Empty uses a plain stand-in.")]
        private GameObject arrowPrefab;

        [SerializeField]
        [Tooltip("Degrees to turn the arrow model by, if it does not point along its local +Z.")]
        private Vector3 arrowRotation;

        [SerializeField]
        [Tooltip("How far ahead of the string the arrow's pivot sits while it rests on the bow. Larger sits it further forward.")]
        private float nockOffset = 0.55f;

        [SerializeField]
        [Tooltip("The streak behind each arrow in flight.")]
        private LavenderArrow.Trail arrowTrail = new LavenderArrow.Trail();

        [SerializeField]
        [Tooltip("How far behind the arrow's pivot its trail starts: the back end of the arrow. For an arrow pivoted at its middle, half its length.")]
        [Min(0f)]
        private float trailStart = 0.4f;

        [Header("Primary: hold")]
        [SerializeField]
        [Tooltip("Distance from the player to the bow's pivot, in the direction of the cursor, while it is held to fire.")]
        [Min(0f)]
        private float holdDistance = 1.8f;

        [SerializeField]
        [Tooltip("Height the bow holds at, relative to the middle of the player's collider.")]
        private float holdHeightOffset;

        [SerializeField]
        [Tooltip("Seconds the bow takes to follow a change of aim.")]
        [Min(0f)]
        private float aimFollowTime = 0.04f;

        [Header("Secondary: volley")]
        [SerializeField]
        [Tooltip("Arrows in the volley.")]
        [Range(1, 21)]
        private int volleyArrows = 7;

        [SerializeField]
        [Tooltip("Degrees the volley fans across, centred on the cursor.")]
        [Range(0f, 180f)]
        private float volleySpread = 90f;

        [SerializeField]
        private LavenderArrow.Settings volleyArrow = new LavenderArrow.Settings
        {
            vulnerability = 1.5f,
            vulnerabilityDuration = 3f,
        };

        [SerializeField]
        [Tooltip("Seconds to snap round and draw before the volley leaves.")]
        [Min(0f)]
        private float volleyDrawTime = 0.15f;

        [SerializeField]
        [Tooltip("Speed the shot shoves the player back at, away from the cursor.")]
        [Min(0f)]
        private float recoilSpeed = 16f;

        [SerializeField]
        [Tooltip("Seconds after the volley before the ability counts as finished.")]
        [Min(0f)]
        private float volleyRecover = 0.25f;

        [Header("Debuff bubbles")]
        [SerializeField] private LavenderBubbles.Settings bubbles = new LavenderBubbles.Settings();

        [Header("Visuals")]
        [SerializeField]
        [Tooltip("Material for the string, bubbles and stand-in arrow, normally NeoNoir/Tear. It is copied and tinted, never edited.")]
        private Material effectMaterial;

        [SerializeField]
        [Tooltip("Colour of the debuff bubbles and of hits. Keep it strongly saturated, or the spot colour threshold crushes it to black or white.")]
        [ColorUsage(false, true)]
        private Color lavenderColour = new Color(0.75f, 0.4f, 1f);

        [SerializeField]
        [Tooltip("Colour of the string and the bubbles' glint.")]
        [ColorUsage(false, true)]
        private Color stringColour = Color.white;

        private enum VolleyPhase { None, Drawing, Recovering }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BloodColorId = Shader.PropertyToID("_BloodColor");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");

        // Primary state.
        private Vector3 aim = Vector3.forward;
        private float holdYaw;
        private float holdYawVelocity;
        private float engage;
        private float cooldown;

        // Secondary state.
        private VolleyPhase volleyPhase;
        private float volleyClock;
        private float volleyBlend;
        private Vector3 volleyAim = Vector3.forward;

        // The arrow on the string, if one has materialised.
        private GameObject nocked;
        private Vector3 nockedScale = Vector3.one;

        private Mesh stringMesh;
        private MeshRenderer stringRenderer;
        private readonly Vector3[] stringVertices = new Vector3[8];

        private Material tintedMaterial;
        private Material tintedSource;
        private Color tintedLavender;
        private Color tintedString;

        private float Period => 1f / (attacksPerSecond * AttackSpeedMultiplier);

        /// <summary>How far the string is drawn, 0 at rest to 1 at full draw.</summary>
        private float Drawn
        {
            get
            {
                float window = Mathf.Min(drawTime, Period);
                float drawn = window > 0f ? 1f - Mathf.Clamp01(cooldown / window) : (cooldown <= 0f ? 1f : 0f);

                if (volleyPhase == VolleyPhase.Drawing)
                {
                    drawn = Mathf.Max(drawn, volleyDrawTime > 0f ? Mathf.Clamp01(volleyClock / volleyDrawTime) : 1f);
                }

                return drawn;
            }
        }

        /// <summary>
        /// A tinted copy of the effect material, rebuilt only when the source
        /// or a colour changes, so tinting never touches the asset.
        /// </summary>
        private Material ResolvedEffectMaterial
        {
            get
            {
                if (effectMaterial == null)
                {
                    return null;
                }

                if (tintedMaterial == null || tintedSource != effectMaterial
                    || tintedLavender != lavenderColour || tintedString != stringColour)
                {
                    if (tintedMaterial != null)
                    {
                        Destroy(tintedMaterial);
                    }

                    tintedMaterial = new Material(effectMaterial) { name = effectMaterial.name + " (Lavender)" };
                    tintedSource = effectMaterial;
                    tintedLavender = lavenderColour;
                    tintedString = stringColour;

                    SetIfPresent(tintedMaterial, BaseColorId, lavenderColour);
                    SetIfPresent(tintedMaterial, BloodColorId, lavenderColour);
                    SetIfPresent(tintedMaterial, OutlineColorId, stringColour);
                }

                return tintedMaterial;
            }
        }

        private static void SetIfPresent(Material material, int id, Color colour)
        {
            if (material.HasProperty(id))
            {
                material.SetColor(id, colour);
            }
        }

        protected override void Awake()
        {
            base.Awake();
            BuildString();
        }

        protected override void Tick(Vector3 orbitPosition, Quaternion orbitRotation, float deltaTime)
        {
            Vector3 ownerPosition = Owner.position;
            bool held = PrimaryHeld;

            // A main bow never orbits: it is always held in front of the
            // player, following the cursor whether or not it is firing.
            bool main = Slot == BladeSlot.Main;

            if (main)
            {
                UpdateAim(ownerPosition);
            }

            bool wasEngaged = engage > 0f;

            cooldown = Mathf.Max(0f, cooldown - deltaTime);
            TickVolley(deltaTime);

            if (held && cooldown <= 0f && volleyPhase == VolleyPhase.None)
            {
                Loose(aim, new[] { aim }, arrow, AimHeight());
                cooldown = Period;
            }

            engage = main ? 1f : 0f;
            volleyBlend = StepEngage(volleyBlend, volleyPhase != VolleyPhase.None, deltaTime);

            if (!wasEngaged && engage > 0f)
            {
                // First frame in hand: face the aim straight away rather than
                // swinging round to it from wherever the yaw was left.
                holdYaw = Yaw(aim);
                holdYawVelocity = 0f;
            }

            holdYaw = Mathf.SmoothDampAngle(holdYaw, Yaw(aim), ref holdYawVelocity,
                                            Mathf.Max(0.0001f, aimFollowTime), Mathf.Infinity, deltaTime);

            Vector3 position = orbitPosition;
            Quaternion rotation = OrbitFacing(orbitPosition, ownerPosition, orbitRotation);

            float primaryWeight = Ease(engage);
            if (primaryWeight > 0f)
            {
                HoldPose(ownerPosition, Quaternion.Euler(0f, holdYaw, 0f) * Vector3.forward,
                         out Vector3 holdPosition, out Quaternion holdRotation);
                position = Vector3.Lerp(position, holdPosition, primaryWeight);
                rotation = Quaternion.Slerp(rotation, holdRotation, primaryWeight);
            }

            float volleyWeight = Ease(volleyBlend);
            if (volleyWeight > 0f)
            {
                HoldPose(ownerPosition, volleyAim, out Vector3 volleyPosition, out Quaternion volleyRotation);
                position = Vector3.Lerp(position, volleyPosition, volleyWeight);
                rotation = Quaternion.Slerp(rotation, volleyRotation, volleyWeight);
            }

            transform.SetPositionAndRotation(position, rotation);

            UpdateNocked();
            UpdateString();
        }

        private void UpdateAim(Vector3 ownerPosition)
        {
            if (!TryGetCursorPoint(AimHeight(), out Vector3 point))
            {
                return;
            }

            Vector3 desired = point - ownerPosition;
            desired.y = 0f;

            // Clicking on the player gives no direction, so keep the last one.
            if (desired.sqrMagnitude > 0.0001f)
            {
                aim = desired.normalized;
            }
        }

        /// <summary>
        /// The bow's pose in orbit: upright, as when held, with its arrow
        /// pointing straight out from the player. The shared orbit pose turns
        /// a blade's flat toward the player, which for a bow leaves it aiming
        /// sideways along the orbit.
        /// </summary>
        private Quaternion OrbitFacing(Vector3 orbitPosition, Vector3 ownerPosition, Quaternion fallback)
        {
            Vector3 outward = orbitPosition - ownerPosition;
            outward.y = 0f;

            return outward.sqrMagnitude > 0.0001f ? BowRotation(outward.normalized) : fallback;
        }

        private float AimHeight()
        {
            return OwnerMiddleHeight() + shotHeightOffset;
        }

        // ---- pose ---------------------------------------------------------

        /// <summary>
        /// The bow held upright in front of the player, as it was modelled,
        /// with its arrow pointing along <paramref name="direction"/>.
        /// </summary>
        private void HoldPose(Vector3 ownerPosition, Vector3 direction, out Vector3 position, out Quaternion rotation)
        {
            position = ownerPosition + direction * holdDistance;
            position.y = OwnerMiddleHeight() + holdHeightOffset;
            rotation = BowRotation(direction);
        }

        /// <summary>
        /// Turns the bow so its shooting direction (draw toward rest, in its
        /// own space) points along <paramref name="direction"/>, keeping its
        /// local up as world up -- only ever a turn about the vertical, so the
        /// bow stays upright as modelled and never tips over to fire.
        /// </summary>
        private Quaternion BowRotation(Vector3 direction)
        {
            return Quaternion.LookRotation(direction, Vector3.up)
                   * Quaternion.Inverse(Quaternion.LookRotation(LocalForward(), Vector3.up));
        }

        /// <summary>The way arrows leave, in the bow's own space: from the draw point toward the rest point.</summary>
        private Vector3 LocalForward()
        {
            Vector3 forward = LocalRest() - LocalDraw();
            forward.z = 0f;
            return forward.sqrMagnitude > 0.000001f ? forward.normalized : Vector3.right;
        }

        private Vector3 LocalRest()
        {
            if (restPoint != null)
            {
                return transform.InverseTransformPoint(restPoint.position);
            }

            return (LocalTop() + LocalBottom()) * 0.5f;
        }

        private Vector3 LocalDraw()
        {
            if (drawPoint != null)
            {
                return transform.InverseTransformPoint(drawPoint.position);
            }

            // No draw point: pull back from rest by a fixed amount.
            return LocalRest() - Vector3.right * 0.6f;
        }

        private Vector3 LocalTop()
        {
            return topString != null ? transform.InverseTransformPoint(topString.position) : Vector3.up;
        }

        private Vector3 LocalBottom()
        {
            return bottomString != null ? transform.InverseTransformPoint(bottomString.position) : Vector3.down;
        }

        private Vector3 LocalNock(float drawn)
        {
            return Vector3.Lerp(LocalRest(), LocalDraw(), drawn);
        }

        private static float Yaw(Vector3 flat)
        {
            return Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        }

        // ---- arrows ---------------------------------------------------------

        /// <summary>
        /// Keeps an arrow on the string while the bow draws, materialising it
        /// by scale as the draw goes on, so a fully drawn bow always holds a
        /// full-size arrow.
        /// </summary>
        private void UpdateNocked()
        {
            float drawn = Drawn;

            if (drawn <= 0f)
            {
                if (nocked != null)
                {
                    Destroy(nocked);
                    nocked = null;
                }

                return;
            }

            if (nocked == null)
            {
                nocked = MakeArrow();
                nockedScale = nocked.transform.localScale;
                nocked.transform.SetParent(transform, false);
            }

            Vector3 forward = LocalForward();
            float grown = 1f - (1f - drawn) * (1f - drawn);

            nocked.transform.localPosition = LocalNock(drawn) + forward * nockOffset;
            // Same up as when it flies, so loosing it never twists it.
            nocked.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(arrowRotation);
            nocked.transform.localScale = nockedScale * grown;

            SetShown(nocked, Visibility >= 0.5f);
        }

        /// <summary>
        /// Looses one arrow along each direction, from the string of the bow
        /// held toward <paramref name="holdDirection"/>, fully drawn. The arrow
        /// on the string takes the middle direction; the rest are made fresh.
        /// </summary>
        private void Loose(Vector3 holdDirection, Vector3[] directions, LavenderArrow.Settings shot, float height)
        {
            if (directions.Length == 0)
            {
                return;
            }

            // From the held pose rather than the bow's current one, which may
            // still be blending out of orbit.
            HoldPose(Owner.position, holdDirection, out Vector3 position, out Quaternion rotation);
            Vector3 release = position + rotation * Vector3.Scale(LocalNock(1f), transform.lossyScale);
            release.y = height;

            Material material = ResolvedEffectMaterial;

            // Middle arrow first, so the nocked one flies straight at the cursor.
            int middle = directions.Length / 2;

            for (int i = 0; i < directions.Length; i++)
            {
                int index = (middle + i) % directions.Length;
                Vector3 direction = directions[index];

                GameObject loosed;

                if (i == 0 && nocked != null)
                {
                    loosed = nocked;
                    loosed.transform.SetParent(null, true);
                    loosed.transform.localScale = nockedScale;
                    nocked = null;
                }
                else
                {
                    loosed = MakeArrow();
                }

                SetShown(loosed, true);
                loosed.transform.SetPositionAndRotation(release + direction * nockOffset,
                                                        Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(arrowRotation));

                LavenderArrow flight = loosed.GetComponent<LavenderArrow>();
                if (flight == null)
                {
                    flight = loosed.AddComponent<LavenderArrow>();
                }

                flight.Launch(direction, shot, arrowTrail, trailStart, obstacleLayers, bubbles, material, lavenderColour);
            }
        }

        /// <summary>
        /// A fresh arrow, unparented, at its prefab's own scale, with its
        /// physics switched off: arrows sweep for hits rather than collide.
        /// </summary>
        private GameObject MakeArrow()
        {
            GameObject made;

            if (arrowPrefab != null)
            {
                made = Instantiate(arrowPrefab);
            }
            else
            {
                made = MakeStandInArrow();
            }

            made.name = "LavenderArrow";

            foreach (Collider part in made.GetComponentsInChildren<Collider>())
            {
                part.enabled = false;
            }

            foreach (Rigidbody body in made.GetComponentsInChildren<Rigidbody>())
            {
                body.isKinematic = true;
                body.useGravity = false;
            }

            return made;
        }

        /// <summary>A plain thin shaft along +Z, until a real arrow prefab is assigned.</summary>
        private GameObject MakeStandInArrow()
        {
            GameObject root = new GameObject();
            GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);

            Destroy(shaft.GetComponent<Collider>());
            shaft.transform.SetParent(root.transform, false);
            shaft.transform.localScale = new Vector3(0.035f, 0.035f, Mathf.Max(0.1f, trailStart * 2f));

            // In the bow's own material: the first renderer on it that is not
            // the string this script built.
            foreach (MeshRenderer bowRenderer in GetComponentsInChildren<MeshRenderer>())
            {
                if (bowRenderer != stringRenderer)
                {
                    shaft.GetComponent<Renderer>().sharedMaterial = bowRenderer.sharedMaterial;
                    break;
                }
            }

            return root;
        }

        private static void SetShown(GameObject target, bool shown)
        {
            foreach (Renderer part in target.GetComponentsInChildren<Renderer>())
            {
                part.enabled = shown;
            }
        }

        // ---- string ---------------------------------------------------------

        private void BuildString()
        {
            GameObject line = new GameObject("String");
            line.transform.SetParent(transform, false);

            stringMesh = new Mesh { name = "LavenderString" };
            stringMesh.MarkDynamic();

            line.AddComponent<MeshFilter>().sharedMesh = stringMesh;

            stringRenderer = line.AddComponent<MeshRenderer>();
            stringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            stringRenderer.receiveShadows = false;

            // u = 1 throughout: NeoNoir/Tear's outline branch, the string colour.
            stringMesh.SetVertices(stringVertices);
            stringMesh.SetUVs(0, new[]
            {
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            });

            var triangles = new System.Collections.Generic.List<int>();
            ScarlettClaw.AddQuad(triangles, 0, 1, 2, 3);
            ScarlettClaw.AddQuad(triangles, 4, 5, 6, 7);
            stringMesh.SetTriangles(triangles, 0);
        }

        /// <summary>
        /// Two flat strips, top to nock and nock to bottom, built in world
        /// space each frame and turned to face the camera, so the string reads
        /// whether the bow is held flat or hanging in orbit.
        /// </summary>
        private void UpdateString()
        {
            if (stringMesh == null)
            {
                return;
            }

            // Held at the world origin, unrotated, so the vertices can be
            // written straight in world space.
            stringRenderer.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            stringRenderer.enabled = Visibility >= 0.5f;

            Material material = ResolvedEffectMaterial;
            if (material != null && stringRenderer.sharedMaterial != material)
            {
                stringRenderer.sharedMaterial = material;
            }

            Vector3 top = transform.TransformPoint(LocalTop());
            Vector3 bottom = transform.TransformPoint(LocalBottom());
            Vector3 nock = transform.TransformPoint(LocalNock(Drawn));

            Camera camera = ResolvedCamera;
            Vector3 toCamera = camera != null ? -camera.transform.forward : Vector3.up;

            StringSegment(0, top, nock, toCamera);
            StringSegment(4, nock, bottom, toCamera);

            stringMesh.SetVertices(stringVertices);
            stringMesh.RecalculateBounds();
        }

        private void StringSegment(int at, Vector3 from, Vector3 to, Vector3 toCamera)
        {
            Vector3 side = Vector3.Cross(to - from, toCamera);
            side = side.sqrMagnitude > 0.0000001f ? side.normalized * (stringWidth * 0.5f) : Vector3.zero;

            stringVertices[at] = from + side;
            stringVertices[at + 1] = from - side;
            stringVertices[at + 2] = to + side;
            stringVertices[at + 3] = to - side;
        }

        // ---- secondary ------------------------------------------------------

        protected override void OnSecondaryBegin()
        {
            Vector3 ownerPosition = Owner.position;

            Vector3 direction = TryGetCursorPoint(AimHeight(), out Vector3 point)
                ? point - ownerPosition
                : Owner.forward;

            direction.y = 0f;
            volleyAim = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;

            volleyPhase = VolleyPhase.Drawing;
            volleyClock = 0f;
        }

        private void TickVolley(float deltaTime)
        {
            if (volleyPhase == VolleyPhase.None)
            {
                return;
            }

            volleyClock += deltaTime;

            if (volleyPhase == VolleyPhase.Drawing)
            {
                if (volleyClock < volleyDrawTime)
                {
                    return;
                }

                FireVolley();
                volleyPhase = VolleyPhase.Recovering;
                volleyClock = 0f;
                return;
            }

            if (volleyClock >= volleyRecover)
            {
                volleyPhase = VolleyPhase.None;
                FinishSecondary();
            }
        }

        private void FireVolley()
        {
            // The nocked arrow may still be materialising if the bow was not
            // ready; the volley is fully drawn either way.
            UpdateNocked();

            Vector3[] directions = new Vector3[volleyArrows];

            for (int i = 0; i < volleyArrows; i++)
            {
                float angle = volleyArrows > 1 ? Mathf.Lerp(-volleySpread * 0.5f, volleySpread * 0.5f, i / (float)(volleyArrows - 1)) : 0f;
                directions[i] = Quaternion.AngleAxis(angle, Vector3.up) * volleyAim;
            }

            Loose(volleyAim, directions, volleyArrow, AimHeight());

            if (Motor != null)
            {
                Motor.AddImpulse(-volleyAim * recoilSpeed);
            }

            // The bow then draws its next arrow as usual.
            cooldown = Period;
        }

        // ---- lifecycle ------------------------------------------------------

        protected override void OnSnap()
        {
            engage = 0f;
            volleyBlend = 0f;
        }

        private void OnDisable()
        {
            // Hidden mid-volley: the ability must still end, or the rotation
            // would wait on it forever.
            if (volleyPhase != VolleyPhase.None)
            {
                volleyPhase = VolleyPhase.None;
                FinishSecondary();
            }
        }

        private void OnDestroy()
        {
            if (nocked != null)
            {
                Destroy(nocked);
            }

            if (stringMesh != null)
            {
                Destroy(stringMesh);
            }

            // Arrows and bubbles still out keep a reference, but Unity
            // tolerates a destroyed material on a renderer.
            if (tintedMaterial != null)
            {
                Destroy(tintedMaterial);
            }
        }
    }
}
