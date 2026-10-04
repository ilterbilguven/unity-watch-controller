using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Local demo feedback and opt-in playback of recordings delivered by the Watch.</summary>
/// <remarks>
/// Enable subscribes to demo and recording events and creates missing audio sources and generated tones.
/// Receipt never starts playback. Disable cancels loading, stops both sources, and releases generated or decoded clips.
/// The component does not delete transferred recording files.
/// </remarks>
public sealed class AppleWatchDemoAudio : MonoBehaviour
{
    [SerializeField] private AppleWatchDemoController controller;
    [SerializeField] private WatchInputReceiver receiver;
    [SerializeField] private AudioSource effectsSource;
    [SerializeField] private AudioSource recordingSource;
    [SerializeField] private bool effectsEnabled = true;

    private readonly Dictionary<string, AudioClip> effects = new();
    private AppleWatchDemoController subscribedController;
    private WatchInputReceiver subscribedReceiver;
    private AudioClip simulationClip;
    private AudioClip decodedRecording;
    private UnityWebRequest recordingRequest;
    private Coroutine recordingLoad;
    private string latestRecordingPath;
    private string latestRecordingName;
    private int recordingVersion;
    private float nextEffectTime;
    private bool playbackStarted;
    private bool simulationPlaying;

    /// <summary>Gets a player-facing description of recording receipt, loading, playback, or failure.</summary>
    public string RecordingStatus { get; private set; } = "No Watch recording received";
    /// <summary>Gets whether local game and controller feedback sounds are enabled.</summary>
    public bool EffectsEnabled => effectsEnabled;
    /// <summary>Gets whether the recording audio source is playing a recording or the simulation tone.</summary>
    public bool IsPlayingRecording => recordingSource != null && recordingSource.isPlaying;
    /// <summary>Gets whether a recording decode request is currently active.</summary>
    public bool IsLoadingRecording => recordingRequest != null;
    /// <summary>Gets whether the latest file exists, has a supported extension, and can start loading now.</summary>
    /// <remarks>This is an eligibility check, not proof that the platform decoder can decode the file.</remarks>
    public bool CanPlayRecording => isActiveAndEnabled && recordingRequest == null
        && !string.IsNullOrEmpty(latestRecordingPath) && File.Exists(latestRecordingPath)
        && SelectRecordingAudioType(latestRecordingPath, Application.platform) != AudioType.UNKNOWN;

    private void OnEnable()
    {
        EnsureAudioSources();
        EnsureTones();
        nextEffectTime = 0f;
        Unsubscribe();
        subscribedController = controller != null ? controller : GetComponent<AppleWatchDemoController>();
        subscribedReceiver = receiver != null ? receiver : GetComponent<WatchInputReceiver>();
        if (subscribedController != null)
            subscribedController.DemoEvent += HandleDemoEvent;
        if (subscribedReceiver != null)
            subscribedReceiver.RecordingReceived += HandleRecordingReceived;
    }

    private void EnsureAudioSources()
    {
        if (effectsSource == null)
            effectsSource = gameObject.AddComponent<AudioSource>();
        if (recordingSource == null || recordingSource == effectsSource)
            recordingSource = gameObject.AddComponent<AudioSource>();
        ConfigureSource(effectsSource);
        ConfigureSource(recordingSource);
    }

    private static void ConfigureSource(AudioSource source)
    {
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
    }

    private void EnsureTones()
    {
        if (effects.Count != 0)
            return;
        effects.Add("Shot", CreateTone("Watch shot", 420f, 180f, 0.10f));
        effects.Add("Tap", CreateTone("Watch tap", 640f, 800f, 0.09f));
        effects.Add("DoubleTap", CreateTone("Watch double tap", 780f, 1100f, 0.14f));
        effects.Add("Swipe", CreateTone("Watch swipe", 300f, 580f, 0.12f));
        effects.Add("Touch", CreateTone("Watch touch", 520f, 520f, 0.07f));
        effects.Add("Crown", CreateTone("Watch crown tick", 950f, 850f, 0.045f));
        effects.Add("Reset", CreateTone("Watch reset", 600f, 250f, 0.22f));
        effects.Add("Hit", CreateTone("Target hit", 880f, 1320f, 0.16f));
        effects.Add("Wave", CreateTone("New target wave", 440f, 880f, 0.32f));
        simulationClip = CreateTone("Simulation tone - not a Watch recording", 440f, 660f, 0.65f);
    }

