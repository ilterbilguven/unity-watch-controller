# Basic Controller

Import this sample through Package Manager. It requires only the package's Input
System and URP 17.7.0 dependencies. Configure a URP pipeline with a valid default
renderer before setup; Pipeline is optional. Select Tools > AppleWatch > Open Basic
Controller, then select this imported folder under Assets. Setup opens an
existing scene without overwriting it. Save dirty scenes before setup.

The setup API is `BasicControllerSampleSetup.Create(sampleRoot, scenePath,
makeStartupScene: false)` in `AppleWatchController.Samples.BasicController.Editor`.
Setup does not change build order unless you explicitly pass `true`.

Open the included scene and enter Play Mode. WASD moves the target; Space fires.
Export `Controller.watch.uxml` through the package's Watch UI menu for the Watch
joystick and Fire button. Configure signing and device deployment separately.

Check that movement changes the target position. Press Fire once and check that
the displayed fire count increases by one. A held press must not repeat fire.
The target briefly grows on fire. Editor simulation does not prove physical
Watch connectivity or latency.
