using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class AppleWatchDemoGameTests
{
    private GameObject root;

    [TearDown]
    public void TearDown()
    {
        if (root != null) Object.DestroyImmediate(root);
    }

    [Test]
    public void ShotHitsNearestTargetAlongAimAndScoresOnce()
    {
        var game = CreateGame(new Vector3(0, 0, 2), new Vector3(0, 0, 4));
        Call(game, "Fire");
        Assert.That(Read<int>(game, "Score"), Is.EqualTo(100));
        Assert.That(Read<int>(game, "RemainingTargets"), Is.EqualTo(1));
        Assert.That(root.transform.Find("Target0").gameObject.activeSelf, Is.False);
        Assert.That(root.transform.Find("Target1").gameObject.activeSelf, Is.True);
    }

    [Test]
    public void MissDoesNotScoreAndCrownAimChangesShotDirection()
    {
        var game = CreateGame(new Vector3(3, 0, 0));
        Call(game, "Fire");
        Assert.That(Read<int>(game, "Score"), Is.Zero);
        root.transform.Find("Aim").rotation = Quaternion.Euler(0, 90, 0);
        Call(game, "Fire");
        Assert.That(Read<int>(game, "Score"), Is.EqualTo(100));
        Assert.That(Read<int>(game, "Shots"), Is.EqualTo(2));
    }

    [Test]
    public void ClearingWaveRespawnsTargetsAndResetRestoresGame()
    {
        var game = CreateGame(new Vector3(0, 0, 2));
        Call(game, "Fire");
        Assert.That(Read<int>(game, "RemainingTargets"), Is.Zero);
        Call(game, "Tick", 2f);
        Assert.That(Read<int>(game, "Wave"), Is.EqualTo(2));
        Assert.That(Read<int>(game, "RemainingTargets"), Is.EqualTo(1));
        Call(game, "ResetGame");
        Assert.That(Read<int>(game, "Score"), Is.Zero);
        Assert.That(Read<int>(game, "Wave"), Is.EqualTo(1));
        Assert.That(Read<int>(game, "Shots"), Is.Zero);
    }

    private Component CreateGame(params Vector3[] positions)
    {
        var type = Type.GetType("AppleWatchDemoGame, AppleWatchController.Samples.TargetPop.Runtime");
        Assert.That(type, Is.Not.Null, "The demo needs a playable target game, not only a telemetry display.");
        root = new GameObject("Game test");
        root.SetActive(false);
        var player = new GameObject("Player").transform;
        player.SetParent(root.transform);
        var aim = new GameObject("Aim").transform;
        aim.SetParent(root.transform);
        var targets = new Transform[positions.Length];
        for (var i = 0; i < positions.Length; i++)
        {
            targets[i] = new GameObject("Target" + i).transform;
            targets[i].SetParent(root.transform);
            targets[i].position = positions[i];
        }
        var game = root.AddComponent(type);
        Set(game, "player", player);
        Set(game, "aim", aim);
        Set(game, "targets", targets);
        root.SetActive(true);
        Call(game, "ResetGame");
        return game;
    }

    private static void Set(Component component, string name, object value) =>
        component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(component, value);
    private static T Read<T>(Component component, string name) => (T)component.GetType().GetProperty(name).GetValue(component);
    private static void Call(Component component, string name, params object[] args) => component.GetType().GetMethod(name).Invoke(component, args);
}
