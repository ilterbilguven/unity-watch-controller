# Third-party notices

## Material Design Icons

Unity's Editor-only Watch preview uses unmodified white, double-density PNGs
from Google Material Design Icons 3.0.1, revision
`40ef0f297970f0e78bf87569f43ea90ec53d571c`. Google distributes these images
under Apache License 2.0. The complete license is
`Editor/WatchUI/Resources/WatchUiSymbols/Material-Icons-LICENSE.txt`.

The preview uses filename aliases to preserve its Resources keys. All original
paths have the form `<category>/drawable-xhdpi/ic_<name>_white_24dp.png`.

| Preview filename (without `.png`) | Original category/name |
| --- | --- |
| `gamecontroller` | `hardware/gamepad` |
| `slider.horizontal.3` | `image/tune` |
| `hand.tap` | `action/touch_app` |
| `digitalcrown.horizontal.arrow.clockwise` | `image/rotate_right` |
| `gyroscope` | `action/3d_rotation` |
| `mic` | `av/mic` |
| `square.grid.2x2` | `navigation/apps` |
| `chevron.right` | `navigation/chevron_right` |
| `wifi` | `notification/wifi` |
| `star` | `toggle/star` |

This preview artwork differs from native Apple system symbols. Generated Swift
keeps the authored system-symbol names. No Apple symbol PNGs are bundled.

## Inter

Target Pop includes Inter by the Inter Project Authors, under the SIL Open Font
License 1.1. Its complete notice is `Samples~/TargetPop/Demo/Inter-OFL.txt`.
The font and generated font assets retain that license; they are not MIT assets.

## Dependencies

Unity Input System and Universal Render Pipeline are resolved separately by
Unity Package Manager. Their licenses and notices remain in those dependencies.
They are not MIT-owned package assets. Apple frameworks are provided by the
platform SDK and are not redistributed by this package.
