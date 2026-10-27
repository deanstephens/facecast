using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facecast
{
    /// <summary>
    /// What Unity draws over the world (UI Toolkit, styled by UI/Facecast.uss): a name above everyone's head that
    /// lights up while they speak, the crosshair with what a click would do (take a note, stick it…), a controls hint
    /// while the mouse is free, and a banner when offline. The page draws the rest (join card, your camera,
    /// microphone and camera buttons, invite link, and the note you write on); outside a browser there's no page, so
    /// the overlay has its own note to write on.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class Overlay : MonoBehaviour
    {
        const float MaxNameDistance = 32f;

        public static Overlay Instance { get; private set; }

        VisualElement _root, _plates, _crosshair;
        Label _hint, _banner, _prompt;
        VisualElement _editor, _paper;
        TextField _noteText;
        Label _noteCount;
        readonly List<Button> _noteSwatches = new();
        int _noteColor;
        readonly Dictionary<RemoteAvatar, Label> _labels = new();
        readonly List<RemoteAvatar> _gone = new();

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Start, not OnEnable: the UIDocument has its root by then.
        void Start()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            _root.Clear();
            _root.AddToClassList("fc-root");
            _root.pickingMode = PickingMode.Ignore;
            _plates = new VisualElement { pickingMode = PickingMode.Ignore };
            _plates.AddToClassList("fc-plates");
            _root.Add(_plates);
            _crosshair = new VisualElement { pickingMode = PickingMode.Ignore };
            _crosshair.AddToClassList("fc-crosshair");
            _root.Add(_crosshair);
            _prompt = new Label { pickingMode = PickingMode.Ignore };
            _prompt.AddToClassList("fc-prompt");
            _root.Add(_prompt);
            _hint = new Label("Click to look around   ·   WASD walk   ·   Shift run   ·   Space jump   ·   M microphone   ·   V camera")
                { pickingMode = PickingMode.Ignore };
            _hint.AddToClassList("fc-hint");
            _root.Add(_hint);
            _banner = new Label("Offline preview with demo bots. The web build is where people meet.") { pickingMode = PickingMode.Ignore };
            _banner.AddToClassList("fc-banner");
            _root.Add(_banner);
            _labels.Clear();
            _editor = null;
        }

        void LateUpdate()
        {
            if (_root == null) return;
            var app = App.Instance;
            bool joined = app && app.Joined;
            bool locked = UnityEngine.Cursor.lockState == CursorLockMode.Locked;
            bool touring = app && !app.Player.InputEnabled;
            var hand = NoteHand.Instance;
            bool writing = NoteEditorOpen || (hand && hand.Writing);  // the page's editor, in a browser
            string prompt = hand ? hand.Prompt : null;
            Show(_crosshair, joined && locked && !touring);
            Show(_prompt, joined && locked && !touring && !string.IsNullOrEmpty(prompt));
            if (!string.IsNullOrEmpty(prompt)) _prompt.text = prompt;
            Show(_hint, joined && !locked && !touring && !writing);
            Show(_banner, app && app.Offline && joined && !touring);
            UpdateNames();
        }

        static void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        // ------------------------------------------------------------------ the note to write on (no page)

        public bool NoteEditorOpen => _editor != null && _editor.style.display == DisplayStyle.Flex;
        /// <summary>What's written on the open note (tests type here).</summary>
        public string NoteDraft { get => _noteText?.value ?? ""; set { if (_noteText != null) _noteText.value = value; } }
        public int NoteDraftColor => _noteColor;

        public void OpenNoteEditor(string text, int color)
        {
            if (_root == null) return;
            if (_editor == null) BuildNoteEditor();
            _noteText.value = text ?? "";
            SetNoteColor(color);
            Show(_editor, true);
            _noteText.schedule.Execute(() => _noteText.Focus());
        }

        public void CloseNoteEditor()
        {
            if (_editor != null) Show(_editor, false);
        }

        /// <summary>Done (<paramref name="ok"/>) or Cancel: closes the note and tells your hand.</summary>
        public void FinishNote(bool ok)
        {
            if (!NoteEditorOpen) return;
            CloseNoteEditor();
            NoteHand.Instance?.OnWritten(ok, Notes.CleanText(_noteText.value), _noteColor);
        }

        void BuildNoteEditor()
        {
            _editor = new VisualElement();
            _editor.AddToClassList("fc-editor");
            var card = new VisualElement();
            card.AddToClassList("fc-editor-card");
            _editor.Add(card);

            _paper = new VisualElement();
            _paper.AddToClassList("fc-note");
            card.Add(_paper);
            _noteText = new TextField { multiline = true, maxLength = Notes.MaxText };
            _noteText.AddToClassList("fc-note-text");
            if (Noticeboards.Instance && Noticeboards.Instance.NoteFont)
                _noteText.style.unityFontDefinition = FontDefinition.FromSDFFont(Noticeboards.Instance.NoteFont);
            _noteText.RegisterValueChangedCallback(e => _noteCount.text = $"{e.newValue.Length} / {Notes.MaxText}");
            _noteText.RegisterCallback<KeyDownEvent>(OnNoteKey, TrickleDown.TrickleDown);
            _paper.Add(_noteText);
            _noteCount = new Label($"0 / {Notes.MaxText}");
            _noteCount.AddToClassList("fc-note-count");
            _paper.Add(_noteCount);

            var swatches = new VisualElement();
            swatches.AddToClassList("fc-note-swatches");
            _noteSwatches.Clear();
            for (int i = 0; i < Notes.Colors.Length; i++)
            {
                int color = i;
                var b = new Button(() => SetNoteColor(color)) { tooltip = Notes.ColorNames[i] };
                b.AddToClassList("fc-note-swatch");
                b.style.backgroundColor = Notes.Colors[i];
                swatches.Add(b);
                _noteSwatches.Add(b);
            }
            card.Add(swatches);

            var actions = new VisualElement();
            actions.AddToClassList("fc-note-actions");
            var cancel = new Button(() => FinishNote(false)) { text = "Cancel" };
            cancel.AddToClassList("fc-button");
            var done = new Button(() => FinishNote(true)) { text = "Done" };
            done.AddToClassList("fc-button");
            done.AddToClassList("fc-button--primary");
            actions.Add(cancel);
            actions.Add(done);
            card.Add(actions);
            var tip = new Label("Ctrl+Enter: done   ·   Esc: cancel   ·   then click a board to stick it");
            tip.AddToClassList("fc-note-tip");
            card.Add(tip);
            _root.Add(_editor);
        }

        void SetNoteColor(int color)
        {
            _noteColor = Mathf.Clamp(color, 0, Notes.Colors.Length - 1);
            _paper.style.backgroundColor = Notes.Colors[_noteColor];
            for (int i = 0; i < _noteSwatches.Count; i++) _noteSwatches[i].EnableInClassList("fc-note-swatch--on", i == _noteColor);
        }

        void OnNoteKey(KeyDownEvent e)
        {
            bool enter = e.keyCode is KeyCode.Return or KeyCode.KeypadEnter;
            if (e.keyCode == KeyCode.Escape) FinishNote(false);
            else if (enter && (e.ctrlKey || e.commandKey)) FinishNote(true);
            else return;
            e.StopImmediatePropagation();
        }

        void UpdateNames()
        {
            var room = Room.Instance;
            var cam = Camera.main;
            var panel = _root.panel;
            if (!room || !cam || panel == null) return;

            foreach (var avatar in room.Avatars)
            {
                if (!_labels.TryGetValue(avatar, out var label))
                {
                    label = new Label { pickingMode = PickingMode.Ignore };
                    label.AddToClassList("fc-plate");
                    _plates.Add(label);
                    _labels[avatar] = label;
                }
                var anchor = avatar.Rig && avatar.Rig.Nameplate ? avatar.Rig.Nameplate.position : avatar.transform.position + Vector3.up * 2.1f;
                var toAnchor = anchor - cam.transform.position;
                float distance = toAnchor.magnitude;
                bool visible = avatar.Placed && distance < MaxNameDistance && Vector3.Dot(toAnchor, cam.transform.forward) > 0.2f;
                Show(label, visible);
                if (!visible) continue;
                label.text = avatar.DisplayName;
                var p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, anchor, cam);
                label.style.left = p.x;
                label.style.top = p.y;
                float scale = Mathf.Clamp(7f / Mathf.Max(distance, 0.1f), 0.55f, 1.15f);
                label.style.scale = new Scale(new Vector3(scale, scale, 1f));
                label.style.opacity = 1f - Mathf.InverseLerp(MaxNameDistance * 0.7f, MaxNameDistance, distance);
                label.EnableInClassList("fc-plate--speaking", avatar.Speaking > 0.15f);
                var c = avatar.Rig ? avatar.Rig.Color : Color.white;
                label.style.borderBottomColor = new Color(c.r, c.g, c.b, 1f);
            }

            _gone.Clear();
            foreach (var kv in _labels)
                if (!kv.Key) _gone.Add(kv.Key);
            foreach (var g in _gone)
            {
                _labels[g].RemoveFromHierarchy();
                _labels.Remove(g);
            }
        }
    }
}