    private static AudioClip CreateTone(string name, float startFrequency, float endFrequency, float duration)
    {
        const int sampleRate = 22050;
        var samples = new float[Mathf.CeilToInt(sampleRate * duration)];
        double phase = 0;
        for (var index = 0; index < samples.Length; index++)
        {
            var progress = (float)index / (samples.Length - 1);
            var frequency = Mathf.Lerp(startFrequency, endFrequency, progress);
            phase += 2 * Math.PI * frequency / sampleRate;
            var attack = Mathf.Min(1f, progress / 0.08f);
            var release = Mathf.Pow(1f - progress, 2f);
            samples[index] = (float)Math.Sin(phase) * attack * release * 0.22f;
        }
        var clip = AudioClip.Create(name, samples.Length, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void HandleDemoEvent(string eventName)
    {
        string cue;
        switch (eventName)
        {
            case "PrimaryButton":
            case "PrimaryAction": cue = "Shot"; break;
            case "Tap": cue = "Tap"; break;
            case "DoubleTap": cue = "DoubleTap"; break;
            case "SwipeUp":
            case "SwipeDown":
            case "SwipeLeft":
            case "SwipeRight": cue = "Swipe"; break;
            case "TouchPress":
            case "Drag": cue = "Touch"; break;
            case "Crown": cue = "Crown"; break;
            case "Reset": cue = "Reset"; break;
            default: return;
        }
        PlayEffect(cue, false);
    }

    /// <summary>Plays a game-result cue without the normal short feedback throttle.</summary>
    /// <param name="cue">Case-sensitive Hit or Wave; other values are ignored.</param>
    /// <remarks>The component must be enabled and effects must be on. A cue replaces the current effect on the shared source.</remarks>
    public void PlayGameCue(string cue)
    {
        if (cue == "Hit" || cue == "Wave")
            PlayEffect(cue, true);
    }

    private void PlayEffect(string cue, bool priority)
    {
        if (!isActiveAndEnabled || !effectsEnabled || effectsSource == null
            || (!priority && Time.unscaledTime < nextEffectTime) || !effects.TryGetValue(cue, out var clip))
            return;
        nextEffectTime = Time.unscaledTime + 0.08f;
        effectsSource.clip = clip;
        effectsSource.Play();
    }

    /// <summary>Enables or mutes generated feedback sounds without changing recording playback.</summary>
    /// <param name="enabled">True to permit effects; false to stop the current effect and mute later cues.</param>
    public void SetEffectsEnabled(bool enabled)
    {
        effectsEnabled = enabled;
        if (!enabled && effectsSource != null)
        {
            effectsSource.Stop();
            effectsSource.clip = null;
        }
    }

    private void HandleRecordingReceived(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        StopRecording();
        latestRecordingPath = path;
        latestRecordingName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(latestRecordingName))
            latestRecordingName = "Watch recording";
        RecordingStatus = "Received " + latestRecordingName + " — press Play to listen";
        if (SelectRecordingAudioType(path, Application.platform) == AudioType.UNKNOWN)
            RecordingStatus = "Received " + latestRecordingName + " — playback unavailable here; try on iPhone";
    }

    /// <summary>Stops current recording playback or loading, then loads and plays the most recently received file.</summary>
    /// <remarks>
    /// Does nothing while disabled. Missing files and unsupported formats update RecordingStatus.
    /// WAV, MP3, and OGG use their matching decoders; CAF, M4A, and AAC select AUDIOQUEUE only on iPhonePlayer.
    /// Selecting a decoder does not guarantee successful device playback. The decode request has a 15-second timeout.
    /// </remarks>
    public void PlayLatestRecording()
    {
        if (!isActiveAndEnabled)
            return;
        StopRecording();
        if (string.IsNullOrEmpty(latestRecordingPath) || !File.Exists(latestRecordingPath))
        {
            RecordingStatus = "Recording file unavailable";
            return;
        }
        var type = SelectRecordingAudioType(latestRecordingPath, Application.platform);
        if (type == AudioType.UNKNOWN)
        {
            RecordingStatus = "Playback unavailable for " + latestRecordingName + " here; try on iPhone";
            return;
        }
        RecordingStatus = "Loading " + latestRecordingName;
        recordingLoad = StartCoroutine(LoadRecording(latestRecordingPath, type, recordingVersion));
    }

    private IEnumerator LoadRecording(string path, AudioType type, int version)
    {
        var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(Path.GetFullPath(path)), type);
        request.timeout = 15;
        recordingRequest = request;
        yield return request.SendWebRequest();
        if (version != recordingVersion || !isActiveAndEnabled)
        {
            request.Dispose();
            yield break;
        }
        recordingRequest = null;
        recordingLoad = null;
        if (request.result != UnityWebRequest.Result.Success)
        {
            RecordingStatus = "Could not decode " + latestRecordingName + " on this device";
            request.Dispose();
            yield break;
        }
        AudioClip clip = null;
        try
        {
            clip = DownloadHandlerAudioClip.GetContent(request);
        }
        catch (Exception)
        {
            // Decoder details can contain private paths; keep the HUD message bounded.
            RecordingStatus = "Could not decode " + latestRecordingName + " on this device";
        }
        request.Dispose();
        if (clip == null || clip.samples == 0)
        {
            DestroyClip(clip);
            RecordingStatus = "Could not decode " + latestRecordingName + " on this device";
            yield break;
        }
        decodedRecording = clip;
        recordingSource.clip = clip;
        recordingSource.Play();
        playbackStarted = true;
        simulationPlaying = false;
        RecordingStatus = "Playing " + latestRecordingName;
    }

