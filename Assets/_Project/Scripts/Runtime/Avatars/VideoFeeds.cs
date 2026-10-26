using System;
using System.Collections.Generic;
using UnityEngine;

namespace Facecast
{
    /// <summary>
    /// Webcam pictures as textures, one per video key (a peer's id, or "self" for your own camera). In a web build the
    /// page draws each peer's &lt;video&gt; straight into a WebGL texture that Unity wraps with
    /// <see cref="Texture2D.CreateExternalTexture"/>, so frames never pass through C#. Elsewhere there is no video and
    /// faces show their cartoon instead.
    /// </summary>
    public static class VideoFeeds
    {
        /// <summary>The video key of your own camera (demo bots wear it in a web build).</summary>
        public const string Self = "self";

        sealed class Feed
        {
            public int Id;
            public Texture2D Texture;
            public Vector2Int Size;
            public int Frame = -1;
        }

        static readonly Dictionary<string, Feed> Feeds = new();

        /// <summary>The newest picture of <paramref name="key"/>'s camera, updated at most once per frame.</summary>
        public static bool TryGet(string key, out Texture texture, out Vector2Int size)
        {
            texture = null;
            size = default;
            if (!WebBridge.Available || string.IsNullOrEmpty(key)) return false;
            if (!Feeds.TryGetValue(key, out var feed))
            {
                int id = WebBridge.CreateVideoTexture();
                if (id <= 0) return false;
                var tex = Texture2D.CreateExternalTexture(2, 2, TextureFormat.RGBA32, false, false, (IntPtr)id);
                tex.name = "Video " + key;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                Feeds[key] = feed = new Feed { Id = id, Texture = tex };
            }
            if (feed.Frame != Time.frameCount)
            {
                feed.Frame = Time.frameCount;
                int packed = WebBridge.UpdateVideoTexture(key, feed.Id);
                feed.Size = packed == 0 ? default : new Vector2Int(packed >> 16, packed & 0xFFFF);
            }
            if (feed.Size.x <= 0 || feed.Size.y <= 0) return false;
            texture = feed.Texture;
            size = feed.Size;
            return true;
        }

        /// <summary>Frees a key's texture (a peer left).</summary>
        public static void Release(string key)
        {
            if (string.IsNullOrEmpty(key) || !Feeds.TryGetValue(key, out var feed)) return;
            Feeds.Remove(key);
            UnityEngine.Object.Destroy(feed.Texture);
            WebBridge.DeleteVideoTexture(feed.Id);
        }
    }

    /// <summary>Fitting a camera picture to a face screen.</summary>
    public static class VideoCrop
    {
        /// <summary>The 4:3 shape of the avatar's face screen (Blender/MODELS.md).</summary>
        public const float ScreenAspect = 4f / 3f;

        /// <summary>
        /// UV scale (xy) and offset (zw) that fill a screen of <paramref name="screenAspect"/> with a picture of
        /// <paramref name="videoSize"/>, cropping the overflow evenly from both sides (like CSS object-fit: cover).
        /// </summary>
        public static Vector4 AspectFill(Vector2Int videoSize, float screenAspect = ScreenAspect)
        {
            if (videoSize.x <= 0 || videoSize.y <= 0) return new Vector4(1, 1, 0, 0);
            float videoAspect = (float)videoSize.x / videoSize.y;
            if (videoAspect > screenAspect)
            {
                float s = screenAspect / videoAspect;  // wider picture: crop left and right
                return new Vector4(s, 1f, (1f - s) * 0.5f, 0f);
            }
            float t = videoAspect / screenAspect;      // taller picture: crop top and bottom
            return new Vector4(1f, t, 0f, (1f - t) * 0.5f);
        }
    }
}
