using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Facecast.Tests
{
    /// <summary>The question corner in the real scene, offline: writing questions, posting them, pulling them out one by one, the table.</summary>
    public class QuestionTests
    {
        App _app;
        Noticeboards _boards;
        NoteHand _hand;
        QuestionBox _box;
        QuestionTable _table;
        readonly List<(NoteData[] notes, string target)> _sent = new();

        IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Plaza");
            yield return null;
            _app = App.Instance;
            float until = Time.realtimeSinceStartup + 2f;
            while (!_app.Joined && Time.realtimeSinceStartup < until) yield return null;
            _boards = Noticeboards.Instance;
            _hand = NoteHand.Instance;
            _box = _boards.QuestionBox;
            _table = _boards.QuestionTable;
            Assert.That(_box && _table);
            _sent.Clear();
            _boards.Sent += (notes, target) => _sent.Add((notes, target));
            yield return null;
        }

        NoteHand.Target Aim(Vector3 feet, Vector3 at)
        {
            var d = at - (feet + Vector3.up * LocalPlayer.EyeHeight);
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            _app.Player.Teleport(new AvatarPose(feet, Mathf.Repeat(yaw, 360f), pitch));
            Physics.SyncTransforms();
            return _hand.Point(_app.Player.Cam);
        }

        Vector3 InFrontOfBox => _box.ReadingPose().Position;
        Vector3 AtTable => _table.ReadingPose().Position;

        static string Question(string id, int place, int v, string text = "Peer's question") =>
            "{\"type\":\"notes\",\"id\":\"peer-1\",\"notes\":[" + JsonUtility.ToJson(new NoteData { id = id, board = place, question = true, text = text, v = v, w = "peer-1" }) + "]}";

        [UnityTest]
        public IEnumerator TheBoxAndTableStandTogetherAndTheBoardsHaveTheirNewTitles()
        {
            yield return Load();
            Assert.That(_boards.Boards.Select(b => b.Title), Is.EqualTo(new[] { "Continue", "Stop", "Invent", "Act" }));
            Assert.That(Vector3.Distance(_box.transform.position, _table.transform.position), Is.InRange(1f, 2.5f), "side by side");
            float r = new Vector2(_table.transform.position.x, _table.transform.position.z).magnitude;
            Assert.That(r, Is.GreaterThan(DemoBots.OuterRadius + 1f), "out of the demo bots' way");
            Assert.That(_table.Size.x, Is.EqualTo(1.5f).Within(0.02f));

            // The tray, the box and the table can each be pointed at.
            Assert.That(Aim(InFrontOfBox, _box.Cards.bounds.center).OnCards);
            Assert.That(Aim(InFrontOfBox, _box.Body.bounds.center + Vector3.up * 0.1f).OnBox);
            var onTable = Aim(AtTable, _table.WorldPoint(new Vector2(0.3f, 0.5f)));
            Assert.That(onTable.OnTable);
            Assert.That(Vector2.Distance(onTable.Point, new Vector2(0.3f, 0.5f)), Is.LessThan(0.01f));

            // Offline, there are questions waiting, and the label says so.
            Assert.That(_box.Count, Is.EqualTo(3));
            Assert.That(_box.Label.Find("Count").GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(4 * 10));
            Assert.That(_table.Shows("sample-q0"), "the card saying how it works");
        }

        [UnityTest]
        public IEnumerator YouWriteAQuestionAndPostItInTheBox()
        {
            yield return Load();
            int before = _box.Count;
            Aim(InFrontOfBox, _box.Cards.bounds.center);
            _hand.Ask();
            yield return null;
            var overlay = Overlay.Instance;
            Assert.That(overlay.NoteEditorOpen && overlay.NoteDraftIsQuestion);
            overlay.NoteDraft = "Where do the paths go?";
            overlay.FinishNote(true);
            var held = _boards.Book.Get(_hand.HeldId);
            Assert.That(held.question && held.text == "Where do the paths go?");
            Assert.That(held.by, Is.Empty, "questions are anonymous");
            yield return null;
            Assert.That(_hand.View.Card && _hand.View.gameObject.activeSelf, "a card in your hand");

            Assert.That(_hand.Stick(0, new Vector2(0.5f, 0.5f)), Is.False, "cards don't go on the boards");
            Assert.That(Aim(InFrontOfBox, _box.Body.bounds.center + Vector3.up * 0.1f).OnBox);
            Assert.That(_hand.Post());
            yield return null;
            Assert.That(_hand.HeldId, Is.Null);
            Assert.That(_boards.Book.Get(held.id).board, Is.EqualTo(Notes.Box));
            Assert.That(_box.Count, Is.EqualTo(before + 1));
            Assert.That(_sent.Last().notes.Single().board, Is.EqualTo(Notes.Box), "everyone's box has it");
            Assert.That(_boards.Boards.Any(b => b.Shows(held.id)) || _table.Shows(held.id), Is.False, "out of sight");

            // A sticky note doesn't go in the box.
            _hand.Take(0);
            overlay.NoteDraft = "Not a question";
            overlay.FinishNote(true);
            Assert.That(_hand.Post(), Is.False);
            Assert.That(_hand.Put(Notes.Table, new Vector2(0.5f, 0.5f)), Is.False);
        }

        [UnityTest]
        public IEnumerator QuestionsComeOutOldestFirstAndAreLaidOnTheTableFacingYou()
        {
            yield return Load();
            var queue = _boards.NotesOn(Notes.Box).Select(n => n.id).ToList();
            Assert.That(queue, Is.EqualTo(new[] { "sample-q1", "sample-q2", "sample-q3" }));

            Aim(InFrontOfBox, _box.Body.bounds.center + Vector3.up * 0.1f);
            Assert.That(_hand.Pull());
            Assert.That(_hand.HeldId, Is.EqualTo("sample-q1"), "the one that's waited longest");
            Assert.That(_hand.Pull(), Is.False, "one at a time");
            yield return null;
            Assert.That(_box.Count, Is.EqualTo(2));

            var spot = new Vector2(0.3f, 0.6f);
            var aim = Aim(AtTable, _table.WorldPoint(spot));
            Assert.That(aim.OnTable);
            Assert.That(_hand.Put(Notes.Table, aim.Point));
            yield return null;
            var laid = _boards.Book.Get("sample-q1");
            Assert.That((laid.board, laid.holder), Is.EqualTo((Notes.Table, "")));
            Assert.That(_table.Shows("sample-q1"));
            var card = _table.ViewOf("sample-q1").transform;
            Assert.That(Vector3.Dot(card.forward, Vector3.up), Is.GreaterThan(0.99f), "face up");
            var look = Vector3.ProjectOnPlane(_app.Player.Cam.transform.forward, Vector3.up).normalized;
            Assert.That(Vector3.Dot(card.up, look), Is.GreaterThan(0.95f), "the right way up for you");
            Assert.That(Vector3.Distance(card.position, _table.WorldPoint(spot)), Is.LessThan(0.02f));

            Aim(InFrontOfBox, _box.Body.bounds.center + Vector3.up * 0.1f);
            Assert.That(_hand.Pull());
            Assert.That(_hand.HeldId, Is.EqualTo("sample-q2"));
        }

        [UnityTest]
        public IEnumerator QuestionsFromPeersArriveInTheBoxAndComeBackIfTheyLeaveHoldingOne()
        {
            yield return Load();
            _app.Room.OnNetEvent(Question("ana-q", Notes.Box, 2));
            yield return null;
            Assert.That(_box.Count, Is.EqualTo(4));
            Assert.That(_boards.NotesOn(Notes.Box).Last().id, Is.EqualTo("ana-q"), "behind the ones already waiting");

            // Ana pulls hers out and leaves with it: back in the box it goes.
            _app.Room.OnNetEvent("{\"type\":\"hello\",\"id\":\"peer-1\",\"name\":\"Ana\",\"color\":\"#F2705A\",\"cam\":false,\"mic\":false}");
            var pulled = JsonUtility.ToJson(new NoteData { id = "ana-q", board = Notes.Box, question = true, text = "Peer's question", v = 9, w = "peer-1", holder = "peer-1" });
            _app.Room.OnNetEvent("{\"type\":\"notes\",\"id\":\"peer-1\",\"notes\":[" + pulled + "]}");
            yield return null;
            Assert.That(_box.Count, Is.EqualTo(3));
            Assert.That(_boards.CarriedView("ana-q")?.Card, Is.True, "a card in her hand");
            _app.Room.OnNetEvent("{\"type\":\"leave\",\"id\":\"peer-1\"}");
            yield return null;
            Assert.That(_box.Count, Is.EqualTo(4));

            // Laid on the table elsewhere: it shows up there.
            _app.Room.OnNetEvent(Question("ana-q", Notes.Table, 10));
            yield return null;
            Assert.That(_table.Shows("ana-q"));
            Assert.That(_box.Count, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator AFullTableTakesNoMoreCards()
        {
            yield return Load();
            var fill = Enumerable.Range(0, Notes.MaxOnTable).Select(i => new NoteData
            {
                id = "t-" + i, board = Notes.Table, question = true, x = (i % 6 + 1) / 7f, y = (i / 6 + 1) / 6f, text = "?", v = 50 + i, w = "peer-1",
            }).ToArray();
            _boards.Receive(fill, "peer-1");
            Assert.That(_hand.Pull());
            Assert.That(_hand.Put(Notes.Table, new Vector2(0.5f, 0.5f)), Is.False);
            Assert.That(_hand.Post(), "back in the box is fine");
            _hand.AlwaysAim = true;
            Aim(AtTable, _table.WorldPoint(new Vector2(0.5f, 0.5f)));
            Assert.That(_hand.Pull());
            yield return null;
            yield return null;
            Assert.That(_hand.Prompt, Does.Contain("full"));
        }
    }
}
