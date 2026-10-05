using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AppleWatchController.Samples.BasicController.Editor
{
    /// <summary>Creates a small controller scene in a configured URP project without changing project settings.</summary>
    public static class BasicControllerSampleSetup
    {
        [MenuItem("Tools/AppleWatch/Open Basic Controller")]
        private static void OpenFromMenu()
        {
            var selected = EditorUtility.OpenFolderPanel("Select imported Basic Controller folder", "Assets", "");
            if (!string.IsNullOrEmpty(selected)) Create(FileUtil.GetProjectRelativePath(selected), "Assets/Scenes/BasicController.unity");
        }

        public static object Create(string sampleRoot, string scenePath, bool makeStartupScene = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before setup.");
            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene changes before setup.");
            if (string.IsNullOrWhiteSpace(sampleRoot)) throw new ArgumentException("Select the imported Basic Controller folder under Assets.");
            sampleRoot = sampleRoot.Replace('\\', '/').TrimEnd('/');
            if (!sampleRoot.StartsWith("Assets/", StringComparison.Ordinal) || sampleRoot.Contains("..") || !AssetDatabase.IsValidFolder(sampleRoot)
                || !File.Exists(sampleRoot + "/Runtime/AppleWatchInputMover.cs") || !File.Exists(sampleRoot + "/Controller.watch.uxml"))
                throw new ArgumentException("Select the explicit imported Basic Controller folder under Assets, including its Watch source.");
            if (string.IsNullOrWhiteSpace(scenePath)) throw new ArgumentException("Use an Assets-relative .unity scene path.");
            scenePath = scenePath.Replace('\\', '/');
            if (!scenePath.StartsWith("Assets/", StringComparison.Ordinal) || scenePath.Contains("..") || !scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use an Assets-relative .unity scene path.");
            RequireUrp();
            var existed = File.Exists(scenePath);
            if (existed) EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            else
            {
                var assets = Path.GetDirectoryName(scenePath).Replace('\\', '/') + "/" + Path.GetFileNameWithoutExtension(scenePath) + "Assets";
                PreflightOutputs(scenePath, assets + "/Target.mat");
                EnsureFolder(Path.GetDirectoryName(scenePath).Replace('\\', '/'));
                EnsureFolder(assets);
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.name = "Controller Target";
                material.SetColor("_BaseColor", new Color(0.12f, 0.82f, 0.96f));
                AssetDatabase.CreateAsset(material, assets + "/Target.mat");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = new GameObject("Basic Controller", typeof(WatchInputReceiver), typeof(AppleWatchInputMover), typeof(BasicControllerFeedback));
                var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
                target.name = "Target";
                target.GetComponent<Renderer>().sharedMaterial = material;
                target.transform.SetParent(root.transform);
                UnityEngine.Object.DestroyImmediate(target.GetComponent<Collider>());
                var mover = new SerializedObject(root.GetComponent<AppleWatchInputMover>());
                mover.FindProperty("target").objectReferenceValue = target.transform;
                mover.FindProperty("logWatchButtons").boolValue = false;
                mover.ApplyModifiedPropertiesWithoutUndo();
                var feedback = new SerializedObject(root.GetComponent<BasicControllerFeedback>());
                feedback.FindProperty("target").objectReferenceValue = target.transform;
                feedback.ApplyModifiedPropertiesWithoutUndo();
                var camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
                camera.tag = "MainCamera";
                camera.transform.position = new Vector3(0, 7, -7);
                camera.transform.LookAt(Vector3.zero);
                camera.orthographic = true;
                camera.orthographicSize = 5;
                var light = new GameObject("Key Light", typeof(Light)).GetComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(45, -30, 0);
                EditorSceneManager.SaveScene(scene, scenePath);
            }
            if (makeStartupScene)
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) }
                    .Concat(EditorBuildSettings.scenes.Where(s => s.path != scenePath)).ToArray();
            return new { Status = existed ? "opened" : "created", ScenePath = scenePath };
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
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }

        private static void RequireUrp()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null) throw new InvalidOperationException("Configure a URP 17.7.0 pipeline before sample setup. Setup does not change pipeline settings.");
            using var data = new SerializedObject(pipeline);
            var index = data.FindProperty("m_DefaultRendererIndex").intValue;
            if (index < 0 || index >= pipeline.rendererDataList.Length || pipeline.rendererDataList[index] == null
                || Shader.Find("Universal Render Pipeline/Lit") == null)
                throw new InvalidOperationException("The active URP pipeline needs a valid default renderer and URP shaders before sample setup.");
        }
    }
}
