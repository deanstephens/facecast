using System;
using System.Collections.Generic;
using UnityEngine;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace Facecast
{
    /// <summary>
    /// The room's four noticeboards and every note on them, kept in step between browsers. Your changes
    /// (<see cref="Commit"/>) go to everyone as a "notes" message; theirs come back through <see cref="Receive"/>,
    /// and the newest version of each note wins (<see cref="NoteBook"/>). Someone arriving gets all the notes from
    /// everyone already here, so the boards last as long as anyone is in the room; your browser also keeps the room's
    /// notes, so a room you come back to has them even if you're first in. A note someone is carrying shows in
    /// their hand, and goes back on its board if they leave with it.
    /// </summary>
    public sealed class Noticeboards : MonoBehaviour
    {
        public static Noticeboards Instance { get; private set; }

        public Noticeboard[] Boards;
        public GameObject NotePrefab;
        /// <summary>The handwriting (a dynamic signed-distance-field font asset).</summary>
        public FontAsset NoteFont;
        public Material InkMaterial;

        public readonly NoteBook Book = new();
        /// <summary>Every note message this game sends: the notes and the peer they're for ("" for everyone).</summary>
        public event Action<NoteData[], string> Sent;

        static readonly int TeamColorId = Shader.PropertyToID("_TeamColor");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        const float SaveDelay = 1.5f;

        Material[] _paper;
        readonly Stack<NoteView> _pool = new();
        readonly Dictionary<string, NoteView> _carried = new();
        readonly List<string> _drop = new();
        readonly List<NoteData> _scratch = new();
        Transform _spare;
        bool _dirty = true;
        float _saveAt = -1f;
        string _saveKey;

        public Material InkMaterialInstance { get; private set; }

        void Awake()
        {
            Instance = this;
            var paper = NotePrefab ? NotePrefab.GetComponent<NoteView>().Paper.sharedMaterial : null;
            _paper = new Material[Notes.Colors.Length];
            for (int i = 0; i < _paper.Length; i++)
            {
                _paper[i] = paper ? new Material(paper) : new Material(Shader.Find("Facecast/Flat"));
                _paper[i].name = $"Note paper ({Notes.ColorNames[i]})";
                _paper[i].SetColor(TeamColorId, Notes.Colors[i]);
            }
            InkMaterialInstance = InkMaterial ? new Material(InkMaterial) { name = "Ink (instance)" } : null;
            SyncInkAtlas();
            for (int i = 0; i < Boards.Length; i++) Boards[i].Index = i;
            _spare = new GameObject("Spare notes").transform;
            _spare.SetParent(transform, false);
            _spare.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_saveAt > 0f) Save();
            foreach (var m in _paper) Destroy(m);
            if (InkMaterialInstance) Destroy(InkMaterialInstance);
        }

        public Material PaperMaterial(int color) => _paper[Mathf.Clamp(color, 0, _paper.Length - 1)];

        /// <summary>
        /// Points the ink at the font's atlas. A dynamic font's atlas is empty in a build until the first characters are
        /// written, and may be set up anew then; called after writing.
        /// </summary>
        public void SyncInkAtlas()
        {
            if (!InkMaterialInstance || !NoteFont || NoteFont.atlasTextures is not { Length: > 0 } atlases) return;
            if (InkMaterialInstance.GetTexture(MainTexId) != atlases[0]) InkMaterialInstance.SetTexture(MainTexId, atlases[0]);
        }

        public Noticeboard Board(int index) => index >= 0 && index < Boards.Length ? Boards[index] : null;

        // ------------------------------------------------------------------ the room

        /// <summary>You joined (the "self" message): your id signs your changes; the room's saved notes come back.</summary>
        public void OnJoined(NetEvent self)
        {
            Book.Self = string.IsNullOrEmpty(self.id) ? "you" : self.id;
            bool offline = App.Instance && App.Instance.Offline;
            _saveKey = offline || string.IsNullOrEmpty(self.room) ? null : "facecast.notes." + self.room;
            Load();
            foreach (var n in SampleNotes.For(offline)) Book.Merge(n);
            _dirty = true;
        }

        /// <summary>Notes from peer <paramref name="from"/> (the "notes" message).</summary>
        public void Receive(NoteData[] notes, string from)
        {
            if (notes == null) return;
            bool changed = false;
            foreach (var n in notes) changed |= Book.Merge(n);
            if (changed) Changed();
        }

        /// <summary>Someone new is here: they get every note we know about.</summary>
        public void PeerArrived(string id)
        {
            _dirty = true;
            if (Book.Count == 0) return;
            var all = new List<NoteData>(Book.All);
            Send(all.ToArray(), id);
        }

        /// <summary>Someone left: anything they were carrying goes back where it was.</summary>
        public void PeerLeft(string id) => _dirty = true;

        /// <summary>Records and shares a change you made (to a clone of the note, or a new one).</summary>
        public NoteData Commit(NoteData changed)
        {
            var n = Book.Commit(changed);
            Send(new[] { n }, "");
            Changed();
            return n;
        }

        void Changed()
        {
            _dirty = true;
            if (_saveKey != null) _saveAt = Time.unscaledTime + SaveDelay;
        }

        public void MarkDirty() => _dirty = true;

        void Send(NoteData[] notes, string target)
        {
            Sent?.Invoke(notes, target);
            WebBridge.SendNotes(JsonUtility.ToJson(new NoteList { notes = notes }), target);
        }

        // ------------------------------------------------------------------ who has what

        /// <summary>Someone in the room (you included) is carrying it.</summary>
        public bool Carried(NoteData n) =>
            !string.IsNullOrEmpty(n.holder) && (n.holder == Book.Self || (Room.Instance && Room.Instance.Get(n.holder)));

        /// <summary>Stuck on its board, for everyone to see.</summary>
        public bool OnBoard(NoteData n) => !n.gone && n.board >= 0 && !Carried(n);

        /// <summary>The notes on <paramref name="board"/>, bottom to top (the most recently stuck on top).</summary>
        public List<NoteData> NotesOn(int board, List<NoteData> into = null)
        {
            into ??= new List<NoteData>();
            into.Clear();
            foreach (var n in Book.All)
                if (n.board == board && OnBoard(n)) into.Add(n);
            into.Sort((a, b) => a.v != b.v ? a.v.CompareTo(b.v) : string.CompareOrdinal(a.w, b.w));
            return into;
        }

        /// <summary>A board's full when it has <see cref="Notes.MaxPerBoard"/> notes (counting ones being carried around).</summary>
        public bool Full(int board, string except = null) => Book.CountOn(board, except) >= Notes.MaxPerBoard;

        // ------------------------------------------------------------------ views

        public NoteView MakeView(Transform parent)
        {
            var view = _pool.Count > 0 ? _pool.Pop() : Instantiate(NotePrefab).GetComponent<NoteView>();
            view.transform.SetParent(parent, false);
            view.transform.localScale = Vector3.one;
            return view;
        }

        public void Recycle(NoteView view)
        {
            if (!view) return;
            view.transform.SetParent(_spare, false);
            _pool.Push(view);
        }

        void LateUpdate()
        {
            if (_dirty) Refresh();
            foreach (var kv in _carried)
            {
                var n = Book.Get(kv.Key);
                var avatar = n != null && Room.Instance ? Room.Instance.Get(n.holder) : null;
                kv.Value.gameObject.SetActive(avatar && avatar.Placed);
            }
            if (_saveAt > 0f && Time.unscaledTime >= _saveAt) Save();
        }

        void Refresh()
        {
            _dirty = false;
            foreach (var board in Boards) board.Show(NotesOn(board.Index, _scratch), this);

            // Notes in other people's hands, held up in front of them.
            _drop.Clear();
            foreach (var id in _carried.Keys) _drop.Add(id);
            var room = Room.Instance;
            foreach (var n in Book.All)
            {
                if (n.gone || string.IsNullOrEmpty(n.holder) || n.holder == Book.Self || !room) continue;
                var avatar = room.Get(n.holder);
                if (!avatar || !avatar.Rig || !avatar.Rig.HandR) continue;
                _drop.Remove(n.id);
                if (!_carried.TryGetValue(n.id, out var view) || view.transform.parent != avatar.Rig.HandR)
                {
                    if (view) Recycle(view);
                    view = MakeView(avatar.Rig.HandR);
                    view.NoteId = n.id;
                    view.name = "Carried note " + n.id;
                    view.SetShadows(true);
                    view.transform.SetLocalPositionAndRotation(new Vector3(-0.02f, 0.11f, 0.07f), Quaternion.Euler(-12f, 0f, 4f));
                    view.transform.localScale = Vector3.one * 0.85f;
                    _carried[n.id] = view;
                }
                view.Show(n);
            }
            foreach (var id in _drop)
            {
                Recycle(_carried[id]);
                _carried.Remove(id);
            }
            if (WebBridge.Available) WebBridge.ReportNotes(Summarize());
        }

        public NoteView CarriedView(string id) => _carried.TryGetValue(id, out var v) ? v : null;

        // ------------------------------------------------------------------ keeping the room's notes

        void Load()
        {
            if (_saveKey == null) return;
            var json = PlayerPrefs.GetString(_saveKey, "");
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var list = JsonUtility.FromJson<NoteList>(json);
                int n = 0;
                if (list?.notes != null)
                    foreach (var note in list.notes)
                        if (note != null && Book.Merge(note)) n++;
                Debug.Log($"[Noticeboards] {n} saved notes for this room");
            }
            catch (ArgumentException e)
            {
                Debug.LogWarning("[Noticeboards] Couldn't read the saved notes: " + e.Message);
            }
        }

        void Save()
        {
            _saveAt = -1f;
            if (_saveKey == null) return;
            var keep = new List<NoteData>();
            foreach (var n in Book.All)
            {
                if (n.board < 0 && !n.gone) continue;  // in someone's hand, never stuck anywhere
                var copy = n.Clone();
                copy.holder = "";
                keep.Add(copy);
            }
            PlayerPrefs.SetString(_saveKey, JsonUtility.ToJson(new NoteList { notes = keep.ToArray() }));
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ for the page's tests

        [Serializable]
        class BoardSummary
        {
            public NoteData[] notes;
        }

        [Serializable]
        class Summary
        {
            public BoardSummary[] boards;
            public string held;
        }

        /// <summary>What's on each board and what you're holding, as JSON (the page keeps it for facecast.debug.notes()).</summary>
        public string Summarize()
        {
            var s = new Summary { boards = new BoardSummary[Boards.Length], held = NoteHand.Instance ? NoteHand.Instance.HeldId ?? "" : "" };
            for (int i = 0; i < Boards.Length; i++) s.boards[i] = new BoardSummary { notes = NotesOn(i).ToArray() };
            return JsonUtility.ToJson(s);
        }
    }
}
