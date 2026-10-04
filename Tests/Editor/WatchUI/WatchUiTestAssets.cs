using System;
using System.IO;
using UnityEditor;
using UnityEngine.UIElements;

/// <summary>Small authored fixtures, independent of optional samples and their import location.</summary>
internal sealed class WatchUiTestAssets : IDisposable
{
    public string Folder { get; }
    public string SourcePath => Folder + "/Fixture.watch.uxml";
    public VisualTreeAsset Source => AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SourcePath);

    public WatchUiTestAssets(string body)
    {
        var name = "WatchUiFixture_" + Guid.NewGuid().ToString("N");
        Folder = "Assets/" + name;
        AssetDatabase.CreateFolder("Assets", name);
        File.WriteAllText(SourcePath,
            "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:watch=\"WatchUI\">" + body + "</ui:UXML>");
        AssetDatabase.ImportAsset(SourcePath, ImportAssetOptions.ForceSynchronousImport);
    }

    public void Dispose() => AssetDatabase.DeleteAsset(Folder);
}
