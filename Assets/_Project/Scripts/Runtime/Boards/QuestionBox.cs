using UnityEngine;

namespace Facecast
{
    /// <summary>
    /// The question box (the QuestionBox model; Blender/MODELS.md). Write a question on a card from the tray on its
    /// side and post it through the slot; anyone can pull them out again, one at a time, oldest first. Its label says
    /// how many are inside. What's in it is <see cref="Notes.Box"/> in the <see cref="NoteBook"/>.
    /// </summary>
    public sealed class QuestionBox : MonoBehaviour
    {
        public Collider Body, Cards;
        public Transform Label;
        /// <summary>The renderers with the corner's colour on them (the lid, the label's border).</summary>
        public Renderer[] Tinted;
        public Color Accent = Color.white;

        static readonly int TeamColorId = Shader.PropertyToID("_TeamColor");
        Material _tint;
        Mesh _title, _count;
        int _shown = -1;

        public int Count => _shown;

        /// <summary>Finds the model's parts by name (the prefab builder calls this so the references are saved).</summary>
        public void Bind()
        {
            Body = Part("Box")?.GetComponent<Collider>();
            Cards = Part("Cards")?.GetComponent<Collider>();
            Label = Part("Label");
            Tinted = new[] { Part("Box")?.GetComponent<Renderer>(), Label ? Label.GetComponent<Renderer>() : null };
        }

        Transform Part(string part)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == part) return t;
            return null;
        }

        void Start()
        {
            foreach (var r in Tinted)
            {
                if (!r) continue;
                if (!_tint)
                {
                    _tint = new Material(r.sharedMaterial) { name = "Question box (instance)" };
                    _tint.SetColor(TeamColorId, Accent);
                }
                r.sharedMaterial = _tint;
            }
            var boards = Noticeboards.Instance;
            if (!Label || !boards) return;
            _title = boards.Inscribe(Label, "Title", "Questions", 0.07f, 0.38f, 0.03f);
            _count = boards.Inscribe(Label, "Count", "", 0.042f, 0.38f, -0.055f);
            int n = _shown;
            _shown = -1;
            ShowCount(Mathf.Max(n, 0));
        }

        void OnDestroy()
        {
            if (_tint) Destroy(_tint);
            if (_title) Destroy(_title);
            if (_count) Destroy(_count);
        }

        public static string CountText(int n) => n == 0 ? "empty" : n == 1 ? "1 question inside" : $"{n} questions inside";

        /// <summary>Writes how many questions are inside on the label.</summary>
        public void ShowCount(int n)
        {
            if (n == _shown) return;
            _shown = n;
            var boards = Noticeboards.Instance;
            if (!_count || !boards) return;
            NoteInk.BuildLabel(_count, boards.NoteFont, CountText(n), 0.042f, 0.38f, NoteInk.InkSoft);
            boards.SyncInkAtlas();
        }

        /// <summary>Where to stand to use the box: <paramref name="distance"/> in front of it, facing it.</summary>
        public AvatarPose ReadingPose(float distance = 1.1f)
        {
            var front = transform.forward;
            var at = transform.position + front * distance;
            float yaw = Mathf.Atan2(-front.x, -front.z) * Mathf.Rad2Deg;
            return new AvatarPose(at, Mathf.Repeat(yaw, 360f), 20f);
        }
    }
}
