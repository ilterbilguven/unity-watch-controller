using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using AppleWatchController.Samples.BasicController.Editor;

[TestFixture("Assets/Samples/Apple Watch Controller/0.1.0-preview.1/Basic Controller")]
public sealed class BasicControllerSampleTests : InputTestFixture
{
    private readonly string sampleRoot;
    public BasicControllerSampleTests(string sampleRoot) => this.sampleRoot = sampleRoot;

    [Test]
    public void DirtySceneIsNotDiscarded()
    {
        var setup = EditorSceneManager.GetSceneManagerSetup();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var marker = new GameObject("Unsaved marker");
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        try
        {
            Assert.Throws<InvalidOperationException>(() => BasicControllerSampleSetup.Create(sampleRoot, "Assets/Task4Dirty.unity"));
            Assert.That(marker, Is.Not.Null);
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.True);
            Assert.That(System.IO.File.Exists("Assets/Task4Dirty.unity"), Is.False);
        }
        finally
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (setup.Length > 0 && System.Linq.Enumerable.All(setup, s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }

    [Test]
    public void OnePressProducesOneFireEvent()
    {
        InputSystem.RegisterLayout<AppleWatchControllerDevice>("AppleWatchController");
        var device = InputSystem.AddDevice<AppleWatchControllerDevice>();
        var gameObject = new GameObject("Feedback", typeof(BasicControllerFeedback));
        var feedback = gameObject.GetComponent<BasicControllerFeedback>();
        typeof(BasicControllerFeedback).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(feedback, null);
        var events = 0;
        feedback.Fired.AddListener(() => events++);
        try
        {
            InputSystem.QueueStateEvent(device, new AppleWatchControllerState { primaryButton = 1 });
            InputSystem.Update();
            Assert.That(device.primaryButton.ReadValue(), Is.EqualTo(1), "The real device must receive the press.");
            var action = (InputAction)typeof(BasicControllerFeedback).GetField("fire", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(feedback);
            Assert.That(action.enabled, Is.True);
            Assert.That(action.ReadValue<float>(), Is.EqualTo(1), "The action must bind the pressed device.");
            InputSystem.QueueStateEvent(device, new AppleWatchControllerState { primaryButton = 1 });
            InputSystem.Update();
            Assert.That(events, Is.EqualTo(1));
            Assert.That(feedback.FireCount, Is.EqualTo(1));
        }
        finally
        {
            typeof(BasicControllerFeedback).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(feedback, null);
            UnityEngine.Object.DestroyImmediate(gameObject);
            InputSystem.RemoveDevice(device);
        }
    }
}
