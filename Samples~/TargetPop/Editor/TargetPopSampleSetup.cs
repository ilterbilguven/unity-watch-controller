using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace AppleWatchController.Samples.TargetPop.Editor
{
    /// <summary>Creates or opens the project's Watch-controlled Target Pop demo scene.</summary>
    /// <remarks>This Editor utility authors the Unity game scene; it does not generate the Watch controller UI.</remarks>
    public static class TargetPopSampleSetup
    {
        /// <summary>The default Assets-relative path of the demo scene.</summary>
        public const string ScenePath = "Assets/Scenes/WatchControllerDemo.unity";
        private const string WatchLabSourceGuid = "7098a3545738473ab6dc4dda4be2cf43";

        [MenuItem("Tools/AppleWatch/Open Game Demo")]
        private static void OpenFromMenu()
        {
            var selected = EditorUtility.OpenFolderPanel("Select imported Target Pop folder", "Assets", "");
            if (string.IsNullOrEmpty(selected)) return;
            var source = EditorUtility.OpenFilePanel("Select authored Watch controller (.watch.uxml)", "Assets", "uxml");
            if (string.IsNullOrEmpty(source)) return;
            CreateWithWatchSource(FileUtil.GetProjectRelativePath(selected), ScenePath, FileUtil.GetProjectRelativePath(source));
        }

        /// <summary>Opens an existing scene or creates and saves a new demo with its supporting assets.</summary>
        /// <param name="sampleRoot">The explicit imported Target Pop folder under Assets.</param>
        /// <param name="scenePath">A non-null Assets-relative .unity path with no parent-directory segments.</param>
        /// <param name="makeStartupScene">Whether to place this scene first and enabled in build settings while retaining other scenes.</param>
        /// <returns>Status and ScenePath fields; a newly created scene also reports its target count.</returns>
        /// <remarks>
        /// Call on Unity's main thread outside Play Mode, with no unsaved changes in any open scene.
        /// Opens the requested scene in Single mode. Existing scene files are opened without rewriting their contents.
        /// New scenes require the demo HUD, theme, font, and URP shaders already in the project.
        /// Creation writes a sibling scene-assets folder with materials, font data, text settings, and panel settings.
        /// It saves assets and the scene, selects the demo root, and can change build settings.
        /// Reusing an existing scene does not check that it contains the expected demo components.
        /// Resolves the imported Watch Lab source by its preserved asset GUID, independent of its folder or sample version.
        /// Other authored Watch sources do not affect that selection. Use CreateWithWatchSource to choose another source.
        /// </remarks>
        /// <exception cref="ArgumentException">The path is not an Assets-relative .unity path or contains '..'.</exception>
        /// <exception cref="InvalidOperationException">Play Mode is active or changing, a scene is dirty, or required demo resources are unavailable.</exception>
        public static object Create(string sampleRoot, string scenePath, bool makeStartupScene = false)
            => CreateWithWatchSource(sampleRoot, scenePath, AssetDatabase.GUIDToAssetPath(WatchLabSourceGuid), makeStartupScene);

        /// <summary>Creates or opens Target Pop with an explicitly selected authored Watch source.</summary>
        /// <param name="sampleRoot">The explicit imported Target Pop folder under Assets.</param>
        /// <param name="scenePath">An Assets-relative .unity path; existing scenes are opened without overwriting them.</param>
        /// <param name="watchUiPath">An existing Assets-relative .watch.uxml source, independent of the sample folders.</param>
        /// <param name="makeStartupScene">Whether to explicitly place this scene first in build settings.</param>
        /// <returns>The same status/scene result as Create.</returns>
        /// <remarks>Requires a configured URP project and clean scenes. Validates the source before writing any scene or assets.</remarks>
        /// <exception cref="ArgumentException">The sample root or scene path is invalid.</exception>
        /// <exception cref="InvalidOperationException">Play Mode, dirty scenes, missing URP, or an invalid/missing Watch source prevents setup.</exception>
        public static object CreateWithWatchSource(string sampleRoot, string scenePath, string watchUiPath, bool makeStartupScene = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before creating or opening the demo.");
            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene changes before opening the demo.");
            sampleRoot = ValidateSampleRoot(sampleRoot);
            if (string.IsNullOrEmpty(scenePath)) throw new ArgumentException("Use an Assets-relative .unity scene path.");
            scenePath = scenePath.Replace('\\', '/');
            if (!scenePath.StartsWith("Assets/", StringComparison.Ordinal) || scenePath.Contains("..")
                || !scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use an Assets-relative .unity scene path.");
            RequireUrp();
            ValidateWatchSource(watchUiPath);
            if (File.Exists(scenePath))
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                if (makeStartupScene) SetStartup(scenePath);
                return new { Status = "opened", ScenePath = scenePath };
            }

            var folder = Path.GetDirectoryName(scenePath).Replace('\\', '/');
            var assets = folder + "/" + Path.GetFileNameWithoutExtension(scenePath) + "Assets";
            PreflightOutputs(new[] { scenePath }.Concat(new[]
            {
                "Floor.mat", "Grid.mat", "Edge.mat", "Cyan.mat", "CyanGlow.mat", "Amber.mat", "AmberGlow.mat", "Violet.mat", "Green.mat", "Touch.mat", "DemoFont.asset", "TextSettings.asset", "PanelSettings.asset"
            }.Select(name => assets + "/" + name)).ToArray());

            var HudPath = sampleRoot + "/Demo/WatchDemoHud.uxml";
            AssetDatabase.ImportAsset(HudPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(sampleRoot + "/Demo/Inter.ttf", ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(sampleRoot + "/Demo/WatchDemoTheme.tss", ImportAssetOptions.ForceSynchronousImport);
            if (AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudPath) == null)
                throw new InvalidOperationException("The demo HUD source is missing.");
            if (AssetDatabase.LoadAssetAtPath<Font>(sampleRoot + "/Demo/Inter.ttf") == null ||
                AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(sampleRoot + "/Demo/WatchDemoTheme.tss") == null)
                throw new InvalidOperationException("The imported Target Pop font or theme is missing.");
            EnsureFolder(assets);

            var floorMaterial = Material(assets, "Floor", new Color(0.025f, 0.055f, 0.085f), false);
            var gridMaterial = Material(assets, "Grid", new Color(0.055f, 0.12f, 0.17f), true);
            var edgeMaterial = Material(assets, "Edge", new Color(0.08f, 0.29f, 0.37f), true);
            var cyan = Material(assets, "Cyan", new Color(0.12f, 0.82f, 0.96f), false);
            var cyanGlow = Material(assets, "CyanGlow", new Color(0.25f, 0.93f, 1f), true);
            var amber = Material(assets, "Amber", new Color(1f, 0.57f, 0.16f), false);
            var amberGlow = Material(assets, "AmberGlow", new Color(1f, 0.74f, 0.25f), true);
            var violet = Material(assets, "Violet", new Color(0.64f, 0.39f, 0.95f), false);
            var green = Material(assets, "Green", new Color(0.22f, 0.88f, 0.55f), false);
            var pink = Material(assets, "Touch", new Color(0.94f, 0.32f, 0.64f), true);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.48f, 0.57f, 0.7f);
            RenderSettings.fog = false;
            var root = new GameObject("Watch Demo");
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(root.transform);
            var camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0, 9, -9);
            camera.transform.LookAt(Vector3.zero);
            camera.orthographic = true;
            camera.orthographicSize = 6.1f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 40f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.012f, 0.025f, 0.046f);
            var lightObject = new GameObject("Key Light", typeof(Light));
            lightObject.transform.SetParent(root.transform);
            lightObject.transform.rotation = Quaternion.Euler(45, -30, 0);
            var light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.shadows = LightShadows.Soft;

            var arena = new GameObject("Arena").transform;
            arena.SetParent(root.transform);
            Primitive("Floor", PrimitiveType.Cube, arena, new Vector3(0, -0.2f, 0), new Vector3(8.8f, 0.3f, 8.8f), floorMaterial);
            for (var i = -4; i <= 4; i++)
            {
                Primitive("Grid X " + i, PrimitiveType.Cube, arena, new Vector3(i, -0.035f, 0), new Vector3(0.018f, 0.02f, 8.5f), gridMaterial);
                Primitive("Grid Z " + i, PrimitiveType.Cube, arena, new Vector3(0, -0.035f, i), new Vector3(8.5f, 0.02f, 0.018f), gridMaterial);
            }
            foreach (var sign in new[] { -1f, 1f })
            {
                Primitive("Side Rail", PrimitiveType.Cube, arena, new Vector3(4.4f * sign, 0, 0), new Vector3(0.08f, 0.08f, 8.8f), edgeMaterial);
                Primitive("End Rail", PrimitiveType.Cube, arena, new Vector3(0, 0, 4.4f * sign), new Vector3(8.8f, 0.08f, 0.08f), edgeMaterial);
            }

            var player = new GameObject("Player").transform;
            player.SetParent(root.transform);
            player.position = new Vector3(0, 0.48f, 0);
            var motionRig = new GameObject("Motion Rig").transform;
            motionRig.SetParent(player, false);
            var body = Primitive("Controller Puck", PrimitiveType.Sphere, motionRig, Vector3.zero, new Vector3(0.85f, 0.65f, 0.85f), cyan);
            Primitive("Motion Fin", PrimitiveType.Cube, motionRig, new Vector3(0, 0.33f, 0), new Vector3(0.12f, 0.35f, 0.55f), violet);
            var aim = new GameObject("Crown Aim").transform;
            aim.SetParent(player, false);
            Primitive("Aim Shaft", PrimitiveType.Cube, aim, new Vector3(0, 0, 0.8f), new Vector3(0.07f, 0.07f, 0.7f), cyanGlow);
            var arrow = Primitive("Aim Tip", PrimitiveType.Cube, aim, new Vector3(0, 0, 1.16f), new Vector3(0.23f, 0.07f, 0.23f), cyanGlow);
            arrow.localRotation = Quaternion.Euler(0, 45, 0);
            var marker = Primitive("Touch Marker", PrimitiveType.Sphere, root.transform, new Vector3(0, 0.02f, 0), new Vector3(0.26f, 0.05f, 0.26f), pink);
            var gyro = new GameObject("Gyroscope").transform;
            gyro.SetParent(root.transform);
            gyro.localPosition = new Vector3(-2.8f, 0.55f, -4.9f);
            Primitive("Gyro X", PrimitiveType.Cube, gyro, Vector3.zero, new Vector3(0.85f, 0.10f, 0.10f), violet);
            Primitive("Gyro Y", PrimitiveType.Cube, gyro, Vector3.zero, new Vector3(0.10f, 0.85f, 0.10f), violet);
            Primitive("Gyro Z", PrimitiveType.Cube, gyro, Vector3.zero, new Vector3(0.10f, 0.10f, 0.85f), violet);
            Primitive("Orientation Tip", PrimitiveType.Sphere, gyro, new Vector3(0.24f, 0.18f, 0.54f), Vector3.one * 0.22f, violet);
            var audioBar = Primitive("Audio Pillar", PrimitiveType.Cube, root.transform, new Vector3(0, 0.35f, -4.9f), new Vector3(0.4f, 0.6f, 0.4f), green);
            var valueBar = Primitive("Value Pillar", PrimitiveType.Cube, root.transform, new Vector3(2.8f, 0.35f, -4.9f), new Vector3(0.4f, 0.6f, 0.4f), amber);

            var targetsRoot = new GameObject("Targets").transform;
            targetsRoot.SetParent(root.transform);
            var positions = new[] { new Vector3(0, 0.5f, 3), new Vector3(2.6f, 0.5f, 2.6f), new Vector3(2.6f, 0.5f, -2.6f),
                new Vector3(0, 0.5f, -3), new Vector3(-2.6f, 0.5f, -2.6f), new Vector3(-2.6f, 0.5f, 2.6f) };
            var targets = new Transform[positions.Length];
            for (var i = 0; i < positions.Length; i++)
            {
                targets[i] = Primitive("Beacon " + (i + 1), PrimitiveType.Sphere, targetsRoot, positions[i], Vector3.one * 0.68f, amber);
                Primitive("Beacon Core", PrimitiveType.Cube, targets[i], new Vector3(0, 0.62f, 0), Vector3.one * 0.3f, amberGlow);
            }
            var shot = new GameObject("Shot", typeof(LineRenderer)).GetComponent<LineRenderer>();
            shot.transform.SetParent(root.transform);
            shot.sharedMaterial = cyanGlow;
            shot.positionCount = 2;
            shot.startWidth = shot.endWidth = 0.045f;
            shot.useWorldSpace = true;
            shot.enabled = false;

            var receiver = root.AddComponent<WatchInputReceiver>();
            var receiverData = new SerializedObject(receiver);
            receiverData.FindProperty("editorCrownScale").floatValue = 15f;
            foreach (var pair in new[] { ("editorGyroUpKey", Key.Digit1), ("editorGyroDownKey", Key.Digit2), ("editorGyroLeftKey", Key.Digit3),
                         ("editorGyroRightKey", Key.Digit4), ("editorGyroForwardKey", Key.Digit5), ("editorGyroBackwardKey", Key.Digit6) })
                receiverData.FindProperty(pair.Item1).intValue = (int)pair.Item2;
            receiverData.ApplyModifiedPropertiesWithoutUndo();
            var controller = root.AddComponent<AppleWatchDemoController>();
            Assign(controller, ("target", player), ("crownDial", aim), ("motionRig", motionRig), ("gyroIndicator", gyro),
                ("touchMarker", marker), ("audioBar", audioBar), ("valueIndicator", valueBar), ("receiver", receiver), ("actorRenderer", body.GetComponent<Renderer>()));
            var effectsSource = root.AddComponent<AudioSource>();
            var recordingSource = root.AddComponent<AudioSource>();
            effectsSource.playOnAwake = recordingSource.playOnAwake = false;
            var audio = root.AddComponent<AppleWatchDemoAudio>();
            Assign(audio, ("controller", controller), ("receiver", receiver), ("effectsSource", effectsSource), ("recordingSource", recordingSource));
            var game = root.AddComponent<AppleWatchDemoGame>();
            Assign(game, ("controller", controller), ("audioFeedback", audio), ("player", player), ("aim", aim), ("shotLine", shot));
            var gameData = new SerializedObject(game);
            var targetArray = gameData.FindProperty("targets");
            targetArray.arraySize = targets.Length;
            for (var i = 0; i < targets.Length; i++) targetArray.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
            gameData.ApplyModifiedPropertiesWithoutUndo();

            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "Watch Demo Panel";
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1280, 720);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
            panelSettings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(sampleRoot + "/Demo/WatchDemoTheme.tss");
            var textSettings = ScriptableObject.CreateInstance<PanelTextSettings>();
            var font = FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>(sampleRoot + "/Demo/Inter.ttf"));
            if (font == null) throw new InvalidOperationException("Could not create the demo's runtime font asset.");
            font.name = "Watch Demo Font";
            AssetDatabase.CreateAsset(font, assets + "/DemoFont.asset");
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var texture in font.atlasTextures) if (texture != null) AssetDatabase.AddObjectToAsset(texture, font);
            using (var serializedTextSettings = new SerializedObject(textSettings))
            {
                serializedTextSettings.FindProperty("m_DefaultFontAsset").objectReferenceValue = font;
                serializedTextSettings.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.CreateAsset(textSettings, assets + "/TextSettings.asset");
            panelSettings.textSettings = textSettings;
            AssetDatabase.CreateAsset(panelSettings, assets + "/PanelSettings.asset");
            var hudObject = new GameObject("HUD", typeof(UIDocument));
            hudObject.transform.SetParent(root.transform);
            var document = hudObject.GetComponent<UIDocument>();
            document.panelSettings = panelSettings;
            // NewScene can unload a preflight asset held only in a local variable.
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudPath);
            var hud = hudObject.AddComponent<AppleWatchDemoHud>();
            Assign(hud, ("controller", controller), ("game", game), ("audioFeedback", audio), ("arenaCamera", camera));
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssetIfDirty(font);
            EditorSceneManager.SaveScene(scene, scenePath);
            if (makeStartupScene) SetStartup(scenePath);
            Selection.activeGameObject = root;
            return new { Status = "created", ScenePath = scenePath, Targets = targets.Length };
        }

        private static string ValidateSampleRoot(string sampleRoot)
        {
            if (string.IsNullOrWhiteSpace(sampleRoot)) throw new ArgumentException("Select an imported Target Pop folder under Assets.");
            sampleRoot = sampleRoot.Replace('\\', '/').TrimEnd('/');
            if (!sampleRoot.StartsWith("Assets/", StringComparison.Ordinal) || sampleRoot.Contains("..") || !AssetDatabase.IsValidFolder(sampleRoot))
                throw new ArgumentException("Select an explicit imported Target Pop folder under Assets.");
            if (!File.Exists(sampleRoot + "/Runtime/AppleWatchDemoGame.cs"))
                throw new ArgumentException("The selected folder is not the imported Target Pop sample.");
            return sampleRoot;
        }

        private static void ValidateWatchSource(string path)
        {
            path = path?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains("..")
                || !path.EndsWith(".watch.uxml", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)
                || AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path) == null)
                throw new InvalidOperationException("Import Watch Lab or explicitly select an existing authored .watch.uxml source under Assets. Sample folders need not be adjacent.");
        }

        private static void RequireUrp()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null) throw new InvalidOperationException("Configure a URP 17.7.0 pipeline before Target Pop setup. Setup does not change pipeline settings.");
            using var data = new SerializedObject(pipeline);
            var index = data.FindProperty("m_DefaultRendererIndex").intValue;
            if (index < 0 || index >= pipeline.rendererDataList.Length || pipeline.rendererDataList[index] == null
                || Shader.Find("Universal Render Pipeline/Lit") == null || Shader.Find("Universal Render Pipeline/Unlit") == null)
                throw new InvalidOperationException("The active URP pipeline needs a valid default renderer and URP shaders before Target Pop setup.");
        }

        private static void SetStartup(string path)
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) }
                .Concat(EditorBuildSettings.scenes.Where(scene => scene.path != path)).ToArray();
        }

        private static Transform Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            item.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(item.GetComponent<Collider>());
            return item.transform;
        }

        private static Material Material(string folder, string name, Color color, bool unlit)
        {
            var material = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
            material.name = name;
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(material, folder + "/" + name + ".mat");
            return material;
        }

        private static void Assign(Object target, params (string Name, Object Value)[] fields)
        {
            var serialized = new SerializedObject(target);
            foreach (var field in fields) serialized.FindProperty(field.Name).objectReferenceValue = field.Value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }


        private static void PreflightOutputs(params string[] paths)
        {
            foreach (var path in paths)
            {
                if (File.Exists(path) || Directory.Exists(path) || File.Exists(path + ".meta"))
                    throw new InvalidOperationException("Sample setup output already exists: " + path + ". Choose a new scene name or move the existing asset.");
                for (var parent = Path.GetDirectoryName(path); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                    if (File.Exists(parent))
                        throw new InvalidOperationException("Sample setup output folder is occupied: " + parent);
            }
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
