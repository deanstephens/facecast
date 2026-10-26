using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Facecast.EditorTools
{
    /// <summary>
    /// Player builds. Batch: -executeMethod Facecast.EditorTools.PlayerBuilds.BuildWeb -buildPath &lt;dir&gt; (also
    /// BuildMac). Output defaults to Builds/&lt;Target&gt;/. The web build is the real app; Tools/deploy_pages.sh
    /// publishes it to GitHub Pages and Tools/serve_web.py serves it locally. Desktop builds play offline with demo
    /// bots and run the -autocapture screenshot tour.
    /// </summary>
    public static class PlayerBuilds
    {
        /// <summary>
        /// The web build, gzip-compressed with decompression fallback: GitHub Pages can't send the
        /// Content-Encoding header Unity's compressed files need, so the loader unpacks them itself.
        /// </summary>
        [MenuItem("Facecast/Player Builds/Web")]
        public static void BuildWeb()
        {
            ProjectSetup.Apply();
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            Build(BuildTarget.WebGL, null);
        }

        [MenuItem("Facecast/Player Builds/macOS")]
        public static void BuildMac()
        {
            // Universal binary by default so it runs on Intel and Apple Silicon; "-macArch x64" or "arm64" for one.
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = ArgValue("-macArch") switch
            {
                "x64" => UnityEditor.Build.OSArchitecture.x64,
                "arm64" => UnityEditor.Build.OSArchitecture.ARM64,
                _ => UnityEditor.Build.OSArchitecture.x64ARM64,
            };
            Build(BuildTarget.StandaloneOSX, "Facecast.app");
        }

        static void Build(BuildTarget target, string fileName)
        {
            var dir = ArgValue("-buildPath") ?? Path.Combine("Builds", target.ToString());
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                target = target,
                locationPathName = string.IsNullOrEmpty(fileName) ? dir : Path.Combine(dir, fileName),
                options = Application.isBatchMode && ArgValue("-development") != null ? BuildOptions.Development : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[PlayerBuilds] {target}: {report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalSize / (1024 * 1024)} MB → {options.locationPathName}");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            if (i < 0) return null;
            return i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : "";
        }
    }
}
