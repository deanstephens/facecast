using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Facecast.Tests
{
    /// <summary>Sticky notes: agreeing on versions without a server, cleaning what arrives, fitting words on the paper, stacking.</summary>
    public class NoteTests
    {
        static NoteData Note(string id, int v, string w, string text = "hi", int board = 0) =>
            new() { id = id, v = v, w = w, text = text, board = board };

        [Test]
        public void TheNewestVersionWinsWhicheverOrderTheyArrive()
        {
            var a = new NoteBook();
            var b = new NoteBook();
            var first = Note("n", 1, "ana", "first");
            var second = Note("n", 2, "ben", "second");
            a.Merge(first.Clone());
            a.Merge(second.Clone());
            b.Merge(second.Clone());
            b.Merge(first.Clone());
            Assert.That(a.Get("n").text, Is.EqualTo("second"));
            Assert.That(b.Get("n").text, Is.EqualTo("second"));
            Assert.That(a.Merge(first.Clone()), Is.False, "old news changes nothing");
            Assert.That(a.Merge(second.Clone()), Is.False, "nor does the same news twice");
        }

        [Test]
        public void SimultaneousChangesAgreeOnTheSameWinner()
        {
            var a = new NoteBook();
            var b = new NoteBook();
            var ana = Note("n", 5, "ana", "Ana's");
            var ben = Note("n", 5, "ben", "Ben's");
            a.Merge(ana.Clone());
            a.Merge(ben.Clone());
            b.Merge(ben.Clone());
            b.Merge(ana.Clone());
            Assert.That(a.Get("n").text, Is.EqualTo(b.Get("n").text));
            Assert.That(a.Get("n").text, Is.EqualTo("Ben's"), "ties go to the higher id");
        }

        [Test]
        public void YourChangesOutrankEverythingYouveSeen()
        {
            var mine = new NoteBook { Self = "me" };
            mine.Merge(Note("n", 7, "ben"));
            var change = mine.Get("n").Clone();
            change.text = "mine now";
            var committed = mine.Commit(change);
            Assert.That(committed.v, Is.EqualTo(8));
            Assert.That(committed.w, Is.EqualTo("me"));

            var theirs = new NoteBook { Self = "ben" };
            theirs.Merge(Note("n", 7, "ben"));
            Assert.That(theirs.Merge(committed.Clone()));
            Assert.That(theirs.Get("n").text, Is.EqualTo("mine now"));
            Assert.That(mine.NewId(), Does.StartWith("me-"));
            Assert.That(mine.NewId(), Is.Not.EqualTo(mine.NewId()));
        }

        [Test]
        public void ThrownAwayNotesStayThrownAway()
        {
            var book = new NoteBook();
            book.Merge(Note("n", 3, "ana"));
            var gone = Note("n", 4, "ben");
            gone.gone = true;
            book.Merge(gone);
            Assert.That(book.Merge(Note("n", 3, "ana")), Is.False, "an older copy can't bring it back");
            Assert.That(book.Get("n").gone);
            Assert.That(book.CountOn(0), Is.EqualTo(0));
        }

        [Test]
        public void NotesFromOutsideAreCleanedUp()
        {
            var n = new NoteData
            {
                id = "x", board = 1, x = float.NaN, y = 7f, tilt = 90f, color = 12, by = new string('b', 50), holder = null,
                text = "  Bell\u0007s\r\n1\n2\n3\n4\n5\n6\n7\n8\n9\n" + new string('z', 300),
            };
            Assert.That(n.Sanitize());
            Assert.That(n.x, Is.EqualTo(0.5f));
            Assert.That(n.y, Is.EqualTo(1f));
            Assert.That(n.tilt, Is.EqualTo(Notes.MaxTilt));
            Assert.That(n.color, Is.EqualTo(Notes.Colors.Length - 1));
            Assert.That(n.by.Length, Is.EqualTo(24));
            Assert.That(n.holder, Is.EqualTo(""));
            Assert.That(n.text, Does.StartWith("Bells\n1\n"));
            Assert.That(n.text.Length, Is.LessThanOrEqualTo(Notes.MaxText));
            Assert.That(n.text.Count(c => c == '\n'), Is.EqualTo(Notes.MaxLines - 1));

            Assert.That(new NoteData { id = "x", board = 9 }.Sanitize(), Is.False);
            Assert.That(new NoteData { id = "", board = 0 }.Sanitize(), Is.False);
            Assert.That(new NoteBook().Merge(new NoteData { id = null }), Is.False);
            Assert.That(Notes.Clip("ab\U0001F600", 3), Is.EqualTo("ab"), "never half an emoji");
        }

        [Test]
        public void OnlySoManyThrownAwayNotesAreRemembered()
        {
            var book = new NoteBook();
            for (int i = 0; i < NoteBook.MaxKept + 50; i++)
            {
                var n = Note("n" + i, i + 1, "ana");
                n.gone = true;
                book.Merge(n);
            }
            book.Merge(Note("live", 1, "ana"));
            Assert.That(book.Count, Is.EqualTo(NoteBook.MaxKept));
            Assert.That(book.Get("live"), Is.Not.Null, "live notes are kept");
            Assert.That(book.Get("n0"), Is.Null, "the oldest are forgotten");
            Assert.That(book.Get("n" + (NoteBook.MaxKept + 49)), Is.Not.Null);
        }

        [Test]
        public void QuestionCardsGoInTheBoxOrOnTheTableAndFaceAnyWayThere()
        {
            Assert.That(Notes.Fits(question: true, Notes.Box) && Notes.Fits(true, Notes.Table));
            Assert.That(Notes.Fits(true, 0) || Notes.Fits(false, Notes.Box) || Notes.Fits(false, Notes.Table) || Notes.Fits(true, -1), Is.False);
            Assert.That(Notes.Fits(false, 3));
            Assert.That((Notes.Capacity(0), Notes.Capacity(Notes.Box), Notes.Capacity(Notes.Table)),
                Is.EqualTo((Notes.MaxPerBoard, Notes.MaxInBox, Notes.MaxOnTable)));

            var onTable = new NoteData { id = "q", board = Notes.Table, question = true, tilt = 190f };
            Assert.That(onTable.Sanitize());
            Assert.That(onTable.tilt, Is.EqualTo(-170f).Within(1e-4f), "turned to face whoever laid it down");
            var onBoard = new NoteData { id = "n", board = 2, tilt = 190f };
            Assert.That(onBoard.Sanitize());
            Assert.That(onBoard.tilt, Is.EqualTo(Notes.MaxTilt), "a note on a board only leans");
            Assert.That(new NoteData { id = "x", board = Notes.PlaceCount }.Sanitize(), Is.False);
            Assert.That(Notes.HalfSize(true), Is.EqualTo(new Vector2(0.1f, 0.07f)));
        }

        static float Mono(char c) => 0.5f;
        static readonly float[] Sizes = { 0.04f, 0.03f, 0.02f, 0.01f };

        [Test]
        public void ShortNotesAreWrittenBigAndLongOnesSmall()
        {
            var big = NoteLayout.Fit("Hi there", Mono, 0.17f, 0.14f, Sizes, 1.2f);
            Assert.That(big.Size, Is.EqualTo(Sizes[0]));
            Assert.That(big.Lines, Is.EqualTo(new[] { "Hi there" }));

            var text = string.Join(" ", Enumerable.Repeat("words", 20));
            var small = NoteLayout.Fit(text, Mono, 0.17f, 0.14f, Sizes, 1.2f);
            Assert.That(small.Size, Is.LessThan(Sizes[0]));
            Assert.That(small.Truncated, Is.False);
            Assert.That(small.Lines.Count * 1.2f * small.Size, Is.LessThanOrEqualTo(0.14f + 1e-5f));
            foreach (var line in small.Lines) Assert.That(NoteLayout.Measure(line, Mono) * small.Size, Is.LessThanOrEqualTo(0.17f + 1e-5f), line);
            Assert.That(string.Join(" ", small.Lines), Is.EqualTo(text), "every word, in order");
        }

        [Test]
        public void WordsWrapAtSpacesAndOverlongWordsBreak()
        {
            float One(char c) => 1f;
            Assert.That(NoteLayout.Wrap("aaaa bbbb", One, 5f), Is.EqualTo(new[] { "aaaa", "bbbb" }));
            Assert.That(NoteLayout.Wrap("aa bb", One, 5f), Is.EqualTo(new[] { "aa bb" }));
            Assert.That(NoteLayout.Wrap("abcdefghij", One, 4f), Is.EqualTo(new[] { "abcd", "efgh", "ij" }));
            Assert.That(NoteLayout.Wrap("x abcdefghij", One, 4f), Is.EqualTo(new[] { "x", "abcd", "efgh", "ij" }));
            Assert.That(NoteLayout.Wrap("a\n\nb", One, 4f), Is.EqualTo(new[] { "a", "", "b" }));
        }

        [Test]
        public void TooMuchWritingIsCutShort()
        {
            var text = string.Join(" ", Enumerable.Repeat("blah", 60));
            var fit = NoteLayout.Fit(text, Mono, 0.17f, 0.03f, Sizes, 1.2f);
            Assert.That(fit.Size, Is.EqualTo(Sizes[^1]));
            Assert.That(fit.Truncated);
            Assert.That(fit.Lines.Count, Is.EqualTo(2));
            Assert.That(fit.Lines[^1], Does.EndWith(NoteLayout.Ellipsis));
            Assert.That(NoteLayout.Measure(fit.Lines[^1], Mono) * fit.Size, Is.LessThanOrEqualTo(0.17f + 1e-5f));
        }

        [Test]
        public void OverlappingNotesStackAndSeparateOnesLieFlat()
        {
            var layers = Noticeboard.Layers(new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.05f, 0.05f), new Vector2(0.1f, 0.02f), new Vector2(1.5f, 1f) }, 0.2f);
            Assert.That(layers, Is.EqualTo(new[] { 0, 0, 1, 2, 0 }));
        }

        [Test]
        public void NotesTravelAsJsonAndArriveInTheRoomsMessages()
        {
            var n = Note("n", 3, "ana", "Line one\nline \"two\"", 2);
            n.x = 0.25f;
            n.tilt = -3f;
            n.holder = "ben";
            var json = JsonUtility.ToJson(new NoteList { notes = new[] { n } });
            var back = JsonUtility.FromJson<NoteList>(json).notes[0];
            Assert.That(back.text, Is.EqualTo(n.text));
            Assert.That((back.board, back.x, back.tilt, back.holder, back.v, back.w), Is.EqualTo((2, 0.25f, -3f, "ben", 3, "ana")));

            var e = NetEvent.Parse("{\"type\":\"notes\",\"id\":\"peer-1\",\"notes\":" + json.Substring(json.IndexOf('[')).TrimEnd('}') + "}");
            Assert.That(e.type, Is.EqualTo("notes"));
            Assert.That(e.notes.Length, Is.EqualTo(1));
            Assert.That(e.notes[0].text, Is.EqualTo(n.text));
            var write = NetEvent.Parse("{\"type\":\"write\",\"ok\":true,\"text\":\"Hi\",\"tint\":2}");
            Assert.That((write.ok, write.text, write.tint), Is.EqualTo((true, "Hi", 2)));
        }
    }
}
