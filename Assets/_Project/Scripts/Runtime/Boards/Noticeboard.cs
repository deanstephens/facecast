using System;
using System.Collections.Generic;
using UnityEngine;

namespace Facecast
{
    /// <summary>
    /// One of the square's four noticeboards (the NoticeBoard model; Blender/MODELS.md): a cork you stick notes on,
    /// a title plate, and a shelf of four notepads you take fresh notes from. It knows where things are on its cork
    /// (positions 0..1 from the reader's bottom left) and lays out the notes stuck on it, later ones on top.
    /// </summary>
    public sealed class Noticeboard : MonoBehaviour
    {
        /// <summary>The notepads, in <see cref="Notes.Colors"/> order.</summary>
        public static readonly string[] PadNames = { "PadYellow", "PadPink", "PadBlue", "PadGreen" };
        /// <summary>How far a note stands off the cork, and how much higher each note it overlaps lifts it (clearing a curl).</summary>
        public const float Lift = 0.0015f, LayerLift = 0.0045f;
        const int MaxLayers = 8;

        public int Index;
        public string Title = "Notices";
        /// <summary>The roof and title frame's colour.</summary>
        public Color Accent = Color.white;
        public Transform Cork, Header;
        public Collider CorkCollider;
        public Collider[] Pads;
        public Renderer FrameRenderer;
        /// <summary>The cork's width and height in metres.</summary>
        public Vector2 Size = new(2.1f, 1.25f);

        static readonly int TeamColorId = Shader.PropertyToID("_TeamColor");
        readonly Dictionary<string, NoteView> _views = new();
        readonly List<string> _gone = new();
        Material _frame;
        Mesh _title;

        /// <summary>Finds the model's parts by name (the prefab builder calls this so the references are saved).</summary>
        public void Bind()
        {
            Cork = Find("Cork");
            Header = Find("Header");
            CorkCollider = Cork ? Cork.GetComponent<Collider>() : null;
            Pads = Array.ConvertAll(PadNames, n => Find(n) is { } t ? t.GetComponent<Collider>() : null);
            var frame = Find("Frame");
            FrameRenderer = frame ? frame.GetComponent<Renderer>() : null;
            if (Cork && Cork.GetComponent<MeshFilter>() is { sharedMesh: { } mesh })
                Size = new Vector2(mesh.bounds.size.x, mesh.bounds.size.y);
        }

