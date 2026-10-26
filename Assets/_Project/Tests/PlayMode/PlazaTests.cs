using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Facecast.Tests
{
    /// <summary>The real scene, offline: you, the demo bots, and peers fed in as the page would send them.</summary>
    public class PlazaTests
    {
        App _app;

        IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Plaza");
            yield return null;
            _app = App.Instance;
            Assert.That(_app, Is.Not.Null);
            yield return Until(() => _app.Joined, 2f);
        }

        static IEnumerator Until(Func<bool> cond, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!cond() && Time.realtimeSinceStartup < until) yield return null;
        }

        static NetEvent Pose(string id, Vector3 at, float yaw = 0f) =>
            new() { type = "pose", id = id, p = new AvatarPose(at, yaw, 0f).ToWire(Time.realtimeSinceStartup) };

        [UnityTest]
        public IEnumerator OfflineYouJoinAtOnceWithThreeDemoBots()
        {
            yield return Load();
            Assert.That(_app.Offline);
            Assert.That(_app.Joined);
            var player = _app.Player;
            Assert.That(player.Spawned);
            Assert.That(player.Cam.transform.parent, Is.EqualTo(player.transform), "first person");
            Assert.That(player.Avatar.GetComponentsInChildren<Renderer>().All(r => r.shadowCastingMode == ShadowCastingMode.ShadowsOnly || r == player.Avatar.FaceRenderer),
                "your own avatar only casts its shadow");

            yield return Until(() => _app.Room.Avatars.All(a => a.Placed), 2f);
            Assert.That(_app.Bots.Count, Is.EqualTo(3));
            Assert.That(_app.Room.Count, Is.EqualTo(3));
            foreach (var bot in _app.Room.Avatars)
            {
                Assert.That(bot.Placed, bot.DisplayName);
                Assert.That(bot.Rig.FaceRenderer.sharedMaterial.shader.name, Is.EqualTo("Facecast/Face"));
                Assert.That(bot.Rig.ShowsVideo, Is.False, "no camera offline: the cartoon face");
                float r = new Vector2(bot.transform.position.x, bot.transform.position.z).magnitude;
                Assert.That(r, Is.InRange(DemoBots.InnerRadius - 0.1f, DemoBots.OuterRadius + 0.1f), "strolling round the fountain");
            }
        }

        [UnityTest]
        public IEnumerator APeerAppearsWithTheirFirstPoseFollowsTheirMovesAndLeaves()
        {
            yield return Load();
            var room = _app.Room;
            room.OnNetEvent("{\"type\":\"hello\",\"id\":\"peer-1\",\"name\":\"Ana\",\"color\":\"#F2705A\",\"cam\":true,\"mic\":true}");
            var ana = room.Get("peer-1");
            Assert.That(ana, Is.Not.Null);
            Assert.That(ana.DisplayName, Is.EqualTo("Ana"));
            Assert.That(ana.CamOn && ana.MicOn);
            yield return null;
            Assert.That(ana.Placed, Is.False, "hidden until a pose arrives");
            Assert.That(ana.GetComponentsInChildren<Renderer>().All(r => !r.enabled));

            // Walk her from x = 2 to x = 4 over half a second, 15 poses a second, as a peer would.
            var start = new Vector3(2f, 0f, -6f);
            for (int i = 0; i <= 8; i++)
            {
                room.Handle(Pose("peer-1", start + Vector3.right * (i * 0.25f), 90f));
                yield return new WaitForSeconds(1f / 15f);
            }
            Assert.That(ana.Placed);
            Assert.That(ana.GetComponentsInChildren<Renderer>().All(r => r.enabled));
            Assert.That(ana.transform.position.x, Is.InRange(2.5f, 4f), "played back a little behind");
            // She stands still: a peer keeps sending its pose all the same.
            for (int i = 0; i < 8; i++)
            {
                room.Handle(Pose("peer-1", start + Vector3.right * 2f, 90f));
                yield return new WaitForSeconds(1f / 15f);
            }
            Assert.That(Vector3.Distance(ana.transform.position, start + Vector3.right * 2f), Is.LessThan(0.05f), "catches up once she stops");
            Assert.That(ana.transform.eulerAngles.y, Is.EqualTo(90f).Within(0.5f));
            Assert.That(ana.Rig.Color.r, Is.GreaterThan(0.9f), "coral");
            Assert.That(ana.Rig.ShowsVideo, Is.False, "no video element outside a browser: the cartoon face");

            room.OnNetEvent("{\"type\":\"leave\",\"id\":\"peer-1\"}");
            yield return null;
            Assert.That(room.Get("peer-1"), Is.Null);
            Assert.That(ana == null, "her avatar is gone");
        }

        [UnityTest]
        public IEnumerator PosesForStrangersAndNonsenseAreIgnored()
        {
            yield return Load();
            int before = _app.Room.Count;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("can't read"));
            _app.Room.OnNetEvent("{oops");
            _app.Room.Handle(Pose("nobody", Vector3.zero));
            _app.Room.OnNetEvent("{\"type\":\"pose\",\"id\":\"bot-0\",\"p\":[1,2]}");
            yield return null;
            Assert.That(_app.Room.Count, Is.EqualTo(before));
        }

        [UnityTest]
        public IEnumerator EveryoneHasANameOverTheirHead()
        {
            yield return Load();
            yield return Until(() => _app.Room.Avatars.All(a => a.Placed), 2f);
            // Face the bots so their names are on screen.
            _app.Player.Teleport(new AvatarPose(new Vector3(0f, 0f, -12f), 0f, 0f));
            yield return null;
            yield return null;
            var root = UnityEngine.Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            var plates = root.Query<Label>(className: "fc-plate").ToList();
            Assert.That(plates.Count, Is.EqualTo(3));
            Assert.That(plates.Select(p => p.text), Is.EquivalentTo(_app.Room.Avatars.Select(a => a.DisplayName)));
        }

        [UnityTest]
        public IEnumerator TheFountainStopsYouAndLookingIsClamped()
        {
            yield return Load();
            var player = _app.Player;
            foreach (var id in _app.Room.Avatars.Select(a => a.Id).ToList()) _app.Room.Remove(id);  // nobody in the way
            player.InputEnabled = false;
            player.Teleport(new AvatarPose(new Vector3(0f, 0f, -5f), 0f, 0f));
            for (int i = 0; i < 120; i++)
            {
                player.Step(Vector2.up, Vector2.zero, true, false, 1f / 60f);
                yield return null;
            }
            float r = new Vector2(player.transform.position.x, player.transform.position.z).magnitude;
            Assert.That(r, Is.GreaterThan(2.6f), "stopped at the basin wall");
            Assert.That(r, Is.LessThan(3.5f), "but walked up to it");

            float yaw = player.Pose.Yaw;
            player.Step(Vector2.zero, new Vector2(100f, 0f), false, false, 1f / 60f);
            Assert.That(Mathf.DeltaAngle(yaw, player.Pose.Yaw), Is.EqualTo(100f * player.LookDegreesPerPixel).Within(1e-3f));
            player.Step(Vector2.zero, new Vector2(0f, -100000f), false, false, 1f / 60f);
            Assert.That(player.Pose.Pitch, Is.EqualTo(85f), "looking down stops at your feet");
        }

        [UnityTest]
        public IEnumerator YouGetGoingAtAnyFrameRate()
        {
            yield return Load();
            var player = _app.Player;
            player.InputEnabled = false;
            // A thousand frames a second, and the controller's default minimum move (1 mm): every early step is
            // shorter than that, which must not stop the speed building up.
            player.GetComponent<CharacterController>().minMoveDistance = 0.001f;
            player.Teleport(new AvatarPose(new Vector3(0f, 0f, -11f), 90f, 0f));
            yield return null;
            var from = player.transform.position;
            for (int i = 0; i < 600; i++) player.Step(Vector2.up, Vector2.zero, false, false, 0.001f);
            Assert.That(player.transform.position.x - from.x, Is.GreaterThan(1.2f), "0.6 s of walking");
        }

        [UnityTest]
        public IEnumerator TheWallOnTheLawnKeepsYouIn()
        {
            yield return Load();
            var player = _app.Player;
            player.InputEnabled = false;
            // Out along the east path, past the hedge, towards the wall.
            player.Teleport(new AvatarPose(new Vector3(20f, 0f, 0f), 90f, 0f));
            for (int i = 0; i < 180; i++)
            {
                player.Step(Vector2.up, Vector2.zero, true, false, 1f / 60f);
                yield return null;
            }
            Assert.That(player.transform.position.x, Is.InRange(27f, 30.1f));
            Assert.That(player.transform.position.y, Is.GreaterThan(-0.5f), "still on the ground");
        }
    }

    /// <summary>Walking with a simulated keyboard through the Input System test fixture.</summary>
    public class PlazaInputTests : InputTestFixture
    {
        [UnityTest]
        public IEnumerator HoldingWWalksYouForward()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("Plaza");
            yield return null;
            var app = App.Instance;
            float until = Time.realtimeSinceStartup + 2f;
            while (!app.Joined && Time.realtimeSinceStartup < until) yield return null;
            var player = app.Player;
            player.Teleport(new AvatarPose(new Vector3(0f, 0f, -11f), 90f, 0f));  // facing east, along the paving
            yield return null;
            var from = player.transform.position;

            Press(keyboard.wKey);
            yield return new WaitForSeconds(0.6f);
            Release(keyboard.wKey);
            yield return null;

            var moved = player.transform.position - from;
            Assert.That(moved.x, Is.GreaterThan(1f), "walked east");
            Assert.That(Mathf.Abs(moved.z), Is.LessThan(0.3f));
        }
    }
}
