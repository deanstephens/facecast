using UnityEngine;

namespace Facecast
{
    /// <summary>
    /// Starts everything. In a web build the page (facecast.js) shows the join card over a slow tour of the square;
    /// once you join it sends a "self" message and you appear. Everywhere else (the Editor, desktop builds) there's no
    /// network: you join an offline square at once, with demo bots for company.
    /// </summary>
    public sealed class App : MonoBehaviour
    {
        public static App Instance { get; private set; }

        public Room Room;
        public LocalPlayer Player;
        public DemoBots Bots;
        [Header("Ambient light (Flat shader)")]
        public Color AmbientSky = new(0.56f, 0.62f, 0.72f);
        public Color AmbientGround = new(0.42f, 0.38f, 0.3f);

        public bool Joined { get; private set; }
        public bool Offline => !WebBridge.Available;
        public string RoomName { get; private set; } = "";
        public string SelfName { get; private set; } = "";

        void Awake()
        {
            Instance = this;
            Shader.SetGlobalColor("_FC_AmbientSky", AmbientSky);
            Shader.SetGlobalColor("_FC_AmbientGround", AmbientGround);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            var options = LaunchOptions.Current;
            int bots = options.Bots >= 0 ? options.Bots : Offline ? 3 : 0;
            if (bots > 0) Bots.Spawn(bots, Room);
            if (Offline)
                Room.Handle(new NetEvent { type = "self", id = "you", name = "You", color = "#5AA6F2", room = "offline" });
            else
                WebBridge.Ready();  // the page now sends what it queued, "self" once you press Join
            if (!string.IsNullOrEmpty(options.AutoCapture))
                gameObject.AddComponent<ScreenshotTour>().Begin(options.AutoCapture);
        }

        /// <summary>The page says you're in (or, offline, you're in at once).</summary>
        public void OnJoined(NetEvent self)
        {
            RoomName = self.room ?? "";
            SelfName = self.name ?? "";
            Player.Spawn(SpawnPose(Random.value), self.ParsedColor(Palette.Colors[0]));
            Joined = true;
            Debug.Log($"[App] Joined room '{RoomName}' as {SelfName} ({self.id})");
        }

        /// <summary>A place on the paving around the fountain, facing it, for <paramref name="t"/> in 0..1.</summary>
        public static AvatarPose SpawnPose(float t)
        {
            float a = t * Mathf.PI * 2f;
            float r = 5.6f;
            var pos = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            float yaw = Mathf.Atan2(-pos.x, -pos.z) * Mathf.Rad2Deg;
            return new AvatarPose(pos, Mathf.Repeat(yaw, 360f), 8f);
        }
    }
}
