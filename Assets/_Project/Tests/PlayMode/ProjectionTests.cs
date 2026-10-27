using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Facecast.Tests
{
    /// <summary>Shared screens' projections in the real scene, offline: yours, peers', where they go up and come down.</summary>
    public class ProjectionTests
    {
        App _app;
        Projections _projections;

        IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Plaza");
            yield return null;
            _app = App.Instance;
            float until = Time.realtimeSinceStartup + 2f;
            while (!_app.Joined && Time.realtimeSinceStartup < until) yield return null;
            _projections = Projections.Instance;
            Assert.That(_projections);
            yield return null;
        }

        /// <summary>How far apart two places are across the ground (a player's feet settle a few centimetres when they land).</summary>
        static float Across(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        static string Share(string id, bool on, Vector3 at, float yaw, float size = 2.4f) =>
            $"{{\"type\":\"share\",\"id\":\"{id}\",\"on\":{(on ? "true" : "false")},\"p\":[{at.x},{at.y},{at.z},{yaw}],\"size\":{size.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";

        [UnityTest]
        public IEnumerator YourScreenGoesUpInFrontOfYouFacingYouAndStaysPut()
        {
            yield return Load();
            var player = _app.Player;
            // Out on the south side of the square, facing the fountain, with open paving ahead.
            player.Teleport(new AvatarPose(new Vector3(0f, 0f, -11f), 0f, 0f));
            yield return null;
            _app.Room.OnNetEvent("{\"type\":\"sharing\",\"on\":true}");
            var mine = _projections.Get(Projections.Self);
            Assert.That(mine, Is.Not.Null);
            Assert.That(_projections.Sharing);
            Assert.That(mine.VideoKey, Is.EqualTo("screen:self"));
            Assert.That(mine.Label, Is.EqualTo("Your screen"));
            var t = mine.transform;
            Assert.That(Across(t.position, new Vector3(0f, 0f, -8.2f)), Is.LessThan(0.01f), "2.8 m ahead, nothing in the way");
            Assert.That(t.position.y, Is.EqualTo(player.transform.position.y).Within(0.01f), "standing where you stand");
            Assert.That(Vector3.Dot(t.forward, Vector3.back), Is.GreaterThan(0.999f), "facing you");

            yield return new WaitForSeconds(0.8f);
            Assert.That(mine.Unrolled, Is.EqualTo(1f), "unrolled");
            Assert.That(mine.Screen.localScale.y, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(mine.Plate.Find("Name").GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(4 * 8), "its label is written on it");

            // Walk off: it stays where it went up.
            player.Teleport(new AvatarPose(new Vector3(5f, 0f, -6f), 90f, 0f));
            yield return null;
            Assert.That(Across(t.position, new Vector3(0f, 0f, -8.2f)), Is.LessThan(0.01f));

            _app.Room.OnNetEvent("{\"type\":\"sharing\",\"on\":false}");
            Assert.That(_projections.Sharing, Is.False);
            yield return new WaitForSeconds(0.9f);
            Assert.That(mine == null, "rolled up and gone");
        }

        [UnityTest]
        public IEnumerator SomethingInTheWayBringsItCloser()
        {
            yield return Load();
            var player = _app.Player;
            // Five metres from the fountain's middle, facing it: at 2.8 m the screen would stand in the basin.
            player.Teleport(new AvatarPose(new Vector3(0f, 0f, -5f), 0f, 0f));
            yield return null;
            var pose = Projections.PlaceInFront(player.transform.position, 0f, player.transform);
            float d = Vector3.Distance(pose.position, player.transform.position);
            Assert.That(d, Is.LessThan(2.8f - 0.01f));
            Assert.That(d, Is.GreaterThan(1.5f));
            var hits = Physics.OverlapBox(pose.position + Vector3.up * 1.25f, new Vector3(1.3f, 0.95f, 0.2f), pose.rotation);
            foreach (var h in hits)
                Assert.That(h.transform.IsChildOf(player.transform) || h.GetComponentInParent<RemoteAvatar>(), h.name + " is in the way");
        }

        [UnityTest]
        public IEnumerator APeersScreenGoesUpWhereTheyPutItAndComesDownWhenTheyStopOrLeave()
        {
            yield return Load();
            var room = _app.Room;
            var at = new Vector3(6f, 0f, -9f);
            room.OnNetEvent(Share("peer-1", true, at, 200f));
            var theirs = _projections.Get("peer-1");
            Assert.That(theirs, Is.Not.Null);
            Assert.That(Vector3.Distance(theirs.transform.position, at), Is.LessThan(0.01f));
            Assert.That(theirs.transform.eulerAngles.y, Is.EqualTo(200f).Within(0.1f));
            Assert.That(theirs.VideoKey, Is.EqualTo("screen:peer-1"));
            Assert.That(theirs.Label, Is.EqualTo("Shared screen"), "their name isn't known yet");

            room.OnNetEvent("{\"type\":\"hello\",\"id\":\"peer-1\",\"name\":\"Ana\",\"color\":\"#F2705A\",\"cam\":false,\"mic\":false}");
            yield return null;
            Assert.That(theirs.Label, Is.EqualTo("Ana's screen"));

            room.OnNetEvent(Share("peer-1", true, at, 200f));  // sent again (to a newcomer): no change
            Assert.That(_projections.Get("peer-1"), Is.EqualTo(theirs));
            Assert.That(_projections.Count, Is.EqualTo(1));

            room.OnNetEvent(Share("peer-1", false, at, 200f));
            Assert.That(_projections.Get("peer-1"), Is.Null);
            yield return new WaitForSeconds(0.9f);
            Assert.That(theirs == null);

            room.OnNetEvent(Share("peer-1", true, at, 200f));
            Assert.That(_projections.Get("peer-1"), Is.Not.Null);
            room.OnNetEvent("{\"type\":\"leave\",\"id\":\"peer-1\"}");
            Assert.That(_projections.Get("peer-1"), Is.Null, "gone with them");

            room.OnNetEvent("{\"type\":\"share\",\"id\":\"peer-2\",\"on\":true,\"p\":[1,2]}");
            Assert.That(_projections.Count, Is.EqualTo(0), "nonsense is ignored");
        }

        [UnityTest]
        public IEnumerator TheSharerPicksTheSizeAndItGrowsWhereItStands()
        {
            yield return Load();
            var player = _app.Player;
            player.Teleport(new AvatarPose(new Vector3(0f, 0f, -11f), 0f, 0f));
            yield return null;
            // Large: 3.6 m wide, half as big again as medium, and half as far again in front of you.
            _app.Room.OnNetEvent("{\"type\":\"sharing\",\"on\":true,\"size\":3.6}");
            var mine = _projections.Get(Projections.Self);
            Assert.That(mine.Width, Is.EqualTo(3.6f).Within(1e-4f));
            Assert.That(mine.transform.localScale.x, Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(Across(mine.transform.position, new Vector3(0f, 0f, -6.8f)), Is.LessThan(0.01f), "4.2 m ahead");

            // Huge, while it's up: it grows where it stands.
            var at = mine.transform.position;
            _app.Room.OnNetEvent("{\"type\":\"shareSize\",\"size\":5}");
            Assert.That(_projections.Get(Projections.Self), Is.EqualTo(mine), "the same projection");
            yield return new WaitForSeconds(1f);
            Assert.That(mine.transform.localScale.x, Is.EqualTo(5f / 2.4f).Within(1e-3f));
            Assert.That(mine.transform.position, Is.EqualTo(at), "where it stood");

            // A peer's: small from the start, then resized by their next message.
            var theirs = new Vector3(8f, 0f, 3f);
            _app.Room.OnNetEvent(Share("peer-1", true, theirs, 90f, 1.6f));
            var p = _projections.Get("peer-1");
            Assert.That(p.transform.localScale.x, Is.EqualTo(1.6f / 2.4f).Within(1e-3f));
            _app.Room.OnNetEvent(Share("peer-1", true, theirs, 90f, 99f));
            Assert.That(_projections.Get("peer-1"), Is.EqualTo(p));
            Assert.That(p.Width, Is.EqualTo(Projections.MaxWidth), "no bigger than the biggest");
            _app.Room.OnNetEvent("{\"type\":\"share\",\"id\":\"peer-1\",\"on\":true,\"p\":[8,0,3,90]}");
            Assert.That(p.Width, Is.EqualTo(Projections.DefaultWidth), "an older page, sending no size: medium");
        }

        [UnityTest]
        public IEnumerator APictureIsShownWholeAndOfflinePIsADemo()
        {
            yield return Load();
            _app.Player.Teleport(new AvatarPose(new Vector3(0f, 0f, -11f), 0f, 0f));
            yield return null;
            _projections.ToggleDemo();
            var demo = _projections.Get(Projections.Self);
            Assert.That(demo, Is.Not.Null);
            yield return new WaitForSeconds(0.5f);
            Assert.That(demo.HasPicture, "a picture of the square from above");

            // A 4:3 picture on the 16:9 screen: full height, bars at the sides.
            var picture = new Texture2D(4, 3);
            demo.SetPicture(picture, new Vector2Int(640, 480));
            yield return null;
            var fit = demo.ScreenMaterial.GetVector("_Fit");
            Assert.That(fit.x, Is.EqualTo(0.75f).Within(0.01f));
            Assert.That(fit.y, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(fit.z, Is.EqualTo(0.125f).Within(0.01f));

            // [ and ]: the demo's size.
            _projections.StepDemoSize(1);
            Assert.That(demo.Width, Is.EqualTo(3.6f).Within(1e-4f));
            _projections.StepDemoSize(-1);
            _projections.StepDemoSize(-1);
            Assert.That(demo.Width, Is.EqualTo(1.6f).Within(1e-4f));

            _projections.ToggleDemo();
            Assert.That(_projections.Sharing, Is.False);
            Object.Destroy(picture);
        }
    }
}