        Transform Find(string part)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == part) return t;
            return null;
        }

        void Start()
        {
            if (FrameRenderer)
            {
                _frame = new Material(FrameRenderer.sharedMaterial) { name = "Board frame (instance)" };
                _frame.SetColor(TeamColorId, Accent);
                FrameRenderer.sharedMaterial = _frame;
            }
            WriteTitle();
        }

        void OnDestroy()
        {
            if (_frame) Destroy(_frame);
            if (_title) Destroy(_title);
        }

        void WriteTitle()
        {
            var boards = Noticeboards.Instance;
            if (!Header || !boards) return;
            var go = new GameObject("Title", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(Header, false);
            // The ink plane faces the reader: its +X is their right, its +Z points into the plate.
            go.transform.SetLocalPositionAndRotation(new Vector3(0f, 0f, 0.0006f), Quaternion.Euler(0f, 180f, 0f));
            _title = new Mesh { name = "Board title" };
            NoteInk.BuildLabel(_title, boards.NoteFont, Title, 0.13f, 1.1f, NoteInk.Ink);
            boards.SyncInkAtlas();
            go.GetComponent<MeshFilter>().sharedMesh = _title;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = boards.InkMaterialInstance;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ places on the cork

        /// <summary>The world position of board point <paramref name="p"/>, <paramref name="lift"/> metres off the cork.</summary>
        public Vector3 WorldPoint(Vector2 p, float lift = 0f) =>
            Cork.TransformPoint(new Vector3(-(p.x - 0.5f) * Size.x, (p.y - 0.5f) * Size.y, lift));

        /// <summary>The board point (0..1 across from the reader's left, and up) at a world position on the cork.</summary>
        public Vector2 BoardPoint(Vector3 world)
        {
            var l = Cork.InverseTransformPoint(world);
            return new Vector2(0.5f - l.x / Size.x, 0.5f + l.y / Size.y);
        }

        /// <summary>A note's rotation on this board, leaning <paramref name="tilt"/> degrees.</summary>
        public Quaternion NoteRotation(float tilt) => Cork.rotation * Quaternion.Euler(0f, 0f, tilt);

        /// <summary>The nearest point where a whole note fits on the cork.</summary>
        public Vector2 Clamp(Vector2 p)
        {
            float hx = Notes.Size * 0.52f / Size.x, hy = Notes.Size * 0.52f / Size.y;
            return new Vector2(Mathf.Clamp(p.x, hx, 1f - hx), Mathf.Clamp(p.y, hy, 1f - hy));
        }

        public int PadOf(Collider c) => c ? Array.IndexOf(Pads, c) : -1;

        /// <summary>Where to stand to read the board: <paramref name="distance"/> in front of it, facing it.</summary>
        public AvatarPose ReadingPose(float distance = 1.5f)
        {
            var front = Cork.forward;
            var at = Cork.position + front * distance;
            at.y = transform.position.y;
            float yaw = Mathf.Atan2(-front.x, -front.z) * Mathf.Rad2Deg;
            return new AvatarPose(at, Mathf.Repeat(yaw, 360f), 2f);
        }

        // ------------------------------------------------------------------ the notes on it

        /// <summary>
        /// Shows exactly <paramref name="notes"/> (bottom to top) on the cork. A note overlapping ones below it stands
        /// a little further off the board than they do, so it covers them.
        /// </summary>
        public void Show(IReadOnlyList<NoteData> notes, Noticeboards pool)
        {
            var centres = new Vector2[notes.Count];
            for (int i = 0; i < notes.Count; i++) centres[i] = new Vector2(notes[i].x * Size.x, notes[i].y * Size.y);
            var layers = Layers(centres, Notes.Size);

            _gone.Clear();
            foreach (var id in _views.Keys) _gone.Add(id);
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                _gone.Remove(n.id);
                if (!_views.TryGetValue(n.id, out var view))
                {
                    view = pool.MakeView(transform);
                    view.NoteId = n.id;
                    view.name = "Note " + n.id;
                    view.SetShadows(true);
                    _views[n.id] = view;
                }
                view.Show(n);
                view.transform.SetPositionAndRotation(WorldPoint(new Vector2(n.x, n.y), Lift + layers[i] * LayerLift), NoteRotation(n.tilt));
            }
            foreach (var id in _gone)
            {
                pool.Recycle(_views[id]);
                _views.Remove(id);
            }
        }

        public bool Shows(string id) => _views.ContainsKey(id);
        public NoteView ViewOf(string id) => _views.TryGetValue(id, out var v) ? v : null;

        /// <summary>
        /// For notes at <paramref name="centres"/> (metres, bottom to top), how many overlapping notes each sits on.
        /// Notes are <paramref name="size"/> square and lean a little, so "overlapping" allows a margin.
        /// </summary>
        public static int[] Layers(IReadOnlyList<Vector2> centres, float size)
        {
            var layers = new int[centres.Count];
            float reach = size * 1.08f;
            for (int i = 0; i < centres.Count; i++)
            {
                int layer = 0;
                for (int j = 0; j < i; j++)
                {
                    var d = centres[i] - centres[j];
                    if (Mathf.Abs(d.x) < reach && Mathf.Abs(d.y) < reach) layer = Mathf.Max(layer, layers[j] + 1);
                }
                layers[i] = Mathf.Min(layer, MaxLayers);
            }
            return layers;
        }

        /// <summary>The topmost of <paramref name="notes"/> (bottom to top) covering board point <paramref name="p"/>.</summary>
        public NoteData NoteAt(Vector2 p, IReadOnlyList<NoteData> notes)
        {
            var at = new Vector2(p.x * Size.x, p.y * Size.y);
            for (int i = notes.Count - 1; i >= 0; i--)
            {
                var n = notes[i];
                var d = at - new Vector2(n.x * Size.x, n.y * Size.y);
                // Into the note's own frame: undo its lean (board x runs the reader's way, opposite the cork's local x).
                var local = Quaternion.Euler(0f, 0f, n.tilt) * new Vector3(d.x, d.y, 0f);
                if (Mathf.Abs(local.x) <= Notes.Size * 0.5f && Mathf.Abs(local.y) <= Notes.Size * 0.5f) return n;
            }
            return null;
        }
    }
}
