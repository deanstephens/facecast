using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace Facecast.EditorTools
{
    /// <summary>
    /// Shared generated assets: materials, one prefab per Blender model (the avatar with its <see cref="AvatarRig"/>,
    /// the scenery with colliders), the UI font and PanelSettings, the post-processing profile and the render
    /// pipeline settings.
    /// </summary>
    public static class FacecastAssets
    {
        public static void BuildAll()
        {
            foreach (var dir in new[] { Paths.MaterialsDir, Paths.PrefabsDir, Paths.ScenesDir, Paths.SettingsDir, Paths.FontAssetsDir, Paths.UIDir })
                EnsureFolder(dir);
            CreateMaterials();
            CreateFont();
            CreatePanelSettings();
            CreateVolumeProfile();
            ConfigureRenderPipeline();
            BuildPrefabs();
            AssetDatabase.SaveAssets();
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ------------------------------------------------------------------ materials

        public static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{Paths.MaterialsDir}/{name}.mat");

        static Material EnsureMaterial(string name, string shader)
        {
            string path = $"{Paths.MaterialsDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (!sh) throw new System.Exception($"Shader {shader} not found");
            if (!m)
            {
                m = new Material(sh) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = sh;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void CreateMaterials()
        {
            var avatar = EnsureMaterial("M_Avatar", "Facecast/Flat");
            avatar.SetColor("_TeamColor", Palette.Colors[0]);
            avatar.SetFloat("_Bands", 0.2f);
            avatar.SetFloat("_Wrap", 0.5f);
            avatar.SetColor("_Rim", new Color(1f, 0.97f, 0.9f, 0.12f));
            var face = EnsureMaterial("M_Face", "Facecast/Face");
            face.SetColor("_Tint", Palette.Colors[0]);
            var scenery = EnsureMaterial("M_Scenery", "Facecast/Flat");
            scenery.SetFloat("_Bands", 0.25f);
            scenery.enableInstancing = true;
            var ground = EnsureMaterial("M_Ground", "Facecast/Flat");
            ground.SetFloat("_Noise", 1f);
            ground.SetFloat("_Wrap", 0.5f);
            ground.SetFloat("_Bands", 0.15f);
            ground.SetFloat("_AmbientScale", 1.05f);
            // A clear late-afternoon sky.
            var sky = EnsureMaterial("M_Sky", "Skybox/Procedural");
            sky.SetFloat("_SunDisk", 2f);
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_SunSizeConvergence", 6f);
            sky.SetFloat("_AtmosphereThickness", 0.75f);
            sky.SetColor("_SkyTint", new Color(0.52f, 0.62f, 0.78f));
            sky.SetColor("_GroundColor", new Color(0.55f, 0.6f, 0.5f));
            sky.SetFloat("_Exposure", 1.25f);
        }

        // ------------------------------------------------------------------ prefabs

        static void BuildPrefabs()
        {
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Paths.ModelsDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = Path.GetFileNameWithoutExtension(path);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!model) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.name = name;
                if (name == "Avatar") SetUpAvatar(go);
                else SetUpScenery(name, go);
                PrefabUtility.SaveAsPrefabAsset(go, $"{Paths.PrefabsDir}/{name}.prefab");
                Object.DestroyImmediate(go);
                count++;
            }
            Debug.Log($"[FacecastAssets] {count} prefabs");
        }

        static void SetUpAvatar(GameObject go)
        {
            var rig = go.AddComponent<AvatarRig>();
            rig.Bind();
            if (!rig.FaceRenderer || !rig.Head || !rig.Eye) throw new System.Exception("Avatar.fbx is missing Face, Head or Eye (see Blender/MODELS.md)");
            rig.Face.SetParent(rig.Head, true);  // the screen pitches with the head
            foreach (var r in rig.BodyRenderers) Plain(r, Mat("M_Avatar"));
            Plain(rig.FaceRenderer, Mat("M_Face"));
            rig.FaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        static void Plain(Renderer r, Material m)
        {
            r.sharedMaterial = m;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>Materials, shadows and colliders for the square's scenery.</summary>
        static void SetUpScenery(string name, GameObject go)
        {
            var material = name == "PlazaGround" || name == "Hills" ? Mat("M_Ground") : Mat("M_Scenery");
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                Plain(r, material);
                if (name == "PlazaGround" || name == "Hills") r.shadowCastingMode = ShadowCastingMode.Off;
            }
            switch (name)
            {
                case "PlazaGround":
                case "Fountain":
                case "Bench":
                case "Lamp":
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                        mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    break;
                case "TreeRound":
                case "TreeTall":
                    var trunk = go.AddComponent<CapsuleCollider>();
                    trunk.center = new Vector3(0f, 1.5f, 0f);
                    trunk.radius = 0.3f;
                    trunk.height = 3f;
                    break;
                case "Planter":
                    var box = go.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 0.45f, 0f);
                    box.size = new Vector3(1.4f, 0.9f, 1.4f);
                    break;
                case "Hedge":
                    var hedge = go.AddComponent<BoxCollider>();
                    hedge.center = new Vector3(0f, 0.7f, 0f);
                    hedge.size = new Vector3(3f, 1.4f, 0.9f);
                    break;
            }
        }

        // ------------------------------------------------------------------ UI

        /// <summary>Fira Sans SemiBold for the names and hints; built players have no system fonts to fall back on.</summary>
        static void CreateFont()
        {
            const string fontName = "FiraSans-SemiBold";
            var path = $"{Paths.FontAssetsDir}/{fontName}-SDF.asset";
            if (!AssetDatabase.LoadAssetAtPath<FontAsset>(path))
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>($"{Paths.FontsDir}/{fontName}.ttf");
                var fa = FontAsset.CreateFontAsset(font, 64, 6, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
                fa.name = fontName + "-SDF";
                AssetDatabase.CreateAsset(fa, path);
                fa.atlasTextures[0].name = fontName + " Atlas";
                AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
                fa.material.name = fontName + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }
            // The stylesheet names the font by path; re-import it now that the font exists.
            foreach (var sheet in new[] { Paths.UIDir + "/Facecast.uss", Paths.ThemePath })
                if (File.Exists(sheet)) AssetDatabase.ImportAsset(sheet, ImportAssetOptions.ForceUpdate);
        }

        static void CreatePanelSettings()
        {
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(Paths.PanelSettingsPath);
            if (!ps)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, Paths.PanelSettingsPath);
            }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Paths.ThemePath);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            EditorUtility.SetDirty(ps);
        }

        // ------------------------------------------------------------------ rendering

        static void CreateVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Paths.VolumeProfilePath);
            if (profile) return;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, Paths.VolumeProfilePath);
            // Bloom only catches what's brighter than white, so with HDR (desktop) glowing screens and lamps bloom a
            // little; the web build renders without HDR and skips it.
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.4f);
            bloom.threshold.Override(1.0f);
            bloom.scatter.Override(0.6f);
            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.Override(TonemappingMode.Neutral);
            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(10f);
            color.contrast.Override(6f);
            color.postExposure.Override(0.1f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.5f);
            vignette.color.Override(new Color(0.08f, 0.07f, 0.1f));
            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);
        }

        /// <summary>
        /// MSAA for crisp low-poly edges, soft sun shadows over the square, no additional lights. The PC asset is the
        /// desktop quality level's (with HDR); the Mobile asset is the web build's (WebGL's default quality level),
        /// without HDR, because a browser's half-float render targets cost a lot of frame rate.
        /// </summary>
        static void ConfigureRenderPipeline()
        {
            foreach (var (name, pc) in new[] { ("PC_RPAsset", true), ("Mobile_RPAsset", false) })
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>($"Assets/Settings/{name}.asset");
                if (!asset) continue;
                var so = new SerializedObject(asset);
                Set(so, "m_MSAA", 4);
                Set(so, "m_SupportsHDR", pc ? 1 : 0);
                Set(so, "m_RenderScale", 1f);
                Set(so, "m_UpscalingFilter", 0);
                Set(so, "m_RequireDepthTexture", 0);
                Set(so, "m_RequireOpaqueTexture", 0);
                Set(so, "m_MainLightRenderingMode", 1);
                Set(so, "m_MainLightShadowsSupported", 1);
                Set(so, "m_MainLightShadowmapResolution", pc ? 4096 : 2048);
                Set(so, "m_ShadowDistance", pc ? 60f : 45f);
                Set(so, "m_ShadowCascadeCount", 2);
                Set(so, "m_Cascade2Split", 0.3f);
                Set(so, "m_SoftShadowsSupported", 1);
                Set(so, "m_AdditionalLightsRenderingMode", 0);
                Set(so, "m_AdditionalLightShadowsSupported", 0);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
        }

        static void Set(SerializedObject so, string prop, float v)
        {
            var p = so.FindProperty(prop);
            if (p == null)
            {
                Debug.LogWarning($"[FacecastAssets] {so.targetObject.name} has no {prop}");
                return;
            }
            switch (p.propertyType)
            {
                case SerializedPropertyType.Boolean: p.boolValue = v > 0.5f; break;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Enum: p.intValue = (int)v; break;
                default: p.floatValue = v; break;
            }
        }
    }
}
