# See working input in the Editor

[Guide index](index.md) · Previous: [Installation](installation.md) · Next: [Watch UI](watch-ui.md)

This walkthrough moves a Cube and logs one Fire event per press. It needs the installed package and a configured URP project, but no Watch, Xcode, CLI, or sample. It tests the receiver-to-game path, not physical connectivity.

## Create the game object

1. Save your current scene or create a new scene for this test.
2. Choose **GameObject > 3D Object > Cube**. Make sure the camera can see it.
3. Create `Assets/WatchInputTutorial.cs` with the complete script below. Wait for compilation.
4. Select the Cube and choose **Add Component > Watch Input Tutorial**. Unity also adds `WatchInputReceiver` because the script requires it.
5. In the receiver's Inspector, leave **Enable Editor Simulation** enabled.

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(WatchInputReceiver))]
public sealed class WatchInputTutorial : MonoBehaviour
{
    [SerializeField] private float speed = 3f;
    private InputAction move;
    private InputAction fire;
    private int fireCount;

    private void OnEnable()
    {
        move = new InputAction("Move", InputActionType.Value,
            "<AppleWatchController>/primaryStick");
        fire = new InputAction("Fire", InputActionType.Button,
            "<AppleWatchController>/primaryButton");
        fire.performed += OnFire;
        move.Enable();
        fire.Enable();
    }

    private void Update()
    {
        Vector2 input = move.ReadValue<Vector2>();
        transform.position += new Vector3(input.x, 0f, input.y)
            * (speed * Time.deltaTime);
    }

    private void OnFire(InputAction.CallbackContext context)
    {
        fireCount++;
        Debug.Log($"Watch Fire count: {fireCount}", this);
    }

    private void OnDisable()
    {
        fire.performed -= OnFire;
        move.Disable();
        fire.Disable();
        move.Dispose();
        fire.Dispose();
        move = null;
        fire = null;
    }
}
```

The receiver reads native messages on iOS. In the Editor it supplies keyboard input to the same software device. `<AppleWatchController>` is the layout name, not the C# type name. The `Move` and `Fire` actions express what the game does; the binding paths select where the values come from.

## Check the result

Enter Play Mode and click the **Game** view so it receives keyboard input. Press WASD: the Cube should move in the X/Z plane. Release the keys: it should stop. Press Space once: Console should show `Watch Fire count: 1`. Hold Space: the count should not increase repeatedly. Release and press again: the count should become 2. Stop Play, then repeat to check that the action lifecycle still works.

If nothing moves, check Console errors, Active Input Handling, Game-view focus, and the receiver's simulation setting. If Fire repeats every rendered frame in your own code, use an action callback rather than polling a nonzero button as a new event.

## Connect the Watch later

Create and export the [small Watch interface](watch-ui.md), then follow [native export and deployment](build-and-test.md). Its joystick feeds `primaryStick`; its Fire button feeds `primaryButton`, so the tutorial script needs no Swift calls or binding changes. Keep one receiver active in the scene. Multiple receivers can duplicate polling or overwrite state.

For a ready-made scene with visible Fire feedback, import [Basic Controller](samples.md#basic-controller). Use [Input System](input-system.md) when you want more controls or an Input Actions asset.
