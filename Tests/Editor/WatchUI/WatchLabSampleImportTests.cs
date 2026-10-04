using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WatchUI.Tests
{
    [TestFixture("Assets/Samples/Apple Watch Controller/0.1.0-preview.1/Watch Lab")]
    public sealed class WatchLabSampleImportTests
    {
        private readonly string sampleRoot;
        public WatchLabSampleImportTests(string sampleRoot) => this.sampleRoot = sampleRoot;

        [Test]
        public void ImportAllSamplesHasNoDuplicateGuidsOrTypes()
        {
            var samples = Sample.FindByPackage("com.ilter.apple-watch-controller", "0.1.0-preview.1").ToArray();
            Assert.That(samples.Select(s => s.displayName), Is.EquivalentTo(new[] { "Basic Controller", "Watch Lab", "Target Pop" }));
            foreach (var sample in samples) Assert.That(sample.isImported, Is.True, sample.displayName);
            var guids = Directory.GetFiles("Assets/Samples", "*.meta", SearchOption.AllDirectories)
                .Select(File.ReadAllText).Select(t => System.Text.RegularExpressions.Regex.Match(t, @"guid: ([a-f0-9]{32})").Groups[1].Value).ToArray();
            Assert.That(guids.All(g => g.Length == 32), Is.True);
            Assert.That(guids.Distinct().Count(), Is.EqualTo(guids.Length));
            foreach (var name in new[] { "AppleWatchInputMover", "AppleWatchDemoController", "AppleWatchDemoAudio", "AppleWatchDemoGame", "AppleWatchDemoHud" })
                Assert.That(UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies().SelectMany(a => a.GetTypes()).Count(t => t.FullName == name), Is.EqualTo(1), name);
            Assert.That(File.Exists(sampleRoot + "/WatchControllerSample.watch.uxml"), Is.True);
        }


    [Test]
    public void SampleMotionExportsOptInAttitudeAtTwentyHertz()
    {
        var path = sampleRoot + "/WatchControllerSample.watch.uxml";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var motion = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path).CloneTree().Q<WatchMotion>();
        Assert.That(motion.IncludeAttitude, Is.True);
        Assert.That(motion.IncludeAcceleration && motion.IncludeGyro, Is.True);
        Assert.That(motion.UpdateInterval, Is.EqualTo(0.05f));
        Assert.That(new WatchMotion().IncludeAttitude, Is.False, "Attitude remains opt-in for existing authored controls.");
        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(motion, out var swift, out _), Is.True);
        Assert.That(swift, Does.Contain("interval: 0.05, includeAcceleration: true, includeGyro: true, includeAttitude: true)"));
    }

    [Test]
    public void SampleHandGesturePageHasOneNativePrimaryButtonWithoutScrolling()
    {
        var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(sampleRoot + "/WatchControllerSample.watch.uxml");
        var root = asset.CloneTree();
        var pages = root.Query<WatchNavigationDestination>().ToList().Where(page => page.Route == "hand-gestures").ToList();
        Assert.That(pages.Count, Is.EqualTo(1));
        var page = pages[0];
        Assert.That(page.Query<WatchScrollView>().ToList(), Is.Empty);
        Assert.That(page.Query<WatchButton>().ToList().Count, Is.EqualTo(1));
        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(root.Q<WatchNavigationStack>(), out var swift, out var diagnostics),
            Is.True, string.Join("\n", diagnostics.Errors));
        Assert.That(Regex.Matches(swift, @"\.handGestureShortcut\(\.primaryAction\)").Count, Is.EqualTo(1));
        Assert.That(swift, Does.Contain("NavigationLink(value: \"hand-gestures\")"));
        Assert.That(swift, Does.Contain("sourceId: \"handPrimary\", kind: \"WatchButton\""));
    }

    [TestCase("fire")]
    [TestCase("motion")]
    [TestCase("microphone")]
    public void SampleControlsRemainReachableOnASmallWatchScreen(string id)
    {
        var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(sampleRoot + "/WatchControllerSample.watch.uxml");
        Assert.That(asset, Is.Not.Null);
        var root = asset.CloneTree();
        var control = root.Query<WatchUiElement>().ToList().Single(element => element.WatchId == id);

        Assert.That(control.GetFirstAncestorOfType<WatchScrollView>(), Is.Not.Null,
            "The long sample page needs a scroll viewport so this control does not remain clipped offscreen.");
    }

    [TestCase("joystick")]
    [TestCase("pad")]
    [TestCase("tapArea")]
    [TestCase("doubleTapArea")]
    [TestCase("longPressArea")]
    [TestCase("swipeArea")]
    [TestCase("dragArea")]
    [TestCase("crown")]
    public void SampleDirectInputHasOneSurfaceOnANonScrollingPage(string id)
    {
        var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(sampleRoot + "/WatchControllerSample.watch.uxml");
        var root = asset.CloneTree();
        var control = root.Query<WatchUiElement>().ToList().Single(element => element.WatchId == id);
        var page = control.GetFirstAncestorOfType<WatchNavigationDestination>();

        Assert.That(page, Is.Not.Null);
        Assert.That(control.GetFirstAncestorOfType<WatchScrollView>(), Is.Null,
            "Scrolling must not compete with direct Crown, drag, or gesture input.");
        Assert.That(page.Query<WatchUiElement>().ToList().Count(element =>
            element is WatchCrown || element is WatchJoystick || element is WatchTouchpad || element is WatchGestureArea),
            Is.EqualTo(1), "Each dedicated input page contains one direct-input surface.");
        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(page.GetAuthoredChildren().Single(), out var swift, out _), Is.True);
        Assert.That(swift, Does.Not.Contain("ScrollView("));
    }

        [Test]
        public void SetupDefaultsPreserveBuildOrder()
        {
            var type = UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies().Select(a => a.GetType("AppleWatchController.Samples.BasicController.Editor.BasicControllerSampleSetup")).FirstOrDefault(t => t != null);
            Assert.That(type, Is.Not.Null, "Import Basic Controller to use its standalone setup service.");
            var original = EditorBuildSettings.scenes;
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var path = "Assets/Task4DefaultTest.unity";
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var method = type.GetMethod("Create");
                Assert.That(method.GetParameters()[2].DefaultValue, Is.EqualTo(false));
                method.Invoke(null, new object[] { "Assets/Samples/Apple Watch Controller/0.1.0-preview.1/Basic Controller", path, false });
                CollectionAssert.AreEqual(original.Select(s => (s.path, s.enabled)), EditorBuildSettings.scenes.Select(s => (s.path, s.enabled)));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.DeleteAsset("Assets/Task4DefaultTestAssets");
                if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }
    }
}
