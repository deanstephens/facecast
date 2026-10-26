using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace Facecast.EditorTools
{
    /// <summary>Player settings: names, linear colour, and the web build's page template and graphics API.</summary>
    public static class ProjectSetup
    {
        [MenuItem("Facecast/Build/Project Settings", priority = 20)]
        public static void Apply()
        {
            PlayerSettings.companyName = "Facecast";
            PlayerSettings.productName = "Facecast";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            // Keep sending your pose and showing everyone while the tab or window is in the background.
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.facecast.app");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.WebGL, "com.facecast.app");
            // The page shows its own join card while the game loads behind it.
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;

            // The web page around the game (Assets/WebGLTemplates/Facecast) and WebGL 2, which the face screens'
            // video textures (Plugins/WebGL/Facecast.jslib) are written for.
            PlayerSettings.WebGL.template = "PROJECT:Facecast";
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            AssetDatabase.SaveAssets();
        }
    }
}
