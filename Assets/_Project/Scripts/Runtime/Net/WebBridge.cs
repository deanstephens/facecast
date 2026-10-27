using System.Runtime.InteropServices;

namespace Facecast
{
    /// <summary>
    /// Calls into the page around the web build (Plugins/WebGL/Facecast.jslib, which forwards to window.facecast in
    /// WebGLTemplates/Facecast/facecast.js). The page owns the WebRTC room, the camera, the microphone and the voice
    /// mix; Unity owns the world. Everywhere except a web build these do nothing.
    /// </summary>
    public static class WebBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void FC_Ready();
        [DllImport("__Internal")] static extern void FC_SendPose(float x, float y, float z, float yaw, float pitch, float time);
        [DllImport("__Internal")] static extern void FC_ToggleMic();
        [DllImport("__Internal")] static extern void FC_ToggleCam();
        [DllImport("__Internal")] static extern void FC_ToggleZoom();
        [DllImport("__Internal")] static extern int FC_VideoCrop(string key, float[] crop);
        [DllImport("__Internal")] static extern float FC_AudioLevel(string key);
        [DllImport("__Internal")] static extern int FC_CreateVideoTexture();
        [DllImport("__Internal")] static extern int FC_UpdateVideoTexture(string key, int texture);
        [DllImport("__Internal")] static extern void FC_DeleteVideoTexture(int texture);
        [DllImport("__Internal")] static extern void FC_SendNotes(string json, string target);
        [DllImport("__Internal")] static extern void FC_OpenNoteEditor(string text, int color);
        [DllImport("__Internal")] static extern void FC_CloseNoteEditor();
        [DllImport("__Internal")] static extern void FC_ReportNotes(string json);

        public static bool Available => true;
        public static void Ready() => FC_Ready();
        public static void SendPose(AvatarPose p, float time) => FC_SendPose(p.Position.x, p.Position.y, p.Position.z, p.Yaw, p.Pitch, time);
        public static void ToggleMic() => FC_ToggleMic();
        public static void ToggleCam() => FC_ToggleCam();
        public static void ToggleZoom() => FC_ToggleZoom();
        /// <summary>
        /// The part of <paramref name="key"/>'s picture their face is in, into <paramref name="crop"/> (x, y, w, h:
        /// fractions of the picture from its top left). False when the whole picture should show.
        /// </summary>
        public static bool VideoCrop(string key, float[] crop) => FC_VideoCrop(key, crop) != 0;
        public static float AudioLevel(string key) => FC_AudioLevel(key);
        public static int CreateVideoTexture() => FC_CreateVideoTexture();
        /// <summary>Copies the newest frame of <paramref name="key"/>'s video into the texture. Returns width &lt;&lt; 16 | height, or 0 without video.</summary>
        public static int UpdateVideoTexture(string key, int texture) => FC_UpdateVideoTexture(key, texture);
        public static void DeleteVideoTexture(int texture) => FC_DeleteVideoTexture(texture);
        /// <summary>Sends a "notes" message (a <see cref="NoteList"/>) to <paramref name="target"/>, or everyone when "".</summary>
        public static void SendNotes(string json, string target) => FC_SendNotes(json, target ?? "");
        /// <summary>Opens the page's note to write on; it answers with a "write" message.</summary>
        public static void OpenNoteEditor(string text, int color) => FC_OpenNoteEditor(text ?? "", color);
        public static void CloseNoteEditor() => FC_CloseNoteEditor();
        /// <summary>What's on the boards, for the page's test hooks (facecast.debug.notes()).</summary>
        public static void ReportNotes(string json) => FC_ReportNotes(json);
#else
        public static bool Available => false;
        public static void Ready() { }
        public static void SendPose(AvatarPose p, float time) { }
        public static void ToggleMic() { }
        public static void ToggleCam() { }
        public static void ToggleZoom() { }
        public static bool VideoCrop(string key, float[] crop) => false;
        public static float AudioLevel(string key) => 0f;
        public static int CreateVideoTexture() => 0;
        public static int UpdateVideoTexture(string key, int texture) => 0;
        public static void DeleteVideoTexture(int texture) { }
        public static void SendNotes(string json, string target) { }
        public static void OpenNoteEditor(string text, int color) { }
        public static void CloseNoteEditor() { }
        public static void ReportNotes(string json) { }
#endif
    }
}
