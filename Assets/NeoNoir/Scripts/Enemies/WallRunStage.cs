using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The level's half of <see cref="ChromatistPhaseThree"/>: the wall the
    /// player is thrown onto, and the flat strip the run really happens on.
    /// The boss is spawned in, so it cannot point at the level itself; it
    /// finds this when its third phase starts.
    /// </summary>
    /// <remarks>
    /// Put one in the boss's scene, on any object.
    ///   - <see cref="wallBase"/> is an empty at the foot of the real wall, on
    ///     its face, its Z (blue) arrow pointing out of the wall into the
    ///     arena. The player lands straight above it,
    ///     <see cref="landingHeight"/> up, as far to either side as they were,
    ///     up to <see cref="landingSpread"/>.
    ///   - <see cref="stripBase"/> is an empty at the near end of the strip,
    ///     on its surface, its Z arrow pointing up the run. It stands for the
    ///     wall's foot: a spot some height up the wall is that far along the
    ///     strip from it.
    ///   - The strip's art should be the wall's face laid flat, so the two
    ///     match where the player lands. Select this to see the landing spot
    ///     on both: orange on the wall, blue on the strip. The strip needs a
    ///     floor collider, and should reach a good way back past the Strip
    ///     Base, where the bottom of the screen looks and the boss leaps up
    ///     from.
    ///
    /// The ending, once the boss dies (<see cref="Finish"/>):
    ///   1. No more strip is laid: <see cref="endPiece"/> -- the top of the
    ///      wall, laid flat, built facing +Z like a strip piece -- goes on the
    ///      end of it instead, its near edge on the last piece. The player
    ///      runs on toward it.
    ///   2. <see cref="takeOffBefore"/> short of its child named
    ///      <see cref="edgeName"/> -- an empty on the top edge, in the middle
    ///      -- the take-off: the reverse of the landing. The player and the
    ///      camera are moved by the one turn that stands the end piece back
    ///      up as the real wall top at <see cref="roofEdge"/>, so the picture
    ///      does not change, but the player is really on a wall face.
    ///   3. The leap, for real: up and over the edge onto the roof,
    ///      <see cref="roofLanding"/> in from it, the player turning upright
    ///      and the camera back to its normal look down. They land on the
    ///      roof with their own controls back, and <see cref="Finished"/> is
    ///      raised.
    ///   - <see cref="roofEdge"/> is an empty on the real wall's face at its
    ///     top edge, its Z arrow pointing out of the wall, the way the run
    ///     came from. The roof behind it is real, flat ground with a floor
    ///     collider, its height the Roof Edge's. The wall face below it should
    ///     look like the end piece, so the take-off is seamless.
    /// </remarks>
    public sealed class WallRunStage : MonoBehaviour
    {
        [Header("Wall")]
        [SerializeField]
        [Tooltip("An empty at the foot of the wall, on its face, its Z (blue) arrow pointing out of the wall into the arena.")]
        private Transform wallBase;

        [SerializeField]
        [Tooltip("How far up the wall the player lands. 12 or more keeps the camera above the arena floor as it turns to face the wall.")]
        [Min(0f)]
        private float landingHeight = 14f;

        [SerializeField]
        [Tooltip("Furthest to either side of the Wall Base the player lands; they land as far to the side as they were, up to this.")]
        [Min(0f)]
        private float landingSpread = 3f;

        [Header("Strip")]
        [SerializeField]
        [Tooltip("An empty at the near end of the strip, on its surface, its Z arrow pointing up the run. It stands for the Wall Base: a spot some height up the wall is that far along the strip from it.")]
        private Transform stripBase;

        [SerializeField]
        [Tooltip("The last piece of strip placed in the scene by hand. Copies are laid on from it, up the run. Empty lays none, for a strip built long enough by hand.")]
        private Transform chainStart;

        [SerializeField]
        [Tooltip("The repeating strip prefab the copies are made from. Empty copies Chain Start itself.")]
        private GameObject hallSegment;

        [SerializeField]
        [Tooltip("Distance from one strip piece to the next. 0 measures Chain Start.")]
        [Min(0f)]
        private float segmentLength;

        [SerializeField]
        [Tooltip("How far ahead of the player the strip is always laid. Keep it past the edge of the screen.")]
        [Min(1f)]
        private float buildAhead = 80f;

        [SerializeField]
        [Tooltip("How far behind the player laid pieces are kept before being removed. Keep it past the boss and the edge of the screen.")]
        [Min(0f)]
        private float keepBehind = 50f;

        [Header("Landing")]
        [SerializeField]
        [Tooltip("Switched off as the player lands: the real wall and arena, say, if they would show round the strip.")]
        private List<GameObject> hideOnLanding = new List<GameObject>();

        [SerializeField]
        [Tooltip("Switched on as the player lands.")]
        private List<GameObject> showOnLanding = new List<GameObject>();

        [Header("Ending")]
        [SerializeField]
        [Tooltip("The top of the wall, laid flat, placed on the end of the strip once the boss dies. Built facing +Z, like a strip piece.")]
        private GameObject endPiece;

        [SerializeField]
        [Tooltip("Turns the End Piece as it is placed, for one not built facing +Z: Y 180 for one built facing the other way.")]
        private Vector3 endPieceRotation;

        [SerializeField]
        [Tooltip("Name of the empty child in the End Piece on its top edge, in the middle. Without one, the far end of its art.")]
        private string edgeName = "Edge";

        [SerializeField]
        [Tooltip("An empty on the real wall's face at its top edge, its Z (blue) arrow pointing out of the wall. The roof behind it is where the player lands.")]
        private Transform roofEdge;

        [SerializeField]
        [Tooltip("How far short of the edge the player takes off.")]
        [Min(0f)]
        private float takeOffBefore = 2f;

        [SerializeField]
        [Tooltip("How far in from the edge, onto the roof, the player lands.")]
        [Min(0f)]
        private float roofLanding = 6f;

        [SerializeField]
        [Tooltip("Seconds the leap onto the roof takes.")]
        [Min(0.1f)]
        private float leapTime = 1.1f;

        [SerializeField]
        [Tooltip("How high above the edge the leap rises.")]
        [Min(0f)]
        private float leapHeight = 5f;

        [SerializeField] private float roofShake = 0.8f;
        [SerializeField] [Min(0f)] private float roofShakeDuration = 0.4f;

        [SerializeField]
        [Tooltip("Flashes the screen with the impact frames as the player lands on the roof, if the scene has them.")]
        private bool roofImpactFrames = true;

        [SerializeField]
        [Tooltip("Puffs of dust ringing out round the player as they land on the roof.")]
        [Min(0)]
        private int roofPuffs = 24;

        [SerializeField]
        [Min(0f)]
        private float roofPuffSpeed = 6f;

        [SerializeField]
        private DustTrail.Settings roofDust = new DustTrail.Settings
        {
            size = 0.45f,
            lifetime = 0.8f,
            rise = 0.4f,
            colour = new Color(0.85f, 0.85f, 0.85f),
        };

        [SerializeField]
        [Tooltip("Switched off at the take-off: the strip, say.")]
        private List<GameObject> hideOnTakeOff = new List<GameObject>();

        [SerializeField]
        [Tooltip("Switched on at the take-off: the roof, say, if kept hidden till now.")]
        private List<GameObject> showOnTakeOff = new List<GameObject>();

        private PlayerMotor motor;
        private PlayerInputReader input;
        private Rigidbody playerBody;
        private RigidbodyInterpolation playerInterpolation;
        private bool flying;
        private bool finishing;
        private Vector3 along;
        private Transform edge;

        // How far up the run the end piece starts, and where the player takes off.
        private float pieceStart;
        private float takeOffAt;

        /// <summary>Raised once the player has landed on the roof, their controls back.</summary>
        public event Action<WallRunStage> Finished;

        public Transform WallBase => wallBase;
        public float LandingHeight => landingHeight;
        public float LandingSpread => landingSpread;
        public Transform StripBase => stripBase;
        public Transform ChainStart => chainStart;
        public GameObject HallSegment => hallSegment;
        public float SegmentLength => segmentLength;
        public float BuildAhead => buildAhead;
        public float KeepBehind => keepBehind;
        public List<GameObject> HideOnLanding => hideOnLanding;
        public List<GameObject> ShowOnLanding => showOnLanding;

        /// <summary>
        /// The boss is dead: lays the end piece on the strip at
        /// <paramref name="chainEnd"/> up <paramref name="runAlong"/> from
        /// the Strip Base, and takes the running player the rest of the way --
        /// to the edge, up onto the roof and their own controls.
        /// </summary>
        public void Finish(PlayerMotor player, Vector3 runAlong, float chainEnd)
        {
            if (finishing || player == null)
            {
                return;
            }

            finishing = true;
            motor = player;
            input = player.GetComponent<PlayerInputReader>();
            playerBody = player.GetComponent<Rigidbody>();
            along = runAlong;

            if (endPiece == null || roofEdge == null || chainStart == null)
            {
                string missing = endPiece == null ? "End Piece" : roofEdge == null ? "Roof Edge" : "Chain Start";
                Debug.LogWarning($"[WallRunStage] {name} has no {missing}, so the run just stops.", this);
                Release();
                return;
            }

            // Nothing behind to face any more: the player looks ahead, and
            // the camera's lead back toward the boss eases away.
            motor.FacingOverride = null;
            StartCoroutine(EaseFocusShift(0.6f));

            PlaceEnd(chainEnd);
            StartCoroutine(RunOut());
        }

        /// <summary>Eases the camera's focus shift back to nothing over <paramref name="seconds"/>.</summary>
        private IEnumerator EaseFocusShift(float seconds)
        {
            Vector3 from = TopDownCamera.FocusShift;

            for (float clock = 0f; clock < seconds && finishing && !flying; clock += Time.deltaTime)
            {
                TopDownCamera.FocusShift = Vector3.Lerp(from, Vector3.zero, Smooth(clock / seconds));
                yield return null;
            }

            TopDownCamera.FocusShift = Vector3.zero;
        }

        private void OnDisable()
        {
            if (finishing)
            {
                Release();
            }
        }

        /// <summary>The end piece, its near edge on the end of the strip, in line with the pieces.</summary>
        private void PlaceEnd(float chainEnd)
        {
            Quaternion facing = Quaternion.LookRotation(along, Vector3.up);
            GameObject placed = Instantiate(endPiece, chainStart.position,
                                            facing * Quaternion.Euler(endPieceRotation) * endPiece.transform.rotation);
            placed.name = endPiece.name;

            Vector2 extent = Extent(placed, along);
            placed.transform.position = chainStart.position + along * (chainEnd - extent.x - Progress(chainStart.position));

            pieceStart = chainEnd;
            float artEnd = chainEnd + (extent.y - extent.x);
            edge = FindChild(placed.transform, edgeName);

            if (edge == null)
            {
                Debug.LogWarning($"[WallRunStage] {endPiece.name} has no child named \"{edgeName}\", so the far end of its art is taken as the top of the wall. "
                               + "Put an empty called that on its top edge, in the middle, for the take-off to line up with the Roof Edge.", this);

                // The far end of its art, on the strip's centre line.
                GameObject marker = new GameObject(edgeName);
                marker.transform.SetParent(placed.transform, false);
                marker.transform.position = OnCentreLine(artEnd, stripBase.position.y);
                edge = marker.transform;
            }
            else if (Progress(edge.position) > artEnd + 0.5f)
            {
                Debug.LogWarning($"[WallRunStage] {endPiece.name}'s \"{edgeName}\" is {Progress(edge.position) - artEnd:0.#} past the far end of its art, "
                               + "so the player would run off the end before taking off. They take off before the end of the art instead.", this);
            }

            takeOffAt = Mathf.Min(Progress(edge.position), artEnd) - takeOffBefore;
        }

        /// <summary>
        /// On to the edge, then the take-off and the leap -- or the moment
        /// there is no floor under the player on the end piece, so they can
        /// never run off it into the air first.
        /// </summary>
        private IEnumerator RunOut()
        {
            while (motor != null)
            {
                float progress = Progress(motor.transform.position);

                if (progress >= takeOffAt || (progress >= pieceStart && !OnFloor()))
                {
                    break;
                }

                yield return null;
            }

            if (motor != null)
            {
                yield return Leap();
            }
        }

        /// <summary>
        /// The cut back to the real wall, then up and over onto the roof, the
        /// player turning upright and the camera back to looking down.
        /// </summary>
        private IEnumerator Leap()
        {
            // The turn standing the end piece back up as the wall top: up the
            // run to up the wall, up off the strip to out of the wall.
            Vector3 normal = Flat(roofEdge.forward);
            normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : -along;
            Quaternion toRoof = Quaternion.LookRotation(Vector3.up, normal)
                              * Quaternion.Inverse(Quaternion.LookRotation(along, Vector3.up));
            Vector3 edgeAt = edge.position;
            Vector3 roofAt = roofEdge.position;

            Vector3 ToRoof(Vector3 point) => roofAt + toRoof * (point - edgeAt);

            float footOffset = PlayerFootOffset();
            Transform view = Camera.main != null ? Camera.main.transform : null;

            Vector3 start = ToRoof(motor.transform.position);
            Quaternion startRotation = toRoof * motor.transform.rotation;
            Vector3 cameraStart = view != null ? ToRoof(view.position) : start;
            Quaternion cameraStartRotation = view != null ? toRoof * view.rotation : Quaternion.identity;

            // The run is over: the player is flown by hand from here.
            ChaseFrame.End();
            TopDownCamera.FocusShift = Vector3.zero;
            motor.StopAutoRun();
            motor.CancelDash();
            motor.FacingOverride = null;
            SetLocked(true);

            flying = true;
            motor.enabled = false;

            if (playerBody != null)
            {
                playerInterpolation = playerBody.interpolation;
                playerBody.interpolation = RigidbodyInterpolation.None;
                playerBody.isKinematic = true;
            }

            SetAll(hideOnTakeOff, false);
            SetAll(showOnTakeOff, true);
            motor.transform.SetPositionAndRotation(start, startRotation);
            TopDownCamera.Hold(cameraStart, cameraStartRotation);

            // Over the edge, onto the roof, facing on in.
            Vector3 inward = -normal;
            Vector3 landing = roofAt + inward * roofLanding + Vector3.up * footOffset;
            Vector3 over = roofAt + normal * footOffset + Vector3.up * (leapHeight + footOffset);
            Quaternion landingRotation = Quaternion.LookRotation(inward, Vector3.up);

            float yaw = Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg;
            TopDownCamera.Framing(landing, yaw, out Vector3 cameraEnd, out Quaternion cameraEndRotation);
            Vector3 cameraFrom = cameraStart - start;
            Vector3 cameraTo = cameraEnd - landing;

            for (float clock = 0f; clock < leapTime && motor != null; clock += Time.deltaTime)
            {
                SetLocked(true);

                float t = clock / leapTime;
                float turn = Smooth(t);
                float u = 1f - t;
                Vector3 at = u * u * start + 2f * u * t * over + t * t * landing;
                motor.transform.SetPositionAndRotation(at, Quaternion.Slerp(startRotation, landingRotation, turn));

                // Kept round the player as it swings, so they stay in view.
                TopDownCamera.Hold(at + Vector3.Slerp(cameraFrom, cameraTo, turn),
                                   Quaternion.Slerp(cameraStartRotation, cameraEndRotation, turn));
                yield return null;
            }

            if (motor == null)
            {
                yield break;
            }

            RestorePlayerBody();
            motor.Teleport(landing);
            motor.transform.rotation = landingRotation;

            if (playerBody != null)
            {
                playerBody.rotation = landingRotation;
            }

            TopDownCamera.Release(yaw);
            TopDownCamera.Shake(roofShake, roofShakeDuration);

            if (roofImpactFrames)
            {
                ImpactFrames frames = FindAnyObjectByType<ImpactFrames>();

                if (frames != null)
                {
                    frames.TriggerImpact(landing);
                }
            }

            RingDust(new Vector3(landing.x, roofAt.y, landing.z));
            SetLocked(false);
            finishing = false;
            Finished?.Invoke(this);
        }

        /// <summary>Gives the player their normal controls back, whatever state the ending left them in.</summary>
        private void Release()
        {
            finishing = false;

            if (flying)
            {
                RestorePlayerBody();
                TopDownCamera.Release(Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg);
            }

            ChaseFrame.End();
            TopDownCamera.FocusShift = Vector3.zero;

            if (motor != null)
            {
                motor.StopAutoRun();
                motor.FacingOverride = null;
            }

            SetLocked(false);
        }

        private void RestorePlayerBody()
        {
            if (!flying)
            {
                return;
            }

            flying = false;

            if (motor != null)
            {
                motor.enabled = true;
            }

            if (playerBody != null)
            {
                playerBody.isKinematic = false;
                playerBody.interpolation = playerInterpolation;
            }
        }

        private void SetLocked(bool locked)
        {
            if (input != null)
            {
                input.Locked = locked;
            }
        }

        /// <summary>How far up the run <paramref name="position"/> is from the Strip Base.</summary>
        private float Progress(Vector3 position)
        {
            return Vector3.Dot(position - stripBase.position, along);
        }

        /// <summary>The point on the strip's centre line <paramref name="progress"/> up it, at <paramref name="height"/>.</summary>
        private Vector3 OnCentreLine(float progress, float height)
        {
            Vector3 point = stripBase.position + along * progress;
            point.y = height;
            return point;
        }

        /// <summary>True while there is solid ground just under the player.</summary>
        private bool OnFloor()
        {
            Vector3 from = motor.transform.position + Vector3.up;

            foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, 1f + PlayerFootOffset() + 1.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider.transform.IsChildOf(motor.transform))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Height of the player's pivot above the bottom of their colliders.</summary>
        private float PlayerFootOffset()
        {
            float bottom = float.MaxValue;

            foreach (Collider part in motor.GetComponentsInChildren<Collider>())
            {
                if (!part.isTrigger && part.enabled)
                {
                    bottom = Mathf.Min(bottom, part.bounds.min.y);
                }
            }

            return bottom < float.MaxValue ? motor.transform.position.y - bottom : 0f;
        }

        /// <summary>Puffs of dust spread evenly round the spot, blown outward across the floor.</summary>
        private void RingDust(Vector3 floor)
        {
            if (roofPuffs <= 0)
            {
                return;
            }

            DustTrail.Cloud cloud = DustTrail.Cloud.Create("RoofLandingDust");
            float start = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

            for (int i = 0; i < roofPuffs; i++)
            {
                float angle = start + Mathf.PI * 2f * i / roofPuffs + UnityEngine.Random.Range(-0.1f, 0.1f);
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 velocity = outward * (roofPuffSpeed * UnityEngine.Random.Range(0.7f, 1.1f))
                                   + Vector3.up * (roofDust.rise * UnityEngine.Random.Range(0.5f, 1f));
                cloud.Emit(floor + outward * 0.6f + Vector3.up * (roofDust.size * 0.3f), velocity, roofDust);
            }

            cloud.Release();
        }

        private static void SetAll(List<GameObject> objects, bool active)
        {
            foreach (GameObject thing in objects)
            {
                if (thing != null)
                {
                    thing.SetActive(active);
                }
            }
        }

        private static Transform FindChild(Transform root, string childName)
        {
            if (string.IsNullOrEmpty(childName))
            {
                return null;
            }

            // The root aside: it is named after the prefab, and is no marker.
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != root && child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>How far behind (x) and ahead (y) of its pivot, along <paramref name="direction"/>, the renderers of <paramref name="piece"/> reach.</summary>
        private static Vector2 Extent(GameObject piece, Vector3 direction)
        {
            Vector3 pivot = piece.transform.position;
            float near = float.MaxValue;
            float far = float.MinValue;

            foreach (Renderer part in piece.GetComponentsInChildren<Renderer>())
            {
                Bounds bounds = part.bounds;

                // Every corner, so a turned box is measured right.
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));

                    float distance = Vector3.Dot(corner - pivot, direction);
                    near = Mathf.Min(near, distance);
                    far = Mathf.Max(far, distance);
                }
            }

            return near <= far ? new Vector2(near, far) : Vector2.zero;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static Vector3 Flat(Vector3 vector)
        {
            return new Vector3(vector.x, 0f, vector.z);
        }

        private void OnDrawGizmos()
        {
            if (roofEdge != null)
            {
                // The roof's edge, and where the player lands on it.
                Vector3 outward = Flat(roofEdge.forward).normalized;
                Vector3 roofLandingSpot = roofEdge.position - outward * roofLanding;

                Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.9f);
                Gizmos.DrawRay(roofEdge.position, outward * 3f);
                Gizmos.DrawLine(roofEdge.position, roofLandingSpot);
                Gizmos.DrawWireSphere(roofLandingSpot, 0.6f);
                Gizmos.DrawLine(roofEdge.position, roofEdge.position + Vector3.down * takeOffBefore);
            }

            if (wallBase == null)
            {
                return;
            }

            // The landing spot on the wall, and the band the player can land in.
            Vector3 normal = new Vector3(wallBase.forward.x, 0f, wallBase.forward.z).normalized;
            Vector3 wallRight = Vector3.Cross(normal, Vector3.up);
            Vector3 landing = wallBase.position + Vector3.up * landingHeight;

            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            Gizmos.DrawLine(wallBase.position, landing);
            Gizmos.DrawLine(landing - wallRight * landingSpread, landing + wallRight * landingSpread);
            Gizmos.DrawWireSphere(landing, 0.6f);
            Gizmos.DrawRay(wallBase.position, normal * 3f);

            if (stripBase == null)
            {
                return;
            }

            // The same spot on the strip: the wall's foot to it, laid flat.
            Vector3 up = new Vector3(stripBase.forward.x, 0f, stripBase.forward.z).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, up);
            Vector3 stripLanding = stripBase.position + up * landingHeight;

            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            Gizmos.DrawLine(stripBase.position, stripLanding);
            Gizmos.DrawLine(stripLanding - right * landingSpread, stripLanding + right * landingSpread);
            Gizmos.DrawWireSphere(stripLanding, 0.6f);
            Gizmos.DrawRay(stripBase.position - right * landingSpread, up * 3f);
            Gizmos.DrawRay(stripBase.position + right * landingSpread, up * 3f);
        }
    }
}
