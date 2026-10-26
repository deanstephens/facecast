using UnityEngine;

namespace Facecast
{
    /// <summary>
    /// Someone else in the room (a peer, or a demo bot): plays back their poses, shows their camera on the face
    /// screen and lights up while they speak. Stays hidden until their first pose arrives.
    /// </summary>
    [RequireComponent(typeof(AvatarRig))]
    public sealed class RemoteAvatar : MonoBehaviour
    {
        /// <summary>Your camera closer than this to an avatar's head fades it out, so it never fills the screen.</summary>
        const float FadeNear = 0.45f, FadeFar = 0.95f;

        public readonly PoseBuffer Poses = new();
        public string Id { get; private set; }
        public string DisplayName { get; private set; } = "";
        /// <summary>Whose camera the face shows: the peer's own id, or <see cref="VideoFeeds.Self"/> for demo bots in a web build.</summary>
        public string VideoKey { get; set; }
        public bool CamOn { get; private set; }
        public bool MicOn { get; private set; }
        public bool IsBot { get; set; }
        /// <summary>Demo bots set their own speaking level; peers' levels come from the page's voice mix.</summary>
        public float BotSpeaking { get; set; }
        /// <summary>Smoothed speaking level, 0..1.</summary>
        public float Speaking { get; private set; }
        public bool Placed { get; private set; }
        public AvatarPose Pose { get; private set; }
        public AvatarRig Rig { get; private set; }

        Vector3 _velocity;

        public void Init(string id)
        {
            Id = id;
            VideoKey = id;
            Rig = GetComponent<AvatarRig>();
            Rig.SetVisible(false);
            name = "Avatar " + id;
        }

        /// <summary>Applies a "hello": name, colour and whether the camera and microphone are on.</summary>
        public void Apply(NetEvent hello)
        {
            DisplayName = string.IsNullOrWhiteSpace(hello.name) ? "Guest" : hello.name.Trim();
            CamOn = hello.cam;
            MicOn = hello.mic;
            Rig.SetColor(hello.ParsedColor(Palette.Colors[0]));
        }

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (Poses.TrySample(Time.realtimeSinceStartup, out var pose))
            {
                var previous = transform.position;
                transform.SetPositionAndRotation(pose.Position, Quaternion.Euler(0f, pose.Yaw, 0f));
                var velocity = Placed ? (pose.Position - previous) / dt : Vector3.zero;
                _velocity = Vector3.Lerp(_velocity, velocity, 1f - Mathf.Exp(-12f * dt));
                Pose = pose;
                if (!Placed)
                {
                    Placed = true;
                    Rig.SetVisible(true);
                }
            }
            if (!Placed) return;

            float level = IsBot ? BotSpeaking : MicOn ? WebBridge.AudioLevel(Id) : 0f;
            // Quick to light up, slower to fade, so speech doesn't flicker.
            Speaking = Mathf.Lerp(Speaking, level, 1f - Mathf.Exp((level > Speaking ? -18f : -6f) * dt));

            if (CamOn && VideoFeeds.TryGet(VideoKey, out var texture, out var size)) Rig.SetVideo(texture, size);
            else Rig.ShowCartoon();

            var cam = Camera.main;
            if (cam && Rig.Head)
            {
                float d = Vector3.Distance(cam.transform.position, Rig.Head.position + Vector3.up * 0.25f);
                Rig.SetFade(1f - Mathf.InverseLerp(FadeNear, FadeFar, d));
            }

            Rig.Animate(transform.InverseTransformDirection(_velocity), Pose.Pitch, Speaking, dt);
        }
    }
}
