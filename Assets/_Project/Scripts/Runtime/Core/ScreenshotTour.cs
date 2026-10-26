using System.Collections;
using System.IO;
using UnityEngine;

namespace Facecast
{
    /// <summary>
    /// Unattended screenshot tour, to check the look. Launch a desktop build (or the Editor) with
    /// <c>-autocapture &lt;dir&gt;</c>: it photographs the square from above, then poses three demo bots in front of
    /// you and saves your first-person view, a close-up of a face screen and a wider shot with their names, then quits.
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

            Look(cam, you + new Vector3(2.2f, 2.6f, -2.4f), new Vector3(0f, 1.3f, -4.2f));
            yield return Shot("group");

            Debug.Log("[ScreenshotTour] Done");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
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
