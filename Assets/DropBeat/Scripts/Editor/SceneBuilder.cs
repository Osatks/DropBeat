using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DropBeat.EditorTools
{
    /// <summary>Builds Assets/DropBeat/Scenes/Game.unity from scratch: materials, post FX, camera, ball, systems.</summary>
    public static class SceneBuilder
    {
        private const string Root = "Assets/DropBeat";
        public const string ScenePath = Root + "/Scenes/Game.unity";

        [MenuItem("DropBeat/Build Game Scene", priority = 2)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Ensure(Root + "/Materials");
            Ensure(Root + "/Scenes");
            Ensure(Root + "/Settings");

            var library = AssetDatabase.LoadAssetAtPath<SongLibrary>(SongTools.LibraryPath);
            if (library == null || library.PlayableCount == 0)
            {
                SongTools.AnalyzeAllSongs();
                library = AssetDatabase.LoadAssetAtPath<SongLibrary>(SongTools.LibraryPath);
            }

            Material neon = MakeMaterial("Neon", "DropBeat/Neon", m => m.SetColor("_Color", new Color(0f, 2f, 2.4f)));
            Material additive = MakeMaterial("Additive", "DropBeat/NeonAdditive", m => m.SetFloat("_Soft", 0f));
            Material soft = MakeMaterial("SoftAdditive", "DropBeat/NeonAdditive", m =>
            {
                m.SetFloat("_Soft", 1f);
                m.SetFloat("_SoftPower", 2f);
            });
            Material chrome = MakeMaterial("Chrome", "DropBeat/Chrome", m => { });
            Material ring = MakeMaterial("Ring", "DropBeat/Ring", m => { });
            Material ring3d = MakeMaterial("Ring3D", "DropBeat/Ring3D", m => { });
            Material backdrop = MakeMaterial("Backdrop", "DropBeat/Backdrop", m =>
            {
                m.SetVector("_SunCenter", new Vector4(8f, 11f, 0f, 0f)); // off-centre, clear of the combo counter
                m.SetFloat("_SunRadius", 13f);
                m.SetFloat("_SunIntensity", 0.24f);
            });
            VolumeProfile profile = MakePostFx();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Camera
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.015f, 0.008f, 0.04f, 1f);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 250f;
            cam.allowHDR = true;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing; // cheap on phones
            var rig = camGo.AddComponent<CameraRig>();
            camGo.transform.position = new Vector3(0f, 4f, -13f);

            // Post processing
            var volGo = new GameObject("Post FX");
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            // Systems
            var game = new GameObject("Game");
            var music = game.AddComponent<AudioSource>();
            music.playOnAwake = false;
            var clock = game.AddComponent<SongClock>();
            SetRef(clock, "music", music);
            var sfx = game.AddComponent<SfxPlayer>();
            var ui = game.AddComponent<GameUI>();
            var controller = game.AddComponent<GameController>();

            var bgGo = new GameObject("Background");
            var bg = bgGo.AddComponent<BackgroundFX>();

            var rings = new GameObject("Rings");

            // Ball: Ball -> Squash (stretch) -> Body (chrome sphere that spins)
            var ballGo = new GameObject("Ball");
            var ball = ballGo.AddComponent<BallView>();
            var squash = new GameObject("Squash");
            squash.transform.SetParent(ballGo.transform, false);
            squash.transform.localScale = Vector3.one * 0.8f;
            var body = new GameObject("Body");
            body.transform.SetParent(squash.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = SavedMesh("BallSphere", ProceduralMeshes.Sphere());
            var bodyRenderer = body.AddComponent<MeshRenderer>();
            bodyRenderer.sharedMaterial = chrome;
            bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;
            bodyRenderer.receiveShadows = false;

            var glow = new GameObject("Glow");
            glow.transform.SetParent(ballGo.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0f, 0.4f);
            glow.AddComponent<MeshFilter>().sharedMesh = SavedQuad();
            var glowRenderer = glow.AddComponent<MeshRenderer>();
            glowRenderer.sharedMaterial = soft;
            glowRenderer.shadowCastingMode = ShadowCastingMode.Off;

            var trail = ballGo.AddComponent<TrailRenderer>();
            trail.sharedMaterial = soft;
            trail.time = 0.32f;
            trail.minVertexDistance = 0.03f;
            trail.widthMultiplier = 0.42f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.numCapVertices = 4;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.alignment = LineAlignment.View;

            SetRef(ball, "squash", squash.transform);
            SetRef(ball, "body", bodyRenderer);
            SetRef(ball, "glow", glowRenderer);
            SetRef(ball, "trail", trail);

            // Wire the controller.
            var so = new SerializedObject(controller);
            so.FindProperty("library").objectReferenceValue = library;
            so.FindProperty("clock").objectReferenceValue = clock;
            so.FindProperty("sfx").objectReferenceValue = sfx;
            so.FindProperty("cameraRig").objectReferenceValue = rig;
            so.FindProperty("ball").objectReferenceValue = ball;
            so.FindProperty("background").objectReferenceValue = bg;
            so.FindProperty("ui").objectReferenceValue = ui;
            so.FindProperty("ringRoot").objectReferenceValue = rings.transform;
            so.FindProperty("visuals.neon").objectReferenceValue = neon;
            so.FindProperty("visuals.additive").objectReferenceValue = additive;
            so.FindProperty("visuals.softAdditive").objectReferenceValue = soft;
            so.FindProperty("visuals.backdrop").objectReferenceValue = backdrop;
            so.FindProperty("visuals.ring").objectReferenceValue = ring;
            so.FindProperty("visuals.ring3d").objectReferenceValue = ring3d;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.runInBackground = true;
            PlayerSettings.productName = "DropBeat";
            ConfigureMobile();
            AssetDatabase.SaveAssets();
            Debug.Log("[DropBeat] Game scene built: " + ScenePath);
        }

        /// <summary>Portrait phone game: portrait orientation, lower-latency audio buffer.</summary>
        private static void ConfigureMobile()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = true;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            // "Good latency" DSP buffer (512): taps and hit sounds feel tighter than the 1024 default.
            var audioManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");
            if (audioManager != null && audioManager.Length > 0)
            {
                var so = new SerializedObject(audioManager[0]);
                var buffer = so.FindProperty("m_DSPBufferSize");
                if (buffer != null && buffer.intValue != 512)
                {
                    buffer.intValue = 512;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private static Mesh SavedMesh(string name, Mesh source)
        {
            string path = Root + "/Materials/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            mesh = Object.Instantiate(source);
            mesh.name = name;
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Mesh SavedQuad()
        {
            string path = Root + "/Materials/GlowQuad.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            mesh = Object.Instantiate(ProceduralMeshes.Quad());
            mesh.name = "GlowQuad";
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Material MakeMaterial(string name, string shaderName, System.Action<Material> setup)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.InvalidOperationException("Shader not found: " + shaderName);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            setup(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static VolumeProfile MakePostFx()
        {
            string path = Root + "/Settings/PostFX.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            Bloom bloom = Get<Bloom>(profile);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(1.1f);
            bloom.scatter.Override(0.72f);
            bloom.highQualityFiltering.Override(false); // mobile: bilinear upsampling
            bloom.downscale.Override(BloomDownscaleMode.Half);

            Vignette vignette = Get<Vignette>(profile);
            vignette.intensity.Override(0.32f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(new Color(0.05f, 0f, 0.1f));

            // Chromatic aberration costs a full-screen pass on phones for very little; keep it off.
            if (profile.TryGet(out ChromaticAberration ca)) ca.active = false;

            Tonemapping tone = Get<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.None);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
            {
                component = profile.Add<T>(true);
                component.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            component.active = true;
            return component;
        }

        private static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Ensure(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Ensure(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
