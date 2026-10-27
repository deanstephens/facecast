using System.Collections;
using System.IO;
using UnityEngine;

namespace Facecast
{
    /// <summary>
    /// Unattended screenshot tour, to check the look. Launch a desktop build (or the Editor) with
    /// <c>-autocapture &lt;dir&gt;</c>: it photographs the square from above, then poses three demo bots in front of
    /// you and saves your first-person view, a close-up of a face screen and a wider shot with their names (one of
    /// them carrying a note); then it writes a note at a noticeboard, holds it, holds it up to the board and sticks
    /// it; then it starts writing a question, pulls one out of the question box, reads it and lays it on the table;
    /// and quits.
    /// </summary>
    public sealed class ScreenshotTour : MonoBehaviour
    {
        string _dir;
        int _shot;

        public void Begin(string dir)
        {
            _dir = dir;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            var app = App.Instance;
            while (!app.Joined) yield return null;
            var player = app.Player;
            var cam = player.Cam;
            yield return new WaitForSeconds(2.5f);

            player.InputEnabled = false;
            cam.transform.SetParent(null, true);
            Look(cam, new Vector3(12.5f, 6.5f, -13.5f), new Vector3(0f, 1f, 0f));
            yield return Shot("overview");

            // Three bots in a half circle in front of you, on the far side of the paving from the fountain.
            var bots = app.Bots;
            var you = new Vector3(0f, 0f, -7.6f);
            player.Teleport(new AvatarPose(you, 0f, 6f));
            if (bots.Count >= 3)
            {
                bots.Frozen = true;
                for (int i = 0; i < 3; i++)
                {
                    float angle = Mathf.Deg2Rad * (-90f + (i - 1) * 24f);
                    var at = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 4.5f;
                    var to = you - at;
                    bots.Place(i, 4.5f, angle, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                }
                yield return new WaitForSeconds(1f);  // let the poses play through
            }

            cam.transform.SetParent(player.transform, false);
            cam.transform.localPosition = new Vector3(0f, LocalPlayer.EyeHeight, 0f);
            cam.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
            yield return Shot("first_person");

            if (bots.Count >= 3)
            {
                var face = bots.AvatarOf(1).Rig.Face;
                cam.transform.SetParent(null, true);
                Look(cam, face.position + face.forward * 0.85f + Vector3.up * 0.04f, face.position);
                yield return Shot("face_closeup");
            }

            var boards = Noticeboards.Instance;
            if (bots.Count >= 3 && boards)
            {
                var carrier = bots.AvatarOf(1).Id;
                boards.Receive(new[] { new NoteData { id = "tour-carried", holder = carrier, color = 1, text = "Hello!", by = "Kit", v = 1000, w = carrier } }, carrier);
            }
            yield return null;
            Look(cam, you + new Vector3(2.2f, 2.6f, -2.4f), new Vector3(0f, 1.3f, -4.2f));
            yield return Shot("group");

            // At the first noticeboard: write a note, hold it up where it'll go, stick it.
            var hand = NoteHand.Instance;
            if (boards && hand)
            {
                var board = boards.Boards[0];
                var spot = new Vector2(0.63f, 0.4f);
                cam.transform.SetParent(player.transform, false);
                cam.transform.localPosition = new Vector3(0f, LocalPlayer.EyeHeight, 0f);
                var stand = board.ReadingPose(1.3f).Position;
                player.Teleport(Facing(stand, board.WorldPoint(new Vector2(0.5f, 0.45f))));
                hand.Take(2);
                yield return null;
                var overlay = Overlay.Instance;
                if (overlay && overlay.NoteEditorOpen)
                {
                    overlay.NoteDraft = "Picnic on the lawn, Saturday at noon!";
                    yield return Shot("note_editor");
                    overlay.FinishNote(true);
                }
                hand.AlwaysAim = true;
                player.Teleport(Facing(stand, new Vector3(0f, 1.2f, 0f)));  // turned away, towards the fountain
                yield return null;
                yield return Shot("note_in_hand");
                player.Teleport(Facing(stand, board.WorldPoint(spot)));
                yield return null;
                yield return Shot("noticeboard");
                hand.Stick(0, spot);
                hand.AlwaysAim = false;
                yield return null;
                cam.transform.SetParent(null, true);
                var side = board.WorldPoint(new Vector2(-0.35f, 0.25f), 2.1f);
                Look(cam, side, board.WorldPoint(new Vector2(0.55f, 0.45f)));
                yield return Shot("board_wide");
            }

            // The question corner: pull a question out of the box, read it, lay it on the table.
            if (boards && hand && boards.QuestionBox && boards.QuestionTable)
            {
                var box = boards.QuestionBox;
                var table = boards.QuestionTable;
                cam.transform.SetParent(player.transform, false);
                cam.transform.localPosition = new Vector3(0f, LocalPlayer.EyeHeight, 0f);
                var stand = table.ReadingPose(0.95f).Position;
                player.Teleport(Facing(stand, box.transform.position + Vector3.up * 1f));
                hand.Ask();
                yield return null;
                var overlay = Overlay.Instance;
                if (overlay && overlay.NoteEditorOpen)
                {
                    overlay.NoteDraft = "Could we have a picnic here next Saturday?";
                    yield return Shot("question_editor");
                    overlay.FinishNote(false);
                }
                hand.AlwaysAim = true;
                hand.Pull();
                yield return null;
                yield return Shot("question_in_hand");
                var spot = new Vector2(0.64f, 0.5f);
                player.Teleport(Facing(stand, table.WorldPoint(spot)));
                yield return null;
                hand.Put(Notes.Table, spot);
                hand.AlwaysAim = false;
                yield return null;
                cam.transform.SetParent(null, true);
                var corner = (box.transform.position + table.transform.position) * 0.5f;
                Look(cam, corner + box.transform.forward * 2.4f + box.transform.right * 0.5f + Vector3.up * 1.9f, corner + Vector3.up * 0.75f);
                yield return Shot("question_corner");
            }

            Debug.Log("[ScreenshotTour] Done");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Standing at <paramref name="feet"/>, eyes on <paramref name="at"/>.</summary>
        static AvatarPose Facing(Vector3 feet, Vector3 at)
        {
            var d = at - (feet + Vector3.up * LocalPlayer.EyeHeight);
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            return new AvatarPose(feet, Mathf.Repeat(yaw, 360f), pitch);
        }

        static void Look(Camera cam, Vector3 from, Vector3 at) =>
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));

        IEnumerator Shot(string name)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            Directory.CreateDirectory(_dir);
            var path = Path.Combine(_dir, $"{++_shot:00}_{name}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[ScreenshotTour] " + path);
            yield return null;
            yield return null;
        }
    }
}
