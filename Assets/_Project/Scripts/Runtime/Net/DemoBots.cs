using System.Collections.Generic;
using UnityEngine;

namespace Facecast
{
    /// <summary>
    /// Demo bots, so the square isn't empty when you're alone (three by default offline, <c>?bots=N</c> on the web).
    /// They stroll around the fountain, stop to face whoever is nearby and chat (their mouths move and their lights
    /// glow). They talk to <see cref="Room"/> with the same hello and pose messages a real peer sends, so they
    /// exercise the same playback path. In a web build they wear your own camera on their faces, a quick way to
    /// check your picture. Only you see them.
    /// </summary>
    public sealed class DemoBots : MonoBehaviour
    {
        /// <summary>The ring they stroll in: clear of the fountain basin, inside the benches.</summary>
        public const float InnerRadius = 3.7f, OuterRadius = 8.1f;
        const float WalkSpeed = 1.25f;
        const float SendInterval = 1f / 15f;

        sealed class Bot
        {
            public string Id;
            public RemoteAvatar Avatar;
            public float Radius, Angle, TargetRadius, TargetAngle;
            public float Yaw, Pitch, Pause, Seed, NextSend;
            public bool Talking;
        }

        readonly List<Bot> _bots = new();
        Room _room;

        public int Count => _bots.Count;
        /// <summary>Holds every bot where it is, facing the way it faces (the screenshot tour poses them).</summary>
        public bool Frozen { get; set; }

        public void Spawn(int count, Room room)
        {
            _room = room;
            for (int k = 0; k < count; k++)
            {
                int i = _bots.Count;
                var bot = new Bot
                {
                    Id = "bot-" + i,
                    Radius = Random.Range(InnerRadius + 0.3f, OuterRadius - 0.3f),
                    Angle = Random.Range(0f, Mathf.PI * 2f),
                    Seed = Random.value * 100f,
                    Pause = Random.Range(0f, 2f),
                };
                bot.TargetRadius = bot.Radius;
                bot.TargetAngle = bot.Angle;
                bot.Yaw = Random.Range(0f, 360f);
                var color = Palette.Colors[(i + 1) % Palette.Colors.Length];
                room.Handle(new NetEvent
                {
                    type = "hello", id = bot.Id, name = Palette.BotNames[i % Palette.BotNames.Length],
                    color = "#" + ColorUtility.ToHtmlStringRGB(color), cam = WebBridge.Available, mic = false,
                });
                bot.Avatar = room.Get(bot.Id);
                bot.Avatar.IsBot = true;
                if (WebBridge.Available) bot.Avatar.VideoKey = VideoFeeds.Self;
                _bots.Add(bot);
                Send(bot, Time.realtimeSinceStartup);
            }
        }

        public Vector3 PositionOf(int index) => Polar(_bots[index].Radius, _bots[index].Angle);
        public RemoteAvatar AvatarOf(int index) => _bots[index].Avatar;

        /// <summary>Places a bot exactly (the screenshot tour), facing <paramref name="yaw"/>.</summary>
        public void Place(int index, float radius, float angle, float yaw)
        {
            var b = _bots[index];
            b.Radius = b.TargetRadius = radius;
            b.Angle = b.TargetAngle = angle;
            b.Yaw = yaw;
            b.Pitch = 0f;
        }

        static Vector3 Polar(float r, float a) => new(r * Mathf.Cos(a), 0f, r * Mathf.Sin(a));

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            float dt = Time.deltaTime;
            foreach (var b in _bots)
            {
                if (!b.Avatar) continue;
                if (!Frozen) Step(b, dt);
                float chatter = Mathf.PerlinNoise(Time.time * 5.5f, b.Seed);
                b.Avatar.BotSpeaking = b.Talking ? Mathf.Clamp01((chatter - 0.38f) * 2.4f) : 0f;
                if (now >= b.NextSend) Send(b, now);
            }
        }

        void Send(Bot b, float now)
        {
            b.NextSend = now + SendInterval;
            var pose = new AvatarPose(Polar(b.Radius, b.Angle), b.Yaw, b.Pitch);
            _room.Handle(new NetEvent { type = "pose", id = b.Id, p = pose.ToWire(now) });
        }

        void Step(Bot b, float dt)
        {
            if (b.Pause > 0f)
            {
                b.Pause -= dt;
                // Face whoever is nearest and look them in the eye.
                var here = Polar(b.Radius, b.Angle);
                if (Nearest(b, here, out var other))
                {
                    var to = other - here;
                    float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    b.Yaw = Mathf.MoveTowardsAngle(b.Yaw, yaw, 160f * dt);
                    float pitch = -Mathf.Atan2(other.y - 1.5f, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
                    b.Pitch = Mathf.MoveTowards(b.Pitch, Mathf.Clamp(pitch, -30f, 30f), 40f * dt);
                }
                if (b.Pause <= 0f) PickTarget(b);
                return;
            }

            // Walk in polar coordinates, so the path curves around the fountain instead of through it.
            float dA = Mathf.DeltaAngle(b.Angle * Mathf.Rad2Deg, b.TargetAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float dR = b.TargetRadius - b.Radius;
            var step = new Vector2(dR, dA * b.Radius);
            float dist = step.magnitude;
            if (dist < 0.05f)
            {
                b.Pause = Random.Range(2.5f, 7f);
                b.Talking = Random.value < 0.65f;
                return;
            }
            var before = Polar(b.Radius, b.Angle);
            float f = Mathf.Min(1f, WalkSpeed * dt / dist);
            b.Radius += dR * f;
            b.Angle += dA * f;
            var moved = Polar(b.Radius, b.Angle) - before;
            if (moved.sqrMagnitude > 1e-8f)
                b.Yaw = Mathf.MoveTowardsAngle(b.Yaw, Mathf.Atan2(moved.x, moved.z) * Mathf.Rad2Deg, 240f * dt);
            b.Pitch = Mathf.MoveTowards(b.Pitch, 0f, 40f * dt);
            b.Talking = false;
        }

        void PickTarget(Bot b)
        {
            b.TargetRadius = Random.Range(InnerRadius, OuterRadius);
            b.TargetAngle = b.Angle + Random.Range(0.4f, 1.6f) * (Random.value < 0.5f ? -1f : 1f);
        }

        /// <summary>The head of the nearest avatar (another bot, a peer or you) within chatting distance.</summary>
        bool Nearest(Bot self, Vector3 here, out Vector3 head)
        {
            var best = (distance: 7f * 7f, head: Vector3.zero, found: false);
            void Consider(Vector3 feet, float height)
            {
                float d = (feet - here).sqrMagnitude;
                if (d < 0.01f || d >= best.distance) return;
                best = (d, feet + Vector3.up * height, true);
            }
            foreach (var a in _room.Avatars)
                if (a != self.Avatar && a.Placed) Consider(a.Pose.Position, 1.5f);
            var app = App.Instance;
            if (app && app.Player && app.Player.Spawned) Consider(app.Player.transform.position, LocalPlayer.EyeHeight);
            head = best.head;
            return best.found;
        }
    }
}
