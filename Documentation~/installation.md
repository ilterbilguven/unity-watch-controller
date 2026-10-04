# Install the package

[Guide index](index.md) · Next: [Getting started](getting-started.md)

## Prepare a Unity project

Use Unity 6000.7.0b2 with a **Universal 3D** project. The package supports URP only and declares URP 17.7.0 and Input System 6.7.0 as dependencies. Installing the package does not select or create your project's URP settings.

For an existing project, configure a URP asset and a valid default renderer in **Edit > Project Settings > Graphics**, and check overrides in **Quality**. A dependency in Package Manager alone does not make URP active. Sample setup rejects an absent pipeline, invalid default renderer, or missing required shaders before creating assets. Do not switch a production project's pipeline just to try this package; use a small test project if necessary.

For native export, install **iOS Build Support** for this Editor through Unity Hub. Use a Mac with Xcode and iOS/watchOS SDKs that can build iOS 18 and watchOS 11 applications. Unity's Swift exporter is experimental. This package does not provide an Objective-C fallback or support older Unity releases. You can test Editor input before setting up physical devices.

## Use Package Manager

Open **Window > Package Management > Package Manager**.

If the [public repository](https://github.com/ilterbilguven/unity-watch-controller) is accessible, open the **+** menu, choose **Install package from git URL**, and enter:

```text
https://github.com/ilterbilguven/unity-watch-controller.git
```

This route follows the repository's default branch. To freeze a version after its release tag exists, append `#v0.1.0-preview.1`. Do not assume that a version in `package.json` means its tag or repository is already available. Publication is pending for this initial preview.

If you already have a local checkout or package archive, choose **Install package from disk** and select its root `package.json`. Extract an archive first. Do not select a sample's folder or copy package scripts into `Assets`.

Wait for Package Manager to finish and for Unity to compile. Select **Apple Watch Controller** and confirm its dependencies. Resolve Console errors before importing samples. Package import must not change your bundle identifier, signing, build scene order, or current scene.

Open **Edit > Project Settings > Player**, expand **Other Settings**, and set **Active Input Handling** to **Input System Package (New)** or **Both**. Accept Unity's restart if requested. This is an explicit consumer setting; the package does not silently rewrite it.

## Import a sample

Select this package in Package Manager and expand **Samples**. Click **Import** for **Basic Controller**. Unity copies editable assets into:

```text
Assets/Samples/Apple Watch Controller/0.1.0-preview.1/Basic Controller/
```

Samples are optional. Import **Watch Lab** for the full Watch interface and import **Target Pop** for the game demo. The [sample guide](samples.md) explains their relationship and exact paths. Importing a sample does not add its scene to your build or overwrite the scene you are editing.

## Registry status and manual manifest edits

OpenUPM submission is pending. Do not add a registry for this package or run a registry installation command until a version has been accepted and its availability has been verified. A repository, release tag, and accepted registry version are separate steps.

Package Manager is the preferred route. If you maintain `Packages/manifest.json` by hand, add only the new dependency. Preserve all existing `dependencies`, `scopedRegistries`, and other fields. The example below shows a merge into a project that already uses Input System and URP; it is not a replacement manifest:

```json
{
  "scopedRegistries": [
    {
      "name": "Existing project registry",
      "url": "https://registry.example.com",
      "scopes": ["com.example"]
    }
  ],
  "dependencies": {
    "com.unity.inputsystem": "6.7.0",
    "com.unity.render-pipelines.universal": "17.7.0",
    "com.ilter.apple-watch-controller": "https://github.com/ilterbilguven/unity-watch-controller.git"
  }
}
```

Keep your actual registry entries and every existing dependency, even when they do not appear in this example. The example registry is not a package distribution endpoint.
