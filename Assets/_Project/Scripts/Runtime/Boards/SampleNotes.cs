using System.Collections.Generic;

namespace Facecast
{
    /// <summary>
    /// Notes already on the boards when you arrive. Online, one note on the first board says how they work; the
    /// offline preview fills all four boards, so there's something to read. Every browser makes the same ones (same
    /// ids, version 1), so they merge as one note, and anyone's change to one outranks them.
    /// </summary>
    public static class SampleNotes
    {
        /// <summary>The boards' titles, in order (SceneBuilder writes them on the title plates).</summary>
        public static readonly string[] Titles = { "Notices", "Ideas", "Questions", "Thank you!" };

        public static IEnumerable<NoteData> For(bool offline)
        {
            yield return Note("sample-0", 0, 0.5f, 0.66f, -2f, 0, "Take a note from the shelf below, write on it, then stick it anywhere!", "the square");
            if (!offline) yield break;
            yield return Note("sample-1", 0, 0.18f, 0.7f, 4f, 2, "Choir practice by the fountain, 6pm", "Robin");
            yield return Note("sample-2", 0, 0.8f, 0.62f, -5f, 1, "Lost: one antenna. Answers to Zap.", "Kit");
            yield return Note("sample-3", 0, 0.32f, 0.27f, 3f, 3, "Bench 4 is the sunniest", "Sol");
            yield return Note("sample-4", 1, 0.25f, 0.6f, -3f, 3, "Kite day on the lawn!", "Juno");
            yield return Note("sample-5", 1, 0.55f, 0.36f, 5f, 0, "More lamps by the hedge", "Wren");
            yield return Note("sample-6", 1, 0.78f, 0.7f, -6f, 2, "Fountain should play music?", "Ivo");
            yield return Note("sample-7", 2, 0.3f, 0.55f, 2f, 1, "Who planted the flowers? They're lovely", "Mae");
            yield return Note("sample-8", 2, 0.68f, 0.42f, -4f, 0, "What's over the hills?", "Pax");
            yield return Note("sample-9", 3, 0.4f, 0.6f, -2f, 1, "Thanks for fixing my ear light, Sol :)", "Kit");
            yield return Note("sample-10", 3, 0.7f, 0.3f, 4f, 0, "Lovely evening, everyone", "Robin");
        }

        static NoteData Note(string id, int board, float x, float y, float tilt, int color, string text, string by) => new()
        {
            id = id, board = board, x = x, y = y, tilt = tilt, color = color,
            text = text, by = by, v = 1, w = "sample",
        };
    }
}
