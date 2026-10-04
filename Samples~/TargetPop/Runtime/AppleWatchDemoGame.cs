using UnityEngine;

/// <summary>Runs the target-shooting game driven by Watch demo events.</summary>
/// <remarks>
/// Subscribes while enabled. Primary-button, primary-action, tap, and double-tap events fire; Reset restarts the game.
/// Targets use a planar distance test, not physics raycasts. Each hit scores 100 points and a cleared wave waits 1.2 seconds.
/// </remarks>
[DisallowMultipleComponent]
public sealed class AppleWatchDemoGame : MonoBehaviour
{
    [SerializeField] private AppleWatchDemoController controller;
    [SerializeField] private AppleWatchDemoAudio audioFeedback;
    [SerializeField] private Transform player;
    [SerializeField] private Transform aim;
    [SerializeField] private Transform[] targets;
    [SerializeField] private LineRenderer shotLine;
    [SerializeField] private float range = 10f;
    [SerializeField] private float hitRadius = 0.55f;

    private AppleWatchDemoController subscribedController;
    private float waveDelay;
    private float shotTime;
    private bool initialized;

    /// <summary>Gets points earned since the last game reset; each target awards 100.</summary>
    public int Score { get; private set; }
    /// <summary>Gets the current one-based wave number.</summary>
    public int Wave { get; private set; } = 1;
    /// <summary>Gets accepted firing attempts since reset, including misses.</summary>
    public int Shots { get; private set; }
    /// <summary>Gets explicit reset calls during this component instance's lifetime; initial setup does not count.</summary>
    public int ResetCount { get; private set; }
    /// <summary>Gets targets left in the current wave, as tracked by this game.</summary>
    public int RemainingTargets { get; private set; }
    /// <summary>Gets the latest player-facing result or instruction for the HUD.</summary>
    public string LastResult { get; private set; } = "Button fires • Crown aims";

    private void OnEnable()
    {
        subscribedController = controller;
        if (subscribedController != null) subscribedController.DemoEvent += OnDemoEvent;
        if (!initialized) ResetGameState();
    }

    private void OnDisable()
    {
        if (subscribedController != null) subscribedController.DemoEvent -= OnDemoEvent;
        subscribedController = null;
        if (shotLine != null) shotLine.enabled = false;
    }

    private void OnDemoEvent(string action)
    {
        switch (action)
        {
            case "PrimaryButton":
            case "PrimaryAction":
            case "Tap":
            case "DoubleTap": Fire(); break;
            case "Reset": ResetGame(); break;
        }
    }

    /// <summary>Fires along the aim transform's forward direction projected onto the world X/Z plane.</summary>
    /// <remarks>
    /// Does nothing while disabled, without a player or aim, or during the wave delay.
    /// A valid attempt increments Shots and can deactivate the nearest eligible target within range and hit radius.
    /// </remarks>
    public void Fire()
    {
        if (!isActiveAndEnabled || player == null || aim == null || waveDelay > 0f) return;
        Shots++;
        var origin = player.position;
        var direction = Vector3.ProjectOnPlane(aim.forward, Vector3.up).normalized;
        if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;
        Transform hit = null;
        var distance = Mathf.Max(0.1f, range);
        if (targets != null)
        {
            foreach (var candidate in targets)
            {
                if (candidate == null || !candidate.gameObject.activeSelf) continue;
                var offset = Vector3.ProjectOnPlane(candidate.position - origin, Vector3.up);
                var along = Vector3.Dot(offset, direction);
                if (along < 0f || along > distance) continue;
                if ((offset - direction * along).sqrMagnitude > hitRadius * hitRadius) continue;
                distance = along;
                hit = candidate;
            }
        }
        if (shotLine != null)
        {
            shotLine.SetPosition(0, origin + Vector3.up * 0.3f);
            shotLine.SetPosition(1, origin + direction * distance + Vector3.up * 0.3f);
            shotLine.enabled = true;
            shotTime = 0.14f;
        }
        if (hit == null)
        {
            LastResult = "Miss — rotate the Crown or move closer";
            return;
        }
        hit.gameObject.SetActive(false);
        Score += 100;
        RemainingTargets--;
        LastResult = "+100 • Target popped";
        audioFeedback?.PlayGameCue("Hit");
        if (RemainingTargets == 0)
        {
            LastResult = "Wave cleared!";
            waveDelay = 1.2f;
            audioFeedback?.PlayGameCue("Wave");
        }
    }

    private void LateUpdate() => Tick(Time.deltaTime);

    /// <summary>Advances the shot-line timeout and starts the next wave after its delay.</summary>
    /// <param name="deltaTime">Elapsed seconds; zero, negative, and nonfinite values are ignored.</param>
    /// <remarks>LateUpdate calls this automatically. Tests can call it directly; an extra runtime call advances timers twice.</remarks>
    public void Tick(float deltaTime)
    {
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f) return;
        shotTime = Mathf.Max(0f, shotTime - deltaTime);
        if (shotLine != null && shotTime == 0f) shotLine.enabled = false;
        if (waveDelay <= 0f) return;
        waveDelay = Mathf.Max(0f, waveDelay - deltaTime);
        if (waveDelay > 0f) return;
        Wave++;
        RestoreTargets();
        LastResult = "New wave • Pop all " + RemainingTargets + " targets";
    }

    /// <summary>Clears score and shots, returns to wave one, restores targets, and increments the reset count.</summary>
    /// <remarks>This resets game state only; use the controller's reset to restore the demo transforms too.</remarks>
    public void ResetGame()
    {
        ResetGameState();
        ResetCount++;
        LastResult = "Game reset";
    }

    private void ResetGameState()
    {
        initialized = true;
        Score = 0;
        Shots = 0;
        Wave = 1;
        waveDelay = 0f;
        shotTime = 0f;
        if (shotLine != null) shotLine.enabled = false;
        RestoreTargets();
    }

    private void RestoreTargets()
    {
        RemainingTargets = 0;
        if (targets == null) return;
        foreach (var target in targets)
        {
            if (target == null) continue;
            target.gameObject.SetActive(true);
            RemainingTargets++;
        }
    }
}
