using UnityEngine;
using UnityEngine.Rendering;

namespace Facecast
{
    /// <summary>
    /// An avatar model (Blender/MODELS.md): its moving parts, its colour and its face screen. Nothing is rigged:
    /// the hovering body bobs and leans into its movement, the floating hands swing, the head pitches with the
    /// player's look, and the ear and antenna lights glow while the player speaks. The face screen shows the
    /// player's webcam (<see cref="SetVideo"/>) or, without one, a cartoon face (<see cref="ShowCartoon"/>).
    /// Every avatar gets its own copies of the two materials.
    /// </summary>
    public sealed class AvatarRig : MonoBehaviour
    {
        public Transform Body, Head, HandL, HandR, Face, Eye, Nameplate;
        public Renderer FaceRenderer;
        public Renderer[] BodyRenderers;

        static readonly int TeamColorId = Shader.PropertyToID("_TeamColor");
        static readonly int SignalGlowId = Shader.PropertyToID("_SignalGlow");
        static readonly int RimId = Shader.PropertyToID("_Rim");
        static readonly int DitherId = Shader.PropertyToID("_Dither");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int VideoStId = Shader.PropertyToID("_VideoST");
        static readonly int HasVideoId = Shader.PropertyToID("_HasVideo");
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly int SpeakingId = Shader.PropertyToID("_Speaking");
        static readonly int SeedId = Shader.PropertyToID("_Seed");

        Material _flat, _face;
        Vector3 _bodyPos, _headPos, _handLPos, _handRPos;
        float _phase, _stride, _videoTarget, _video, _fade = -1f;
        Vector3 _lean;
        bool _ready;

        public Color Color { get; private set; } = Color.white;
        public bool ShowsVideo => _videoTarget > 0.5f;

        /// <summary>Finds the model's parts by name (the prefab builder calls this so the references are saved).</summary>
        public void Bind()
        {
            Body = Find("Body");
            Head = Find("Head");
            HandL = Find("HandL");
            HandR = Find("HandR");
            Face = Find("Face");
            Eye = Find("Eye");
            Nameplate = Find("Nameplate");
            FaceRenderer = Face ? Face.GetComponent<Renderer>() : null;
            var all = GetComponentsInChildren<MeshRenderer>(true);
            BodyRenderers = System.Array.FindAll(all, r => r != FaceRenderer);
        }

