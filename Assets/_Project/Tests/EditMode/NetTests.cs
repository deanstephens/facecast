using NUnit.Framework;
using UnityEngine;

namespace Facecast.Tests
{
    /// <summary>Pose playback, picture cropping, the page's messages and start-up options.</summary>
    public class NetTests
    {
        static AvatarPose At(float x, float yaw = 0f, float pitch = 0f) => new(new Vector3(x, 0f, 0f), yaw, pitch);

        [Test]
        public void PosesPlayBackSmoothlyBehindTheNewest()
        {
            var buffer = new PoseBuffer();
            // The sender's clock runs 100 s ahead of ours; the offset must not matter.
            for (int i = 0; i <= 10; i++) buffer.Add(100f + i * 0.1f, i * 0.1f, At(i));
            Assert.That(buffer.TrySample(1.0f, out var pose));
            // 1.0 s on our clock is 101.0 s on theirs; playing PoseBuffer.Delay behind gives x = 8.5.
            Assert.That(pose.Position.x, Is.EqualTo(10f - PoseBuffer.Delay * 10f).Within(1e-3f));
        }

        [Test]
        public void ADelayedUpdateDoesNotJerkPlaybackBackwards()
        {
            var buffer = new PoseBuffer();
            buffer.Add(0.0f, 0.00f, At(0));
            buffer.Add(0.1f, 0.10f, At(1));
            buffer.Add(0.2f, 0.45f, At(2));   // arrived 250 ms late
            buffer.Add(0.3f, 0.46f, At(3));
            // The fastest delivery sets the clock offset (about 0, creeping up slowly), so the late ones don't push
            // playback back towards x = 0.9, where trusting the latest delivery would put it.
            Assert.That(buffer.TrySample(0.4f, out var pose));
            Assert.That(pose.Position.x, Is.EqualTo(2.5f).Within(0.2f));
        }

        [Test]
        public void WhenUpdatesStopItCarriesOnBrieflyThenStands()
        {
            var buffer = new PoseBuffer();
            buffer.Add(0.0f, 0.0f, At(0));
            buffer.Add(0.1f, 0.1f, At(1));
            Assert.That(buffer.TrySample(0.1f + PoseBuffer.Delay + 0.1f, out var ahead));
            Assert.That(ahead.Position.x, Is.EqualTo(2f).Within(1e-3f), "extrapolates along the last step");
            Assert.That(buffer.TrySample(10f, out var stopped));
            Assert.That(stopped.Position.x, Is.EqualTo(1f + PoseBuffer.MaxExtrapolation * 10f).Within(1e-3f), "but not far");
        }

        [Test]
        public void StaleAndDuplicatePosesAreIgnored()
        {
            var buffer = new PoseBuffer();
            buffer.Add(1.0f, 1.0f, At(1));
            buffer.Add(0.5f, 1.1f, At(-5));
            buffer.Add(1.0f, 1.2f, At(-5));
            Assert.That(buffer.Count, Is.EqualTo(1));
        }

