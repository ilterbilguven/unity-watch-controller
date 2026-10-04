# Watch Lab

Import this sample through Package Manager. Select
`WatchControllerSample.watch.uxml`, then use the Watch UI preview or export menu.
The UXML and USS are the authored source; Swift is generated output.

The gallery covers controls, touch, hand gestures, Digital Crown, motion, audio,
and components. Direct-input pages do not scroll; long value pages do. Motion
includes opt-in attitude at 20 Hz. Hand activation requires a supported Watch
and system configuration. Preview icons use Material Design Icons; generated
Swift keeps native system symbols, so their artwork differs.

Watch Lab and Target Pop can live in different folders or sample-version directories.
Target Pop's required setup API resolves this source by its preserved asset GUID,
not a global search or sibling path. The setup menu asks you to select the source;
`CreateWithWatchSource` and CLI `--watch_ui_path` accept another authored source
under Assets. Other Watch sources do not affect the GUID-based default. If this
designated source is absent, setup fails before writing; it does not choose a
different source automatically. The package supports URP 17.7.0 projects only.
Pipeline is optional.
