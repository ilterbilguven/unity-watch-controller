using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

public sealed class AppleWatchDemoAudioTests : InputTestFixture
{
    private GameObject demo;
    private Component audio;
    private AudioSource effects;
    private AudioSource recording;
    private WatchInputReceiver receiver;
    private Type audioType;

    [SetUp]
    public void SetUp()
    {
        audioType = typeof(AppleWatchDemoAudio);
        Assert.That(audioType, Is.Not.Null, "The demo needs a runtime audio component.");
        demo = new GameObject("Watch demo audio test");
        demo.SetActive(false);
        receiver = demo.AddComponent<WatchInputReceiver>();
        effects = demo.AddComponent<AudioSource>();
        recording = demo.AddComponent<AudioSource>();
        audio = demo.AddComponent(audioType);
        SetField("receiver", receiver);
        SetField("effectsSource", effects);
        SetField("recordingSource", recording);
        demo.SetActive(true);
        Invoke("OnEnable");
    }

    [TearDown]
    public override void TearDown()
    {
        if (demo != null)
            UnityEngine.Object.DestroyImmediate(demo);
        foreach (var device in InputSystem.devices.OfType<AppleWatchControllerDevice>().ToArray())
            InputSystem.RemoveDevice(device);
        base.TearDown();
    }

    [Test]
    public void SimulationProducesAudibleSamplesWithoutPretendingARecordingArrived()
    {
        Invoke("PlaySimulationTone");
        Assert.That(recording.clip, Is.Not.Null);
        var samples = new float[recording.clip.samples * recording.clip.channels];
        Assert.That(recording.clip.GetData(samples, 0), Is.True);
        Assert.That(Array.Exists(samples, sample => Math.Abs(sample) > 0.01f), Is.True);
        Assert.That(GetProperty<string>("RecordingStatus"), Does.Contain("Simulation"));
        Assert.That(GetProperty<bool>("CanPlayRecording"), Is.False);
    }

    [Test]
    public void RecordingReceiptShowsOnlyFilenameAndDoesNotStartPlayback()
    {
        Receive("/private/watch/voice-note.caf");
        Assert.That(GetProperty<string>("RecordingStatus"), Does.Contain("voice-note.caf"));
        Assert.That(GetProperty<string>("RecordingStatus"), Does.Not.Contain("/private"));
        Assert.That(recording.clip, Is.Null);
        Assert.That(GetProperty<bool>("IsPlayingRecording"), Is.False);
    }

    [Test]
    public void MissingRecordingFailsGracefullyWithoutExposingItsPrivatePath()
    {
        Receive("/private/watch/missing.wav");
        Invoke("PlayLatestRecording");
        Assert.That(GetProperty<string>("RecordingStatus"), Does.Contain("unavailable"));
        Assert.That(GetProperty<string>("RecordingStatus"), Does.Not.Contain("/private"));
        Assert.That(GetProperty<bool>("IsPlayingRecording"), Is.False);
    }

