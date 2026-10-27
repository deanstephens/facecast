using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Facecast.EditorTools
{
    /// <summary>
    /// Builds Plaza.unity, the only scene: the town square (fountain, ring of benches, lamps flanking the four paths,
    /// four noticeboards between them with planters either side, a hedge ring with trees and hills beyond, an
    /// invisible wall out on the lawn), a golden-hour sun and sky, and the systems: App, Room, the noticeboards, the
    /// player and camera, and the overlay.
    /// </summary>
    public static class SceneBuilder
    {
        /// <summary>Where you can walk: out onto the lawn, short of the outer trees.</summary>
        public const float BoundaryRadius = 30f;
        const float HedgeRadius = 15.8f;
        const float BoardRadius = 12.1f;
        /// <summary>The boards' roofs and title frames: coral, sky, mint, violet.</summary>
        static readonly Color[] BoardColors = { Palette.Colors[1], Palette.Colors[0], Palette.Colors[3], Palette.Colors[4] };
        static readonly float[] PathAngles = { 0f, 90f, 180f, 270f };

        [MenuItem("Facecast/Build/Build Everything", priority = 0)]
        public static void BuildEverything()
        {
            ProjectSetup.Apply();
            FacecastAssets.BuildAll();
            BuildScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[SceneBuilder] Built everything.");
        }

        /// <summary>Batch entry point: -executeMethod Facecast.EditorTools.SceneBuilder.BuildEverythingBatch</summary>
        public static void BuildEverythingBatch()
        {
            try
            {
                BuildEverything();
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static GameObject Prefab(string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{Paths.PrefabsDir}/{name}.prefab") ?? throw new System.Exception($"No prefab {name}: run Build Everything after building the Blender models");

        static GameObject Place(string prefab, Transform parent, Vector3 position, float yaw, float scale = 1f, bool collide = true)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab(prefab), parent);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * scale;
            if (!collide)
            {
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            foreach (Transform child in go.transform)
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, StaticEditorFlags.BatchingStatic);
            return go;
        }

        static Vector3 Polar(float r, float deg) => new(r * Mathf.Cos(deg * Mathf.Deg2Rad), 0f, r * Mathf.Sin(deg * Mathf.Deg2Rad));

        /// <summary>Yaw that turns a model's front (+Z) towards the square's centre from <paramref name="p"/>.</summary>
        static float FacingCentre(Vector3 p) => Mathf.Atan2(-p.x, -p.z) * Mathf.Rad2Deg;

        static bool NearPath(float deg, float halfWidthDeg)
        {
            foreach (var a in PathAngles)
                if (Mathf.Abs(Mathf.DeltaAngle(deg, a)) < halfWidthDeg) return true;
            return false;
        }

        [MenuItem("Facecast/Build/Scene Only", priority = 1)]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var boards = BuildWorld();
            BuildLighting();

            // Systems.
            var appGo = new GameObject("App");
            var app = appGo.AddComponent<App>();
            app.Bots = appGo.AddComponent<DemoBots>();
            var roomGo = new GameObject("Room");  // the page sends its messages to this name
            app.Room = roomGo.AddComponent<Room>();
            app.Room.AvatarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Paths.AvatarPrefab);
            var notes = new GameObject("Noticeboards").AddComponent<Noticeboards>();
            notes.Boards = boards;
            notes.NotePrefab = Prefab("StickyNote");
            notes.NoteFont = AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>(Paths.NoteFontPath);
            notes.InkMaterial = FacecastAssets.Mat("M_Ink");

            // You: a character controller the size of an avatar, and the camera (which circles the square until you join).
            var playerGo = new GameObject("Player");
            playerGo.transform.position = App.SpawnPose(0.75f).Position;
            var cc = playerGo.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.35f;
            cc.slopeLimit = 50f;
            cc.skinWidth = 0.03f;
            cc.minMoveDistance = 0f;  // every step counts, however high the frame rate
            var player = playerGo.AddComponent<LocalPlayer>();
            player.AvatarPrefab = app.Room.AvatarPrefab;
            app.Player = player;
            playerGo.AddComponent<NoteHand>();

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.fieldOfView = 72f;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;
            camData.renderShadows = true;
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 4.2f, -15.5f), Quaternion.Euler(10f, 0f, 0f));
            player.Cam = cam;

            var volGo = new GameObject("Post Processing");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Paths.VolumeProfilePath);

            var uiGo = new GameObject("UI");
            var doc = uiGo.AddComponent<UIDocument>();
            doc.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(Paths.PanelSettingsPath);
            uiGo.AddComponent<Overlay>();

            EditorSceneManager.SaveScene(scene, Paths.GameScene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Paths.GameScene, true) };
            Debug.Log("[SceneBuilder] Saved " + Paths.GameScene);
        }

        static Noticeboard[] BuildWorld()
        {
            var world = new GameObject("World").transform;
            Place("PlazaGround", world, Vector3.zero, 0f);
            Place("Hills", world, Vector3.zero, 0f, 1f, false);
            Place("Fountain", world, Vector3.zero, 0f);

            // Eight benches round the fountain, facing it, between the paths.
            for (int i = 0; i < 8; i++)
            {
                var p = Polar(9.4f, 22.5f + 45f * i);
                Place("Bench", world, p, FacingCentre(p));
            }
            // A lamp each side of every path where it leaves the square; planters between the paths.
            foreach (var a in PathAngles)
                foreach (var side in new[] { -7.5f, 7.5f })
                    Place("Lamp", world, Polar(12.2f, a + side), 0f);
            // A noticeboard between each pair of paths, facing the fountain, with a planter either side.
            var boards = new Noticeboard[Notes.BoardCount];
            for (int i = 0; i < boards.Length; i++)
            {
                float a = 45f + 90f * i;
                var p = Polar(BoardRadius, a);
                var board = Place("NoticeBoard", world, p, FacingCentre(p)).GetComponent<Noticeboard>();
                board.Index = i;
                board.Title = SampleNotes.Titles[i];
                board.Accent = BoardColors[i];
                board.name = "Noticeboard " + board.Title;
                PrefabUtility.RecordPrefabInstancePropertyModifications(board);
                boards[i] = board;
                foreach (var side in new[] { -11.5f, 11.5f })
                {
                    var q = Polar(11.9f, a + side);
                    Place("Planter", world, q, FacingCentre(q));
                }
            }

            // The hedge ring, open where the paths run out to the lawn.
            const float step = 10.6f;
            for (float a = 0f; a < 360f - 0.01f; a += step)
            {
                if (NearPath(a, 7.5f)) continue;
                var p = Polar(HedgeRadius, a);
                var go = Place("Hedge", world, p, 0f);
                go.transform.rotation = Quaternion.LookRotation(p.normalized);
            }

            // Trees on the lawn (solid trunks) and a wood beyond the wall (just scenery), seeded so rebuilds match.
            var rng = new System.Random(42);
            float R(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
            var trees = new List<Vector3>();
            void Trees(int want, float r0, float r1, float spacing, bool collide)
            {
                for (int tries = 0, placed = 0; placed < want && tries < want * 40; tries++)
                {
                    float a = R(0f, 360f), r = R(r0, r1);
                    if (NearPath(a, Mathf.Rad2Deg * 2.6f / r)) continue;  // keep the paths clear out to the wall
                    var p = Polar(r, a);
                    if (trees.Exists(t => (t - p).sqrMagnitude < spacing * spacing)) continue;
                    trees.Add(p);
                    Place(rng.NextDouble() < 0.55 ? "TreeRound" : "TreeTall", world, p, R(0f, 360f), R(0.85f, 1.3f), collide);
                    placed++;
                }
            }
            Trees(24, 18.5f, BoundaryRadius - 1.5f, 4.2f, true);
            Trees(70, BoundaryRadius + 2f, 64f, 4.5f, false);

            // The invisible wall.
            var wall = new GameObject("Boundary").transform;
            wall.SetParent(world, false);
            const int segments = 48;
            for (int i = 0; i < segments; i++)
            {
                float a = 360f * i / segments;
                var seg = new GameObject("Wall " + i).transform;
                seg.SetParent(wall, false);
                var p = Polar(BoundaryRadius + 0.25f, a);
                seg.SetPositionAndRotation(p + Vector3.up * 2f, Quaternion.LookRotation(p.normalized));
                var box = seg.gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(2f * Mathf.PI * BoundaryRadius / segments + 0.6f, 6f, 0.5f);
            }
            return boards;
        }

        static void BuildLighting()
        {
            // A warm late-afternoon sun, low enough for long soft shadows across the paving.
            var sunGo = new GameObject("Sun");
            sunGo.transform.rotation = Quaternion.Euler(36f, -38f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.92f, 0.8f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.7f;
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.4f;
            RenderSettings.sun = sun;
            RenderSettings.skybox = FacecastAssets.Mat("M_Sky");
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.54f, 0.6f);
            // Haze that softens the hills into the sky.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0075f;
            RenderSettings.fogColor = new Color(0.74f, 0.82f, 0.9f);
        }
    }
}
