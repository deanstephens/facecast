using System;
using UnityEngine;

namespace Facecast
{
    /// <summary>
    /// A message from the page's networking (facecast.js) or from the demo bots, delivered to <see cref="Room"/>.
    /// The page sends JSON; every type uses a subset of the fields:
    /// <list type="bullet">
    /// <item><c>self</c>: you joined room <see cref="room"/> as <see cref="id"/>, with <see cref="name"/> and <see cref="color"/>.</item>
    /// <item><c>hello</c>: peer <see cref="id"/> introduced itself, or changed its name, colour, <see cref="cam"/> or <see cref="mic"/>.</item>
    /// <item><c>pose</c>: peer <see cref="id"/> moved; <see cref="p"/> is <see cref="AvatarPose.ToWire"/>.</item>
    /// <item><c>leave</c>: peer <see cref="id"/> left.</item>
    /// <item><c>notes</c>: peer <see cref="id"/> sent <see cref="notes"/> (changes, or every note when you arrive).</item>
    /// <item><c>write</c>: the page's note editor closed: <see cref="ok"/> with <see cref="text"/> in colour <see cref="tint"/>, or cancelled.</item>
    /// <item><c>sharing</c>: you started sharing your screen (<see cref="on"/>), or stopped.</item>
    /// <item><c>share</c>: peer <see cref="id"/> put up a projection of their screen at <see cref="p"/> (x, y, z, yaw), or took it down (<see cref="on"/> false).</item>
    /// </list>
    /// </summary>
    [Serializable]
    public class NetEvent
    {
        public string type;
        public string id;
        public string name;
        public string color;
        public bool cam;
        public bool mic;
        public float[] p;
        public string room;
        public NoteData[] notes;
        public string text;
        public int tint;
        public bool ok;
        public bool on;

        public static NetEvent Parse(string json)
        {
            try
            {
                var e = JsonUtility.FromJson<NetEvent>(json);
                return string.IsNullOrEmpty(e?.type) ? null : e;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public Color ParsedColor(Color fallback) =>
            !string.IsNullOrEmpty(color) && ColorUtility.TryParseHtmlString(color, out var c) ? c : fallback;
    }
}
