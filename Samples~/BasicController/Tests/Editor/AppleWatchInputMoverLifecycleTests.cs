using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class AppleWatchInputMoverLifecycleTests : InputTestFixture
{
    [Test]
    public void ReenabledMoverResumesReadingItsInputActions()
    {
        InputSystem.RegisterLayout<AppleWatchControllerDevice>();
        var gameObject = new GameObject("Watch mover lifecycle test");
        AppleWatchInputMover mover = null;
        try
        {
            mover = gameObject.AddComponent<AppleWatchInputMover>();
            typeof(AppleWatchInputMover).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(mover, gameObject.transform);
            InvokeLifecycle(mover, "Start");

            var action = (InputAction)typeof(AppleWatchInputMover)
                .GetField("moveAction", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mover);
            Assert.That(action.enabled, Is.True);

            mover.enabled = false;
            InvokeLifecycle(mover, "OnDisable");
            Assert.That(action.enabled, Is.False);
            mover.enabled = true;
            InvokeLifecycle(mover, "OnEnable");

            Assert.That(action.enabled, Is.True, "Start runs once; OnEnable must resume previously created actions.");
            AppleWatchControllerDevice.SubmitState(0.75f, 0f, false);
            InputSystem.Update();
            Assert.That(action.ReadValue<Vector2>(), Is.EqualTo(new Vector2(0.75f, 0f)));
        }
        finally
        {
            if (mover != null)
                InvokeLifecycle(mover, "OnDestroy");
            Object.DestroyImmediate(gameObject);
            foreach (var device in InputSystem.devices.OfType<AppleWatchControllerDevice>().ToArray())
                InputSystem.RemoveDevice(device);
        }
    }

    private static void InvokeLifecycle(AppleWatchInputMover mover, string name)
    {
        typeof(AppleWatchInputMover).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(mover, null);
    }
}
