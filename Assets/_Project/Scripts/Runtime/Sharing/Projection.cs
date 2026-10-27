using UnityEngine;

namespace Facecast
{
    /// <summary>
    /// A projection screen showing someone's shared screen (the ProjectionScreen model; Blender/MODELS.md). It stands
    /// where it was put up, unrolls from its case, glows in the sharer's colour until their picture arrives, then shows
    /// it fitted whole (Facecast/Screen), with their name on the case; when they stop, it rolls back up and goes. The
    /// sharer picks its size (<see cref="SetWidth"/>): the whole projector scales about its feet, smoothly.
    /// </summary>
    public sealed class Projection : MonoBehaviour
    {
        const float RollTime = 0.7f;
        /// <summary>How quickly it grows or shrinks to a new size (per second).</summary>
        const float ResizeRate = 6f;

        public Transform Screen, Rig, Plate;
        public Renderer ScreenRenderer, CaseRenderer;

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int FitId = Shader.PropertyToID("_Fit");
        static readonly int HasVideoId = Shader.PropertyToID("_HasVideo");
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly int TeamColorId = Shader.PropertyToID("_TeamColor");

        Material _screen, _case;
        Mesh _label;
        string _labelText;
        float _roll, _video, _scale = 1f, _targetScale = 1f;
        bool _closing;
        Texture _picture;
        Vector2Int _pictureSize;

        /// <summary>Whose it is: a peer id, or <see cref="Projections.Self"/>.</summary>
        public string Owner { get; private set; }
        /// <summary>The video feed it shows (<see cref="VideoFeeds"/>).</summary>
        public string VideoKey { get; private set; }
        public string Label => _labelText;
        public bool HasPicture { get; private set; }
        /// <summary>How far it has unrolled, 0..1.</summary>
        public float Unrolled => _roll;
        public bool Closing => _closing;
        /// <summary>The model's screen's width and height in metres, before any resizing.</summary>
        public Vector2 ScreenSize { get; private set; } = new(2.4f, 1.35f);
        /// <summary>The width its screen is (or is growing or shrinking to), in metres.</summary>
        public float Width => ScreenSize.x * _targetScale;
        /// <summary>How much bigger than the model it is (or is becoming).</summary>
        public float Scale => _targetScale;
        public Material ScreenMaterial => _screen;

        /// <summary>Finds the model's parts by name (the prefab builder calls this so the references are saved).</summary>
        public void Bind()
        {
            Screen = Find("Screen");
            Rig = Find("Rig");
            Plate = Find("Plate");
            ScreenRenderer = Screen ? Screen.GetComponent<Renderer>() : null;
            var c = Find("Case");
            CaseRenderer = c ? c.GetComponent<Renderer>() : null;
        }

        Transform Find(string part)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == part) return t;
            return null;
        }

        void Awake()
        {
            _screen = new Material(ScreenRenderer.sharedMaterial) { name = "Projection screen (instance)" };
            ScreenRenderer.sharedMaterial = _screen;
            if (CaseRenderer)
            {
                _case = new Material(CaseRenderer.sharedMaterial) { name = "Projection case (instance)" };
                CaseRenderer.sharedMaterial = _case;
            }
            if (Screen.GetComponent<MeshFilter>() is { sharedMesh: { } mesh })
                ScreenSize = new Vector2(mesh.bounds.size.x, mesh.bounds.size.y);
            Roll(0f);
        }

        void OnDestroy()
        {
            if (_screen) Destroy(_screen);
            if (_case) Destroy(_case);
            if (_label) Destroy(_label);
            if (!string.IsNullOrEmpty(VideoKey)) VideoFeeds.Release(VideoKey);
        }

        public void Init(string owner, string videoKey, string label, Color color, float width)
        {
            Owner = owner;
            VideoKey = videoKey;
            name = "Projection " + owner;
            SetColor(color);
            SetLabel(label);
            SetWidth(width, true);
        }

        /// <summary>Makes its screen <paramref name="width"/> metres wide (within the sizes on offer): at once, or growing to it.</summary>
        public void SetWidth(float width, bool now = false)
        {
            _targetScale = Mathf.Clamp(width, Projections.MinWidth, Projections.MaxWidth) / ScreenSize.x;
            if (now) _scale = _targetScale;
            transform.localScale = Vector3.one * _scale;
        }

        public void SetColor(Color c)
        {
            if (_case) _case.SetColor(TeamColorId, c);
            _screen.SetColor(TintId, c);
        }

        /// <summary>Writes <paramref name="text"/> on the plate on the case.</summary>
        public void SetLabel(string text)
        {
            if (text == _labelText) return;
            _labelText = text;
            var boards = Noticeboards.Instance;
            if (!Plate || !boards) return;
            if (!_label) _label = boards.Inscribe(Plate, "Name", text, 0.06f, 0.64f);
            else
            {
                NoteInk.BuildLabel(_label, boards.NoteFont, text, 0.06f, 0.64f, NoteInk.Ink);
                boards.SyncInkAtlas();
            }
        }

        /// <summary>A picture to show instead of a video feed (the offline demo, tests).</summary>
        public void SetPicture(Texture picture, Vector2Int size)
        {
            _picture = picture;
            _pictureSize = size;
        }

        /// <summary>Rolls it back up, then it's gone.</summary>
        public void Close() => _closing = true;

        void Update()
        {
            float dt = Time.deltaTime;
            _roll = Mathf.MoveTowards(_roll, _closing ? 0f : 1f, dt / RollTime);
            Roll(_roll);
            if (!Mathf.Approximately(_scale, _targetScale))
            {
                _scale = Mathf.Lerp(_scale, _targetScale, 1f - Mathf.Exp(-ResizeRate * dt));
                if (Mathf.Abs(_scale - _targetScale) < 0.002f) _scale = _targetScale;
                transform.localScale = Vector3.one * _scale;
            }
            if (_closing && _roll <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            var picture = _picture;
            var size = _pictureSize;
            if (!picture && VideoFeeds.TryGet(VideoKey, out var video, out var videoSize))
            {
                picture = video;
                size = videoSize;
            }
            HasPicture = picture;
            if (HasPicture)
            {
                _screen.SetTexture(MainTexId, picture);
                _screen.SetVector(FitId, VideoCrop.AspectFit(size, ScreenSize.x / ScreenSize.y));
            }
            _video = Mathf.MoveTowards(_video, HasPicture && !_closing ? 1f : 0f, dt * 3f);
            _screen.SetFloat(HasVideoId, Mathf.SmoothStep(0f, 1f, _video));
        }

        /// <summary>The screen and its poles rise out of the case (eased), 0 rolled up to 1 all the way.</summary>
        void Roll(float t)
        {
            float k = Mathf.Max(0.002f, t * t * (3f - 2f * t));
            var scale = new Vector3(1f, k, 1f);
            Screen.localScale = scale;
            if (Rig) Rig.localScale = scale;
        }
    }
}