        [Test]
        public void YawBlendsTheShortWayRound()
        {
            var mid = AvatarPose.Lerp(At(0, 350f), At(0, 10f), 0.5f);
            Assert.That(Mathf.DeltaAngle(mid.Yaw, 0f), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(mid.Yaw, Is.InRange(0f, 360f));
        }

        [Test]
        public void PosesSurviveTheWireAndBadOnesAreRejected()
        {
            var pose = new AvatarPose(new Vector3(1.5f, 0.25f, -3f), 270f, 12f);
            Assert.That(AvatarPose.TryFromWire(pose.ToWire(42f), out var back, out float time));
            Assert.That(back.Position, Is.EqualTo(pose.Position));
            Assert.That(back.Yaw, Is.EqualTo(270f));
            Assert.That(back.Pitch, Is.EqualTo(12f));
            Assert.That(time, Is.EqualTo(42f));
            Assert.That(AvatarPose.TryFromWire(new[] { 1f, 2f, 3f }, out _, out _), Is.False);
            Assert.That(AvatarPose.TryFromWire(new[] { float.NaN, 0f, 0f, 0f, 0f, 0f }, out _, out _), Is.False);
            Assert.That(AvatarPose.TryFromWire(new[] { 0f, 0f, 0f, 0f, 500f, 0f }, out var clamped, out _));
            Assert.That(clamped.Pitch, Is.EqualTo(89f), "a peer can't bend its head backwards");
        }

        [Test]
        public void WidePicturesAreCroppedAtTheSidesAndTallOnesTopAndBottom()
        {
            // 16:9 into the 4:3 screen: keep the middle three quarters of the width.
            var wide = VideoCrop.AspectFill(new Vector2Int(1280, 720));
            Assert.That(wide.x, Is.EqualTo(0.75f).Within(1e-4f));
            Assert.That(wide.y, Is.EqualTo(1f));
            Assert.That(wide.z, Is.EqualTo(0.125f).Within(1e-4f));
            Assert.That(wide.w, Is.EqualTo(0f));
            // A portrait phone camera (3:4) keeps the middle 56% of the height.
            var tall = VideoCrop.AspectFill(new Vector2Int(480, 640));
            Assert.That(tall.x, Is.EqualTo(1f));
            Assert.That(tall.y, Is.EqualTo(0.5625f).Within(1e-4f));
            Assert.That(tall.w, Is.EqualTo(0.21875f).Within(1e-4f));
            // 4:3 fits exactly; no picture yet leaves the UVs alone.
            Assert.That(VideoCrop.AspectFill(new Vector2Int(640, 480)), Is.EqualTo(new Vector4(1, 1, 0, 0)));
            Assert.That(VideoCrop.AspectFill(Vector2Int.zero), Is.EqualTo(new Vector4(1, 1, 0, 0)));
        }

        [Test]
        public void ProjectionsStepThroughTheirSizes()
        {
            Assert.That(Projections.Widths, Is.EqualTo(new[] { 1.6f, 2.4f, 3.6f, 5f }));
            Assert.That(Projections.Step(2.4f, 1), Is.EqualTo(3.6f));
            Assert.That(Projections.Step(2.4f, -1), Is.EqualTo(1.6f));
            Assert.That(Projections.Step(5f, 1), Is.EqualTo(5f), "no bigger than huge");
            Assert.That(Projections.Step(1.6f, -1), Is.EqualTo(1.6f), "no smaller than small");
            Assert.That(Projections.Step(3.3f, 1), Is.EqualTo(5f), "from the nearest size (large)");
        }

        [Test]
        public void ASharedScreenIsFittedWholeWithBars()
        {
            float screen = 16f / 9f;
            Assert.That(VideoCrop.AspectFit(new Vector2Int(1920, 1080), screen), Is.EqualTo(new Vector4(1, 1, 0, 0)), "16:9 fills it");
            var narrow = VideoCrop.AspectFit(new Vector2Int(1024, 768), screen);  // 4:3: bars at the sides
            Assert.That(narrow.x, Is.EqualTo(0.75f).Within(1e-4f));
            Assert.That(narrow.y, Is.EqualTo(1f));
            Assert.That(narrow.z, Is.EqualTo(0.125f).Within(1e-4f));
            var wide = VideoCrop.AspectFit(new Vector2Int(3440, 1440), screen);   // ultrawide: bars above and below
            Assert.That(wide.x, Is.EqualTo(1f));
            Assert.That(wide.y, Is.EqualTo(screen / (3440f / 1440f)).Within(1e-4f));
            Assert.That(wide.w, Is.EqualTo((1f - wide.y) / 2f).Within(1e-4f));
            Assert.That(VideoCrop.AspectFit(Vector2Int.zero, screen), Is.EqualTo(new Vector4(1, 1, 0, 0)));
        }

        [Test]
        public void AFaceCropFromThePageTurnsIntoTheScreensUvs()
        {
            // The page measures from the picture's top left; the texture's v runs up from the bottom.
            var st = VideoCrop.FromRect(0.25f, 0.1f, 0.5f, 0.5f);
            Assert.That(Vector4.Distance(st, new Vector4(0.5f, 0.5f, 0.25f, 0.4f)), Is.LessThan(1e-6f));
            // The screen's top left corner lands on the crop's top left corner.
            var topLeft = new Vector2(0f, 1f) * new Vector2(st.x, st.y) + new Vector2(st.z, st.w);
            Assert.That(Vector2.Distance(topLeft, new Vector2(0.25f, 0.9f)), Is.LessThan(1e-6f));
            Assert.That(VideoCrop.FromRect(0f, 0f, 1f, 1f), Is.EqualTo(new Vector4(1, 1, 0, 0)), "the whole picture");
            var wild = VideoCrop.FromRect(0.9f, -0.5f, 0.6f, 0f);
            Assert.That(wild.x, Is.EqualTo(0.6f).Within(1e-6f), "kept inside the picture");
            Assert.That(wild.y, Is.EqualTo(0.05f).Within(1e-6f));
            Assert.That(wild.z + wild.x, Is.LessThanOrEqualTo(1f + 1e-6f));
            Assert.That(wild.w + wild.y, Is.LessThanOrEqualTo(1f + 1e-6f));
        }

        [Test]
        public void ThePagesMessagesParse()
        {
            var hello = NetEvent.Parse("{\"type\":\"hello\",\"id\":\"abc\",\"name\":\"Ana\",\"color\":\"#F2705A\",\"cam\":true,\"mic\":false}");
            Assert.That(hello.type, Is.EqualTo("hello"));
            Assert.That(hello.name, Is.EqualTo("Ana"));
            Assert.That(hello.cam, Is.True);
            Assert.That(hello.mic, Is.False);
            Assert.That(hello.ParsedColor(Color.black), Is.EqualTo(Palette.Hex("#F2705A")));
            var pose = NetEvent.Parse("{\"type\":\"pose\",\"id\":\"abc\",\"p\":[1,0,2,90,-10,5.5]}");
            Assert.That(pose.p, Has.Length.EqualTo(6));
            Assert.That(NetEvent.Parse("not json"), Is.Null);
            Assert.That(NetEvent.Parse("{}"), Is.Null);
            Assert.That(NetEvent.Parse("{\"type\":\"hello\",\"color\":\"red-ish\"}").ParsedColor(Color.black), Is.EqualTo(Color.black));
        }

        [Test]
        public void OptionsComeFromTheCommandLineOrThePageAddress()
        {
            var args = LaunchOptions.FromArgs(new[] { "Facecast", "-bots", "5", "-autocapture", "Shots" });
            Assert.That(args.Bots, Is.EqualTo(5));
            Assert.That(args.AutoCapture, Is.EqualTo("Shots"));
            Assert.That(LaunchOptions.FromArgs(new[] { "-autocapture" }).AutoCapture, Is.EqualTo("Screenshots"));
            Assert.That(LaunchOptions.FromArgs(new string[0]).Bots, Is.EqualTo(-1));
            Assert.That(LaunchOptions.FromUrl("https://x.github.io/facecast/?bots=2#mint-plaza-12").Bots, Is.EqualTo(2));
            Assert.That(LaunchOptions.FromUrl("https://x.github.io/facecast/?bots#room").Bots, Is.EqualTo(3));
            Assert.That(LaunchOptions.FromUrl("https://x.github.io/facecast/?bots=99").Bots, Is.EqualTo(8));
            Assert.That(LaunchOptions.FromUrl("https://x.github.io/facecast/#room?bots=2").Bots, Is.EqualTo(-1), "the room name isn't a query");
        }
    }
}
