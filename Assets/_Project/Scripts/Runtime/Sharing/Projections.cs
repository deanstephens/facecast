using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Facecast
{
    /// <summary>
    /// Shared screens. When you share yours (the page's share button, or P), a projection screen goes up a couple of
    /// metres in front of where you stand, facing you, and stays there however you move until you stop. The page
    /// sends its place to everyone with your screen's picture ("share"), and to anyone who arrives later; each
    /// browser puts up the same screen in the same place. Several people can share at once, each with their own.
    /// Offline there's no page to share a screen with, so P puts up a demo showing the square from above.
    /// </summary>
    public sealed class Projections : MonoBehaviour
    {
        /// <summary>The owner name for your own projection.</summary>
        public const string Self = "self";
        /// <summary>How far in front of you it goes up, trying each in turn until one is clear.</summary>
        static readonly float[] Distances = { 2.8f, 2.4f, 2.0f, 1.6f };
        /// <summary>The space a projection takes up, as half extents about its middle (width, 0.3 to 2.2 m up, depth).</summary>
        static readonly Vector3 HalfSize = new(1.3f, 0.95f, 0.2f);
        const float MiddleHeight = 1.25f;

        public static Projections Instance { get; private set; }

        public GameObject Prefab;

        readonly Dictionary<string, Projection> _shown = new();
        readonly List<string> _owners = new();
        RenderTexture _demo;

        /// <summary>You're sharing (or showing the offline demo).</summary>
        public bool Sharing => _shown.ContainsKey(Self);
        public int Count => _shown.Count;
        public Projection Get(string owner) => owner != null && _shown.TryGetValue(owner, out var p) ? p : null;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_demo) _demo.Release();
        }

        // ------------------------------------------------------------------ the room

        /// <summary>The page says you started sharing your screen (or stopped): put yours up in front of you.</summary>
        public void OnSharing(bool on)
        {
            if (!on)
            {
                Take(Self);
                return;
            }
            var player = App.Instance ? App.Instance.Player : null;
            if (!player || !player.Spawned) return;
            var pose = PlaceInFront(player.transform.position, player.Pose.Yaw, player.transform);
            float yaw = pose.rotation.eulerAngles.y;
            WebBridge.ShareAt(pose.position.x, pose.position.y, pose.position.z, yaw);
            Put(Self, pose, VideoFeeds.ScreenOf(VideoFeeds.Self), "Your screen", player.Avatar ? player.Avatar.Color : Palette.Colors[0]);
        }

        /// <summary>Peer <paramref name="id"/> put up their projection at <paramref name="at"/> (x, y, z, yaw), or took it down.</summary>
        public void OnShare(string id, bool on, float[] at)
        {
            if (string.IsNullOrEmpty(id) || id == Self) return;
            if (!on || at == null || at.Length < 4 || !float.IsFinite(at[0]) || !float.IsFinite(at[1]) || !float.IsFinite(at[2]) || !float.IsFinite(at[3]))
            {
                Take(id);
                return;
            }
            var flat = Vector2.ClampMagnitude(new Vector2(at[0], at[2]), 40f);
            var position = new Vector3(flat.x, Mathf.Clamp(at[1], -2f, 6f), flat.y);
            var avatar = Room.Instance ? Room.Instance.Get(id) : null;
            Put(id, new Pose(position, Quaternion.Euler(0f, at[3], 0f)), VideoFeeds.ScreenOf(id), LabelFor(avatar), ColorOf(avatar));
        }

        /// <summary>Someone left: their projection goes with them.</summary>
        public void PeerLeft(string id) => Take(id);

        static string LabelFor(RemoteAvatar avatar) =>
            avatar && !string.IsNullOrEmpty(avatar.DisplayName) ? $"{avatar.DisplayName}'s screen" : "Shared screen";

        static Color ColorOf(RemoteAvatar avatar) => avatar && avatar.Rig ? avatar.Rig.Color : Palette.Colors[0];

        Projection Put(string owner, Pose pose, string videoKey, string label, Color color)
        {
            if (_shown.TryGetValue(owner, out var p) && !p.Closing &&
                (p.transform.position - pose.position).sqrMagnitude < 1e-4f && Quaternion.Angle(p.transform.rotation, pose.rotation) < 0.5f)
            {
                p.SetLabel(label);
                p.SetColor(color);
                return p;
            }
            Take(owner);
            p = Instantiate(Prefab, pose.position, pose.rotation, transform).GetComponent<Projection>();
            p.Init(owner, videoKey, label, color);
            _shown[owner] = p;
            Debug.Log($"[Projections] {label} is up at {pose.position}");
            return p;
        }

        void Take(string owner)
        {
            if (_shown.Remove(owner, out var p) && p) p.Close();
        }

        void Update()
        {
            // A peer's name and colour may arrive after their projection did.
            var room = Room.Instance;
            if (!room) return;
            _owners.Clear();
            _owners.AddRange(_shown.Keys);
            foreach (var owner in _owners)
            {
                if (owner == Self || room.Get(owner) is not { } avatar) continue;
                var p = _shown[owner];
                p.SetLabel(LabelFor(avatar));
                p.SetColor(ColorOf(avatar));
            }
        }

        // ------------------------------------------------------------------ where it goes

        /// <summary>
        /// Where a projection goes for someone standing at <paramref name="feet"/> facing <paramref name="yaw"/>: as far
        /// in front of them of <see cref="Distances"/> as is clear of the scenery (people don't count, they can move),
        /// facing back at them.
        /// </summary>
        public static Pose PlaceInFront(Vector3 feet, float yaw, Transform ignore)
        {
            var forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var facing = Quaternion.LookRotation(-forward);
            Vector3 at = feet;
            foreach (float d in Distances)
            {
                at = feet + forward * d;
                if (!Blocked(at + Vector3.up * MiddleHeight, facing, ignore)) break;
            }
            return new Pose(at, facing);
        }

        static readonly Collider[] Hits = new Collider[32];

        static bool Blocked(Vector3 middle, Quaternion rotation, Transform ignore)
        {
            int n = Physics.OverlapBoxNonAlloc(middle, HalfSize, Hits, rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = Hits[i];
                if (ignore && c.transform.IsChildOf(ignore)) continue;
                if (c.GetComponentInParent<RemoteAvatar>()) continue;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ offline

        /// <summary>
        /// Offline there's no screen to share, so this puts up (or takes down) your projection showing the square from
        /// above: a way to see projections in the Editor and the desktop build.
        /// </summary>
        public void ToggleDemo()
        {
            if (Sharing)
            {
                Take(Self);
                return;
            }
            var player = App.Instance ? App.Instance.Player : null;
            if (!player || !player.Spawned) return;
            var pose = PlaceInFront(player.transform.position, player.Pose.Yaw, player.transform);
            var p = Put(Self, pose, VideoFeeds.ScreenOf(VideoFeeds.Self), "Your screen", player.Avatar ? player.Avatar.Color : Palette.Colors[0]);
            p.SetPicture(Photograph(), new Vector2Int(1280, 720));
        }

        /// <summary>A picture of the square from above for the demo, taken once with a camera of its own.</summary>
        RenderTexture Photograph()
        {
            if (_demo) return _demo;
            _demo = new RenderTexture(1280, 720, 24) { name = "Square from above", antiAliasing = 4 };
            var go = new GameObject("Demo camera");
            var cam = go.AddComponent<Camera>();
            if (Camera.main) cam.CopyFrom(Camera.main);
            cam.enabled = false;  // draws once, when asked
            cam.fieldOfView = 50f;
            var from = new Vector3(14f, 13f, -16f);
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(new Vector3(0f, 0.5f, 0f) - from));
            var request = new RenderPipeline.StandardRequest { destination = _demo };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            Destroy(go);
            return _demo;
        }
    }
}
