using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Facecast
{
    /// <summary>
    /// Sticky note and question card constants: where they can be (<see cref="NoteData.board"/>), the colours (the
    /// notepads on every board, in order), sizes and limits.
    /// </summary>
    public static class Notes
    {
        /// <summary>Places: the four noticeboards are 0..3, then the question box and the table beside it.</summary>
        public const int BoardCount = 4, Box = 4, Table = 5, PlaceCount = 6;
        public const int MaxText = 140;
        public const int MaxLines = 8;
        public const int MaxPerBoard = 40, MaxInBox = 60, MaxOnTable = 30;
        /// <summary>A sticky note's side in metres (Blender/MODELS.md, StickyNote).</summary>
        public const float Size = 0.2f;
        /// <summary>A question card, 0.2 × 0.14 m (Blender/MODELS.md, QuestionCard).</summary>
        public static readonly Vector2 CardSize = new(0.2f, 0.14f);
        public const float MaxTilt = 8f;

        /// <summary>Half a note's (or card's) width and height, in metres.</summary>
        public static Vector2 HalfSize(bool question) => question ? CardSize * 0.5f : new Vector2(Size, Size) * 0.5f;

        public static int Capacity(int place) => place == Box ? MaxInBox : place == Table ? MaxOnTable : MaxPerBoard;

        /// <summary>Sticky notes go on the boards; question cards in the box or on the table.</summary>
        public static bool Fits(bool question, int place) =>
            question ? place == Box || place == Table : place >= 0 && place < BoardCount;

        /// <summary>Yellow, pink, blue and green: the notepads from left to right (Blender's NoteYellow, NotePink, …).</summary>
        public static readonly Color[] Colors = { Palette.Hex("#FFE27A"), Palette.Hex("#FFA9C6"), Palette.Hex("#9DD7FF"), Palette.Hex("#B6EA98") };
        public static readonly string[] ColorNames = { "yellow", "pink", "blue", "green" };

        /// <summary>
        /// What a note may say: no control characters, at most <see cref="MaxLines"/> lines and
        /// <see cref="MaxText"/> characters, no blank lines at either end.
        /// </summary>
        public static string CleanText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(Math.Min(text.Length, MaxText));
            int lines = 1;
            foreach (char c in text.Replace("\r\n", "\n"))
            {
                if (c == '\n' || c == '\r')
                {
                    if (lines < MaxLines)
                    {
                        sb.Append('\n');
                        lines++;
                    }
                    else sb.Append(' ');
                }
                else if (c == '\t') sb.Append(' ');
                else if (!char.IsControl(c)) sb.Append(c);
            }
            return Clip(sb.ToString().Trim(), MaxText);
        }

        /// <summary>At most <paramref name="max"/> characters, never splitting a surrogate pair.</summary>
        public static string Clip(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            int n = char.IsHighSurrogate(s[max - 1]) ? max - 1 : max;
            return s.Substring(0, n);
        }
    }

    /// <summary>
    /// One sticky note or question card, as it travels between browsers and is saved: where it is, what it says,
    /// who's carrying it. Every change gets a new version (<see cref="v"/>, a Lamport clock, with the changer's id <see cref="w"/>
    /// breaking ties) and the newest version of each note wins everywhere, so everyone's boards agree without a
    /// server, however the messages cross.
    /// </summary>
    [Serializable]
    public class NoteData
    {
        public string id;
        /// <summary>
        /// Where it is: a board (0..3), the question box (<see cref="Notes.Box"/>) or the table (<see cref="Notes.Table"/>);
        /// or -1 while it's in someone's hand and has never been put anywhere.
        /// </summary>
        public int board = -1;
        /// <summary>A question card rather than a sticky note.</summary>
        public bool question;
        /// <summary>Its centre on its board or the table, 0..1 across and up (see <see cref="NoteSurface"/>).</summary>
        public float x = 0.5f, y = 0.5f;
        /// <summary>
        /// Degrees it's turned about the surface's normal: a slight lean on a board; on the table, which way it faces
        /// (towards whoever put it down).
        /// </summary>
        public float tilt;
        public int color;
        public string text = "";
        /// <summary>Who wrote it (their name).</summary>
        public string by = "";
        /// <summary>Who's carrying it (their peer id), or "" when it's on its board.</summary>
        public string holder = "";
        /// <summary>Thrown away. Kept, so an older copy of the note can't bring it back.</summary>
        public bool gone;
        /// <summary>Version: the Lamport clock of the last change.</summary>
        public int v;
        /// <summary>Whose change that was (a peer id).</summary>
        public string w = "";

        public NoteData Clone() => (NoteData)MemberwiseClone();

        public bool IsNewerThan(NoteData other)
        {
            if (other == null) return true;
            if (v != other.v) return v > other.v;
            return string.CompareOrdinal(w ?? "", other.w ?? "") > 0;
        }

        public bool StuckOn(int b) => !gone && board == b;

        /// <summary>Makes a note that came from elsewhere safe to use. False if it's beyond repair.</summary>
        public bool Sanitize()
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
            if (board < -1 || board >= Notes.PlaceCount) return false;
            x = float.IsFinite(x) ? Mathf.Clamp01(x) : 0.5f;
            y = float.IsFinite(y) ? Mathf.Clamp01(y) : 0.5f;
            if (!float.IsFinite(tilt)) tilt = 0f;
            tilt = board == Notes.Table ? Mathf.DeltaAngle(0f, tilt) : Mathf.Clamp(tilt, -Notes.MaxTilt, Notes.MaxTilt);
            color = Mathf.Clamp(color, 0, Notes.Colors.Length - 1);
            text = Notes.CleanText(text);
            by = Notes.Clip(by?.Trim(), 24);
            holder = holder != null && holder.Length <= 64 ? holder : "";
            w = Notes.Clip(w, 64);
            if (v < 0) v = 0;
            return true;
        }
    }

    /// <summary>The wire and save format: a list of notes (JsonUtility can't write a bare array).</summary>
    [Serializable]
    public class NoteList
    {
        public NoteData[] notes;
    }

    /// <summary>
    /// Every note and question this browser knows about, on the boards, in the box and on the table: the shared state
    /// of the room. Changes made here are
    /// <see cref="Commit"/>ted with a new version and sent to everyone; notes from others are <see cref="Merge"/>d
    /// in when they're newer than ours.
    /// </summary>
    public sealed class NoteBook
    {
        /// <summary>Thrown-away notes are remembered up to this many notes in all; the oldest are forgotten first.</summary>
        public const int MaxKept = 600;

        readonly Dictionary<string, NoteData> _notes = new();
        int _made;

        /// <summary>Your peer id: the writer of your changes, and the holder of what you carry.</summary>
        public string Self { get; set; } = "you";
        public int Clock { get; private set; }
        public int Count => _notes.Count;
        public IEnumerable<NoteData> All => _notes.Values;

        public NoteData Get(string id) => id != null && _notes.TryGetValue(id, out var n) ? n : null;

        /// <summary>A fresh note id, unique to you.</summary>
        public string NewId() => $"{Self}-{++_made}";

        /// <summary>Takes in a note from elsewhere (a peer, or saved) if it's newer than ours. True if anything changed.</summary>
        public bool Merge(NoteData note)
        {
            if (note == null || !note.Sanitize()) return false;
            Clock = Math.Max(Clock, note.v);
            if (_notes.TryGetValue(note.id, out var mine) && !note.IsNewerThan(mine)) return false;
            _notes[note.id] = note;
            Prune();
            return true;
        }

        /// <summary>Records a change you made (to a <see cref="NoteData.Clone"/> or a new note): a new version, in your name.</summary>
        public NoteData Commit(NoteData note)
        {
            note.Sanitize();
            note.v = ++Clock;
            note.w = Self;
            _notes[note.id] = note;
            Prune();
            return note;
        }

        /// <summary>Live notes in place <paramref name="board"/> (a board, the box or the table), counting ones being carried around.</summary>
        public int CountOn(int board, string except = null)
        {
            int n = 0;
            foreach (var note in _notes.Values)
                if (note.StuckOn(board) && note.id != except) n++;
            return n;
        }

        public void Clear()
        {
            _notes.Clear();
            Clock = 0;
        }

        void Prune()
        {
            if (_notes.Count <= MaxKept) return;
            var gone = new List<NoteData>();
            foreach (var n in _notes.Values)
                if (n.gone) gone.Add(n);
            gone.Sort((a, b) => a.v.CompareTo(b.v));
            for (int i = 0; i < gone.Count && _notes.Count > MaxKept; i++) _notes.Remove(gone[i].id);
        }
    }
}
