# Understand motion and audio

[Guide index](index.md) · Previous: [Input System](input-system.md) · Next: [Build and test](build-and-test.md)

## Motion units and validity

Orientation answers "How is the Watch rotated relative to its reference?" Angular velocity answers "How fast is it rotating now?" Turn your wrist, then hold it still: orientation should retain a pose while angular velocity returns toward zero.

`attitude` is a quaternion, not Euler angles. The bridge preserves raw right-handed Core Motion coordinates; it does not silently convert them to Unity coordinates. `gyro` uses radians per second. `acceleration` uses g and removes gravity, so a still Watch should be near zero. One g is about 9.81 meters per second squared.

Author a motion element in a complete UXML root:

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:watch="WatchUI">
    <watch:watch-motion watch-id="motion" label="Start motion"
        update-interval="0.05" include-acceleration="true"
        include-gyro="true" include-attitude="true" />
</ui:UXML>
```

Press **Start motion** on the Watch. `include-attitude` defaults to false, so enable it explicitly when you need a pose. The interval is in seconds; 0.05 requests 20 updates per second. Generated motion intervals are bounded to 0.05–2 seconds. A request rate is not a guaranteed delivery rate.

Leaving the control or making the Watch scene inactive stops its provider. Start it again when you return. Several motion controls can share one Core Motion manager: it uses the fastest active request while each source keeps its own send timer. Physical behavior with concurrent controls still needs testing.

Check `attitudeTracked` before using a pose. Identity `(0, 0, 0, 1)` can mean a real pose or neutral fallback. Recover the session reference's unsigned bits without rejecting a negative signed representation:

```csharp
var device = AppleWatchControllerDevice.Current;
bool tracked = device.attitudeTracked.isPressed;
Quaternion raw = device.attitude.ReadValue();
uint reference = unchecked((uint)device.attitudeReference.ReadValue());
```

Zero means no tracked reference. A new sensor session changes the nonzero reference. A valid identity pose can leave an Input System Value action waiting, with an all-zero default result; read the resolved quaternion control for that tracked device rather than inventing a pose from the default.

Target Pop maps the pose with `(raw.x, raw.y, -raw.z, -raw.w)`, stores a mapped center, and uses `Quaternion.Inverse(center) * mapped`. Its first valid pose and a new reference establish a center. **Center orientation** explicitly resets that center. A short stale gap with the same reference retains it. This is relative orientation, not guaranteed compass heading. Review [AppleWatchDemoController.cs](../Samples~/TargetPop/Runtime/AppleWatchDemoController.cs) before adapting the mapping to your game axes.

## Freshness and diagnostics

The receiver defaults to a 0.25-second stale timeout. Expired continuous input resets; expired attitude becomes identity and untracked. Attitude has its own sample age, so unrelated packets cannot extend an old pose's lifetime. Neutral input is a safety fallback, not proof of disconnection.

`NativeFramesPerSecond` measures fresh native frames accepted by the receiver. It does not count Editor simulation or every Watch send. `LastNativeQueueAgeSeconds` measures time in the phone's native buffer, not time before arrival. `SecondsSinceNativeFrame` measures time since acceptance and starts at positive infinity. `MotionSampleInterval` comes from emitted sensor timestamps, not Unity frame timing. None of these is a complete end-to-end latency measurement.

## Meters and recordings are separate

`microphoneLevel` and `microphonePeak` are bounded meter values derived from decibels. They are not PCM samples, linear amplitude, or calibrated sound-pressure readings.

A recording is a file. The generated Watch recorder writes mono 16 kHz AAC in a CAF file and transfers it through WatchConnectivity's file-transfer path. Live meters and button events use a different path, so receiving a recording does not prove live input is reachable.

In Watch Lab's Audio page, press **Start recording**, grant microphone permission, and press again if recording did not start after permission. Speak briefly and press **Send Recording** before leaving. Leaving cancels the provider; it does not send the file.

The phone bridge copies a received file to an application-owned path. `WatchInputReceiver.RecordingReceived` reports the local path on Unity's main thread. Subscribe when your component enables and unsubscribe when it disables. The event neither verifies file existence nor decodes or plays audio. Do not expose recording contents or full paths in shared diagnostics.

Target Pop requires **Play recording** before playback. **Stop** cancels loading or playback. Its iOS loader attempts AAC/CAF decoding through Audio Queue; other supported formats use Unity decoders. The [sample audio component](../Samples~/TargetPop/Runtime/AppleWatchDemoAudio.cs) includes failure handling. **Test sound** plays a synthetic tone: it tests output, not Watch capture, transfer, or recording decoding.
