using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AppleWatchController.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

public sealed class PackageDependencyTests
{
    private static readonly string[] AdapterDefinitions =
    {
        "Editor/Integrations/Pipeline/AppleWatchController.Pipeline.Editor.asmdef",
        "Editor/Integrations/Pipeline/Build/AppleWatchController.Pipeline.Build.Editor.asmdef",
        "Tests/Editor/Integrations/Pipeline/AppleWatchController.Pipeline.Tests.Editor.asmdef"
    };

    [Test]
    public void CoreWorksWithoutPipeline()
    {
        Assert.That(typeof(WatchUiExportMenu).Assembly.GetReferencedAssemblies()
            .Any(a => a.Name.StartsWith("Unity.Pipeline", StringComparison.Ordinal)), Is.False);
        Assert.That(typeof(AppleWatchControllerDevice).Assembly.GetReferencedAssemblies()
            .Any(a => a.Name.Contains("Universal") || a.Name.Contains("Unity.Pipeline")), Is.False);
        var manifest = File.ReadAllText(WatchPackagePaths.FilePath("package.json"));
        Assert.That(manifest, Does.Not.Contain("com.unity.pipeline"));
        Assert.That(manifest, Does.Contain("\"com.unity.render-pipelines.universal\": \"17.7.0\""));
        Assert.That(manifest, Does.Not.Contain("com.unity.ide.rider"));
    }

    [Test]
    public void AbsentOrUnsupportedPipelineDoesNotBreakCore()
    {
        var package = PackageInfo.GetAllRegisteredPackages().SingleOrDefault(p => p.name == "com.unity.pipeline");
        var supported = package != null && AcceptsVersion("[0.7.0-exp.1,0.8.0-exp.1)", package.version);
        var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor).Select(a => a.name).ToArray();
        Assert.That(assemblies, Does.Contain("AppleWatchController.Editor"));
        Assert.That(assemblies, Does.Contain("AppleWatchController.Runtime"));
        Assert.That(assemblies.Contains("AppleWatchController.Pipeline.Editor"), Is.EqualTo(supported));
        Assert.That(assemblies.Contains("AppleWatchController.Pipeline.Build.Editor"),
            Is.EqualTo(supported && EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS));
        Assert.That(assemblies.Contains("AppleWatchController.Pipeline.Tests.Editor"), Is.EqualTo(supported));
    }

    [TestCase("0.6.9", false)]
    [TestCase("0.7.0-exp.0", false)]
    [TestCase("0.7.0-exp.1", true)]
    [TestCase("0.7.0", true)]
    [TestCase("0.7.9", true)]
    [TestCase("0.8.0-exp.1", false)]
    [TestCase("0.8.0", false)]
    public void PipelineVersionConstraintFixtureUsesUnityEvaluator(string version, bool expected)
    {
        foreach (var path in AdapterDefinitions)
        {
            Assert.That(File.Exists(WatchPackagePaths.FilePath(path)), Is.True, path);
            var definition = JsonUtility.FromJson<AssemblyDefinition>(File.ReadAllText(WatchPackagePaths.FilePath(path)));
            var guard = definition.versionDefines.Single(v => v.name == "com.unity.pipeline");
            Assert.That(guard.define, Is.EqualTo("WATCH_CONTROLLER_PIPELINE"), path);
            Assert.That(definition.defineConstraints, Does.Contain("WATCH_CONTROLLER_PIPELINE"), path);
            Assert.That(AcceptsVersion(guard.expression, version), Is.EqualTo(expected), path + " @ " + version);
            if (path.Contains("/Build/"))
                Assert.That(definition.defineConstraints, Does.Contain("UNITY_IOS"));
            else
                Assert.That(definition.references, Does.Not.Contain("AppleWatchController.Build.Editor"));
        }
    }

    // Exercise the installed Editor's asmdef semantic-version evaluator, not a parallel parser.
    private static bool AcceptsVersion(string expression, string version)
    {
        var editor = typeof(UnityEditor.Editor).Assembly;
        const string prefix = "UnityEditor.Scripting.ScriptCompilation.";
        var semVersion = editor.GetType(prefix + "SemVersion", true);
        var parser = editor.GetType(prefix + "SemVersionParser", true);
        var parsed = parser.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static)
            .Invoke(null, new object[] { version, false });
        var factoryType = editor.GetType(prefix + "VersionRangesFactory`1", true).MakeGenericType(semVersion);
        var factory = Activator.CreateInstance(factoryType, true);
        var evaluated = factoryType.GetMethod("GetExpression").Invoke(factory, new object[] { expression });
        return (bool)evaluated.GetType().GetMethod("IsValid").Invoke(evaluated, new[] { parsed });
    }

    [Serializable]
    private sealed class AssemblyDefinition
    {
        public string[] references = Array.Empty<string>();
        public string[] defineConstraints = Array.Empty<string>();
        public VersionDefinition[] versionDefines = Array.Empty<VersionDefinition>();
    }

    [Serializable]
    private sealed class VersionDefinition
    {
        public string name = "";
        public string expression = "";
        public string define = "";
    }
}
