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
    }

    [Test]
    public void NativeFrameAndDeviceStateKeepTheirConsumerWireSizes()
    {
        Assert.That(AppleWatchInputFrame.ByteLength, Is.EqualTo(216));
        Assert.That(Marshal.SizeOf<AppleWatchControllerState>(), Is.EqualTo(96));
    }
}
