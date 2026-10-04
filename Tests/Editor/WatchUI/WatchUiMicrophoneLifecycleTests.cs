using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using WatchUI;

public sealed class WatchUiMicrophoneLifecycleTests
{
    [TestCase("permission-after-stop")]
    [TestCase("permission-after-replacement")]
    [TestCase("single-owner")]
    [TestCase("initially-inactive")]
    [TestCase("inactive-stops-providers")]
    [TestCase("permission-after-scene-return")]
    [TestCase("recording-paths")]
    [TestCase("transfer-cleanup")]
    [TestCase("unavailable-transfer")]
    [TestCase("motion-attitude")]
    [TestCase("motion-sample-dedup")]
    [TestCase("motion-shared-rate")]
    [TestCase("motion-reference-epochs")]
    [TestCase("motion-common-mode")]
    [TestCase("motion-restart-freshness")]
    [TestCase("swipe-directions")]
    public void GeneratedMicrophoneBridgePreservesOwnershipAndCancellation(string scenario)
    {
        if (Application.platform != RuntimePlatform.OSXEditor || !File.Exists("/usr/bin/xcrun"))
            Assert.Ignore("The generated Swift lifecycle harness requires macOS and Xcode command-line tools.");

        var swift = WatchUiSwiftEmitter.ToSwiftUI(new WatchUiNode { kind = "WatchMicrophone", id = "microphone" }, out _);
        var harnessPath = AppleWatchController.Editor.WatchPackagePaths.FilePath("Tests/Editor/WatchUI/MicrophoneLifecycleHarness.swift.txt");
        var harness = File.ReadAllText(harnessPath)
            .Replace("// GENERATED_SENSOR_BRIDGE", ExtractClass(swift, "WatchSensorBridge"))
            .Replace("// GENERATED_CONNECTIVITY_SESSION", ExtractClass(swift, "WatchConnectivitySession"));
        var motion = JsonUtility.FromJson<WatchUiNode>("{\"kind\":\"WatchMotion\",\"id\":\"attitude-target\",\"action\":\"motion\",\"enabled\":true,\"updateInterval\":0.05,\"includeAcceleration\":true,\"includeGyro\":true,\"includeAttitude\":true}");
        var motionSwift = WatchUiSwiftEmitter.ToSwiftUI(motion, out _);
        var startMotion = Regex.Match(motionSwift, @"WatchSensorBridge\.shared\.startMotion\([^\r\n]+?\)").Value;
        Assert.That(startMotion, Is.Not.Empty);
        harness = harness.Replace("// GENERATED_START_ATTITUDE_MOTION",
            startMotion.Replace("sourceId: \"attitude-target\"", "sourceId: sourceId"));
        var swipeSwift = WatchUiSwiftEmitter.ToSwiftUI(new WatchUiNode
            { kind = "WatchGestureArea", id = "swipe-test", action = "swipe", gesture = "swipe", minimumDistance = 32 }, out _);
        var releaseStart = swipeSwift.IndexOf(".onEnded { value in", StringComparison.Ordinal);
        Assert.That(releaseStart, Is.GreaterThan(0));
        releaseStart += ".onEnded { value in".Length;
        var releaseEnd = releaseStart;
        var depth = 1;
        for (; releaseEnd < swipeSwift.Length; releaseEnd++)
        {
            if (swipeSwift[releaseEnd] == '{') depth++;
            if (swipeSwift[releaseEnd] == '}' && --depth == 0) break;
        }
        Assert.That(depth, Is.Zero);
        harness = harness.Replace("// GENERATED_SWIPE_RELEASE", swipeSwift.Substring(releaseStart, releaseEnd - releaseStart));
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "watch-ui-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var sourcePath = Path.Combine(temporaryDirectory, "Lifecycle.swift");
            File.WriteAllText(sourcePath, harness);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/xcrun",
                Arguments = "swift \"" + sourcePath + "\" " + scenario,
                WorkingDirectory = temporaryDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            Assert.That(process, Is.Not.Null);
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60000))
            {
                process.Kill();
                Assert.Fail("Generated Swift lifecycle harness timed out.");
            }

            Assert.That(process.ExitCode, Is.Zero, output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult());
            Assert.That(output.GetAwaiter().GetResult(), Does.Contain("PASS: " + scenario));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private static string ExtractClass(string swift, string className)
    {
        var start = swift.IndexOf("private final class " + className, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        var end = swift.IndexOf("#endif", start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start));
        return swift.Substring(start, end - start);
    }
}
