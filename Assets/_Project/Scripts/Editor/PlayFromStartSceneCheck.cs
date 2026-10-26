using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Facecast.EditorTools
{
    /// <summary>
    /// Checks for <see cref="PlayFromStartScene"/>. Run: opens an empty scene, enters Play mode and confirms the game
    /// scene is what actually plays.
    /// Run: Unity -batchmode -projectPath . -executeMethod Facecast.EditorTools.PlayFromStartSceneCheck.Run (no -quit)
    /// </summary>
    public static class PlayFromStartSceneCheck
    {
        const string Key = "Facecast.PlayFromStartSceneCheck";

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetInt(Key, 1);
            // With domain reload on, Hook re-registers this after the reload; with it off, this registration survives.
            EditorApplication.update -= Watch;
            EditorApplication.update += Watch;
            Debug.Log($"[PlayFromStartSceneCheck] Entering Play mode from '{SceneManager.GetActiveScene().name}' (empty)");
            EditorApplication.EnterPlaymode();
        }

        /// <summary>
        /// Startup check, for a windowed Editor (the startup hook skips batch mode) on a project with no scene to
        /// restore: confirms <see cref="PlayFromStartScene"/> has opened the game scene once the Editor settles.
        /// Run: Unity -projectPath . -executeMethod Facecast.EditorTools.PlayFromStartSceneCheck.Startup
        /// </summary>
        public static void Startup()
        {
            double until = EditorApplication.timeSinceStartup + 5.0;
            EditorApplication.update += Wait;

            void Wait()
            {
                if (EditorApplication.timeSinceStartup < until) return;
                EditorApplication.update -= Wait;
                var scene = SceneManager.GetActiveScene();
                bool ok = scene.path == Paths.GameScene;
                Debug.Log($"[PlayFromStartSceneCheck] Editor opened on '{scene.path}': {(ok ? "PASS" : "FAIL")}");
                EditorApplication.Exit(ok ? 0 : 1);
            }
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            if (SessionState.GetInt(Key, 0) == 0) return;
            EditorApplication.update -= Watch;
            EditorApplication.update += Watch;
        }

        static int _frames;

        static void Watch()
        {
            if (!EditorApplication.isPlaying) return;
            if (++_frames < 30) return;
            EditorApplication.update -= Watch;
            SessionState.EraseInt(Key);
            var scene = SceneManager.GetActiveScene();
            bool ok = scene.path == Paths.GameScene && Object.FindAnyObjectByType<App>();
            Debug.Log($"[PlayFromStartSceneCheck] Playing '{scene.path}': {(ok ? "PASS" : "FAIL")}");
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
