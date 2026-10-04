using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEditor.PackageManager;

public sealed class AppleWatchPackageTests
{
    [Test]
    public void RuntimeComesFromTheRegisteredPackageAssembly()
    {
        Assert.That(typeof(AppleWatchControllerDevice).Assembly.GetName().Name,
            Is.EqualTo("AppleWatchController.Runtime"));
        var package = PackageInfo.FindForAssembly(typeof(AppleWatchControllerDevice).Assembly);
        Assert.That(package, Is.Not.Null);
        Assert.That(package.name, Is.EqualTo("com.ilter.apple-watch-controller"));
        Assert.That(UnityEditor.AssetDatabase.GUIDToAssetPath("eb93f831209c74a659e7cdb3f55db6f7"),
            Is.EqualTo("Packages/com.ilter.apple-watch-controller/Runtime/Input/WatchInputFrame.cs"));
        Assert.That(UnityEditor.AssetDatabase.GUIDToAssetPath("41b8648135ea4aee9efb09aab2490346"),
            Is.EqualTo("Packages/com.ilter.apple-watch-controller/Runtime/Input/AppleWatchControllerDevice.cs"));
        Assert.That(UnityEditor.AssetDatabase.GUIDToAssetPath("4e239748b957455793ffa0a3e9722d17"),
            Is.EqualTo("Packages/com.ilter.apple-watch-controller/Runtime/Input/WatchInputReceiver.cs"));
    }

    [Test]
    public void NativeFrameAndDeviceStateKeepTheirConsumerWireSizes()
    {
        Assert.That(AppleWatchInputFrame.ByteLength, Is.EqualTo(216));
        Assert.That(Marshal.SizeOf<AppleWatchControllerState>(), Is.EqualTo(96));
    }
}
