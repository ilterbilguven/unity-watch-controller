using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Provides a small example that moves a target from input actions and optionally logs Watch channels.</summary>
/// <remarks>
/// Start clones configured actions or creates Watch-only fallback actions. The component enables, disables,
/// and disposes only those owned actions. Movement uses world X/Z at the configured units per second.
/// Assign a target, or provide a PlayerInput component for the target fallback. This example has no arena bounds
/// and does not interpret attitude; the separate Target Pop sample demonstrates motion.
/// </remarks>
[DisallowMultipleComponent]
public class AppleWatchInputMover : MonoBehaviour
{
    [SerializeField] private InputActionAsset actionAsset;
    [SerializeField] private string moveActionMap = "Player";
    [SerializeField] private string moveActionName = "Move";
    [SerializeField] private string interactActionMap = "Player";
    [SerializeField] private string interactActionName = "Attack";
    [SerializeField] private string watchActionMap = "Player";

    [SerializeField] private Transform target;
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private bool logAttackTrigger;
    [SerializeField] private bool logWatchButtons = true;
    [SerializeField] private bool logWatchAxes;

    [SerializeField] private string touchPressActionName = "WatchTouchPress";
    [SerializeField] private string tapActionName = "WatchTap";
    [SerializeField] private string dragActionName = "WatchDrag";
    [SerializeField] private string doubleTapActionName = "WatchDoubleTap";
    [SerializeField] private string primaryActionName = "WatchPrimaryAction";
    [SerializeField] private string longPressActionName = "WatchLongPress";
    [SerializeField] private string swipeUpActionName = "WatchSwipeUp";
    [SerializeField] private string swipeDownActionName = "WatchSwipeDown";
    [SerializeField] private string swipeLeftActionName = "WatchSwipeLeft";
    [SerializeField] private string swipeRightActionName = "WatchSwipeRight";
    [SerializeField] private string crownActionName = "WatchCrown";
    [SerializeField] private string accelerationActionName = "WatchAcceleration";
    [SerializeField] private string gyroActionName = "WatchGyro";
    [SerializeField] private string micLevelActionName = "WatchMicrophoneLevel";
    [SerializeField] private string micPeakActionName = "WatchMicrophonePeak";
    [SerializeField] private string touchPositionActionName = "WatchTouchPosition";
    [SerializeField] private string axis0ActionName = "WatchAxis0";

    private InputAction moveAction;
    private InputAction attackAction;
    private InputAction touchPressAction;
    private InputAction tapAction;
    private InputAction doubleTapAction;
    private InputAction dragAction;
    private InputAction primaryAction;
    private InputAction longPressAction;
    private InputAction swipeUpAction;
    private InputAction swipeDownAction;
    private InputAction swipeLeftAction;
    private InputAction swipeRightAction;
    private InputAction crownAction;
    private InputAction accelerationAction;
    private InputAction gyroAction;
    private InputAction micLevelAction;
    private InputAction micPeakAction;
    private InputAction touchPositionAction;
    private InputAction axis0Action;

    private Vector3 lastAcceleration;
    private Vector3 lastGyro;
    private Vector2 lastTouchPosition;
    private float lastCrown;
    private float lastAxis0;
    private float lastMicLevel;
    private float lastMicPeak;
    private readonly string logWatchAxesFormat =
        "AppleWatch axes -> crown:{0:F2}, axis0:{1:F2}, micLevel:{2:F2}, micPeak:{3:F2}, acc:{4}, gyro:{5}, touch:{6}";