    [Test]
    public void PendingRecordingRequestCanBeObservedAndCancelledBeforePlayback()
    {
        var loadingProperty = audioType.GetProperty("IsLoadingRecording");
        Assert.That(loadingProperty, Is.Not.Null, "The HUD needs to offer Stop while a recording is loading.");
        Assert.That(loadingProperty.GetValue(audio), Is.False);
        using (var request = UnityWebRequestMultimedia.GetAudioClip("file:///watch-audio-lifecycle.wav", AudioType.WAV))
        {
            SetField("recordingRequest", request);
            Assert.That(loadingProperty.GetValue(audio), Is.True);
            Assert.That(GetProperty<bool>("IsPlayingRecording"), Is.False);
            Invoke("StopRecording");
            Assert.That(loadingProperty.GetValue(audio), Is.False);
            Assert.That(audioType.GetField("recordingRequest", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(audio), Is.Null);
        }
    }

    [TestCase("voice.caf", RuntimePlatform.IPhonePlayer, AudioType.AUDIOQUEUE)]
    [TestCase("voice.caf", RuntimePlatform.OSXEditor, AudioType.UNKNOWN)]
    [TestCase("voice.wav", RuntimePlatform.OSXEditor, AudioType.WAV)]
    [TestCase("voice.mp3", RuntimePlatform.OSXEditor, AudioType.MPEG)]
    [TestCase("voice.ogg", RuntimePlatform.OSXEditor, AudioType.OGGVORBIS)]
    [TestCase("voice.bin", RuntimePlatform.IPhonePlayer, AudioType.UNKNOWN)]
    public void RecordingDecoderMatchesContainerAndPlatform(string path, RuntimePlatform platform, AudioType expected)
    {
        var selector = audioType.GetMethod("SelectRecordingAudioType", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(selector, Is.Not.Null);
        Assert.That(selector.Invoke(null, new object[] { path, platform }), Is.EqualTo(expected));
    }

    [Test]
    public void StopAndDisableReleaseAudioWithoutARecordingAutoReplay()
    {
        Invoke("PlaySimulationTone");
        Invoke("StopRecording");
        Assert.That(GetProperty<bool>("IsPlayingRecording"), Is.False);
        Invoke("PlaySimulationTone");
        Invoke("OnDisable");
        Assert.That(recording.clip, Is.Null);
        Assert.That(effects.clip, Is.Null);
        var oldStatus = GetProperty<string>("RecordingStatus");
        Receive("/private/ignored.wav");
        Assert.That(GetProperty<string>("RecordingStatus"), Is.EqualTo(oldStatus));
    }

    [Test]
    public void MutingEffectsPreservesRecordingPlaybackAndBlocksGameFeedback()
    {
        Invoke("PlaySimulationTone");
        var simulation = recording.clip;
        Invoke("SetEffectsEnabled", false);
        Invoke("HandleDemoEvent", "Tap");
        Assert.That(GetProperty<bool>("EffectsEnabled"), Is.False);
        Assert.That(effects.clip, Is.Null);
        Assert.That(recording.clip, Is.SameAs(simulation));
        Invoke("SetEffectsEnabled", true);
        Invoke("HandleDemoEvent", "Tap");
        Assert.That(effects.clip, Is.Not.Null);
        var samples = new float[effects.clip.samples];
        effects.clip.GetData(samples, 0);
        Assert.That(Array.Exists(samples, sample => Math.Abs(sample) > 0.01f), Is.True);
    }

    [Test]
    public void NewReceiptStopsOldPlaybackAndSelectsTheNewestFilename()
    {
        Invoke("PlaySimulationTone");
        Receive("/private/watch/latest.caf");
        Assert.That(recording.clip, Is.Null);
        Assert.That(GetProperty<bool>("IsPlayingRecording"), Is.False);
        Assert.That(GetProperty<string>("RecordingStatus"), Does.Contain("latest.caf"));
    }

    [Test]
    public void ControllerSubscriptionStopsOnDisableAndResumesOnEnable()
    {
        var controllerType = typeof(AppleWatchDemoController);
        Assert.That(controllerType, Is.Not.Null);
        var controller = demo.AddComponent(controllerType);
        Invoke("OnDisable");
        SetField("controller", controller);
        Invoke("OnEnable");
        RaiseControllerEvent(controller, "Tap");
        Assert.That(effects.clip, Is.Not.Null);
        Invoke("OnDisable");
        RaiseControllerEvent(controller, "Tap");
        Assert.That(effects.clip, Is.Null);
        Invoke("OnEnable");
        RaiseControllerEvent(controller, "Tap");
        Assert.That(effects.clip, Is.Not.Null);
    }

    [TestCase("Hit")]
    [TestCase("Wave")]
    public void TargetGameCuesProduceAudibleFeedback(string cue)
    {
        Invoke("PlayGameCue", cue);
        Assert.That(effects.clip, Is.Not.Null);
        var samples = new float[effects.clip.samples];
        effects.clip.GetData(samples, 0);
        Assert.That(Array.Exists(samples, sample => Math.Abs(sample) > 0.01f), Is.True);
    }

    private static void RaiseControllerEvent(Component controller, string eventName)
    {
        var subscribers = (Action<string>)controller.GetType()
            .GetField("DemoEvent", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        subscribers?.Invoke(eventName);
    }

    private void Receive(string path)
    {
        var subscribers = (Action<string>)typeof(WatchInputReceiver)
            .GetField("RecordingReceived", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(receiver);
        subscribers?.Invoke(path);
    }

    private void SetField(string name, object value)
    {
        audioType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(audio, value);
    }

    private T GetProperty<T>(string name)
    {
        return (T)audioType.GetProperty(name).GetValue(audio);
    }

    private void Invoke(string name, params object[] arguments)
    {
        var method = audioType.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, name + " must exist.");
        method.Invoke(audio, arguments);
    }
}
