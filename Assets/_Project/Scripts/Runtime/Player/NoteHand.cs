using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Facecast
{
    /// <summary>
    /// Your hand, for sticky notes. Look at a noticeboard's notepad and click to take a note: a note to write on opens
    /// (the page's in a browser, the overlay's elsewhere). Done, it's in your hand; look at any board and click to stick
    /// it where you're pointing. Click a note on a board to pick it up and move it; while you hold one, E writes on
    /// it again and X throws it away. Within <see cref="Reach"/> only.
    /// </summary>
    [RequireComponent(typeof(LocalPlayer))]
    public sealed class NoteHand : MonoBehaviour
    {
        public const float Reach = 3.2f;
        /// <summary>Where the note in your hand sits in front of your eyes.</summary>
        static readonly Vector3 HandPosition = new(0.2f, -0.17f, 0.46f);
        static readonly Quaternion HandRotation = Quaternion.Euler(16f, 180f - 18f, -6f);
        const float HandScale = 0.8f;

        public static NoteHand Instance { get; private set; }

        /// <summary>What you're pointing at.</summary>
        public struct Target
        {
            public Noticeboard Board;
            public int Pad;            // a notepad (colour index), or -1
            public bool OnCork;
            public Vector2 Point;      // on the cork (0..1)
            public NoteData Note;      // the note there, if any
        }

        LocalPlayer _player;
        InputAction _use, _write, _discard;
        NoteView _view;
        NoteData _draft;               // a fresh note being written: nobody else knows about it yet
        readonly List<NoteData> _onBoard = new();
        bool _wasLocked;
        string _lost;
        float _lostUntil;

        /// <summary>The note you're carrying, or null.</summary>
        public string HeldId { get; private set; }
        /// <summary>A note to write on is open.</summary>
        public bool Writing { get; private set; }
        public Target Aim { get; private set; }
        /// <summary>Points with the camera even without the mouse captured or with input off (the screenshot tour).</summary>
        public bool AlwaysAim { get; set; }
        /// <summary>What a click would do now, for the overlay; null when there's nothing to say.</summary>
        public string Prompt { get; private set; }
        public NoteView View => _view;

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
                Lose($"{who} took that note first");
            }

            bool locked = Cursor.lockState == CursorLockMode.Locked;
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
            var board = hit.collider.GetComponentInParent<Noticeboard>();
            if (!board) return target;
            target.Board = board;
            target.Pad = board.PadOf(hit.collider);
            if (hit.collider == board.CorkCollider)
            {
                target.OnCork = true;
                target.Point = board.BoardPoint(hit.point);
                target.Note = board.NoteAt(target.Point, Boards.NotesOn(board.Index, _onBoard));
            }
            return target;
        }

        string PromptFor(Target t)
        {
            if (_lost != null && Time.unscaledTime < _lostUntil) return _lost;
            _lost = null;
            if (Writing) return null;
            if (HeldId != null)
            {
                if (t.OnCork) return Boards.Full(t.Board.Index, HeldId) ? "This board is full: take a note down first" : "Click to stick it here";
                return "Click a board to stick your note   ·   E write on it   ·   X throw it away";
            }
            if (t.Pad >= 0) return $"Click to take a {Notes.ColorNames[t.Pad]} note";
            if (t.Note != null) return "Click to pick it up";
            return null;
        }

        void Use()
        {
            var t = Aim;
            if (!t.Board) return;
            if (HeldId != null)
            {
                if (t.OnCork) Stick(t.Board.Index, t.Point);
            }
            else if (t.Pad >= 0) Take(t.Pad);
            else if (t.Note != null) PickUp(t.Note.id);
        }

        // ------------------------------------------------------------------ what you can do

        /// <summary>Takes a fresh note of <paramref name="color"/> from a notepad and opens it to write on.</summary>
        public void Take(int color)
        {
            if (HeldId != null || Writing) return;
            var book = Boards.Book;
            _draft = new NoteData { id = book.NewId(), board = -1, color = Mathf.Clamp(color, 0, Notes.Colors.Length - 1), by = SelfName(), holder = book.Self };
            OpenEditor(_draft.text, _draft.color);
        }

        /// <summary>Writes on the note you're holding again.</summary>
        public void Write()
        {
            var n = HeldId != null ? Boards.Book.Get(HeldId) : null;
            if (n == null || Writing) return;
            OpenEditor(n.text, n.color);
        }

        void OpenEditor(string text, int color)
        {
            Writing = true;
            _player.Typing = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (WebBridge.Available) WebBridge.OpenNoteEditor(text, color);
            else if (Overlay.Instance) Overlay.Instance.OpenNoteEditor(text, color);
            else OnWritten(true, text, color);  // nowhere to write: keep it as it is
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
                draft.color = color;
                HeldId = boards.Commit(draft).id;
                return;
            }
            var n = HeldId != null ? boards.Book.Get(HeldId) : null;
            if (!ok || n == null) return;
            var changed = n.Clone();
            changed.text = text;
            changed.color = color;
            changed.by = SelfName();
            boards.Commit(changed);
        }

        /// <summary>Sticks the note you're holding on board <paramref name="board"/> at <paramref name="point"/>.</summary>
        public bool Stick(int board, Vector2 point)
        {
            var boards = Boards;
            var b = boards.Board(board);
            var n = HeldId != null ? boards.Book.Get(HeldId) : null;
            if (!b || n == null || boards.Full(board, n.id)) return false;
            var p = b.Clamp(point);
            var changed = n.Clone();
            changed.board = board;
            changed.x = p.x;
            changed.y = p.y;
            changed.tilt = Random.Range(-5f, 5f);
            changed.holder = "";
            boards.Commit(changed);
            HeldId = null;
            return true;
        }

        /// <summary>Takes a note off its board into your hand.</summary>
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

        /// <summary>Throws away the note you're holding.</summary>
        public void Discard()
        {
            var boards = Boards;
            var n = HeldId != null ? boards.Book.Get(HeldId) : null;
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

        // ------------------------------------------------------------------ the note in your hand

        void ShowHeld()
        {
            var boards = Boards;
            var n = HeldId != null ? boards.Book.Get(HeldId) : _draft;
            if (n == null || !(_player.InputEnabled || AlwaysAim))
            {
                if (_view) _view.gameObject.SetActive(false);
                return;
            }
            if (!_view)
            {
                _view = boards.MakeView(null);
                _view.name = "Your note";
            }
            _view.gameObject.SetActive(true);
            _view.Show(n);
            var t = Aim;
            if (HeldId != null && t.OnCork && !boards.Full(t.Board.Index, HeldId))
            {
                // Held against the board where it would go.
                _view.transform.SetParent(t.Board.transform, true);
                _view.transform.SetPositionAndRotation(t.Board.WorldPoint(t.Board.Clamp(t.Point), Noticeboard.Lift + 0.012f), t.Board.NoteRotation(0f));
                _view.transform.localScale = Vector3.one;
                _view.SetShadows(true);
            }
            else
            {
                _view.transform.SetParent(_player.Cam.transform, false);
                _view.transform.SetLocalPositionAndRotation(HandPosition, HandRotation);
                _view.transform.localScale = Vector3.one * HandScale;
                _view.SetShadows(false);
            }
        }

        // ------------------------------------------------------------------ test hooks

        /// <summary>
        /// The page's test hooks (<c>facecast.debug.unity("note …")</c>): <c>note take &lt;colour&gt;</c>,
        /// <c>note stick &lt;board&gt; &lt;x&gt; &lt;y&gt;</c>, <c>note pick &lt;id&gt;</c>, <c>note write</c>, <c>note drop</c>,
        /// <c>note goto &lt;board&gt;</c> (stand in front of it).
        /// </summary>
        public void DebugCommand(string[] w)
        {
            float F(int i) => i < w.Length && float.TryParse(w[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
            switch (w.Length > 1 ? w[1] : "")
            {
                case "take": Take((int)F(2)); break;
                case "stick": Stick((int)F(2), new Vector2(F(3), F(4))); break;
                case "pick" when w.Length > 2: PickUp(w[2]); break;
                case "write": Write(); break;
                case "drop": Discard(); break;
                case "goto" when Boards.Board((int)F(2)) is { } b: _player.Teleport(b.ReadingPose()); break;
                default: Debug.LogWarning("[NoteHand] Unknown note command: " + string.Join(" ", w)); break;
            }
        }
    }
}
