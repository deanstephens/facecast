using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Facecast
{
    /// <summary>
    /// What Unity draws over the world (UI Toolkit, styled by UI/Facecast.uss): a name above everyone's head that
    /// lights up while they speak, the crosshair, a controls hint while the mouse is free, and a banner when offline.
    /// The page draws the rest (join card, your camera, microphone and camera buttons, invite link).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class Overlay : MonoBehaviour
    {
        const float MaxNameDistance = 32f;

        VisualElement _root, _plates, _crosshair;
        Label _hint, _banner;
        readonly Dictionary<RemoteAvatar, Label> _labels = new();
        readonly List<RemoteAvatar> _gone = new();

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
            _hint = new Label("Click to look around   ·   WASD walk   ·   Shift run   ·   Space jump   ·   M microphone   ·   V camera")
                { pickingMode = PickingMode.Ignore };
            _hint.AddToClassList("fc-hint");
            _root.Add(_hint);
            _banner = new Label("Offline preview with demo bots. The web build is where people meet.") { pickingMode = PickingMode.Ignore };
            _banner.AddToClassList("fc-banner");
            _root.Add(_banner);
            _labels.Clear();
        }

        void LateUpdate()
        {
            if (_root == null) return;
            var app = App.Instance;
            bool joined = app && app.Joined;
            bool locked = UnityEngine.Cursor.lockState == CursorLockMode.Locked;
            bool touring = app && !app.Player.InputEnabled;
            Show(_crosshair, joined && locked && !touring);
            Show(_hint, joined && !locked && !touring);
            Show(_banner, app && app.Offline && joined && !touring);
            UpdateNames();
        }

        static void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

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
