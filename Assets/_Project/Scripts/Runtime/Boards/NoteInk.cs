using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace Facecast
{
    /// <summary>
    /// Fitting a note's words onto its paper: wrapped into lines (breaking a word only when it's longer than a whole
    /// line) at the biggest handwriting that fits, like a real note where three words get written large. Measures in
    /// ems through a function, so it works with any font, or none (the tests).
    /// </summary>
    public static class NoteLayout
    {
        public sealed class Result
        {
            public float Size;
            public readonly List<string> Lines = new();
            public bool Truncated;
        }

        public const string Ellipsis = "…";

        /// <summary>
        /// Wraps <paramref name="text"/> into a <paramref name="width"/> × <paramref name="height"/> box at the first
        /// of <paramref name="sizes"/> (em sizes, biggest first) whose lines fit; at the last size, cuts it short with
        /// an ellipsis. <paramref name="advance"/> is a character's width in ems, <paramref name="lineHeight"/> the
        /// line spacing in ems.
        /// </summary>
        public static Result Fit(string text, Func<char, float> advance, float width, float height, IReadOnlyList<float> sizes, float lineHeight)
        {
            var result = new Result();
            for (int i = 0; i < sizes.Count; i++)
            {
                float size = sizes[i];
                var lines = Wrap(text, advance, width / size);
                int fits = Mathf.Max(1, Mathf.FloorToInt(height / (lineHeight * size) + 1e-4f));
                if (lines.Count <= fits || i == sizes.Count - 1)
                {
                    result.Size = size;
                    if (lines.Count > fits)
                    {
                        lines.RemoveRange(fits, lines.Count - fits);
                        lines[fits - 1] = Shorten(lines[fits - 1], advance, width / size);
                        result.Truncated = true;
                    }
                    result.Lines.AddRange(lines);
                    return result;
                }
            }
            return result;
        }

        public static float Measure(string s, Func<char, float> advance)
        {
            float w = 0f;
            foreach (char c in s) w += advance(c);
            return w;
        }

        public static List<string> Wrap(string text, Func<char, float> advance, float maxEm)
        {
            var lines = new List<string>();
            float space = advance(' ');
            foreach (var paragraph in (text ?? "").Split('\n'))
            {
                string line = "";
                float lineW = 0f;
                foreach (var word in paragraph.Split(' '))
                {
                    if (word.Length == 0) continue;
                    var rest = word;
                    float restW = Measure(rest, advance);
                    // A word longer than a whole line gets broken across lines.
                    while (restW > maxEm && rest.Length > 1)
                    {
                        if (line.Length > 0)
                        {
                            lines.Add(line);
                            line = "";
                            lineW = 0f;
                        }
                        int n = 1;
                        float w = advance(rest[0]);
                        while (n < rest.Length && w + advance(rest[n]) <= maxEm) w += advance(rest[n++]);
                        if (n < rest.Length && char.IsLowSurrogate(rest[n])) n++;
                        lines.Add(rest.Substring(0, n));
                        rest = rest.Substring(n);
                        restW = Measure(rest, advance);
                    }
                    if (rest.Length == 0) continue;
                    if (line.Length == 0)
                    {
                        line = rest;
                        lineW = restW;
                    }
                    else if (lineW + space + restW <= maxEm)
                    {
                        line += " " + rest;
                        lineW += space + restW;
                    }
                    else
                    {
                        lines.Add(line);
                        line = rest;
                        lineW = restW;
                    }
                }
                lines.Add(line);
            }
            return lines;
        }

        static string Shorten(string line, Func<char, float> advance, float maxEm)
        {
            float ell = Measure(Ellipsis, advance);
            line = line.TrimEnd();
            while (line.Length > 0 && Measure(line, advance) + ell > maxEm) line = line.Substring(0, line.Length - 1).TrimEnd();
            return line + Ellipsis;
        }
    }

    /// <summary>
    /// Handwriting as geometry: builds a mesh of glyph quads from a TextCore font asset's signed-distance-field atlas
    /// (drawn by the Facecast/Ink shader, crisp at any distance). Characters the font hasn't drawn yet are added to
    /// its atlas on the way; ones it doesn't have (emoji) are left out.
    /// </summary>
    // FontAsset.characterLookupTable is marked obsolete because UI Toolkit's own text generator no longer needs it,
    // but it's still how a character finds its glyph in the atlas.
#pragma warning disable CS0618
    public static class NoteInk
    {
        /// <summary>Handwriting sizes in metres (em), biggest first.</summary>
        public static readonly float[] Sizes = { 0.036f, 0.03f, 0.025f, 0.021f, 0.0175f, 0.0145f };
        /// <summary>
        /// The writing area on a note (metres from its centre, y up): the paper less its margins and the signature, all
        /// above the bottom 3 cm, which curls off the board (Blender/MODELS.md) and would cover ink drawn on it.
        /// </summary>
        public const float Width = 0.17f, BodyTop = 0.085f, BodyBottom = -0.05f;
        const float SignatureY = -0.067f, SignatureSize = 0.0175f, SignatureRight = 0.082f;

        public static readonly Color Ink = Palette.Hex("#26305A");
        public static readonly Color InkSoft = new(0.2f, 0.24f, 0.4f, 0.75f);

        static readonly List<Vector3> Verts = new();
        static readonly List<Vector3> Normals = new();
        static readonly List<Color> Colors = new();
        static readonly List<Vector2> Uvs = new();
        static readonly List<int> Tris = new();

        /// <summary>A note's writing, centred on its paper, with "— <paramref name="by"/>" small in the corner.</summary>
        public static NoteLayout.Result BuildNote(Mesh mesh, FontAsset font, string text, string by)
        {
            Begin();
            NoteLayout.Result layout = null;
            if (font)
            {
                Prepare(font, text + by + NoteLayout.Ellipsis + "—");
                var f = new Metrics(font);
                layout = NoteLayout.Fit(text, f.Advance, Width, BodyTop - BodyBottom, Sizes, f.LineHeight);
                float lineH = f.LineHeight * layout.Size;
                float top = (BodyTop + BodyBottom) * 0.5f + lineH * layout.Lines.Count * 0.5f;
                for (int i = 0; i < layout.Lines.Count; i++)
                {
                    float baseline = top - f.Ascent * layout.Size - i * lineH;
                    string line = layout.Lines[i];
                    float w = NoteLayout.Measure(line, f.Advance) * layout.Size;
                    Write(font, f, line, -w * 0.5f, baseline, layout.Size, Ink);
                }
                if (!string.IsNullOrEmpty(by))
                {
                    string sig = "— " + by;
                    float w = NoteLayout.Measure(sig, f.Advance) * SignatureSize;
                    Write(font, f, sig, Mathf.Max(SignatureRight - w, -Width * 0.5f), SignatureY, SignatureSize, InkSoft);
                }
            }
            End(mesh);
            return layout;
        }

        /// <summary>One line centred on the origin (a board's title), as big as <paramref name="size"/> but no wider than <paramref name="maxWidth"/>.</summary>
        public static void BuildLabel(Mesh mesh, FontAsset font, string text, float size, float maxWidth, Color color)
        {
            Begin();
            if (font && !string.IsNullOrEmpty(text))
            {
                Prepare(font, text);
                var f = new Metrics(font);
                float em = NoteLayout.Measure(text, f.Advance);
                if (em * size > maxWidth) size = maxWidth / em;
                Write(font, f, text, -em * size * 0.5f, -f.CapHeight * size * 0.5f, size, color);
            }
            End(mesh);
        }

        /// <summary>Font measurements in ems.</summary>
        readonly struct Metrics
        {
            readonly FontAsset _font;
            readonly float _perEm;
            public readonly float LineHeight, Ascent, CapHeight;

            public Metrics(FontAsset font)
            {
                _font = font;
                var face = font.faceInfo;
                _perEm = 1f / Mathf.Max(1f, face.pointSize);
                LineHeight = face.lineHeight * _perEm;
                Ascent = face.ascentLine * _perEm;
                CapHeight = face.capLine * _perEm;
            }

            public float Advance(char c) =>
                _font.characterLookupTable.TryGetValue(c, out var ch) && ch.glyph != null
                    ? ch.glyph.metrics.horizontalAdvance * ch.glyph.scale * _perEm
                    : c == ' ' ? 0.3f : 0f;

            public float PerEm => _perEm;
        }

        /// <summary>Asks a dynamic font to draw any characters of <paramref name="text"/> it hasn't drawn yet.</summary>
        static void Prepare(FontAsset font, string text)
        {
            if (font.atlasPopulationMode != AtlasPopulationMode.Dynamic) return;
            string missing = null;
            foreach (char c in text)
                if (c >= ' ' && !char.IsSurrogate(c) && !font.characterLookupTable.ContainsKey(c))
                    missing += c;
            if (missing != null) font.TryAddCharacters(missing, out _);
        }

        static void Write(FontAsset font, Metrics f, string s, float x, float baseline, float size, Color color)
        {
            float k = size * f.PerEm;   // metres per atlas pixel
            float pad = font.atlasPadding;
            float aw = font.atlasWidth, ah = font.atlasHeight;
            foreach (char c in s)
            {
                if (!font.characterLookupTable.TryGetValue(c, out var ch) || ch.glyph == null)
                {
                    x += f.Advance(c) * size;  // a space the font has no glyph for, or nothing at all
                    continue;
                }
                var g = ch.glyph;
                var m = g.metrics;
                var r = g.glyphRect;
                float gk = k * g.scale;
                if (r.width > 0 && r.height > 0 && g.atlasIndex == 0)
                {
                    float x0 = x + (m.horizontalBearingX - pad) * gk;
                    float x1 = x0 + (m.width + 2f * pad) * gk;
                    float y1 = baseline + (m.horizontalBearingY + pad) * gk;
                    float y0 = y1 - (m.height + 2f * pad) * gk;
                    var uv0 = new Vector2((r.x - pad) / aw, (r.y - pad) / ah);
                    var uv1 = new Vector2((r.x + r.width + pad) / aw, (r.y + r.height + pad) / ah);
                    Quad(x0, y0, x1, y1, uv0, uv1, color);
                }
                x += m.horizontalAdvance * gk;
            }
        }

        static void Quad(float x0, float y0, float x1, float y1, Vector2 uv0, Vector2 uv1, Color color)
        {
            int i = Verts.Count;
            Verts.Add(new Vector3(x0, y0, 0f));
            Verts.Add(new Vector3(x0, y1, 0f));
            Verts.Add(new Vector3(x1, y1, 0f));
            Verts.Add(new Vector3(x1, y0, 0f));
            Uvs.Add(uv0);
            Uvs.Add(new Vector2(uv0.x, uv1.y));
            Uvs.Add(uv1);
            Uvs.Add(new Vector2(uv1.x, uv0.y));
            for (int k = 0; k < 4; k++)
            {
                Normals.Add(Vector3.back);  // towards the reader: the ink plane's +Z points into the paper
                Colors.Add(color);
            }
            Tris.Add(i);
            Tris.Add(i + 1);
            Tris.Add(i + 2);
            Tris.Add(i);
            Tris.Add(i + 2);
            Tris.Add(i + 3);
        }

        static void Begin()
        {
            Verts.Clear();
            Normals.Clear();
            Colors.Clear();
            Uvs.Clear();
            Tris.Clear();
        }

        static void End(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(Verts);
            mesh.SetNormals(Normals);
            mesh.SetColors(Colors);
            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Tris, 0);
            mesh.RecalculateBounds();
        }
    }
#pragma warning restore CS0618
}
