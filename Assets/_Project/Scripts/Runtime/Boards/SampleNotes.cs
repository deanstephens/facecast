using System.Collections.Generic;

namespace Facecast
{
    /// <summary>
    /// Notes and questions already there when you arrive. Online, a note on the first board and a card on the
    /// question table say how they work; the offline preview also fills the boards and the box, so there's something
    /// to read. Every browser makes the same ones (same ids, version 1), so they merge as one, and anyone's change to
    /// one outranks them.
    /// </summary>
    public static class SampleNotes
    {
        /// <summary>The boards' titles, in order (SceneBuilder writes them on the title plates): a team retrospective.</summary>
        public static readonly string[] Titles = { "Continue", "Stop", "Invent", "Act" };

        public static IEnumerable<NoteData> For(bool offline)
        {
            yield return Note("sample-0", 0, 0.5f, 0.66f, -2f, 0, "Take a note from the shelf below, write on it, then stick it anywhere!", "the square");
            yield return Question("sample-q0", Notes.Table, 0.5f, 0.45f, 3f,
                "Write a question on a card from the box's tray and post it. Pull them out one at a time, read them out, then lay them here.");
            if (!offline) yield break;
            yield return Note("sample-1", 0, 0.18f, 0.7f, 4f, 2, "Friday demos by the fountain", "Robin");
            yield return Note("sample-2", 0, 0.8f, 0.62f, -5f, 3, "Pairing on the tricky bugs", "Kit");
            yield return Note("sample-3", 1, 0.3f, 0.6f, 3f, 1, "Meetings with no agenda", "Sol");
            yield return Note("sample-4", 1, 0.7f, 0.4f, -3f, 0, "Pushing changes late on a Friday", "Juno");
            yield return Note("sample-5", 2, 0.25f, 0.6f, -3f, 3, "Kite day on the lawn!", "Wren");
            yield return Note("sample-6", 2, 0.55f, 0.36f, 5f, 0, "A fountain that plays music", "Ivo");
            yield return Note("sample-7", 2, 0.78f, 0.7f, -6f, 2, "Benches that face the sunset", "Mae");
            yield return Note("sample-8", 3, 0.3f, 0.55f, 2f, 1, "Book the picnic for Saturday", "Pax");
            yield return Note("sample-9", 3, 0.68f, 0.42f, -4f, 0, "Fix the wobbly bench by the east path", "Kit");
            yield return Question("sample-q1", Notes.Box, 0.5f, 0.5f, 0f, "What should we build next?");
            yield return Question("sample-q2", Notes.Box, 0.5f, 0.5f, 0f, "Who brought the kites?");
            yield return Question("sample-q3", Notes.Box, 0.5f, 0.5f, 0f, "Can we meet here every Friday?");
        }

        static NoteData Note(string id, int board, float x, float y, float tilt, int color, string text, string by) => new()
        {
            id = id, board = board, x = x, y = y, tilt = tilt, color = color,
            text = text, by = by, v = 1, w = "sample",
        };

        static NoteData Question(string id, int place, float x, float y, float tilt, string text) => new()
        {
            id = id, board = place, question = true, x = x, y = y, tilt = tilt, text = text, v = 1, w = "sample",
        };
    }
}
