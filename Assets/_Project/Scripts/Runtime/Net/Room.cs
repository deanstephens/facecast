using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Facecast
{
    /// <summary>
    /// Everyone else in the room. The page's networking (facecast.js) calls <see cref="OnNetEvent"/> with every
    /// message from the WebRTC room; <see cref="DemoBots"/> feed <see cref="Handle"/> the same messages directly. A
    /// peer's avatar appears with their first "hello", moves with their poses and goes with "leave".
    /// </summary>
    public sealed class Room : MonoBehaviour
    {
        public static Room Instance { get; private set; }

        public GameObject AvatarPrefab;

        readonly Dictionary<string, RemoteAvatar> _avatars = new();

        public IReadOnlyCollection<RemoteAvatar> Avatars => _avatars.Values;
        public int Count => _avatars.Count;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Called by the page: <c>unityInstance.SendMessage("Room", "OnNetEvent", json)</c>.</summary>
        public void OnNetEvent(string json)
        {
            var e = NetEvent.Parse(json);
            if (e == null) Debug.LogWarning("[Room] Ignored a message the game can't read: " + json);
            else Handle(e);
        }

        public void Handle(NetEvent e)
        {
            switch (e.type)
            {
                case "self":
                    App.Instance?.OnJoined(e);
                    Noticeboards.Instance?.OnJoined(e);
                    break;
                case "hello" when !string.IsNullOrEmpty(e.id):
                    bool known = _avatars.ContainsKey(e.id);
                    Get(e.id, true).Apply(e);
                    if (known) break;
                    Debug.Log($"[Room] {e.name} ({e.id}) is here: camera {(e.cam ? "on" : "off")}, microphone {(e.mic ? "on" : "off")}");
                    Noticeboards.Instance?.PeerArrived(e.id);
                    break;
                case "pose" when e.id != null && _avatars.TryGetValue(e.id, out var avatar):
                    if (AvatarPose.TryFromWire(e.p, out var pose, out float sent))
                        avatar.Poses.Add(sent, Time.realtimeSinceStartup, pose);
                    break;
                case "leave" when e.id != null:
                    Remove(e.id);
                    Noticeboards.Instance?.PeerLeft(e.id);
                    Projections.Instance?.PeerLeft(e.id);
                    break;
                case "sharing":
                    Projections.Instance?.OnSharing(e.on);
                    break;
                case "share":
                    Projections.Instance?.OnShare(e.id, e.on, e.p);
                    break;
                case "notes":
                    Noticeboards.Instance?.Receive(e.notes, e.id);
                    break;
                case "write":
                    NoteHand.Instance?.OnWritten(e.ok, e.text ?? "", e.tint);
                    break;
            }
        }

        public RemoteAvatar Get(string id, bool create = false)
        {
            if (_avatars.TryGetValue(id, out var avatar) || !create) return avatar;
            var go = Instantiate(AvatarPrefab, transform);
            avatar = go.AddComponent<RemoteAvatar>();
            avatar.Init(id);
            // Solid to walk into, but moved only by its poses.
            var body = go.AddComponent<CapsuleCollider>();
            body.height = 1.8f;
            body.radius = 0.32f;
            body.center = new Vector3(0f, 0.9f, 0f);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
            _avatars[id] = avatar;
            return avatar;
        }

        public void Remove(string id)
        {
            if (!_avatars.Remove(id, out var avatar)) return;
            Debug.Log($"[Room] {avatar.DisplayName} ({id}) left");
            if (avatar.VideoKey == id) VideoFeeds.Release(id);
            Destroy(avatar.gameObject);
        }

        /// <summary>
        /// Test hooks the page can call (<c>facecast.debug.unity("…")</c> in the browser console):
        /// <c>teleport x z yaw [pitch]</c> moves you; <c>bots n</c> adds demo bots; <c>note …</c> handles sticky notes
        /// (<see cref="NoteHand.DebugCommand"/>).
        /// </summary>
        public void OnDebugCommand(string command)
        {
            var w = command.Split(' ');
            float F(int i) => i < w.Length && float.TryParse(w[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
            var app = App.Instance;
            switch (w[0])
            {
                case "teleport" when app && app.Player.Spawned:
                    app.Player.Teleport(new AvatarPose(new Vector3(F(1), 0f, F(2)), F(3), F(4)));
                    break;
                case "bots" when app:
                    app.Bots.Spawn(Mathf.Clamp((int)F(1), 1, 8), this);
                    break;
                case "note" when NoteHand.Instance:
                    NoteHand.Instance.DebugCommand(w);
                    break;
                default:
                    Debug.LogWarning("[Room] Unknown debug command: " + command);
                    break;
            }
        }
    }
}
