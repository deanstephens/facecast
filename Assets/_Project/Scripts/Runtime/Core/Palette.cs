using UnityEngine;

namespace Facecast
{
    /// <summary>The avatar colours players choose from (the join card in WebGLTemplates/Facecast/index.html offers the same eight).</summary>
    public static class Palette
    {
        public static readonly Color[] Colors =
        {
            Hex("#5AA6F2"), // sky
            Hex("#F2705A"), // coral
            Hex("#F2C14E"), // sunflower
            Hex("#4CC9A0"), // mint
            Hex("#9B7BF2"), // violet
            Hex("#F27BB4"), // rose
            Hex("#F2963C"), // tangerine
            Hex("#3CB4C8"), // teal
        };

        /// <summary>Names for the demo bots.</summary>
        public static readonly string[] BotNames = { "Juno", "Kit", "Robin", "Sol", "Wren", "Ivo", "Mae", "Pax" };

        public static Color Hex(string html) => ColorUtility.TryParseHtmlString(html, out var c) ? c : Color.white;
    }
}
