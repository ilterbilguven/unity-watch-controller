# Bind game actions to Watch input

[Guide index](index.md) · Previous: [Watch UI](watch-ui.md) · Next: [Motion and audio](motion-and-audio.md)

A **device** groups input controls. `AppleWatchControllerDevice` is a software device inside Unity, not a direct Bluetooth connection. A **control** holds a value. An **action** describes what the game wants to do and can bind to several devices. An **action map** groups actions that you enable together.

Use `<AppleWatchController>/primaryStick`, not `<AppleWatchControllerDevice>/primaryStick`. The layout name differs from the C# class name. The complete [tutorial](getting-started.md) shows action creation, callbacks, enabling, and disposal. Use Input System APIs on Unity's main thread.

## Use an Input Actions asset

Create an **Input Actions** asset in the Project window and open it. Add a Player map with Move (Value, Vector2) and Fire (Button) actions. Enter the binding paths below or use the device selector when the Watch device is available. Enable the map through your player component or `PlayerInput`. Merely creating an action does not enable it.

Bind Move to `<AppleWatchController>/primaryStick` and Fire to `<AppleWatchController>/primaryButton`. Keep one enabled `WatchInputReceiver` in the scene. The receiver publishes native input on iOS and keyboard simulation in the Editor. Your game consumes the same action in both cases.

## Control reference

Every path starts with `<AppleWatchController>/`.

| Control | Value type | Meaning |
| --- | --- | --- |
| `primaryStick` | Vector2 | Movement; canonical native components are in [-1, 1] |
| `primaryButton` | Button | Primary-button activation |
| `touchPress`, `tap`, `doubleTap` | Button | Touch and tap pulses |
| `primaryAction` | Button | Shared primary-action pulse, not an input-origin flag |
| `longPress`, `drag` | Button | Long-press recognition and drag-update pulses |
| `swipeUp`, `swipeDown`, `swipeLeft`, `swipeRight` | Button | Directional swipe pulses |
| `crown` | Float | Per-sample Crown delta, not accumulated angle or rate |
| `acceleration` | Vector3 | Gravity-free user acceleration in Watch coordinates, in g |
| `gyro` | Vector3 | Angular velocity in Watch coordinates, in radians/second |
| `attitude` | Quaternion | Normalized raw right-handed Core Motion orientation |
| `attitudeTracked` | Button | Held validity flag for orientation, not a pulse |
| `attitudeReference` | Integer | Motion-session reference bits; zero means untracked |
| `microphoneLevel`, `microphonePeak` | Float | [0, 1] microphone meters, not an audio waveform |
| `touchPosition` | Vector2 | SwiftUI points in the emitting control's coordinate space |
| `axis0` | Float | Shared scalar; the emitting component defines its range and units |

Read the emitting component's contract before combining values. A slider value and swipe displacement are not interchangeable units. Two independently named UXML controls do not automatically create two analog channels. Text/picker metadata is not a general string control on this device.

## Pulses, held keys, and deltas

Native buttons and gestures are pulses. The receiver releases discrete fields before another pulse and clears retained pulses on later updates. Use `performed` callbacks for Fire, Reset, or Swipe. Do not count every rendered frame with a nonzero value as another event.

Editor simulation can hold a button state. The tutorial's Button action fires on the press transition, so holding Space does not repeat Fire. A native Watch button does not carry the finger's held duration: an Input System Hold interaction on `primaryButton` cannot reconstruct it. Author a `gesture="long-press"` surface and bind `longPress` instead.

Consume Crown deltas once, as they arrive. For example, create this action alongside your other actions, then enable and dispose it through the same lifecycle:

```csharp
var crown = new InputAction("Aim delta", InputActionType.PassThrough,
    "<AppleWatchController>/crown");
float aimDegrees = 0f;
crown.performed += context => aimDegrees += context.ReadValue<float>();
```

Do not multiply a delta by `Time.deltaTime` or add a cached delta every Update. Native canonicalization clamps Crown values to [-1, 1]. Editor simulations can use a different scale, so keyboard steps do not measure physical sensitivity.

`AppleWatchControllerDevice.SubmitState` queues an entire state for a later Input System update; it does not expire stale state or release pulses for custom callers. `ResetState` queues neutral input and untracked identity attitude. The receiver provides these lifecycle policies for the normal native path. See [runtime XML comments](../Runtime/Input/AppleWatchControllerDevice.cs) before submitting custom state.