    private void Start()
    {
        if (actionAsset == null)
        {
            var playerInput = GetComponent<PlayerInput>();
            if (playerInput != null)
            {
                actionAsset = playerInput.actions;
            }
        }

        if (target == null)
        {
            if (actionAsset != null)
            {
                var actionAssetPlayer = GetComponent<PlayerInput>();
                if (actionAssetPlayer != null && actionAssetPlayer.transform != null)
                {
                    target = actionAssetPlayer.transform;
                }
            }
        }

        if (target == null)
        {
            Debug.LogWarning("[AppleWatch] AppleWatchInputMover target is missing. Assign a target Transform manually.");
        }

        moveAction = ResolveAction(actionAsset, moveActionMap, moveActionName)?.Clone()
                    ?? new InputAction("AppleWatchMove", InputActionType.Value, "<AppleWatchController>/primaryStick");

        attackAction = ResolveAction(actionAsset, interactActionMap, interactActionName)?.Clone()
            ?? new InputAction("AppleWatchAttack", InputActionType.Button, "<AppleWatchController>/primaryButton");

        touchPressAction = ResolveAction(actionAsset, watchActionMap, touchPressActionName)?.Clone()
            ?? new InputAction("AppleWatchTouchPress", InputActionType.Button, "<AppleWatchController>/touchPress");
        tapAction = ResolveAction(actionAsset, watchActionMap, tapActionName)?.Clone()
            ?? new InputAction("AppleWatchTap", InputActionType.Button, "<AppleWatchController>/tap");
        dragAction = ResolveAction(actionAsset, watchActionMap, dragActionName)?.Clone()
            ?? new InputAction("AppleWatchDrag", InputActionType.Button, "<AppleWatchController>/drag");
        doubleTapAction = ResolveAction(actionAsset, watchActionMap, doubleTapActionName)?.Clone()
            ?? new InputAction("AppleWatchDoubleTap", InputActionType.Button, "<AppleWatchController>/doubleTap");
        primaryAction = ResolveAction(actionAsset, watchActionMap, primaryActionName)?.Clone()
            ?? new InputAction("AppleWatchPrimaryAction", InputActionType.Button, "<AppleWatchController>/primaryAction");
        longPressAction = ResolveAction(actionAsset, watchActionMap, longPressActionName)?.Clone()
            ?? new InputAction("AppleWatchLongPress", InputActionType.Button, "<AppleWatchController>/longPress");
        swipeUpAction = ResolveAction(actionAsset, watchActionMap, swipeUpActionName)?.Clone()
            ?? new InputAction("AppleWatchSwipeUp", InputActionType.Button, "<AppleWatchController>/swipeUp");
        swipeDownAction = ResolveAction(actionAsset, watchActionMap, swipeDownActionName)?.Clone()
            ?? new InputAction("AppleWatchSwipeDown", InputActionType.Button, "<AppleWatchController>/swipeDown");
        swipeLeftAction = ResolveAction(actionAsset, watchActionMap, swipeLeftActionName)?.Clone()
            ?? new InputAction("AppleWatchSwipeLeft", InputActionType.Button, "<AppleWatchController>/swipeLeft");
        swipeRightAction = ResolveAction(actionAsset, watchActionMap, swipeRightActionName)?.Clone()
            ?? new InputAction("AppleWatchSwipeRight", InputActionType.Button, "<AppleWatchController>/swipeRight");
        crownAction = ResolveAction(actionAsset, watchActionMap, crownActionName)?.Clone()
            ?? new InputAction("AppleWatchCrown", InputActionType.Value, "<AppleWatchController>/crown");
        accelerationAction = ResolveAction(actionAsset, watchActionMap, accelerationActionName)?.Clone()
            ?? new InputAction("AppleWatchAcceleration", InputActionType.Value, "<AppleWatchController>/acceleration");
        gyroAction = ResolveAction(actionAsset, watchActionMap, gyroActionName)?.Clone()
            ?? new InputAction("AppleWatchGyro", InputActionType.Value, "<AppleWatchController>/gyro");
        micLevelAction = ResolveAction(actionAsset, watchActionMap, micLevelActionName)?.Clone()
            ?? new InputAction("AppleWatchMicrophoneLevel", InputActionType.Value, "<AppleWatchController>/microphoneLevel");
        micPeakAction = ResolveAction(actionAsset, watchActionMap, micPeakActionName)?.Clone()
            ?? new InputAction("AppleWatchMicrophonePeak", InputActionType.Value, "<AppleWatchController>/microphonePeak");
        touchPositionAction = ResolveAction(actionAsset, watchActionMap, touchPositionActionName)?.Clone()
            ?? new InputAction("AppleWatchTouchPosition", InputActionType.Value, "<AppleWatchController>/touchPosition");
        axis0Action = ResolveAction(actionAsset, watchActionMap, axis0ActionName)?.Clone()
            ?? new InputAction("AppleWatchAxis0", InputActionType.Value, "<AppleWatchController>/axis0");

        OnEnable();
    }

    private void OnEnable()
    {
        moveAction?.Enable();
        attackAction?.Enable();
        touchPressAction?.Enable();
        tapAction?.Enable();
        dragAction?.Enable();
        doubleTapAction?.Enable();
        primaryAction?.Enable();
        longPressAction?.Enable();
        swipeUpAction?.Enable();
        swipeDownAction?.Enable();
        swipeLeftAction?.Enable();
        swipeRightAction?.Enable();
        crownAction?.Enable();
        accelerationAction?.Enable();
        gyroAction?.Enable();
        micLevelAction?.Enable();
        micPeakAction?.Enable();
        touchPositionAction?.Enable();
        axis0Action?.Enable();
    }

