using UnityEngine;
using UnityEngine.Rendering;

namespace Facecast
{
    /// <summary>
    /// A sticky note or question card you can see: the StickyNote model's paper in the note's colour (or the
    /// QuestionCard model's, which is always cream), and its handwriting (an "Ink" child the prefab builder adds,
    /// facing the reader). The same view shows it on a board or the table, in your hand or in someone else's. It
    /// rebuilds the handwriting only when the words change.
    /// </summary>
    public sealed class NoteView : MonoBehaviour
    {
        public MeshRenderer Paper;
        public MeshFilter Ink;
        /// <summary>A question card (anonymous, cream, written below its rule) rather than a sticky note.</summary>
        public bool Card;

        Mesh _mesh;
        string _text, _by;
        int _color = -1;

        public string NoteId { get; set; }

        public void Show(NoteData note) => Show(note.text, note.by, note.color);

        public void Show(string text, string by, int color)
        {
            var boards = Noticeboards.Instance;
            if (!boards) return;
            if (color != _color && !Card)
            {
                _color = color;
                Paper.sharedMaterial = boards.PaperMaterial(color);
            }
            if (text == _text && by == _by && _mesh) return;
            _text = text;
            _by = by;
            if (!_mesh)
            {
                _mesh = new Mesh { name = "Note ink" };
                _mesh.MarkDynamic();
                Ink.sharedMesh = _mesh;
                Ink.GetComponent<MeshRenderer>().sharedMaterial = boards.InkMaterialInstance;
            }
            NoteInk.BuildNote(_mesh, boards.NoteFont, text ?? "", by ?? "", Card ? NoteInk.Card : NoteInk.Sticky);
            boards.SyncInkAtlas();
        }

        public void SetShadows(bool on) => Paper.shadowCastingMode = on ? ShadowCastingMode.On : ShadowCastingMode.Off;

        void OnDestroy()
        {
            if (_mesh) Destroy(_mesh);
        }
    }
}
