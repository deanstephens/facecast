using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Facecast.Tests
{
    /// <summary>The noticeboards in the real scene, offline: taking, writing, sticking and moving notes, and notes from peers.</summary>
    public class BoardTests
    {
        App _app;
        Noticeboards _boards;
        NoteHand _hand;
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
            Assert.That(_boards && _hand);
            _sent.Clear();
            _boards.Sent += (notes, target) => _sent.Add((notes, target));
            yield return null;
        }

        /// <summary>Stands you at <paramref name="feet"/> looking straight at <paramref name="at"/>.</summary>
        void Look(Vector3 feet, Vector3 at)
        {
            var d = at - (feet + Vector3.up * LocalPlayer.EyeHeight);
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            _app.Player.Teleport(new AvatarPose(feet, Mathf.Repeat(yaw, 360f), pitch));
        }

        NoteHand.Target Aim(Noticeboard board, Vector3 at)
        {
            Look(board.ReadingPose(1.3f).Position, at);
            Physics.SyncTransforms();
            return _hand.Point(_app.Player.Cam);
        }

        static string NotesJson(params NoteData[] notes) =>
            "{\"type\":\"notes\",\"id\":\"peer-1\",\"notes\":" + JsonUtility.ToJson(new NoteList { notes = notes }).Substring(9);

        static NoteData PeerNote(string id, int board, float x, float y, int v, string holder = "") =>
            new() { id = id, board = board, x = x, y = y, text = "From Ana", by = "Ana", v = v, w = "peer-1", holder = holder };

        void PeerArrives(string id) =>
            _app.Room.OnNetEvent($"{{\"type\":\"hello\",\"id\":\"{id}\",\"name\":\"Ana\",\"color\":\"#F2705A\",\"cam\":false,\"mic\":false}}");

        /// <summary>Peer <paramref name="id"/> stands at <paramref name="at"/>, sending poses as a peer does, for a while.</summary>
        IEnumerator Stand(string id, Vector3 at, float seconds)
        {
            for (float until = Time.realtimeSinceStartup + seconds; Time.realtimeSinceStartup < until;)
            {
                _app.Room.Handle(new NetEvent { type = "pose", id = id, p = new AvatarPose(at, 0f, 0f).ToWire(Time.realtimeSinceStartup) });
                yield return new WaitForSeconds(1f / 15f);
            }
        }

        [UnityTest]
        public IEnumerator FourBoardsStandRoundTheSquareFacingTheFountain()
        {
            yield return Load();
            Assert.That(_boards.Boards.Length, Is.EqualTo(4));
            Assert.That(_boards.Boards.Select(b => b.Title).Distinct().Count(), Is.EqualTo(4));
            foreach (var board in _boards.Boards)
            {
                Assert.That(board.CorkCollider && board.Header, board.Title);
                Assert.That(board.Pads.Length == 4 && board.Pads.All(p => p), board.Title);
                Assert.That(board.Size.x, Is.EqualTo(2.1f).Within(0.01f));
                var toCentre = -new Vector3(board.Cork.position.x, 0f, board.Cork.position.z).normalized;
                Assert.That(Vector3.Dot(board.Cork.forward, toCentre), Is.GreaterThan(0.99f), board.Title + " faces the fountain");
                var title = board.Header.Find("Title").GetComponent<MeshFilter>().sharedMesh;
                Assert.That(title.vertexCount, Is.EqualTo(4 * board.Title.Count(c => c != ' ')), board.Title + " is written on its plate");
            }
            // Offline, the boards come with notes to read.
            Assert.That(_boards.Boards[0].Shows("sample-0"));
            Assert.That(_boards.Boards.Sum(b => _boards.NotesOn(b.Index).Count), Is.GreaterThan(8));
            var ink = _boards.Boards[0].ViewOf("sample-0").Ink.sharedMesh;
            Assert.That(ink.vertexCount, Is.GreaterThan(4 * 40), "its words are written on it");
            Assert.That(ink.bounds.extents.x, Is.LessThanOrEqualTo(Notes.Size * 0.5f), "within the paper");
        }

        [UnityTest]
        public IEnumerator YouTakeANoteWriteOnItAndStickItWhereYouLook()
        {
            yield return Load();
            var board = _boards.Boards[1];
            var pad = Aim(board, board.Pads[2].transform.position);
            Assert.That(pad.Board, Is.EqualTo(board));
            Assert.That(pad.Pad, Is.EqualTo(2), "pointing at the blue notepad");

            _hand.Take(pad.Pad);
            yield return null;
            var overlay = Overlay.Instance;
            Assert.That(_hand.Writing && overlay.NoteEditorOpen && _app.Player.Typing);
            Assert.That(overlay.NoteDraftColor, Is.EqualTo(2));
            Assert.That(_sent, Is.Empty, "nobody hears about a note until it's written");
            overlay.NoteDraft = "Hello, board!";
            overlay.FinishNote(true);
            Assert.That(_hand.Writing || overlay.NoteEditorOpen || _app.Player.Typing, Is.False);
            Assert.That(_hand.HeldId, Is.Not.Null);
            var held = _boards.Book.Get(_hand.HeldId);
            Assert.That((held.text, held.color, held.board, held.holder, held.by), Is.EqualTo(("Hello, board!", 2, -1, "you", "You")));
            Assert.That(_sent.Last().notes.Single().id, Is.EqualTo(held.id), "everyone sees it in your hand");

            var spot = new Vector2(0.3f, 0.7f);
            var cork = Aim(board, board.WorldPoint(spot));
            Assert.That(cork.OnCork);
            Assert.That(Vector2.Distance(cork.Point, spot), Is.LessThan(0.01f));
            Assert.That(cork.Note, Is.Null, "an empty spot");
            Assert.That(_hand.Stick(board.Index, cork.Point));
            yield return null;
            var stuck = _boards.Book.Get(held.id);
            Assert.That((stuck.board, stuck.holder), Is.EqualTo((1, "")));
            Assert.That(Vector2.Distance(new Vector2(stuck.x, stuck.y), spot), Is.LessThan(0.01f));
            Assert.That(board.Shows(held.id));
            var view = board.ViewOf(held.id);
            Assert.That(Vector3.Distance(view.transform.position, board.WorldPoint(spot)), Is.LessThan(0.02f));
            Assert.That(view.Ink.sharedMesh.vertexCount, Is.EqualTo(4 * ("Hello,board!".Length + "—You".Length)));
            Assert.That(_sent.Last().notes.Single().board, Is.EqualTo(1));

            // Pick it back up, then throw it away.
            var again = Aim(board, board.WorldPoint(spot));
            Assert.That(again.Note?.id, Is.EqualTo(held.id), "pointing at your note");
            Assert.That(_hand.PickUp(held.id));
            yield return null;
            Assert.That(board.Shows(held.id), Is.False, "off the board, in your hand");
            Assert.That(_hand.View && _hand.View.gameObject.activeSelf);
            _hand.Discard();
            yield return null;
            Assert.That(_boards.Book.Get(held.id).gone);
            Assert.That(_hand.HeldId, Is.Null);
            Assert.That(board.Shows(held.id), Is.False);
            Assert.That(_hand.View.gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator CancellingANewNotePutsItBackAndTheWebEditorAnswersThroughTheRoom()
        {
            yield return Load();
            int before = _boards.Book.Count;
            _hand.Take(0);
            Overlay.Instance.FinishNote(false);
            Assert.That(_hand.HeldId, Is.Null);
            Assert.That(_boards.Book.Count, Is.EqualTo(before));

            // In a browser the page's editor answers with a "write" message.
            _hand.Take(3);
            _app.Room.OnNetEvent("{\"type\":\"write\",\"ok\":true,\"text\":\"Via the page\",\"tint\":1}");
            Assert.That(_hand.Writing, Is.False);
            Assert.That(Overlay.Instance.NoteEditorOpen, Is.False);
            var n = _boards.Book.Get(_hand.HeldId);
            Assert.That((n.text, n.color), Is.EqualTo(("Via the page", 1)));
            // E: write on it again.
            _hand.Write();
            Assert.That(Overlay.Instance.NoteDraft, Is.EqualTo("Via the page"));
            Overlay.Instance.NoteDraft = "Changed my mind";
            Overlay.Instance.FinishNote(true);
            Assert.That(_boards.Book.Get(_hand.HeldId).text, Is.EqualTo("Changed my mind"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator NotesFromAPeerAppearMoveAndGo()
        {
            yield return Load();
            var board = _boards.Boards[2];
            _app.Room.OnNetEvent(NotesJson(PeerNote("ana-1", 2, 0.4f, 0.5f, 50)));
            yield return null;
            Assert.That(board.Shows("ana-1"));
            var view = board.ViewOf("ana-1");
            Assert.That(Vector3.Distance(view.transform.position, board.WorldPoint(new Vector2(0.4f, 0.5f))), Is.LessThan(0.02f));

            _app.Room.OnNetEvent(NotesJson(PeerNote("ana-1", 2, 0.6f, 0.5f, 51)));
            _app.Room.OnNetEvent(NotesJson(PeerNote("ana-1", 2, 0.1f, 0.1f, 40)));  // late, older
            yield return null;
            Assert.That(Vector3.Distance(board.ViewOf("ana-1").transform.position, board.WorldPoint(new Vector2(0.6f, 0.5f))), Is.LessThan(0.02f));

            var gone = PeerNote("ana-1", 2, 0.6f, 0.5f, 52);
            gone.gone = true;
            _app.Room.OnNetEvent(NotesJson(gone));
            yield return null;
            Assert.That(board.Shows("ana-1"), Is.False);
        }

        [UnityTest]
        public IEnumerator APeerCarriesANoteInTheirHandAndItGoesBackWhenTheyLeave()
        {
            yield return Load();
            var board = _boards.Boards[3];
            PeerArrives("peer-1");
            _app.Room.OnNetEvent(NotesJson(PeerNote("ana-2", 3, 0.5f, 0.5f, 10)));
            yield return Stand("peer-1", new Vector3(5f, 0f, -9f), 0.5f);
            Assert.That(board.Shows("ana-2"));

            _app.Room.OnNetEvent(NotesJson(PeerNote("ana-2", 3, 0.5f, 0.5f, 11, "peer-1")));
            yield return null;
            Assert.That(board.Shows("ana-2"), Is.False, "off the board");
            var carried = _boards.CarriedView("ana-2");
            var ana = _app.Room.Get("peer-1");
            Assert.That(carried && carried.transform.parent == ana.Rig.HandR, "in her hand");
            Assert.That(carried.gameObject.activeSelf);

            _app.Room.OnNetEvent("{\"type\":\"leave\",\"id\":\"peer-1\"}");
            yield return null;
            Assert.That(board.Shows("ana-2"), "back where it was");
            Assert.That(_boards.CarriedView("ana-2"), Is.Null);
        }

        [UnityTest]
        public IEnumerator IfSomeoneGrabsYourNoteAtTheSameMomentTheFirstWins()
        {
            yield return Load();
            PeerArrives("zara-1");
            Assert.That(_hand.PickUp("sample-0"));
            var mine = _boards.Book.Get("sample-0");
            // Zara picked it up at the same moment: the same version, and "zara-1" outranks "you".
            var hers = mine.Clone();
            hers.holder = "zara-1";
            hers.w = "zara-1";
            _app.Room.OnNetEvent(NotesJson(hers));
            yield return null;
            Assert.That(_hand.HeldId, Is.Null, "you let go");
            Assert.That(_hand.Prompt, Does.Contain("took that note first"));
            Assert.That(_boards.CarriedView("sample-0"), Is.Not.Null, "she has it");
        }

        [UnityTest]
        public IEnumerator AFullBoardTakesNoMoreNotes()
        {
            yield return Load();
            var board = _boards.Boards[3];
            var fill = Enumerable.Range(0, Notes.MaxPerBoard).Select(i => PeerNote("fill-" + i, 3, (i % 8 + 1) / 9f, (i / 8 + 1) / 6f, 100 + i)).ToArray();
            _boards.Receive(fill, "peer-1");
            _hand.Take(0);
            Overlay.Instance.FinishNote(true);
            Assert.That(_hand.HeldId, Is.Not.Null, "a blank note is fine");
            Assert.That(_hand.Stick(3, new Vector2(0.5f, 0.5f)), Is.False);
            Assert.That(_hand.HeldId, Is.Not.Null, "still in your hand");

            _hand.AlwaysAim = true;
            Look(board.ReadingPose(1.3f).Position, board.WorldPoint(new Vector2(0.95f, 0.95f)));
            yield return null;
            yield return null;
            Assert.That(_hand.Aim.OnCork);
            Assert.That(_hand.Prompt, Does.Contain("full"));
            Assert.That(_hand.Stick(0, new Vector2(0.5f, 0.2f)), "another board has room");
        }

        [UnityTest]
        public IEnumerator SomeoneNewGetsEveryNote()
        {
            yield return Load();
            _sent.Clear();
            PeerArrives("peer-9");
            var (notes, target) = _sent.Single();
            Assert.That(target, Is.EqualTo("peer-9"));
            Assert.That(notes.Length, Is.EqualTo(_boards.Book.Count));
            Assert.That(notes.Select(n => n.id), Does.Contain("sample-0"));
        }
    }
}
