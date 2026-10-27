using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Facecast
{
    /// <summary>
    /// Your hand, for sticky notes and question cards. Look at a noticeboard's notepad and click to take a note: a
    /// note to write on opens (the page's in a browser, the overlay's elsewhere). Done, it's in your hand; look at any
    /// board and click to stick it where you're pointing. Click a note on a board to pick it up and move it.
    /// At the question corner, click the tray on the box's side to write a question, and the box to post it; with
    /// empty hands, click the box to pull out the question that's waited longest, and the table to lay it down once
    /// it's read. While you hold anything, E writes on it again and X throws it away. Within <see cref="Reach"/> only.
    /// </summary>
    [RequireComponent(typeof(LocalPlayer))]
    public sealed class NoteHand : MonoBehaviour
    {
        public const float Reach = 3.2f;
        /// <summary>Where a sticky note sits in front of your eyes; a question card is held up nearer the middle, to read.</summary>
        static readonly Vector3 NoteHandPosition = new(0.2f, -0.17f, 0.46f), CardHandPosition = new(0.11f, -0.12f, 0.4f);
        static readonly Quaternion NoteHandRotation = Quaternion.Euler(16f, 180f - 18f, -6f), CardHandRotation = Quaternion.Euler(12f, 180f - 8f, -3f);
        const float HandScale = 0.8f;

        public static NoteHand Instance { get; private set; }

        /// <summary>What you're pointing at.</summary>
        public struct Target
        {
            /// <summary>A board's cork or the table's top, under the crosshair.</summary>
            public NoteSurface Surface;
            /// <summary>The board you're pointing at (its cork or a notepad).</summary>
            public Noticeboard Board;
            /// <summary>A notepad (colour index), or -1.</summary>
            public int Pad;
            /// <summary>The question box (its body or its tray of cards).</summary>
            public QuestionBox Box;
            public bool OnCards;
            /// <summary>Where on the surface (0..1).</summary>
            public Vector2 Point;
            /// <summary>The note or card there, if any.</summary>
            public NoteData Note;

            public bool OnCork => Surface && Surface is Noticeboard;
            public bool OnTable => Surface && Surface is QuestionTable;
            public bool OnBox => Box && !OnCards;
        }

        LocalPlayer _player;
        InputAction _use, _write, _discard;
        NoteView _noteView, _cardView;
        NoteData _draft;               // a fresh note or question being written: nobody else knows about it yet
        readonly List<NoteData> _onSurface = new();
        bool _wasLocked;
        string _lost;
        float _lostUntil;

        /// <summary>The note or card you're carrying, or null.</summary>
        public string HeldId { get; private set; }
        /// <summary>A note to write on is open.</summary>
        public bool Writing { get; private set; }
        public Target Aim { get; private set; }
        /// <summary>Points with the camera even without the mouse captured or with input off (the screenshot tour).</summary>
        public bool AlwaysAim { get; set; }
        /// <summary>What a click would do now, for the overlay; null when there's nothing to say.</summary>
        public string Prompt { get; private set; }
        /// <summary>The view of what's in your hand.</summary>
        public NoteView View => Held is { question: true } ? _cardView : _noteView;

        NoteData Held => HeldId != null ? Boards.Book.Get(HeldId) : null;

        void Awake()
        {
            Instance = this;
            _player = GetComponent<LocalPlayer>();
            _use = new InputAction("Use", InputActionType.Button, "<Mouse>/leftButton");
            _write = new InputAction("Write", InputActionType.Button, "<Keyboard>/e");
            _discard = new InputAction("Throw away", InputActionType.Button, "<Keyboard>/x");
            _discard.AddBinding("<Keyboard>/delete");
            _discard.AddBinding("<Keyboard>/backspace");
        }

        void OnEnable()
        {
            _use.Enable();
            _write.Enable();
            _discard.Enable();
        }

        void OnDisable()
        {
            _use.Disable();
            _write.Disable();
            _discard.Disable();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _use.Dispose();
            _write.Dispose();
            _discard.Dispose();
        }

        static Noticeboards Boards => Noticeboards.Instance;

        void Update()
        {
            var boards = Boards;
            if (!_player.Spawned || !boards) return;

            // Someone else picked it up at the same moment, and their grab won.
            if (HeldId != null && boards.Book.Get(HeldId) is var held && (held == null || held.gone || held.holder != boards.Book.Self))
            {
                string who = held != null && Room.Instance && Room.Instance.Get(held.holder) is { } other ? other.DisplayName : "Someone";
                LetGo();
                Lose($"{who} took that {(held is { question: true } ? "question" : "note")} first");
            }

            bool locked = MouseLock.Captured;
            bool active = !Writing && (AlwaysAim || (locked && _wasLocked && _player.InputEnabled));
            _wasLocked = locked;
            Aim = active ? Point(_player.Cam) : default;
            if (active && !AlwaysAim)
            {
                if (_use.WasPressedThisFrame()) Use();
                else if (_write.WasPressedThisFrame() && HeldId != null) Write();
                else if (_discard.WasPressedThisFrame() && HeldId != null) Discard();
            }
            Prompt = PromptFor(Aim);
            ShowHeld();
        }

        void Lose(string why)
        {
            _lost = why;
            _lostUntil = Time.unscaledTime + 3f;
        }

        // ------------------------------------------------------------------ pointing

        /// <summary>What the centre of <paramref name="cam"/>'s view falls on, within reach.</summary>
        public Target Point(Camera cam)
        {
            var target = new Target { Pad = -1 };
            if (!cam) return target;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (!Physics.Raycast(ray, out var hit, Reach, ~0, QueryTriggerInteraction.Ignore)) return target;
            var c = hit.collider;
            if (c.GetComponentInParent<Noticeboard>() is { } board)
            {
                target.Board = board;
                target.Pad = board.PadOf(c);
                if (c == board.CorkCollider) target.Surface = board;
            }
            else if (c.GetComponentInParent<QuestionTable>() is { } table && c == table.TopCollider) target.Surface = table;
            else if (c.GetComponentInParent<QuestionBox>() is { } box)
            {
                target.Box = box;
                target.OnCards = c == box.Cards;
            }
            if (target.Surface)
            {
                target.Point = target.Surface.SurfacePoint(hit.point);
                target.Note = target.Surface.NoteAt(target.Point, Boards.NotesOn(target.Surface.Index, _onSurface));
            }
            return target;
        }

        string PromptFor(Target t)
        {
            if (_lost != null && Time.unscaledTime < _lostUntil) return _lost;
            _lost = null;
            if (Writing) return null;
            var held = Held;
            var boards = Boards;
            if (held is { question: true })
            {
                if (t.OnBox) return boards.Full(Notes.Box, held.id) ? "The box is full" : "Click to post it in the box";
                if (t.OnTable) return boards.Full(Notes.Table, held.id) ? "The table is full: clear some cards away first" : "Click to lay it here";
                if (t.OnCork) return "Question cards go in the box, or on the table once they're read";
                return "Click the box to post it, or the table to lay it down   ·   E write on it   ·   X throw it away";
            }
            if (held != null)
            {
                if (t.OnCork) return boards.Full(t.Surface.Index, held.id) ? "This board is full: take a note down first" : "Click to stick it here";
                if (t.OnTable || t.OnBox) return "Sticky notes go on the noticeboards";
                return "Click a board to stick your note   ·   E write on it   ·   X throw it away";
            }
            if (t.Pad >= 0) return $"Click to take a {Notes.ColorNames[t.Pad]} note";
            if (t.OnCards) return "Click to write a question";
            if (t.OnBox)
            {
                int n = boards.NotesOn(Notes.Box, _onSurface).Count;
                return n == 0 ? "The question box is empty" : $"Click to pull out a question ({QuestionBox.CountText(n)})";
            }
            if (t.Note != null) return "Click to pick it up";
            return null;
        }

        void Use()
        {
            var t = Aim;
            if (HeldId != null)
            {
                if (t.Surface) Put(t.Surface.Index, t.Point);
                else if (t.OnBox) Post();
            }
            else if (t.Pad >= 0) Take(t.Pad);
            else if (t.OnCards) Ask();
            else if (t.OnBox) Pull();
            else if (t.Note != null) PickUp(t.Note.id);
        }

        // ------------------------------------------------------------------ what you can do

        /// <summary>Takes a fresh note of <paramref name="color"/> from a notepad and opens it to write on.</summary>
        public void Take(int color)
        {
            if (HeldId != null || Writing) return;
            var book = Boards.Book;
            _draft = new NoteData { id = book.NewId(), board = -1, color = Mathf.Clamp(color, 0, Notes.Colors.Length - 1), by = SelfName(), holder = book.Self };
            OpenEditor(_draft);
        }

        /// <summary>Takes a blank question card from the box's tray and opens it to write on. Questions are anonymous.</summary>
        public void Ask()
        {
            if (HeldId != null || Writing) return;
            var book = Boards.Book;
            _draft = new NoteData { id = book.NewId(), board = -1, question = true, holder = book.Self };
            OpenEditor(_draft);
        }

        /// <summary>Writes on what you're holding again.</summary>
        public void Write()
        {
            var n = Held;
            if (n == null || Writing) return;
            OpenEditor(n);
        }

        void OpenEditor(NoteData n)
        {
            Writing = true;
            _player.Typing = true;
            MouseLock.Release();
            if (WebBridge.Available) WebBridge.OpenNoteEditor(n.text, n.color, n.question);
            else if (Overlay.Instance) Overlay.Instance.OpenNoteEditor(n.text, n.color, n.question);
            else OnWritten(true, n.text, n.color);  // nowhere to write: keep it as it is
        }

        /// <summary>The note to write on closed: <paramref name="ok"/> with what's written on it, or cancelled.</summary>
        public void OnWritten(bool ok, string text, int color)
        {
            if (!Writing) return;
            Writing = false;
            _player.Typing = false;
            if (!WebBridge.Available && Overlay.Instance) Overlay.Instance.CloseNoteEditor();
            var boards = Boards;
            if (_draft != null)
            {
                var draft = _draft;
                _draft = null;
                if (!ok) return;  // put back on the pad
                draft.text = text;
                if (!draft.question) draft.color = color;
                HeldId = boards.Commit(draft).id;
                return;
            }
            var n = Held;
            if (!ok || n == null) return;
            var changed = n.Clone();
            changed.text = text;
            if (!n.question)
            {
                changed.color = color;
                changed.by = SelfName();
            }
            boards.Commit(changed);
        }

        /// <summary>Sticks the note you're holding on board <paramref name="board"/> at <paramref name="point"/>.</summary>
        public bool Stick(int board, Vector2 point) => Put(board, point);

        /// <summary>
        /// Puts what you're holding at <paramref name="point"/> on place <paramref name="place"/>: a sticky note on a
        /// board, leaning a little; a question card on the table, turned to read the right way up from where you stand.
        /// </summary>
        public bool Put(int place, Vector2 point)
        {
            var boards = Boards;
            var surface = boards.Surface(place);
            var n = Held;
            if (!surface || n == null || !Notes.Fits(n.question, place) || boards.Full(place, n.id)) return false;
            var p = surface.Clamp(point, n.question);
            var changed = n.Clone();
            changed.board = place;
            changed.x = p.x;
            changed.y = p.y;
            changed.tilt = place == Notes.Table
                ? surface.TiltFacing(_player.Cam ? _player.Cam.transform.forward : transform.forward) + Random.Range(-6f, 6f)
                : Random.Range(-5f, 5f);
            changed.holder = "";
            boards.Commit(changed);
            HeldId = null;
            return true;
        }

        /// <summary>Posts the question card you're holding into the box.</summary>
        public bool Post()
        {
            var boards = Boards;
            var n = Held;
            if (n == null || !n.question || boards.Full(Notes.Box, n.id)) return false;
            var changed = n.Clone();
            changed.board = Notes.Box;
            changed.holder = "";
            boards.Commit(changed);
            HeldId = null;
            return true;
        }

        /// <summary>Pulls the question that's been in the box longest out into your hand.</summary>
        public bool Pull()
        {
            if (HeldId != null || Writing) return false;
            var next = Boards.NextQuestion();
            return next != null && PickUp(next.id);
        }

        /// <summary>Takes a note off its board, or a card off the table or out of the box, into your hand.</summary>
        public bool PickUp(string id)
        {
            var boards = Boards;
            var n = boards.Book.Get(id);
            if (HeldId != null || Writing || n == null || !boards.OnBoard(n)) return false;
            var changed = n.Clone();
            changed.holder = boards.Book.Self;
            HeldId = boards.Commit(changed).id;
            return true;
        }

        /// <summary>Throws away what you're holding.</summary>
        public void Discard()
        {
            var boards = Boards;
            var n = Held;
            if (n == null || Writing) return;
            var changed = n.Clone();
            changed.gone = true;
            changed.holder = "";
            boards.Commit(changed);
            HeldId = null;
        }

        void LetGo()
        {
            HeldId = null;
            if (!Writing) return;
            Writing = false;
            _player.Typing = false;
            if (WebBridge.Available) WebBridge.CloseNoteEditor();
            else if (Overlay.Instance) Overlay.Instance.CloseNoteEditor();
        }

        static string SelfName() => App.Instance ? App.Instance.SelfName : "";

        // ------------------------------------------------------------------ what's in your hand

        void ShowHeld()
        {
            var boards = Boards;
            var n = Held ?? _draft;
            bool show = n != null && (_player.InputEnabled || AlwaysAim);
            if (_noteView) _noteView.gameObject.SetActive(show && !n.question);
            if (_cardView) _cardView.gameObject.SetActive(show && n.question);
            if (!show) return;

            ref var view = ref n.question ? ref _cardView : ref _noteView;
            if (!view)
            {
                view = boards.MakeView(null, n.question);
                view.name = n.question ? "Your question" : "Your note";
            }
            view.gameObject.SetActive(true);
            view.Show(n);
            var t = Aim;
            if (HeldId != null && t.Surface && Notes.Fits(n.question, t.Surface.Index) && !boards.Full(t.Surface.Index, HeldId))
            {
                // Held against the board, or over the table, where it would go.
                var s = t.Surface;
                float tilt = s.Index == Notes.Table ? s.TiltFacing(_player.Cam.transform.forward) : 0f;
                view.transform.SetParent(s.transform, true);
                view.transform.SetPositionAndRotation(s.WorldPoint(s.Clamp(t.Point, n.question), NoteSurface.Lift + 0.012f), s.NoteRotation(tilt));
                view.transform.localScale = Vector3.one;
                view.SetShadows(true);
            }
            else
            {
                view.transform.SetParent(_player.Cam.transform, false);
                if (n.question) view.transform.SetLocalPositionAndRotation(CardHandPosition, CardHandRotation);
                else view.transform.SetLocalPositionAndRotation(NoteHandPosition, NoteHandRotation);
                view.transform.localScale = Vector3.one * HandScale;
                view.SetShadows(false);
            }
        }

        // ------------------------------------------------------------------ test hooks

        /// <summary>
        /// The page's test hooks (<c>facecast.debug.unity("note …")</c>): <c>note take &lt;colour&gt;</c>,
        /// <c>note stick &lt;board&gt; &lt;x&gt; &lt;y&gt;</c>, <c>note pick &lt;id&gt;</c>, <c>note write</c>, <c>note drop</c>,
        /// <c>note ask</c>, <c>note post</c>, <c>note pull</c>, <c>note lay &lt;x&gt; &lt;y&gt;</c> (on the table),
        /// <c>note goto &lt;board&gt;|box|table</c> (stand in front of it).
        /// </summary>
        public void DebugCommand(string[] w)
        {
            float F(int i) => i < w.Length && float.TryParse(w[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
            string where = w.Length > 2 ? w[2] : "";
            var boards = Boards;
            switch (w.Length > 1 ? w[1] : "")
            {
                case "take": Take((int)F(2)); break;
                case "stick": Stick((int)F(2), new Vector2(F(3), F(4))); break;
                case "pick" when w.Length > 2: PickUp(w[2]); break;
                case "write": Write(); break;
                case "drop": Discard(); break;
                case "ask": Ask(); break;
                case "post": Post(); break;
                case "pull": Pull(); break;
                case "lay": Put(Notes.Table, new Vector2(F(2), F(3))); break;
                case "goto" when where == "box" && boards.QuestionBox: _player.Teleport(boards.QuestionBox.ReadingPose()); break;
                case "goto" when where == "table" && boards.QuestionTable: _player.Teleport(boards.QuestionTable.ReadingPose()); break;
                case "goto" when boards.Board((int)F(2)) is { } b: _player.Teleport(b.ReadingPose()); break;
                default: Debug.LogWarning("[NoteHand] Unknown note command: " + string.Join(" ", w)); break;
            }
        }
    }
}
