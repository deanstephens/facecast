using UnityEngine;

namespace Facecast
{
    /// <summary>
    /// Whether the mouse is captured for looking around. In a browser, the browser has the last word: Esc lets the
    /// pointer go without Unity's <see cref="Cursor.lockState"/> hearing of it, so that would still say "locked" while
    /// the mouse roams the page, and every move would turn your view. So in a web build this asks the page (is anything
    /// holding the pointer?); elsewhere Unity's own state is the truth.
    /// </summary>
    public static class MouseLock
    {
        static bool _browserHadIt;

        public static bool Captured => WebBridge.Available ? WebBridge.PointerLocked() : Cursor.lockState == CursorLockMode.Locked;

        /// <summary>
        /// Captures the mouse. A browser only grants it on the next mouse event (normally the click's release), so it
        /// starts afresh each time: a request the browser refused, or one Unity still thinks it holds, would block it.
        /// </summary>
        public static void Capture()
        {
            if (WebBridge.Available) Cursor.lockState = CursorLockMode.None;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public static void Release()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>
        /// Call once a frame. When the browser lets go (Esc), Unity still thinks it holds the mouse: bring it into line.
        /// Only a real letting go counts (held last frame, not now), not a capture the browser hasn't granted yet.
        /// </summary>
        public static void Sync()
        {
            if (!WebBridge.Available) return;
            bool has = WebBridge.PointerLocked();
            if (_browserHadIt && !has && Cursor.lockState == CursorLockMode.Locked) Release();
            _browserHadIt = has;
        }
    }
}
