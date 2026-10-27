using System.Collections.Generic;
using UnityEngine;

namespace Facecast
{
    /// <summary>
    /// Somewhere notes lie: a noticeboard's cork, or the question table's top. It knows where things are on it
    /// (positions 0..1 across and up, as you face it) and lays out what's on it, later ones on top. Its
    /// <see cref="Frame"/> has +Z out of the surface; position x runs along the frame's -X (the reader's right on a
    /// board) and y along its +Y.
    /// </summary>
    public abstract class NoteSurface : MonoBehaviour
    {
        /// <summary>How far a note stands off the surface, and how much higher each note it overlaps lifts it (clearing a curl).</summary>
        public const float Lift = 0.0015f, LayerLift = 0.0045f;
        const int MaxLayers = 8;

        /// <summary>Its place (<see cref="NoteData.board"/>): a board's number, or <see cref="Notes.Table"/>.</summary>
        public int Index;
        /// <summary>The surface's width and height in metres.</summary>
        public Vector2 Size = new(2.1f, 1.25f);

        readonly Dictionary<string, NoteView> _views = new();
        readonly List<string> _gone = new();

        public abstract Transform Frame { get; }

        protected Transform Find(string part)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == part) return t;
            return null;
        }

        /// <summary>The world position of surface point <paramref name="p"/>, <paramref name="lift"/> metres off it.</summary>
        public Vector3 WorldPoint(Vector2 p, float lift = 0f) =>
            Frame.TransformPoint(new Vector3(-(p.x - 0.5f) * Size.x, (p.y - 0.5f) * Size.y, lift));

        /// <summary>The surface point (0..1 across and up) at a world position on it.</summary>
        public Vector2 SurfacePoint(Vector3 world)
        {
            var l = Frame.InverseTransformPoint(world);
            return new Vector2(0.5f - l.x / Size.x, 0.5f + l.y / Size.y);
        }

        /// <summary>A note's rotation here, turned <paramref name="tilt"/> degrees about the surface's normal.</summary>
        public Quaternion NoteRotation(float tilt) => Frame.rotation * Quaternion.Euler(0f, 0f, tilt);

        /// <summary>The tilt that turns a note's writing to read upright along <paramref name="direction"/> (as someone looking that way sees it).</summary>
        public float TiltFacing(Vector3 direction)
        {
            var d = Frame.InverseTransformDirection(direction);
            return Mathf.Atan2(-d.x, d.y) * Mathf.Rad2Deg;
        }

        /// <summary>The nearest point where a whole note (or card) fits on the surface.</summary>
        public Vector2 Clamp(Vector2 p, bool question = false)
        {
            var half = Notes.HalfSize(question) * 1.04f;
            float r = question ? Mathf.Max(half.x, half.y) : half.x;  // a card on the table can face any way
            float hx = (question ? r : half.x) / Size.x, hy = (question ? r : half.y) / Size.y;
            return new Vector2(Mathf.Clamp(p.x, hx, 1f - hx), Mathf.Clamp(p.y, hy, 1f - hy));
        }

        /// <summary>
        /// Shows exactly <paramref name="notes"/> (bottom to top) here. A note overlapping ones below it stands a little
        /// further off the surface than they do, so it covers them.
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
                    view = pool.MakeView(transform, n.question);
                    view.NoteId = n.id;
                    view.name = (n.question ? "Question " : "Note ") + n.id;
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

        /// <summary>The topmost of <paramref name="notes"/> (bottom to top) covering surface point <paramref name="p"/>.</summary>
        public NoteData NoteAt(Vector2 p, IReadOnlyList<NoteData> notes)
        {
            var at = new Vector2(p.x * Size.x, p.y * Size.y);
            for (int i = notes.Count - 1; i >= 0; i--)
            {
                var n = notes[i];
                var d = at - new Vector2(n.x * Size.x, n.y * Size.y);
                // Into the note's own frame: undo its turn (x here runs opposite the frame's local x).
                var local = Quaternion.Euler(0f, 0f, n.tilt) * new Vector3(d.x, d.y, 0f);
                var half = Notes.HalfSize(n.question);
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y) return n;
            }
            return null;
        }
    }
}