    private void Update()
    {
        if (target != null && moveAction != null)
        {
            var move = moveAction.ReadValue<Vector2>();
            target.Translate(new Vector3(move.x, 0f, move.y) * (moveSpeed * Time.deltaTime), Space.World);
        }

        if (logAttackTrigger && attackAction != null && attackAction.WasPressedThisFrame())
        {
            Debug.Log("AppleWatch attack action triggered");
        }

        if (logWatchButtons)
        {
            if (touchPressAction != null && touchPressAction.WasPressedThisFrame())
                Debug.Log("AppleWatch touch press action triggered");
            if (tapAction != null && tapAction.WasPressedThisFrame())
                Debug.Log("AppleWatch tap action triggered");
            if (dragAction != null && dragAction.WasPressedThisFrame())
                Debug.Log("AppleWatch drag action triggered");
            if (doubleTapAction != null && doubleTapAction.WasPressedThisFrame())
                Debug.Log("AppleWatch double tap action triggered");
            if (primaryAction != null && primaryAction.WasPressedThisFrame())
                Debug.Log("AppleWatch primary action triggered");
            if (longPressAction != null && longPressAction.WasPressedThisFrame())
                Debug.Log("AppleWatch long press action triggered");
            if (swipeUpAction != null && swipeUpAction.WasPressedThisFrame())
                Debug.Log("AppleWatch swipe up action triggered");
            if (swipeDownAction != null && swipeDownAction.WasPressedThisFrame())
                Debug.Log("AppleWatch swipe down action triggered");
            if (swipeLeftAction != null && swipeLeftAction.WasPressedThisFrame())
                Debug.Log("AppleWatch swipe left action triggered");
            if (swipeRightAction != null && swipeRightAction.WasPressedThisFrame())
                Debug.Log("AppleWatch swipe right action triggered");
        }

        if (logWatchAxes)
        {
            var crown = crownAction != null ? crownAction.ReadValue<float>() : 0f;
            var axis0 = axis0Action != null ? axis0Action.ReadValue<float>() : 0f;
            var micLevel = micLevelAction != null ? micLevelAction.ReadValue<float>() : 0f;
            var micPeak = micPeakAction != null ? micPeakAction.ReadValue<float>() : 0f;
            var acceleration = accelerationAction != null ? accelerationAction.ReadValue<Vector3>() : Vector3.zero;
            var gyro = gyroAction != null ? gyroAction.ReadValue<Vector3>() : Vector3.zero;
            var touchPosition = touchPositionAction != null ? touchPositionAction.ReadValue<Vector2>() : Vector2.zero;

            if (
                crown != lastCrown ||
                axis0 != lastAxis0 ||
                micLevel != lastMicLevel ||
                micPeak != lastMicPeak ||
                acceleration != lastAcceleration ||
                gyro != lastGyro ||
                touchPosition != lastTouchPosition)
            {
                Debug.LogFormat(
                    logWatchAxesFormat,
                    crown,
                    axis0,
                    micLevel,
                    micPeak,
                    acceleration,
                    gyro,
                    touchPosition);

                lastCrown = crown;
                lastAxis0 = axis0;
                lastMicLevel = micLevel;
                lastMicPeak = micPeak;
                lastAcceleration = acceleration;
                lastGyro = gyro;
                lastTouchPosition = touchPosition;
            }
        }
    }

    private void OnDisable()
    {
        moveAction?.Disable();
        attackAction?.Disable();
        touchPressAction?.Disable();
        tapAction?.Disable();
        dragAction?.Disable();
        doubleTapAction?.Disable();
        primaryAction?.Disable();
        longPressAction?.Disable();
        swipeUpAction?.Disable();
        swipeDownAction?.Disable();
        swipeLeftAction?.Disable();
        swipeRightAction?.Disable();
        crownAction?.Disable();
        accelerationAction?.Disable();
        gyroAction?.Disable();
        micLevelAction?.Disable();
        micPeakAction?.Disable();
        touchPositionAction?.Disable();
        axis0Action?.Disable();
    }

    private void OnDestroy()
    {
        moveAction?.Dispose();
        attackAction?.Dispose();
        touchPressAction?.Dispose();
        tapAction?.Dispose();
        dragAction?.Dispose();
        doubleTapAction?.Dispose();
        primaryAction?.Dispose();
        longPressAction?.Dispose();
        swipeUpAction?.Dispose();
        swipeDownAction?.Dispose();
        swipeLeftAction?.Dispose();
        swipeRightAction?.Dispose();
        crownAction?.Dispose();
        accelerationAction?.Dispose();
        gyroAction?.Dispose();
        micLevelAction?.Dispose();
        micPeakAction?.Dispose();
        touchPositionAction?.Dispose();
        axis0Action?.Dispose();
    }

    private static InputAction ResolveAction(InputActionAsset actionAsset, string actionMap, string actionName)
    {
        if (actionAsset == null || string.IsNullOrWhiteSpace(actionMap) || string.IsNullOrWhiteSpace(actionName))
            return null;

        var action = actionAsset.FindAction($"{actionMap}/{actionName}", throwIfNotFound: false);
        return action;
    }
}
