using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>Counts one fire event per Watch primary-button press and shows movement and fire feedback.</summary>
public sealed class BasicControllerFeedback : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private UnityEvent fired = new UnityEvent();
    private InputAction fire;
    private float flashUntil;
    public int FireCount { get; private set; }
    public UnityEvent Fired => fired;

    private void OnEnable()
    {
        fire = new InputAction("BasicFire", InputActionType.Button, "<AppleWatchController>/primaryButton");
        fire.performed += OnFire;
        fire.Enable();
    }

    private void OnFire(InputAction.CallbackContext context)
    {
        FireCount++;
        flashUntil = Time.unscaledTime + 0.2f;
        fired.Invoke();
    }

    private void Update()
    {
        if (target != null) target.localScale = Vector3.one * (Time.unscaledTime < flashUntil ? 1.3f : 1f);
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(16, 16, 300, 28), "Basic Controller — Fire events: " + FireCount);
        GUI.Label(new Rect(16, 44, 380, 28), "Move the Watch joystick or use WASD. Press Fire or Space.");
        if (target != null) GUI.Label(new Rect(16, 72, 350, 28), "Target position: " + target.position.ToString("F2"));
    }

    private void OnDisable()
    {
        fire?.Disable();
        fire?.Dispose();
        fire = null;
    }
}
