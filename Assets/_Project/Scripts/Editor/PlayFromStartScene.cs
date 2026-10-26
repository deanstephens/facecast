using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Facecast.EditorTools
{
    /// <summary>
    /// Pressing Play starts the game from the first scene in Build Settings, whichever scene is open in the
    /// Editor, and returns to that scene afterwards. When the Editor starts on an empty Untitled scene it opens that
    /// scene too, so the game is what you see. Turn both off with Facecast ▸ Play From Start Scene.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayFromStartScene
    {
        const string MenuPath = "Facecast/Play From Start Scene";
        const string PrefKey = "Facecast.PlayFromStartScene";
        const string OpenedKey = "Facecast.PlayFromStartScene.OpenedOnStartup";

        static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        static EditorBuildSettingsScene StartScene => EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled);

        static PlayFromStartScene()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            if (!Application.isBatchMode && !SessionState.GetBool(OpenedKey, false))
                EditorApplication.delayCall += OpenStartSceneOnStartup;
        }

        [MenuItem(MenuPath, priority = 100)]
        static void Toggle() => Enabled = !Enabled;

        [MenuItem(MenuPath, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
                EditorSceneManager.playModeStartScene = ChooseStartScene();
        }

        static SceneAsset ChooseStartScene()
        {
            var start = StartScene;
            if (!Enabled || start == null) return null;

            // The Test Runner enters Play mode from its own bootstrap scene, which holds the test controller.
            var active = SceneManager.GetActiveScene();
            if (active.name.StartsWith("InitTestScene")) return null;

            // Already open: play it as usual, unsaved edits included.
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).path == start.path) return null;

            return AssetDatabase.LoadAssetAtPath<SceneAsset>(start.path);
        }

        /// <summary>
        /// The Editor starts on a new Untitled scene when it has none to restore (a fresh clone, or after a batch-mode
        /// build or test run). Open the game instead, once per session, and only if that scene is still untouched.
        /// </summary>
        static void OpenStartSceneOnStartup()
        {
            SessionState.SetBool(OpenedKey, true);
            var start = StartScene;
            if (!Enabled || start == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var active = SceneManager.GetActiveScene();
            if (SceneManager.sceneCount != 1 || !string.IsNullOrEmpty(active.path) || active.isDirty) return;
            EditorSceneManager.OpenScene(start.path);
        }
    }
}