        Transform Find(string part)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == part) return t;
            return null;
        }

        void Awake() => Init();

        void Init()
        {
            if (_ready) return;
            _ready = true;
            if (!Body || !Head || !FaceRenderer) Bind();
            _bodyPos = Body.localPosition;
            _headPos = Head.localPosition;
            _handLPos = HandL.localPosition;
            _handRPos = HandR.localPosition;
            _phase = Random.value * 10f;

            if (BodyRenderers.Length > 0)
            {
                _flat = new Material(BodyRenderers[0].sharedMaterial) { name = "Avatar (instance)" };
                foreach (var r in BodyRenderers) r.sharedMaterial = _flat;
            }
            _face = new Material(FaceRenderer.sharedMaterial) { name = "Face (instance)" };
            FaceRenderer.sharedMaterial = _face;
            _face.SetFloat(SeedId, Random.value * 100f);
            ShowCartoon();
        }

        void OnDestroy()
        {
            if (_flat) Destroy(_flat);
            if (_face) Destroy(_face);
        }

        public void SetColor(Color c)
        {
            Init();
            Color = c;
            if (_flat)
            {
                _flat.SetColor(TeamColorId, c);
                _flat.SetColor(RimId, new Color(1f, 0.97f, 0.9f, 0.12f));
            }
            _face.SetColor(TintId, c);
        }

        /// <summary>Shows a camera picture on the face screen, cropped to fill it.</summary>
        public void SetVideo(Texture texture, Vector2Int size) => SetVideo(texture, VideoCrop.AspectFill(size));

        /// <summary>Shows the part of a camera picture that <paramref name="crop"/> (UV scale xy, offset zw) picks out.</summary>
        public void SetVideo(Texture texture, Vector4 crop)
        {
            Init();
            _face.SetTexture(MainTexId, texture);
            _face.SetVector(VideoStId, crop);
            _videoTarget = 1f;
        }

        /// <summary>Fades the face screen back to the cartoon face (no camera, or the camera turned off).</summary>
        public void ShowCartoon()
        {
            Init();
            _videoTarget = 0f;
        }

        /// <summary>Screen-door fade (0 solid, 1 gone): avatars fade out when your camera is inside them.</summary>
        public void SetFade(float fade)
        {
            Init();
            fade = Mathf.Clamp01(fade);
            if (Mathf.Approximately(fade, _fade)) return;
            _fade = fade;
            if (_flat) _flat.SetFloat(DitherId, fade);
            _face.SetFloat(DitherId, fade);
        }

        /// <summary>Your own avatar: invisible to you, but it still casts its shadow.</summary>
        public void SetShadowsOnly(bool on)
        {
            Init();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = on ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            FaceRenderer.shadowCastingMode = on ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.Off;
        }

        public void SetVisible(bool visible)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
        }

        /// <summary>
        /// One frame of procedural animation. <paramref name="localVelocity"/> is the avatar's movement in its own
        /// frame (z forward, x right), <paramref name="pitch"/> where the player looks (degrees, positive down) and
        /// <paramref name="speaking"/> how loud they are right now (0..1).
        /// </summary>
        public void Animate(Vector3 localVelocity, float pitch, float speaking, float dt)
        {
            Init();
            float t = Time.time + _phase;
            float speed = new Vector2(localVelocity.x, localVelocity.z).magnitude;
            float move = Mathf.Clamp01(speed / 3f);
            _stride += dt * (2.2f + speed * 2.4f);

            // Hover: a slow bob, plus a little bounce in each stride while moving.
            float bob = Mathf.Sin(t * 2.1f) * 0.022f * (1f - move) + Mathf.Abs(Mathf.Sin(_stride)) * 0.045f * move;
            // Lean into the movement, smoothed so starts and stops rock gently.
            var leanTarget = new Vector3(Mathf.Clamp(localVelocity.z * 2.6f, -9f, 11f), 0f, Mathf.Clamp(-localVelocity.x * 2.6f, -9f, 9f));
            _lean = Vector3.Lerp(_lean, leanTarget, 1f - Mathf.Exp(-8f * dt));
            var lean = Quaternion.Euler(_lean);
            var lift = new Vector3(0f, bob, 0f);

            Body.localPosition = _bodyPos + lift;
            Body.localRotation = lean;

            // The head rides on the leaning body and pitches with the look; a little nod while talking.
            float nod = Mathf.Sin(t * 7.5f) * 2.2f * speaking;
            Head.localPosition = lean * _headPos + lift;
            Head.localRotation = lean * Quaternion.Euler(Mathf.Clamp(pitch, -40f, 40f) * 0.85f + nod, 0f, 0f);

            // Hands swing opposite each other while moving, drift while idle, and lift a little while talking.
            float swing = Mathf.Sin(_stride) * 0.13f * move;
            float gesture = speaking * 0.05f;
            HandL.localPosition = lean * (_handLPos + new Vector3(0f, Mathf.Sin(t * 1.7f) * 0.018f + gesture * Mathf.Sin(t * 5f + 1f), swing)) + lift * 0.6f;
            HandR.localPosition = lean * (_handRPos + new Vector3(0f, Mathf.Sin(t * 1.7f + 2f) * 0.018f + gesture * Mathf.Sin(t * 5.6f), -swing)) + lift * 0.6f;

            // Face screen and speaking lights.
            _video = Mathf.MoveTowards(_video, _videoTarget, dt * 4f);
            _face.SetFloat(HasVideoId, Mathf.SmoothStep(0f, 1f, _video));
            _face.SetFloat(SpeakingId, speaking);
            if (_flat) _flat.SetFloat(SignalGlowId, speaking);
        }
    }
}
