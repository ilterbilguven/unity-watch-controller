using System;
using System.IO;
using AppleWatchController.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class WatchPackagePathTests
{
    [TestCase("../outside")]
    [TestCase("Editor/../../outside")]
    [TestCase("/tmp/outside")]
    [TestCase("C:\\outside")]
    [TestCase("Editor\\..\\outside")]
    [TestCase("")]
    [TestCase(null)]
    public void FilePathRejectsPathsOutsidePackage(string path)
    {
        Assert.Throws<ArgumentException>(() => WatchPackagePaths.FilePath(path));
        Assert.Throws<ArgumentException>(() => WatchPackagePaths.AssetPath(path));
    }

    [Test]
    public void AssetPathResolvesPackageStylesheet()
    {
        var path = WatchPackagePaths.AssetPath("Editor/WatchUI/Resources/WatchUiPreview.uss");
        Assert.That(path, Is.EqualTo("Packages/com.ilter.apple-watch-controller/Editor/WatchUI/Resources/WatchUiPreview.uss"));
        Assert.That(AssetDatabase.LoadAssetAtPath<StyleSheet>(path), Is.Not.Null);
    }

    [Test]
    public void FilePathResolvesLocalPackagePhysicalFile()
    {
        Assert.That(File.Exists(WatchPackagePaths.FilePath("Editor/WatchUI/Resources/WatchUiPreview.uss")), Is.True);
    }

    [Test]
    public void NativeHarnessIsExcludedFromPlayerTargets()
    {
        var path = WatchPackagePaths.AssetPath("Tests/Native/apple-watch-buffer-tests.swift");
        var importer = AssetImporter.GetAtPath(path) as PluginImporter;
        Assert.That(importer, Is.Not.Null, "Swift sources have native plugin importers even outside Plugins folders.");
        Assert.That(importer.GetCompatibleWithAnyPlatform(), Is.False);
        Assert.That(importer.GetCompatibleWithEditor(), Is.False);
        Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.iOS), Is.False);
        Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.tvOS), Is.False);
        Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.VisionOS), Is.False);
    }

    [TestCase("Assets")]
    [TestCase("Resources")]
    [TestCase("Packages")]
    public void PreviewLoadsImportedImagesWithoutChangingPackage(string location)
    {
        using var fixture = new WatchUiTestAssets("<watch:watch-image />");
        Texture2D texture;
        string source;
        if (location == "Packages")
        {
            var images = AssetDatabase.FindAssets("t:Texture2D", new[] { "Packages/com.unity.inputsystem" });
            Assert.That(images, Is.Not.Empty, "The required Input System package supplies imported Editor textures.");
            source = AssetDatabase.GUIDToAssetPath(images[0]);
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(source);
        }
        else
        {
            texture = new Texture2D(2, 2);
            texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
            texture.Apply();
            var resourceName = "OwnedWatchImage_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder(fixture.Folder, "Resources");
            var assetPath = fixture.Folder + "/Resources/" + resourceName + ".asset";
            AssetDatabase.CreateAsset(texture, assetPath);
            source = location == "Assets" ? assetPath : resourceName;
        }
        var element = new WatchUI.WatchImage { Source = source };
        element.RefreshPreview();
        Assert.That(element.Q<Image>().image, Is.EqualTo(texture), "Compare Unity object identity; AssetDatabase can reload managed wrappers.");
    }
}