    private static AudioType SelectRecordingAudioType(string path, RuntimePlatform platform)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".wav": return AudioType.WAV;
            case ".mp3": return AudioType.MPEG;
            case ".ogg": return AudioType.OGGVORBIS;
            // AUDIOQUEUE is an Apple decoder. CAF/AAC still needs a real-device check;
            // do not pretend the portable Editor decoder can read Watch recordings.
            case ".caf":
            case ".m4a":
            case ".aac": return platform == RuntimePlatform.IPhonePlayer ? AudioType.AUDIOQUEUE : AudioType.UNKNOWN;
            default: return AudioType.UNKNOWN;
        }
    }

    /// <summary>Stops recording playback or loading and plays a locally generated test tone while enabled.</summary>
    /// <remarks>This tests the Unity audio path only; it does not validate Watch capture, file transfer, or decoding.</remarks>
    public void PlaySimulationTone()
    {
        if (!isActiveAndEnabled)
            return;
        StopRecording();
        EnsureTones();
        recordingSource.clip = simulationClip;
        recordingSource.Play();
        playbackStarted = true;
        simulationPlaying = true;
        RecordingStatus = "Simulation tone — not a Watch recording";
    }

    /// <summary>Cancels recording loading, stops the recording source, and releases its decoded clip.</summary>
    /// <remarks>Also stops the simulation tone. Keeps the latest recording path and file so playback can be requested again.</remarks>
    public void StopRecording()
    {
        recordingVersion++;
        if (recordingLoad != null)
            StopCoroutine(recordingLoad);
        recordingLoad = null;
        if (recordingRequest != null)
        {
            recordingRequest.Abort();
            recordingRequest.Dispose();
            recordingRequest = null;
        }
        if (recordingSource != null)
        {
            recordingSource.Stop();
            recordingSource.clip = null;
        }
        DestroyClip(decodedRecording);
        decodedRecording = null;
        playbackStarted = false;
        simulationPlaying = false;
        RecordingStatus = string.IsNullOrEmpty(latestRecordingName)
            ? "No Watch recording received" : "Stopped " + latestRecordingName;
    }

    private void Update()
    {
        if (playbackStarted && !IsPlayingRecording)
        {
            playbackStarted = false;
            RecordingStatus = simulationPlaying ? "Simulation tone complete — not a Watch recording"
                : "Finished " + latestRecordingName;
        }
    }

    private void Unsubscribe()
    {
        if (subscribedController != null)
            subscribedController.DemoEvent -= HandleDemoEvent;
        if (subscribedReceiver != null)
            subscribedReceiver.RecordingReceived -= HandleRecordingReceived;
        subscribedController = null;
        subscribedReceiver = null;
    }

    private void OnDisable()
    {
        Unsubscribe();
        StopRecording();
        if (effectsSource != null)
        {
            effectsSource.Stop();
            effectsSource.clip = null;
        }
        foreach (var clip in effects.Values)
            DestroyClip(clip);
        effects.Clear();
        DestroyClip(simulationClip);
        simulationClip = null;
    }

    private void OnDestroy() => OnDisable();

    private static void DestroyClip(AudioClip clip)
    {
        if (clip == null)
            return;
        if (Application.isPlaying)
            Destroy(clip);
        else
            DestroyImmediate(clip);
    }
}
