using System;
using UnityEngine;

namespace Facecast
{
    /// <summary>
    /// One of the square's four noticeboards (the NoticeBoard model; Blender/MODELS.md): a cork you stick notes on
    /// (positions 0..1 from the reader's bottom left), a title plate, and a shelf of four notepads you take fresh
    /// notes from.
    /// </summary>
    public sealed class Noticeboard : NoteSurface
    {
        /// <summary>The notepads, in <see cref="Notes.Colors"/> order.</summary>
        public static readonly string[] PadNames = { "PadYellow", "PadPink", "PadBlue", "PadGreen" };

        public string Title = "Notices";
        /// <summary>The roof and title frame's colour.</summary>
        public Color Accent = Color.white;
        public Transform Cork, Header;
        public Collider CorkCollider;
        public Collider[] Pads;
        public Renderer FrameRenderer;

        static readonly int TeamColorId = Shader.PropertyToID("_TeamColor");
        Material _frame;
        Mesh _title;

        public override Transform Frame => Cork;

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
            if (Header && Noticeboards.Instance) _title = Noticeboards.Instance.Inscribe(Header, "Title", Title, 0.13f, 1.1f);
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
    }
}
