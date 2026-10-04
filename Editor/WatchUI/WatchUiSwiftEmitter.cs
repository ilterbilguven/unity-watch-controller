using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WatchUI
{
    /// <summary>Stores one supported Watch control in the intermediate tree used for SwiftUI generation.</summary>
    /// <remarks>
    /// The converter fills the fields that apply to kind. Other fields remain unused for that kind.
    /// This is mutable export data, not a live UI binding or an Input System state object.
    /// Except for explicit field initializers, direct construction uses C# defaults; it does not apply
    /// each authoring control's defaults. Prefer WatchUiElementConverter or fill required fields before validation.
    /// Children contain authored content only. Presentation holds the supported resolved USS subset.
    /// </remarks>
    [Serializable]
    public sealed class WatchUiNode
    {
        /// <summary>The exact supported control type name, such as WatchVStack or WatchButton. Required for export.</summary>
        public string kind;
        /// <summary>The authored watch-id or converter-assigned fallback used for source tracing and generated state names.</summary>
        public string id;
        /// <summary>Display text for text, controls, sensors, or picker options, according to kind.</summary>
        public string label;
        /// <summary>Action metadata copied from action or submit-action. This does not create an arbitrary gameplay route.</summary>
        public string action;
        /// <summary>None or PrimaryAction. Only one supported buttonlike control per exported scene may use PrimaryAction.</summary>
        public string handGestureShortcut;
        /// <summary>The named native image asset, used when imageSystemName is empty.</summary>
        public string imageName;
        /// <summary>The SF Symbols name, which takes priority over the other image source fields.</summary>
        public string imageSystemName;
        /// <summary>The fallback image reference. The emitter treats it as a native image name, not a file-copy instruction.</summary>
        public string imageSource;
        /// <summary>The minimum spacer length in points. Negative or non-finite values use native default sizing.</summary>
        public float minLength;
        /// <summary>The title for a label, navigation link, or text field.</summary>
        public string title;
        /// <summary>The optional SF Symbols name for a label or picker option.</summary>
        public string systemImage;
        /// <summary>The initial native toggle state.</summary>
        public bool boolValue;
        /// <summary>An explicit image tint string. Empty leaves supported resolved styling in control.</summary>
        public string foregroundColor;
        /// <summary>The lower numeric bound for a slider, stepper, or gauge, in the same units as value.</summary>
        public float minimum;
        /// <summary>The upper numeric bound for a slider, stepper, or gauge. It must exceed minimum.</summary>
        public float maximum;
        /// <summary>The numeric increment. Steppers require a positive value; sliders omit stepping when nonpositive.</summary>
        public float step;
        /// <summary>The numeric control or display value. NaN requests indeterminate progress for WatchProgressView.</summary>
        public float value;
        /// <summary>Total work for determinate progress, in the same units as value. It must be positive.</summary>
        public float total;
        /// <summary>The semantic stack or grid alignment. Accepted values depend on kind.</summary>
        public string alignment;
        /// <summary>Stack spacing in points. A negative value omits the explicit native spacing argument.</summary>
        public float spacing;
        /// <summary>Scroll axes: vertical, horizontal, or both.</summary>
        public string axes;
        /// <summary>Whether a scroll view shows native scroll indicators. Defaults to true.</summary>
        public bool showsIndicators = true;
        /// <summary>The nonnegative grid column gap in points.</summary>
        public float horizontalSpacing;
        /// <summary>The nonnegative grid row gap in points.</summary>
        public float verticalSpacing;
        /// <summary>Optional text-field placeholder text.</summary>
        public string prompt;
        /// <summary>Required text-field binding metadata. The current emitter validates it but derives state from id.</summary>
        public string textBinding;
        /// <summary>The exact named route used by a navigation link or direct child navigation destination.</summary>
        public string route;
        /// <summary>The list, picker, progress, or gauge style name; accepted values depend on kind.</summary>
        public string style;
        /// <summary>The selected picker key, or the key represented by a picker option.</summary>
        public string selection;
        /// <summary>Optional display text for a gauge's current reading.</summary>
        public string currentValueLabel;
        /// <summary>The touch-gesture family, such as tap, doubleTap, longPress, swipe, or drag.</summary>
        public string gesture;
        /// <summary>The drag or swipe recognition threshold in points.</summary>
        public float minimumDistance;
        /// <summary>The minimum long-press duration in seconds.</summary>
        public float minimumDuration;
        /// <summary>The maximum movement allowed during a long press, in points. Defaults to 20.</summary>
        public float maximumDistance = 20f;
        /// <summary>The requested motion or microphone-meter update interval in seconds, not an audio sample rate.</summary>
        public float updateInterval;
        /// <summary>Whether motion payloads include Core Motion user acceleration without gravity.</summary>
        public bool includeAcceleration;
        /// <summary>Whether motion payloads include Core Motion rotation rate.</summary>
        public bool includeGyro;
        /// <summary>Whether motion payloads include the x/y/z/w attitude quaternion and reference metadata.</summary>
        public bool includeAttitude;
        /// <summary>The generated drag coordinate space: global or local.</summary>
        public string coordinateSpace;
        /// <summary>The positive joystick surface radius and normalization scale in points.</summary>
        public float radius;
        /// <summary>The joystick's minimum drag distance in points, from zero through radius.</summary>
        public float deadZone;
        /// <summary>Whether a joystick's release payload returns its axes to zero. Defaults to true.</summary>
        public bool springReturn = true;
        /// <summary>Authored USS class names retained as metadata, excluding preview-only classes.</summary>
        public string[] classes;
        /// <summary>The authored control-enabled flag. Defaults to true.</summary>
        public bool enabled = true;
        /// <summary>Whether a text field allows native autocorrection. Defaults to true.</summary>
        public bool autocorrection = true;
        /// <summary>The ordered authored child nodes, or null when there are none.</summary>
        public WatchUiNode[] children;
        /// <summary>The supported panel-resolved style snapshot, or null when no snapshot was captured.</summary>
        public WatchUiStyleSnapshot presentation;
    }

    /// <summary>Collects conversion and emission messages for one generation attempt.</summary>
    /// <remarks>Errors block validated generation. Warnings describe fallback behavior or known export limits.</remarks>
    public sealed class WatchUiConversionDiagnostics
    {
        /// <summary>Mutable error messages in discovery order. The list reference is fixed, but its contents can change.</summary>
        public readonly List<string> Errors = new List<string>();
        /// <summary>Mutable warning messages in discovery order. Warnings alone do not fail validated generation.</summary>
        public readonly List<string> Warnings = new List<string>();
        /// <summary>Gets whether at least one error has been recorded.</summary>
        public bool HasErrors => Errors.Count > 0;
    }

    /// <summary>Emits SwiftUI and supporting Watch source from the supported WatchUiNode tree.</summary>
    /// <remarks>
    /// Prefer TryToSwiftUI to receive validation errors. The result is source text, not a compiled app.
    /// The emitter does not copy image assets, configure Xcode signing, or prove native rendering and device input.
    /// Action strings remain metadata; supported control kinds and payloads determine the transport's input semantics.
    /// </remarks>
    public static class WatchUiSwiftEmitter
    {
        private sealed class WatchUiSwiftStateContext
        {
            private int fallbackCounter;

            public readonly Dictionary<WatchUiNode, string> ToggleStateNames = new();
            public readonly Dictionary<WatchUiNode, string> SliderStateNames = new();
            public readonly Dictionary<WatchUiNode, string> TextFieldStateNames = new();
            public readonly Dictionary<WatchUiNode, string> StepperStateNames = new();
            public readonly Dictionary<WatchUiNode, string> PickerStateNames = new();
            public readonly Dictionary<WatchUiNode, (string Value, string Previous, string Focus)> CrownStateNames = new();
            public readonly HashSet<string> UsedStateNames = new();

            public (string Value, string Previous, string Focus) ResolveCrownStateNames(WatchUiNode node)
            {
                if (CrownStateNames.TryGetValue(node, out var found))
                    return found;

                var names = (EnsureUniqueIdentifier("watchCrownValue"),
                    EnsureUniqueIdentifier("watchCrownPreviousValue"),
                    EnsureUniqueIdentifier("watchCrownFocused"));
                CrownStateNames[node] = names;
                return names;
            }

            public string ResolveToggleStateName(WatchUiNode node)
            {
                if (node == null)
                    return string.Empty;

                if (ToggleStateNames.TryGetValue(node, out var found))
                    return found;

                var baseName = string.IsNullOrWhiteSpace(node.id)
                    ? $"watchToggle_{++fallbackCounter}"
                    : node.id;
                var safeName = MakeSwiftIdentifier(baseName);
                var stateName = EnsureUniqueIdentifier(safeName);
                ToggleStateNames[node] = stateName;
                return stateName;
            }

            public string ResolveSliderStateName(WatchUiNode node)
            {
                if (node == null)
                    return string.Empty;

                if (SliderStateNames.TryGetValue(node, out var found))
                    return found;

                var baseName = string.IsNullOrWhiteSpace(node.id)
                    ? $"watchSlider_{++fallbackCounter}"
                    : node.id;
                var safeName = MakeSwiftIdentifier(baseName) + "Value";
                var stateName = EnsureUniqueIdentifier(safeName);
                SliderStateNames[node] = stateName;
                return stateName;
            }

            public string ResolveTextFieldStateName(WatchUiNode node)
            {
                if (node == null)
                    return string.Empty;

                if (TextFieldStateNames.TryGetValue(node, out var found))
                    return found;

                var baseName = string.IsNullOrWhiteSpace(node.id)
                    ? $"watchText_{++fallbackCounter}"
                    : node.id;
                var safeName = MakeSwiftIdentifier(baseName) + "Text";
                var stateName = EnsureUniqueIdentifier(safeName);
                TextFieldStateNames[node] = stateName;
                return stateName;
            }

            public string ResolveStepperStateName(WatchUiNode node)
            {
                if (node == null)
                    return string.Empty;

                if (StepperStateNames.TryGetValue(node, out var found))
                    return found;

                var baseName = string.IsNullOrWhiteSpace(node.id)
                    ? $"watchStepper_{++fallbackCounter}"
                    : node.id;
                var stateName = EnsureUniqueIdentifier(MakeSwiftIdentifier(baseName) + "Value");
                StepperStateNames[node] = stateName;
                return stateName;
            }

            public string ResolvePickerStateName(WatchUiNode node)
            {
                if (node == null)
                    return string.Empty;

                if (PickerStateNames.TryGetValue(node, out var found))
                    return found;

                var baseName = string.IsNullOrWhiteSpace(node.id)
                    ? $"watchPicker_{++fallbackCounter}"
                    : node.id;
                var stateName = EnsureUniqueIdentifier(MakeSwiftIdentifier(baseName) + "Selection");
                PickerStateNames[node] = stateName;
                return stateName;
            }

            private static string MakeSwiftIdentifier(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return "watchState";

                var source = value.Trim();
                var normalizedChars = new StringBuilder();
                foreach (var ch in source)
                {
                    if (char.IsLetterOrDigit(ch) || ch == '_')
                    {
                        normalizedChars.Append(ch);
                        continue;
                    }

                    normalizedChars.Append('_');
                }

                var normalized = normalizedChars.ToString();
                if (string.IsNullOrWhiteSpace(normalized))
                    normalized = "watchState";

                if (!char.IsLetter(normalized[0]) && normalized[0] != '_')
                {
                    normalized = "_" + normalized;
                }

                return normalized;
            }

            private string EnsureUniqueIdentifier(string identifier)
            {
                if (!UsedStateNames.Contains(identifier))
                {
                    UsedStateNames.Add(identifier);
                    return identifier;
                }

                var suffix = 2;
                while (true)
                {
                    var candidate = $"{identifier}_{suffix}";
                    if (!UsedStateNames.Contains(candidate))
                    {
                        UsedStateNames.Add(candidate);
                        return candidate;
                    }

                    suffix++;
                }
            }
        }

        private static readonly HashSet<string> SupportedKinds = new HashSet<string>
        {
            "WatchVStack",
            "WatchHStack",
            "WatchZStack",
            "WatchScrollView",
            "WatchList",
            "WatchGrid",
            "WatchGridRow",
            "WatchNavigationStack",
            "WatchNavigationLink",
            "WatchNavigationDestination",
            "WatchText",
            "WatchSpacer",
            "WatchButton",
            "WatchTouchpad",
            "WatchGestureArea",
            "WatchCrown",
            "WatchStatus",
            "WatchToggle",
            "WatchSlider",
            "WatchLabel",
            "WatchDivider",
            "WatchImage",
            "WatchTextField",
            "WatchMotion",
            "WatchMicrophone",
            "WatchProgressView",
            "WatchStepper",
            "WatchPicker",
            "WatchPickerOption",
            "WatchGauge",
            "WatchJoystick"
        };

        /// <summary>Emits SwiftUI directly without the normal node-validation pass.</summary>
        /// <param name="root">The already validated export tree.</param>
        /// <param name="hadWarnings">True when emission recorded at least one warning; this is not a success flag.</param>
        /// <returns>Generated source, or empty when emission's hand-shortcut validation blocks output.</returns>
        /// <remarks>
        /// This compatibility entry point does not return errors and is not suitable for unchecked authoring input.
        /// Prefer TryToSwiftUI. Neither method writes files or compiles the result.
        /// </remarks>
        public static string ToSwiftUI(WatchUiNode root, out bool hadWarnings)
        {
            var diagnostics = new WatchUiConversionDiagnostics();
            var swift = EmitSwiftUI(root, diagnostics);
            hadWarnings = diagnostics.Warnings.Count > 0;
            return swift;
        }

        private static string EmitSwiftUI(WatchUiNode root, WatchUiConversionDiagnostics diagnostics)
        {
            ValidateHandGestureShortcuts(root, diagnostics);
            if (diagnostics.HasErrors)
                return string.Empty;

            var builder = new StringBuilder();
            var stateContext = new WatchUiSwiftStateContext();
            builder.AppendLine("import Foundation");
            builder.AppendLine("import WatchConnectivity");
            builder.AppendLine("import SwiftUI");
            builder.AppendLine("import CoreMotion");
            builder.AppendLine("import AVFoundation");
            builder.AppendLine();
            builder.AppendLine("#if os(watchOS)");
            builder.AppendLine("private final class WatchConnectivitySession: NSObject, WCSessionDelegate {");
            builder.AppendLine("    static let shared = WatchConnectivitySession()");
            builder.AppendLine("    private let session: WCSession");
            builder.AppendLine("    private var pendingRecordings: [URL: String] = [:]");
            builder.AppendLine("    private var inFlightRecordingURLs = Set<URL>()");
            builder.AppendLine("    private override init() {");
            builder.AppendLine("        session = WCSession.default");
            builder.AppendLine("        super.init()");
            builder.AppendLine("        session.delegate = self");
            builder.AppendLine("        session.activate()");
            builder.AppendLine("    }");
            builder.AppendLine("    func start() {");
            builder.AppendLine("        if session.activationState != .activated {");
            builder.AppendLine("            session.activate()");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("    func send(_ message: [String: Any]) {");
            builder.AppendLine("        guard session.activationState == .activated, session.isReachable else { return }");
            builder.AppendLine("        session.sendMessage(message, replyHandler: nil) { error in");
            builder.AppendLine("            print(\"AppleWatch send failed: \\(error.localizedDescription)\")");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("    func transferRecording(_ fileURL: URL, sourceId: String) {");
            builder.AppendLine("        pendingRecordings[fileURL] = sourceId");
            builder.AppendLine("        enqueuePendingRecording(fileURL)");
            builder.AppendLine("    }");
            builder.AppendLine("    private func enqueuePendingRecording(_ fileURL: URL) {");
            builder.AppendLine("        guard let sourceId = pendingRecordings[fileURL], !inFlightRecordingURLs.contains(fileURL) else { return }");
            builder.AppendLine("        guard session.activationState == .activated else {");
            builder.AppendLine("            print(\"AppleWatch recording retained until activation: sourceId=\\(sourceId), file=\\(fileURL.path)\")");
            builder.AppendLine("            return");
            builder.AppendLine("        }");
            builder.AppendLine("        inFlightRecordingURLs.insert(fileURL)");
            builder.AppendLine("        session.transferFile(fileURL, metadata: [\"kind\": \"watch-audio-recording\", \"source-id\": sourceId])");
            builder.AppendLine("    }");
            builder.AppendLine("    func session(_ session: WCSession, didFinish fileTransfer: WCSessionFileTransfer, error: Error?) {");
            builder.AppendLine("        let fileURL = fileTransfer.file.fileURL");
            builder.AppendLine("        let failureDescription = error?.localizedDescription");
            builder.AppendLine("        DispatchQueue.main.async { [weak self] in");
            builder.AppendLine("            guard let self, self.inFlightRecordingURLs.remove(fileURL) != nil, let sourceId = self.pendingRecordings[fileURL] else { return }");
            builder.AppendLine("            if let failureDescription {");
            builder.AppendLine("                print(\"AppleWatch recording transfer failed; retained sourceId=\\(sourceId), file=\\(fileURL.path): \\(failureDescription)\")");
            builder.AppendLine("                return");
            builder.AppendLine("            }");
            builder.AppendLine("            self.pendingRecordings.removeValue(forKey: fileURL)");
            builder.AppendLine("            try? FileManager.default.removeItem(at: fileURL)");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("    func session(_ session: WCSession, activationDidCompleteWith activationState: WCSessionActivationState, error: Error?) {");
            builder.AppendLine("        if let error { print(\"AppleWatch WCSession activation failed: \\(error.localizedDescription)\") }");
            builder.AppendLine("        guard activationState == .activated, error == nil else { return }");
            builder.AppendLine("        DispatchQueue.main.async { [weak self] in");
            builder.AppendLine("            guard let self else { return }");
            builder.AppendLine("            for fileURL in Array(self.pendingRecordings.keys) {");
            builder.AppendLine("                self.enqueuePendingRecording(fileURL)");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            builder.AppendLine("#endif");
            builder.AppendLine();
            builder.AppendLine("private final class WatchUiActionSink {");
            builder.AppendLine("    static let shared = WatchUiActionSink()");
            builder.AppendLine("    func send(action: String, sourceId: String, kind: String, kindId: Int = 0, payload: [String: Any]? = nil) {");
            builder.AppendLine("        #if os(watchOS)");
            builder.AppendLine("            let resolvedKindId = kindId != 0 ? kindId : WatchUiActionKindId(kind)");
            builder.AppendLine("            let isKnownKindId = kindId == 0 ? resolvedKindId > 0 : (1...19).contains(kindId)");
            builder.AppendLine("            print(\"AppleWatch UI action: action=\\(action), sourceId=\\(sourceId), kind=\\(kind), kindId=\\(resolvedKindId)\")");
            builder.AppendLine("            var message: [String: Any] = [");
            builder.AppendLine("                \"watch-action\": true,");
            builder.AppendLine("                \"action\": action,");
            builder.AppendLine("                \"source-id\": sourceId,");
            builder.AppendLine("                \"kind\": kind");
            builder.AppendLine("            ]");
            builder.AppendLine("            if isKnownKindId {");
            builder.AppendLine("                message[\"kind-id\"] = resolvedKindId");
            builder.AppendLine("            }");
            builder.AppendLine("            if let payload {");
            builder.AppendLine("                message.merge(payload, uniquingKeysWith: { _, payloadValue in payloadValue })");
            builder.AppendLine("            }");
            builder.AppendLine("            WatchConnectivitySession.shared.send(message)");
            builder.AppendLine("        #else");
            builder.AppendLine("            print(\"AppleWatch UI action: action=\\(action), sourceId=\\(sourceId), kind=\\(kind)\")");
            builder.AppendLine("        #endif");
            builder.AppendLine("    }");
            builder.AppendLine("    private func WatchUiActionKindId(_ kind: String) -> Int {");
            builder.AppendLine("        let normalized = kind.lowercased()");
            builder.AppendLine("        switch normalized {");
            builder.AppendLine("        case \"watchbutton\":");
            builder.AppendLine("            return 1;");
            builder.AppendLine("        case \"watchtouchpad/tap\":");
            builder.AppendLine("            return 2;");
            builder.AppendLine("        case \"watchtouchpad/doubletap\":");
            builder.AppendLine("            return 3;");
            builder.AppendLine("        case \"watchtouchpad/longpress\":");
            builder.AppendLine("            return 4;");
            builder.AppendLine("        case \"watchtouchpad/drag\":");
            builder.AppendLine("            return 5;");
            builder.AppendLine("        case \"watchcrown/press\":");
            builder.AppendLine("            return 6;");
            builder.AppendLine("        case \"watchcrown/rotate\":");
            builder.AppendLine("            return 7;");
            builder.AppendLine("        case \"watchgesturearea/tap\":");
            builder.AppendLine("            return 8;");
            builder.AppendLine("        case \"watchgesturearea/doubletap\":");
            builder.AppendLine("            return 9;");
            builder.AppendLine("        case \"watchgesturearea/longpress\":");
            builder.AppendLine("            return 10;");
            builder.AppendLine("        case \"watchgesturearea/swipe-up\":");
            builder.AppendLine("            return 11;");
            builder.AppendLine("        case \"watchgesturearea/swipe-down\":");
            builder.AppendLine("            return 12;");
            builder.AppendLine("        case \"watchgesturearea/swipe-left\":");
            builder.AppendLine("            return 13;");
            builder.AppendLine("        case \"watchgesturearea/swipe-right\":");
            builder.AppendLine("            return 14;");
            builder.AppendLine("        case \"watchgesturearea/drag\":");
            builder.AppendLine("            return 15;");
            builder.AppendLine("        case \"watchtoggle\":");
            builder.AppendLine("            return 16;");
            builder.AppendLine("        case \"watchslider\":");
            builder.AppendLine("            return 17;");
            builder.AppendLine("        case \"watchtextfield/change\":");
            builder.AppendLine("            return 18;");
            builder.AppendLine("        case \"watchtextfield/submit\":");
            builder.AppendLine("            return 19;");
            builder.AppendLine("        default:");
            builder.AppendLine("            return 0;");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            builder.AppendLine("#if os(watchOS)");
            builder.AppendLine("private final class WatchSensorBridge {");
            builder.AppendLine("    static let shared = WatchSensorBridge()");
            builder.AppendLine("    private let sink = WatchUiActionSink()");
            builder.AppendLine("    private var activeMotionTargets: [String: MotionTargetConfig] = [:]");
            builder.AppendLine("    private var activeMotionTimers: [String: Timer] = [:]");
            builder.AppendLine("    private var lastMotionTimestamps: [String: TimeInterval] = [:]");
            builder.AppendLine("    private var motionReference: UInt32 = UInt32.random(in: 1...UInt32.max)");
            builder.AppendLine("    private var motionStartTimestamp: TimeInterval?");
            builder.AppendLine("    private var activeMicrophoneTargets: [String: String] = [:]");
            builder.AppendLine("    private var activeMicrophoneTimers: [String: Timer] = [:]");
            builder.AppendLine("    private var microphoneSourceId: String?");
            builder.AppendLine("    private var microphoneRequestToken: UUID?");
            builder.AppendLine("    private var isSceneActive = false");
            builder.AppendLine("    private let motionManager = CMMotionManager()");
            builder.AppendLine("    private var microphoneRecorder: AVAudioRecorder?");
            builder.AppendLine("    private struct MotionTargetConfig {");
            builder.AppendLine("        let action: String");
            builder.AppendLine("        let sourceId: String");
            builder.AppendLine("        let interval: TimeInterval");
            builder.AppendLine("        let includeAcceleration: Bool");
            builder.AppendLine("        let includeGyro: Bool");
            builder.AppendLine("        let includeAttitude: Bool");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    func setSceneActive(_ active: Bool) {");
            builder.AppendLine("        isSceneActive = active");
            builder.AppendLine("        guard !active else { return }");
            builder.AppendLine("        for sourceId in Array(activeMotionTargets.keys) {");
            builder.AppendLine("            stopMotion(sourceId: sourceId)");
            builder.AppendLine("        }");
            builder.AppendLine("        if let sourceId = microphoneSourceId {");
            builder.AppendLine("            stopMicrophone(sourceId: sourceId)");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    func startMotion(sourceId: String, action: String, interval: TimeInterval, includeAcceleration: Bool, includeGyro: Bool, includeAttitude: Bool = false) {");
            builder.AppendLine("        guard isSceneActive else { return }");
            builder.AppendLine("        stopMotion(sourceId: sourceId)");
            builder.AppendLine("        let safeInterval = max(0.05, min(2.0, interval))");
            builder.AppendLine("        guard includeAcceleration || includeGyro || includeAttitude else { return }");
            builder.AppendLine("        if !motionManager.isDeviceMotionAvailable { return }");
            builder.AppendLine("        let config = MotionTargetConfig(action: action, sourceId: sourceId, interval: safeInterval, includeAcceleration: includeAcceleration, includeGyro: includeGyro, includeAttitude: includeAttitude)");
            builder.AppendLine("        activeMotionTargets[sourceId] = config");
            builder.AppendLine("        updateMotionInterval()");
            builder.AppendLine("        if !motionManager.isDeviceMotionActive {");
            builder.AppendLine("            motionStartTimestamp = motionManager.deviceMotion?.timestamp");
            builder.AppendLine("            motionReference &+= 1");
            builder.AppendLine("            if motionReference == 0 { motionReference = 1 }");
            builder.AppendLine("            motionManager.startDeviceMotionUpdates(using: .xArbitraryCorrectedZVertical)");
            builder.AppendLine("        }");
            builder.AppendLine("        let timer = Timer(timeInterval: safeInterval, repeats: true) { [weak self] _ in");
            builder.AppendLine("            self?.emitMotionFrame(sourceId: sourceId)");
            builder.AppendLine("        }");
            builder.AppendLine("        activeMotionTimers[sourceId] = timer");
            builder.AppendLine("        RunLoop.main.add(timer, forMode: .common)");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    func stopMotion(sourceId: String) {");
            builder.AppendLine("        if let timer = activeMotionTimers.removeValue(forKey: sourceId) {");
            builder.AppendLine("            timer.invalidate()");
            builder.AppendLine("        }");
            builder.AppendLine("        activeMotionTargets.removeValue(forKey: sourceId)");
            builder.AppendLine("        lastMotionTimestamps.removeValue(forKey: sourceId)");
            builder.AppendLine("        if activeMotionTargets.isEmpty && motionManager.isDeviceMotionActive {");
            builder.AppendLine("            motionManager.stopDeviceMotionUpdates()");
            builder.AppendLine("        } else {");
            builder.AppendLine("            updateMotionInterval()");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private func updateMotionInterval() {");
            builder.AppendLine("        if let fastest = activeMotionTargets.values.map({ $0.interval }).min() {");
            builder.AppendLine("            motionManager.deviceMotionUpdateInterval = fastest");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    func requestAndStartMicrophone(sourceId: String, action: String, interval: TimeInterval) {");
            builder.AppendLine("        guard isSceneActive else { return }");
            builder.AppendLine("        if let previousSourceId = microphoneSourceId {");
            builder.AppendLine("            stopMicrophone(sourceId: previousSourceId)");
            builder.AppendLine("        }");
            builder.AppendLine("        let requestToken = UUID()");
            builder.AppendLine("        microphoneSourceId = sourceId");
            builder.AppendLine("        microphoneRequestToken = requestToken");
            builder.AppendLine("        switch AVAudioApplication.shared.recordPermission {");
            builder.AppendLine("        case .granted:");
            builder.AppendLine("            startMicrophone(sourceId: sourceId, action: action, interval: interval, requestToken: requestToken)");
            builder.AppendLine("        case .undetermined:");
            builder.AppendLine("            AVAudioApplication.requestRecordPermission { [weak self] granted in");
            builder.AppendLine("                DispatchQueue.main.async {");
            builder.AppendLine("                    guard let self, self.microphoneRequestToken == requestToken else { return }");
            builder.AppendLine("                    if granted {");
            builder.AppendLine("                        self.startMicrophone(sourceId: sourceId, action: action, interval: interval, requestToken: requestToken)");
            builder.AppendLine("                    } else {");
            builder.AppendLine("                        self.stopMicrophone(sourceId: sourceId)");
            builder.AppendLine("                    }");
            builder.AppendLine("                }");
            builder.AppendLine("            }");
            builder.AppendLine("        default:");
            builder.AppendLine("            stopMicrophone(sourceId: sourceId)");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private func startMicrophone(sourceId: String, action: String, interval: TimeInterval, requestToken: UUID) {");
            builder.AppendLine("        guard isSceneActive, microphoneSourceId == sourceId, microphoneRequestToken == requestToken else { return }");
            builder.AppendLine("        let safeInterval = max(0.05, min(2.0, interval))");
            builder.AppendLine("        let session = AVAudioSession.sharedInstance()");
            builder.AppendLine("        let tempPath = FileManager.default.temporaryDirectory.appendingPathComponent(\"watchsensor_\" + UUID().uuidString + \".caf\")");
            builder.AppendLine("        guard let recorder = createRecorder(url: tempPath) else { return }");
            builder.AppendLine("        do {");
            builder.AppendLine("            try session.setCategory(.playAndRecord)");
            builder.AppendLine("            try session.setActive(true)");
            builder.AppendLine("            recorder.isMeteringEnabled = true");
            builder.AppendLine("            recorder.record()");
            builder.AppendLine("        } catch {");
            builder.AppendLine("            return");
            builder.AppendLine("        }");
            builder.AppendLine("        microphoneRecorder = recorder");
            builder.AppendLine("        activeMicrophoneTargets[sourceId] = action");
            builder.AppendLine("        activeMicrophoneTimers[sourceId] = Timer.scheduledTimer(withTimeInterval: safeInterval, repeats: true) { [weak self] _ in");
            builder.AppendLine("            self?.emitMicrophoneFrame(sourceId: sourceId)");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    func stopMicrophone(sourceId: String) {");
            builder.AppendLine("        finishMicrophone(sourceId: sourceId, transferRecording: false)");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    func stopAndTransferMicrophone(sourceId: String) {");
            builder.AppendLine("        finishMicrophone(sourceId: sourceId, transferRecording: true)");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private func finishMicrophone(sourceId: String, transferRecording: Bool) {");
            builder.AppendLine("        guard microphoneSourceId == sourceId else { return }");
            builder.AppendLine("        microphoneRequestToken = nil");
            builder.AppendLine("        microphoneSourceId = nil");
            builder.AppendLine("        if let timer = activeMicrophoneTimers.removeValue(forKey: sourceId) {");
            builder.AppendLine("            timer.invalidate()");
            builder.AppendLine("        }");
            builder.AppendLine("        activeMicrophoneTargets.removeValue(forKey: sourceId)");
            builder.AppendLine("        if activeMicrophoneTargets.isEmpty {");
            builder.AppendLine("            let recordingURL = microphoneRecorder?.url");
            builder.AppendLine("            microphoneRecorder?.stop()");
            builder.AppendLine("            microphoneRecorder = nil");
            builder.AppendLine("            try? AVAudioSession.sharedInstance().setActive(false)");
            builder.AppendLine("            if let recordingURL, FileManager.default.fileExists(atPath: recordingURL.path) {");
            builder.AppendLine("                if transferRecording {");
            builder.AppendLine("                    WatchConnectivitySession.shared.transferRecording(recordingURL, sourceId: sourceId)");
            builder.AppendLine("                } else {");
            builder.AppendLine("                    try? FileManager.default.removeItem(at: recordingURL)");
            builder.AppendLine("                }");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private func emitMotionFrame(sourceId: String) {");
            builder.AppendLine("        guard isSceneActive, motionManager.isDeviceMotionActive, let config = activeMotionTargets[sourceId], let motion = motionManager.deviceMotion else { return }");
            builder.AppendLine("        let timestamp = motion.timestamp");
            builder.AppendLine("        guard timestamp.isFinite, timestamp >= 0 else { return }");
            builder.AppendLine("        if let motionStartTimestamp, motionStartTimestamp.isFinite, timestamp <= motionStartTimestamp { return }");
            builder.AppendLine("        let previousTimestamp = lastMotionTimestamps[sourceId]");
            builder.AppendLine("        if let previousTimestamp, timestamp <= previousTimestamp { return }");
            builder.AppendLine("        var payload: [String: Any] = [String: Any]()");
            builder.AppendLine("        if config.includeAcceleration {");
            builder.AppendLine("            payload[\"acceleration\"] = [");
            builder.AppendLine("                Float(motion.userAcceleration.x),");
            builder.AppendLine("                Float(motion.userAcceleration.y),");
            builder.AppendLine("                Float(motion.userAcceleration.z)");
            builder.AppendLine("            ]");
            builder.AppendLine("        }");
            builder.AppendLine("        if config.includeGyro {");
            builder.AppendLine("            payload[\"gyro\"] = [");
            builder.AppendLine("                Float(motion.rotationRate.x),");
            builder.AppendLine("                Float(motion.rotationRate.y),");
            builder.AppendLine("                Float(motion.rotationRate.z)");
            builder.AppendLine("            ]");
            builder.AppendLine("        }");
            builder.AppendLine("        if config.includeAttitude {");
            builder.AppendLine("            let quaternion = motion.attitude.quaternion");
            builder.AppendLine("            let attitude = [Float(quaternion.x), Float(quaternion.y), Float(quaternion.z), Float(quaternion.w)]");
            builder.AppendLine("            if attitude.allSatisfy({ $0.isFinite }) && attitude.contains(where: { $0 != 0 }) {");
            builder.AppendLine("                payload[\"attitude\"] = attitude");
            builder.AppendLine("                payload[\"attitudeReference\"] = motionReference");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
            builder.AppendLine("        if payload.isEmpty { return }");
            builder.AppendLine("        payload[\"motionSampleInterval\"] = previousTimestamp.map { Float(timestamp - $0) } ?? Float(0)");
            builder.AppendLine("        lastMotionTimestamps[sourceId] = timestamp");
            builder.AppendLine("        sink.send(action: config.action, sourceId: config.sourceId, kind: \"WatchMotion\", payload: payload)");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private func emitMicrophoneFrame(sourceId: String) {");
            builder.AppendLine("        guard microphoneSourceId == sourceId, let action = activeMicrophoneTargets[sourceId], let recorder = microphoneRecorder else { return }");
            builder.AppendLine("        recorder.updateMeters()");
            builder.AppendLine("        let level = max(-120.0, Double(recorder.averagePower(forChannel: 0)))");
            builder.AppendLine("        let peak = max(-120.0, Double(recorder.peakPower(forChannel: 0)))");
            builder.AppendLine("        let levelLinear = Float((level + 120.0) / 120.0)");
            builder.AppendLine("        let peakLinear = Float((peak + 120.0) / 120.0)");
            builder.AppendLine("        sink.send(action: action, sourceId: sourceId, kind: \"WatchMicrophone\", payload: [\"microphoneLevel\": levelLinear, \"microphonePeak\": peakLinear])");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private func createRecorder(url: URL) -> AVAudioRecorder? {");
            builder.AppendLine("        let settings: [String: Any] = [");
            builder.AppendLine("            AVFormatIDKey: Int(kAudioFormatMPEG4AAC),");
            builder.AppendLine("            AVSampleRateKey: 16000.0,");
            builder.AppendLine("            AVNumberOfChannelsKey: 1,");
            builder.AppendLine("            AVEncoderAudioQualityKey: AVAudioQuality.medium.rawValue");
            builder.AppendLine("        ]");
            builder.AppendLine("        return try? AVAudioRecorder(url: url, settings: settings)");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            builder.AppendLine("#endif");
            builder.AppendLine();
            builder.AppendLine("struct GeneratedWatchView: View {");
            if (HasWatchNavigation(root))
            {
                builder.AppendLine("    @State private var navigationPath: [String] = []");
            }
            EmitStateDeclarations(root, builder, stateContext);
            if (HasWatchCrown(root))
            {
                foreach (var state in stateContext.CrownStateNames.Values)
                {
                    builder.AppendLine($"    @State private var {state.Value}: Double = 0");
                    builder.AppendLine($"    @State private var {state.Previous}: Double = 0");
                    builder.AppendLine($"    @FocusState private var {state.Focus}: Bool");
                }
            }

            builder.AppendLine("    var body: some View {");
            EmitNode(root, builder, 2, diagnostics, stateContext);
            builder.AppendLine("    }");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("#if os(watchOS)");
            builder.AppendLine("@main");
            builder.AppendLine("struct GeneratedWatchApp: App {");
            builder.AppendLine("    @Environment(\\.scenePhase) private var scenePhase");
            builder.AppendLine("    init() {");
            builder.AppendLine("        WatchConnectivitySession.shared.start()");
            builder.AppendLine("    }");
            builder.AppendLine("    var body: some Scene {");
            builder.AppendLine("        WindowGroup {");
            builder.AppendLine("            GeneratedWatchView()");
            builder.AppendLine("        }");
            builder.AppendLine("        .onChange(of: scenePhase, initial: true) { _, phase in");
            builder.AppendLine("            WatchSensorBridge.shared.setSceneActive(phase == .active)");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            builder.AppendLine("#endif");

            return builder.ToString();
        }

        /// <summary>Validates a supported export tree and emits SwiftUI source when no errors remain.</summary>
        /// <param name="root">The export tree. A null root or missing/unsupported root kind produces an error.</param>
        /// <param name="swift">Generated source on success, or empty when validation prevents emission.</param>
        /// <param name="diagnostics">A new collection containing validation and emission messages.</param>
        /// <returns>True when generation finishes without errors. Warnings alone still allow output.</returns>
        /// <remarks>
        /// This checks the supported node contract, including control ranges, required fields, and hand shortcuts.
        /// It does not validate arbitrary Swift, export assets, or replace compilation of the complete Watch target.
        /// </remarks>
        public static bool TryToSwiftUI(WatchUiNode root, out string swift, out WatchUiConversionDiagnostics diagnostics)
        {
            diagnostics = new WatchUiConversionDiagnostics();

            if (root == null)
            {
                diagnostics.Errors.Add("root is null");
                swift = string.Empty;
                return false;
            }

            if (string.IsNullOrEmpty(root.kind))
            {
                diagnostics.Errors.Add("root.kind is required");
                swift = string.Empty;
                return false;
            }

            if (!SupportedKinds.Contains(root.kind))
            {
                diagnostics.Errors.Add(
                    string.Format(
                        "Unsupported root kind '{0}'. Supported kinds: {1}.",
                        root.kind,
                        string.Join(", ", SupportedKinds)));
                swift = string.Empty;
                return false;
            }

            if (!ValidateNode(root, diagnostics))
            {
                swift = string.Empty;
                return false;
            }

            swift = EmitSwiftUI(root, diagnostics);
            return !diagnostics.HasErrors;
        }

        private static bool ValidateNode(WatchUiNode node, WatchUiConversionDiagnostics diagnostics)
        {
            if (!SupportedKinds.Contains(node.kind))
            {
                diagnostics.Errors.Add(string.Format("Unsupported kind '{0}'.", node.kind));
                return false;
            }

            if (node.kind == "WatchButton" && string.IsNullOrWhiteSpace(node.action))
            {
                diagnostics.Errors.Add(string.Format("WatchButton '{0}' must define action.", node.id));
            }

            if (node.kind == "WatchNavigationLink" && string.IsNullOrWhiteSpace(node.route))
            {
                diagnostics.Errors.Add(string.Format("WatchNavigationLink '{0}' must define route.", node.id));
            }

            if (node.kind == "WatchNavigationDestination" && string.IsNullOrWhiteSpace(node.route))
            {
                diagnostics.Errors.Add(string.Format("WatchNavigationDestination '{0}' must define route.", node.id));
            }

            if (node.kind == "WatchSlider")
            {
                if (node.maximum <= node.minimum)
                {
                    diagnostics.Errors.Add(string.Format("WatchSlider '{0}' requires maximum greater than minimum.", node.id));
                }
            }

            if (node.kind == "WatchScrollView")
            {
                var axes = (node.axes ?? string.Empty).Trim().ToLowerInvariant();
                if (axes != "vertical" && axes != "horizontal" && axes != "both")
                {
                    diagnostics.Errors.Add(string.Format(
                        "WatchScrollView '{0}' axes must be vertical, horizontal, or both.", node.id));
                }
            }

            if (node.kind == "WatchGrid" && (node.horizontalSpacing < 0f || node.verticalSpacing < 0f))
            {
                diagnostics.Errors.Add(string.Format("WatchGrid '{0}' spacing cannot be negative.", node.id));
            }

            if (node.kind == "WatchGridRow" && node.children != null && node.children.Any(child => child?.kind == "WatchGridRow"))
            {
                diagnostics.Errors.Add(string.Format("WatchGridRow '{0}' cannot contain another WatchGridRow.", node.id));
            }

            if (node.kind == "WatchStepper")
            {
                if (node.maximum <= node.minimum)
                {
                    diagnostics.Errors.Add(string.Format("WatchStepper '{0}' requires maximum greater than minimum.", node.id));
                }
                if (node.step <= 0f)
                {
                    diagnostics.Errors.Add(string.Format("WatchStepper '{0}' requires a positive step.", node.id));
                }
            }

            if (node.kind == "WatchPicker")
            {
                var options = node.children == null
                    ? Array.Empty<WatchUiNode>()
                    : node.children.Where(child => child != null && child.kind == "WatchPickerOption").ToArray();
                if (options.Length == 0)
                {
                    diagnostics.Errors.Add(string.Format("WatchPicker '{0}' requires at least one WatchPickerOption.", node.id));
                }
                if (node.children != null && node.children.Any(child => child != null && child.kind != "WatchPickerOption"))
                {
                    diagnostics.Errors.Add(string.Format("WatchPicker '{0}' may contain only WatchPickerOption children.", node.id));
                }
            }

            if (node.kind == "WatchPickerOption")
            {
                if (string.IsNullOrWhiteSpace(node.selection))
                {
                    diagnostics.Errors.Add(string.Format("WatchPickerOption '{0}' requires value.", node.id));
                }
                if (string.IsNullOrWhiteSpace(node.label))
                {
                    diagnostics.Errors.Add(string.Format("WatchPickerOption '{0}' requires label.", node.id));
                }
            }

            if (node.kind == "WatchGauge" && node.maximum <= node.minimum)
            {
                diagnostics.Errors.Add(string.Format("WatchGauge '{0}' requires maximum greater than minimum.", node.id));
            }

            if (node.kind == "WatchJoystick")
            {
                if (node.radius <= 0f)
                {
                    diagnostics.Errors.Add(string.Format("WatchJoystick '{0}' requires a positive radius.", node.id));
                }
                if (node.deadZone < 0f || node.deadZone > node.radius)
                {
                    diagnostics.Errors.Add(string.Format("WatchJoystick '{0}' dead-zone must be between zero and radius.", node.id));
                }
            }

            if (node.kind == "WatchProgressView" && !float.IsNaN(node.value) && node.total <= 0f)
            {
                diagnostics.Errors.Add(string.Format("WatchProgressView '{0}' requires total greater than zero.", node.id));
            }

            if (node.kind == "WatchTextField" && string.IsNullOrWhiteSpace(node.textBinding))
            {
                diagnostics.Errors.Add(string.Format("WatchTextField '{0}' requires text-binding.", node.id));
            }

            if (node.kind == "WatchMotion")
            {
                if (!node.includeAcceleration && !node.includeGyro && !node.includeAttitude)
                {
                    diagnostics.Warnings.Add(
                        string.Format(
                            "WatchMotion '{0}' has no axes enabled; set include-acceleration, include-gyro, or include-attitude to true.",
                            node.id));
                }
                if (node.updateInterval <= 0f)
                {
                    diagnostics.Warnings.Add(
                        string.Format(
                            "WatchMotion '{0}' has non-positive update interval; emitter will use default interval.",
                            node.id));
                }
            }

            if (node.kind == "WatchMicrophone")
            {
                if (node.updateInterval <= 0f)
                {
                    diagnostics.Warnings.Add(
                        string.Format(
                            "WatchMicrophone '{0}' has non-positive update interval; emitter will use default interval.",
                            node.id));
                }
            }

            if (node.kind == "WatchImage" &&
                string.IsNullOrWhiteSpace(node.imageName) &&
                string.IsNullOrWhiteSpace(node.imageSystemName) &&
                string.IsNullOrWhiteSpace(node.imageSource))
            {
                diagnostics.Errors.Add(string.Format("WatchImage '{0}' requires at least one image source.", node.id));
            }

            if ((node.kind == "WatchVStack" || node.kind == "WatchHStack" || node.kind == "WatchZStack")
                && !string.IsNullOrWhiteSpace(node.alignment)
                && string.IsNullOrWhiteSpace(ResolveStackAlignment(node)))
            {
                diagnostics.Warnings.Add(
                    string.Format(
                        "{0} '{1}' alignment '{2}' is not recognized for the target stack type and will use default.",
                        node.kind,
                        node.id,
                        node.alignment));
            }

            if ((node.kind == "WatchVStack" || node.kind == "WatchHStack")
                && !float.IsNaN(node.spacing)
                && !float.IsInfinity(node.spacing)
                && node.spacing < 0f
                && node.spacing != -1f)
            {
                diagnostics.Warnings.Add(
                    string.Format(
                        "{0} '{1}' spacing is negative; SwiftUI will use default spacing.",
                        node.kind,
                        node.id));
            }

            if (node.kind == "WatchGestureArea" && string.IsNullOrWhiteSpace(node.gesture))
            {
                diagnostics.Warnings.Add(string.Format("WatchGestureArea '{0}' has no gesture specified; defaulting to tap.", node.id));
            }

            if (IsInteractiveKind(node.kind) && string.IsNullOrWhiteSpace(node.id))
            {
                diagnostics.Warnings.Add(
                    string.Format(
                        "{0} is interactive; add watch-id for stable tracing between UI and action sink.",
                        node.kind));
            }

            if (node.children == null || node.children.Length == 0)
                return !diagnostics.HasErrors;

            for (var i = 0; i < node.children.Length; i++)
            {
                var child = node.children[i];
                if (child == null)
                {
                    diagnostics.Warnings.Add(
                        string.Format(
                            "Child at index {0} under '{1}' is null.",
                            i,
                            node.id ?? node.kind));
                    continue;
                }

                ValidateNode(child, diagnostics);
            }

            return !diagnostics.HasErrors;
        }

        private static void ValidateHandGestureShortcuts(WatchUiNode root, WatchUiConversionDiagnostics diagnostics)
        {
            WatchUiNode primaryControl = null;
            var hasScrolling = false;
            Visit(root);
            if (primaryControl != null && hasScrolling)
                diagnostics.Warnings.Add("PrimaryAction hand gesture shortcut takes priority over scrolling in this Scene. Keep its control visible on a dedicated input page.");

            void Visit(WatchUiNode node)
            {
                if (node == null)
                    return;

                hasScrolling |= node.kind == "WatchScrollView" || node.kind == "WatchList";
                var shortcut = node.handGestureShortcut;
                if (!string.IsNullOrEmpty(shortcut) && shortcut != "None")
                {
                    if (shortcut != "PrimaryAction")
                    {
                        diagnostics.Errors.Add($"WATCHUI006: {node.kind} '{node.id}' hand-gesture-shortcut '{shortcut}' must be None or PrimaryAction.");
                    }
                    else if (node.kind != "WatchButton" && node.kind != "WatchToggle"
                        && node.kind != "WatchNavigationLink" && node.kind != "WatchTextField")
                    {
                        diagnostics.Errors.Add($"WATCHUI005: {node.kind} '{node.id}' does not support hand-gesture-shortcut. Use a buttonlike control.");
                    }
                    else if (primaryControl != null)
                    {
                        diagnostics.Errors.Add($"WATCHUI102: Only one PrimaryAction hand gesture shortcut is allowed in the entire Scene, including navigation destinations and presentations; '{primaryControl.id}' and '{node.id}' both declare it.");
                    }
                    else
                    {
                        primaryControl = node;
                    }
                }

                if (node.children != null)
                    foreach (var child in node.children)
                        Visit(child);
            }
        }

        private static void EmitStateDeclarations(
            WatchUiNode root,
            StringBuilder builder,
            WatchUiSwiftStateContext stateContext)
        {
            if (builder == null || stateContext == null)
            {
                return;
            }

            CollectInteractiveStateNodes(root, stateContext);

            foreach (var state in stateContext.ToggleStateNames)
            {
                var node = state.Key;
                var stateName = state.Value;
                var initialValue = node?.boolValue == true ? "true" : "false";
                builder.AppendLine($"    @State private var {stateName}: Bool = {initialValue}");
            }

            foreach (var state in stateContext.SliderStateNames)
            {
                var node = state.Key;
                var stateName = state.Value;
                var initial = node?.value ?? 0f;
                var initialText = float.IsNaN(initial) || float.IsInfinity(initial)
                    ? "0"
                    : initial.ToString(System.Globalization.CultureInfo.InvariantCulture);
                builder.AppendLine(
                    "    @State private var " + stateName +
                    string.Format(
                        " : Double = {0}",
                        initialText));
            }

            foreach (var state in stateContext.TextFieldStateNames)
            {
                var stateName = state.Value;
                builder.AppendLine($"    @State private var {stateName}: String = \"\"");
            }

            foreach (var state in stateContext.StepperStateNames)
            {
                var node = state.Key;
                var stateName = state.Value;
                var initial = node?.value ?? 0f;
                var initialText = float.IsNaN(initial) || float.IsInfinity(initial)
                    ? "0"
                    : initial.ToString(System.Globalization.CultureInfo.InvariantCulture);
                builder.AppendLine($"    @State private var {stateName}: Double = {initialText}");
            }

            foreach (var state in stateContext.PickerStateNames)
            {
                var node = state.Key;
                var stateName = state.Value;
                var initialSelection = node?.selection;
                if (string.IsNullOrWhiteSpace(initialSelection) && node?.children != null)
                {
                    initialSelection = node.children
                        .FirstOrDefault(child => child?.kind == "WatchPickerOption")?.selection;
                }
                var initial = EscapeString(initialSelection ?? string.Empty);
                builder.AppendLine($"    @State private var {stateName}: String = \"{initial}\"");
            }
        }

        private static void CollectInteractiveStateNodes(WatchUiNode node, WatchUiSwiftStateContext stateContext)
        {
            if (node == null || stateContext == null)
                return;

            if (node.kind == "WatchToggle")
            {
                stateContext.ResolveToggleStateName(node);
            }
            else if (node.kind == "WatchSlider")
            {
                stateContext.ResolveSliderStateName(node);
            }
            else if (node.kind == "WatchTextField")
            {
                stateContext.ResolveTextFieldStateName(node);
            }
            else if (node.kind == "WatchStepper")
            {
                stateContext.ResolveStepperStateName(node);
            }
            else if (node.kind == "WatchPicker")
            {
                stateContext.ResolvePickerStateName(node);
            }
            else if (node.kind == "WatchCrown" && node.enabled)
            {
                stateContext.ResolveCrownStateNames(node);
            }

            if (node.children == null || node.children.Length == 0)
                return;

            for (var i = 0; i < node.children.Length; i++)
            {
                CollectInteractiveStateNodes(node.children[i], stateContext);
            }
        }

        private static void EmitNode(WatchUiNode node, StringBuilder builder, int indent, WatchUiConversionDiagnostics diagnostics, WatchUiSwiftStateContext stateContext)
        {
            if (!SupportedKinds.Contains(node.kind))
            {
                diagnostics.Errors.Add(string.Format("Unsupported kind '{0}'. Emission skipped.", node.kind));
                return;
            }

            switch (node.kind)
            {
                case "WatchVStack":
                    EmitContainer("VStack", node, builder, indent, diagnostics, stateContext);
                    break;
                case "WatchHStack":
                    EmitContainer("HStack", node, builder, indent, diagnostics, stateContext);
                    break;
                case "WatchZStack":
                    EmitContainer("ZStack", node, builder, indent, diagnostics, stateContext);
                    break;
                case "WatchScrollView":
                    EmitScrollView(node, builder, indent, diagnostics, stateContext);
                    break;
                case "WatchList":
                    EmitList(node, builder, indent, diagnostics, stateContext);
                    break;
                case "WatchGrid":
                    EmitGrid(node, builder, indent, diagnostics, stateContext);
                    break;
                case "WatchGridRow":
                    EmitGridRow(node, builder, indent, diagnostics, stateContext);
                    break;
                case "WatchNavigationStack":
                    EmitNavigationStack(node, builder, indent, diagnostics, stateContext);
                    break;
                case "WatchNavigationLink":
                    EmitNavigationLink(node, builder, indent, diagnostics, stateContext);
                    break;
                case "WatchSpacer":
                    EmitSpacer(node, builder, indent);
                    break;
                case "WatchText":
                    EmitLine(
                        builder,
                        indent,
                        string.Format("Text(\"{0}\")", EscapeString(node.label ?? string.Empty)));
                    break;
                case "WatchButton":
                    EmitButton(node, builder, indent, diagnostics, stateContext);
                    break;
                case "WatchTouchpad":
                    EmitTouchpad(node, builder, indent);
                    break;
                case "WatchGestureArea":
                    EmitGestureArea(node, builder, indent);
                    break;
                case "WatchCrown":
                    EmitCrown(node, builder, indent, stateContext);
                    break;
                case "WatchStatus":
                    EmitLine(
                        builder,
                        indent,
                        FormatNodeExpression(node, string.Format("Text(\"{0}\")", EscapeString(node.label ?? "[WatchStatus]"))));
                    break;
                case "WatchMotion":
                    EmitMotion(node, builder, indent);
                    break;
                case "WatchMicrophone":
                    EmitMicrophone(node, builder, indent);
                    break;
                case "WatchToggle":
                    EmitToggle(node, builder, indent, stateContext);
                    break;
                case "WatchSlider":
                    EmitSlider(node, builder, indent, stateContext);
                    break;
                case "WatchLabel":
                    EmitLabel(node, builder, indent);
                    break;
                case "WatchDivider":
                    EmitLine(builder, indent, "Divider()");
                    break;
                case "WatchImage":
                    EmitImage(node, builder, indent);
                    break;
                case "WatchTextField":
                    EmitTextField(node, builder, indent, stateContext);
                    break;
                case "WatchProgressView":
                    EmitProgressView(node, builder, indent, diagnostics);
                    break;
                case "WatchStepper":
                    EmitStepper(node, builder, indent, stateContext);
                    break;
                case "WatchPicker":
                    EmitPicker(node, builder, indent, diagnostics, stateContext);
                    break;
                case "WatchPickerOption":
                    EmitPickerOption(node, builder, indent);
                    break;
                case "WatchGauge":
                    EmitGauge(node, builder, indent, diagnostics);
                    break;
                case "WatchJoystick":
                    EmitJoystick(node, builder, indent);
                    break;
                case "WatchNavigationDestination":
                    EmitNavigationDestination(node, builder, indent);
                    break;
            }

            if (node.handGestureShortcut == "PrimaryAction")
                EmitLine(builder, indent + 1, ".handGestureShortcut(.primaryAction)");

            if (node.kind != "WatchNavigationDestination" && node.kind != "WatchButton" && node.kind != "WatchNavigationLink"
                && node.kind != "WatchGestureArea" && node.kind != "WatchTouchpad" && node.kind != "WatchCrown" && node.kind != "WatchJoystick")
                EmitPresentation(node, builder, indent + 1);

            if (!node.enabled && node.kind != "WatchNavigationDestination")
            {
                EmitLine(builder, indent + 1, ".disabled(true)");
                EmitLine(builder, indent + 1, ".allowsHitTesting(false)");
            }
        }

        private static void EmitPresentation(WatchUiNode node, StringBuilder builder, int indent)
        {
            var style = node.presentation;
            if (style == null)
                return;

            if (style.fontSize > 0)
                EmitLine(builder, indent, string.Format(".font(.system(size: {0}, weight: .{1}))", StyleNumber(style.fontSize), style.bold ? "bold" : "regular"));
            if (style.italic)
                EmitLine(builder, indent, ".italic()");
            if (node.kind != "WatchImage" || string.IsNullOrWhiteSpace(node.foregroundColor))
                EmitLine(builder, indent, ".foregroundStyle(" + StyleColor(style.color) + ")");
            EmitLine(builder, indent, ".multilineTextAlignment(." + style.textAlignment + ")");
            if (style.stretchWidth)
                EmitLine(builder, indent, ".frame(maxWidth: .infinity)");
            if (style.paddingTop != 0 || style.paddingLeft != 0 || style.paddingBottom != 0 || style.paddingRight != 0)
                EmitLine(builder, indent, string.Format(".padding(EdgeInsets(top: {0}, leading: {1}, bottom: {2}, trailing: {3}))",
                    StyleNumber(style.paddingTop), StyleNumber(style.paddingLeft), StyleNumber(style.paddingBottom), StyleNumber(style.paddingRight)));
            if (style.borderWidth > 0)
                EmitLine(builder, indent, ".padding(" + StyleNumber(style.borderWidth) + ")");

            var constraints = new List<string>();
            if (style.minWidth >= 0) constraints.Add("minWidth: " + StyleNumber(style.minWidth));
            if (style.maxWidth >= 0) constraints.Add("maxWidth: " + StyleNumber(style.maxWidth));
            if (style.minHeight >= 0) constraints.Add("minHeight: " + StyleNumber(style.minHeight));
            if (style.maxHeight >= 0) constraints.Add("maxHeight: " + StyleNumber(style.maxHeight));
            if (constraints.Count > 0)
                EmitLine(builder, indent, ".frame(" + string.Join(", ", constraints) + ")");

            var shape = "RoundedRectangle(cornerRadius: " + StyleNumber(style.cornerRadius) + ")";
            if (style.backgroundColor.a > 0)
                EmitLine(builder, indent, ".background(" + shape + ".fill(" + StyleColor(style.backgroundColor) + "))");
            if (style.borderWidth > 0)
                EmitLine(builder, indent, ".overlay(" + shape + ".strokeBorder(" + StyleColor(style.borderColor) + ", lineWidth: " + StyleNumber(style.borderWidth) + "))");
            if (style.opacity < 1)
                EmitLine(builder, indent, ".opacity(" + StyleNumber(style.opacity) + ")");
        }

        private static string StyleNumber(float value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void EmitTouchSurfacePresentation(WatchUiNode node, StringBuilder builder, int indent)
        {
            EmitPresentation(node, builder, indent);
            if (node.presentation != null)
                EmitLine(builder, indent, ".contentShape(Rectangle())");
        }

        private static string StyleColor(UnityEngine.Color color)
        {
            return string.Format("Color(red: {0}, green: {1}, blue: {2}, opacity: {3})",
                StyleNumber(color.r), StyleNumber(color.g), StyleNumber(color.b), StyleNumber(color.a));
        }

        private static void EmitContainer(
            string control,
            WatchUiNode node,
            StringBuilder builder,
            int indent,
            WatchUiConversionDiagnostics diagnostics,
            WatchUiSwiftStateContext stateContext)
        {
            EmitLine(builder, indent, string.Format("{0}{1} {{", control, BuildStackConstructionTail(node)));
            if (node.children != null)
            {
                foreach (var child in node.children)
                {
                    if (child == null)
                        continue;

                    EmitNode(child, builder, indent + 1, diagnostics, stateContext);
                }
            }
            EmitLine(builder, indent, "}");
        }

        private static void EmitScrollView(
            WatchUiNode node,
            StringBuilder builder,
            int indent,
            WatchUiConversionDiagnostics diagnostics,
            WatchUiSwiftStateContext stateContext)
        {
            var axes = ResolveScrollAxes(node.axes);
            EmitLine(
                builder,
                indent,
                string.Format("ScrollView({0}, showsIndicators: {1}) {{", axes, node.showsIndicators ? "true" : "false"));
            EmitChildren(node, builder, indent + 1, diagnostics, stateContext);
            EmitLine(builder, indent, "}");
        }

        private static void EmitList(
            WatchUiNode node,
            StringBuilder builder,
            int indent,
            WatchUiConversionDiagnostics diagnostics,
            WatchUiSwiftStateContext stateContext)
        {
            EmitLine(builder, indent, "List {");
            EmitChildren(node, builder, indent + 1, diagnostics, stateContext);
            EmitLine(builder, indent, "}");

            var style = (node.style ?? string.Empty).Trim().ToLowerInvariant();
            if (style == "plain")
            {
                EmitLine(builder, indent + 1, ".listStyle(PlainListStyle())");
            }
            else if (style == "carousel")
            {
                EmitLine(builder, indent + 1, ".listStyle(CarouselListStyle())");
            }
            else if (!string.IsNullOrWhiteSpace(style) && style != "automatic")
            {
                diagnostics.Warnings.Add(string.Format(
                    "WatchList '{0}' style '{1}' is not recognized and will use automatic style.",
                    node.id ?? "<untitled>",
                    style));
            }
        }

        private static void EmitGrid(
            WatchUiNode node,
            StringBuilder builder,
            int indent,
            WatchUiConversionDiagnostics diagnostics,
            WatchUiSwiftStateContext stateContext)
        {
            var alignment = ResolveGridAlignment(node.alignment);
            var horizontalSpacing = Math.Max(0f, node.horizontalSpacing)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            var verticalSpacing = Math.Max(0f, node.verticalSpacing)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            EmitLine(
                builder,
                indent,
                string.Format(
                    "Grid(alignment: {0}, horizontalSpacing: {1}, verticalSpacing: {2}) {{",
                    alignment,
                    horizontalSpacing,
                    verticalSpacing));
            EmitChildren(node, builder, indent + 1, diagnostics, stateContext);
            EmitLine(builder, indent, "}");
        }

        private static void EmitGridRow(
            WatchUiNode node,
            StringBuilder builder,
            int indent,
            WatchUiConversionDiagnostics diagnostics,
            WatchUiSwiftStateContext stateContext)
        {
            EmitLine(builder, indent, string.Format("GridRow(alignment: {0}) {{", ResolveGridRowAlignment(node.alignment)));
            EmitChildren(node, builder, indent + 1, diagnostics, stateContext);
            EmitLine(builder, indent, "}");
        }

        private static void EmitChildren(
            WatchUiNode node,
            StringBuilder builder,
            int indent,
            WatchUiConversionDiagnostics diagnostics,
            WatchUiSwiftStateContext stateContext)
        {
            if (node?.children == null)
                return;

            foreach (var child in node.children)
            {
                if (child != null)
                    EmitNode(child, builder, indent, diagnostics, stateContext);
            }
        }

        private static string ResolveScrollAxes(string axes)
        {
            switch ((axes ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "horizontal":
                    return ".horizontal";
                case "both":
                    return "[.vertical, .horizontal]";
                default:
                    return ".vertical";
            }
        }

        private static string ResolveGridAlignment(string alignment)
        {
            switch (NormalizeStackAlignment(alignment))
            {
                case "leading":
                case "left":
                    return ".leading";
                case "trailing":
                case "right":
                    return ".trailing";
                default:
                    return ".center";
            }
        }

        private static string ResolveGridRowAlignment(string alignment)
        {
            switch (NormalizeStackAlignment(alignment))
            {
                case "top":
                case "up":
                    return ".top";
                case "bottom":
                case "down":
                    return ".bottom";
                default:
                    return ".center";
            }
        }

        private static string BuildStackConstructionTail(WatchUiNode node)
        {
            if (node == null)
            {
                return string.Empty;
            }

            var arguments = new List<string>();

            var alignment = ResolveStackAlignment(node);
            if (!string.IsNullOrWhiteSpace(alignment))
            {
                arguments.Add(string.Format("alignment: {0}", alignment));
            }

            if ((node.kind == "WatchVStack" || node.kind == "WatchHStack") && node.spacing >= 0f)
            {
                arguments.Add(string.Format("spacing: {0}", node.spacing.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            if (arguments.Count == 0)
            {
                return string.Empty;
            }

            return string.Format("({0})", string.Join(", ", arguments));
        }

        private static string ResolveStackAlignment(WatchUiNode node)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.alignment))
            {
                return string.Empty;
            }

            var alignment = NormalizeStackAlignment(node.alignment);
            if (node.kind == "WatchVStack")
            {
                if (alignment == "leading" || alignment == "left")
                {
                    return ".leading";
                }

                if (alignment == "trailing" || alignment == "right")
                {
                    return ".trailing";
                }

                if (alignment == "center")
                {
                    return ".center";
                }

                return string.Empty;
            }

            if (node.kind == "WatchHStack")
            {
                if (alignment == "top" || alignment == "up")
                {
                    return ".top";
                }

                if (alignment == "middle" || alignment == "center")
                {
                    return ".center";
                }

                if (alignment == "bottom" || alignment == "down")
                {
                    return ".bottom";
                }

                return string.Empty;
            }

            if (node.kind == "WatchZStack")
            {
                if (alignment == "center" || alignment == "centered")
                {
                    return ".center";
                }

                if (alignment == "top" || alignment == "up")
                {
                    return ".top";
                }

                if (alignment == "bottom" || alignment == "down")
                {
                    return ".bottom";
                }

                if (alignment == "leading" || alignment == "left")
                {
                    return ".leading";
                }

                if (alignment == "trailing" || alignment == "right")
                {
                    return ".trailing";
                }

                if (alignment == "topleading")
                {
                    return ".topLeading";
                }

                if (alignment == "toptrailing")
                {
                    return ".topTrailing";
                }

                if (alignment == "bottomleading")
                {
                    return ".bottomLeading";
                }

                if (alignment == "bottomtrailing")
                {
                    return ".bottomTrailing";
                }

                return string.Empty;
            }

            return string.Empty;
        }

        private static string NormalizeStackAlignment(string alignment)
        {
            if (string.IsNullOrWhiteSpace(alignment))
            {
                return string.Empty;
            }

            var normalized = new StringBuilder();
            foreach (var ch in alignment.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    normalized.Append(ch);
                }
            }

            return normalized.ToString();
        }

        private static void EmitNavigationStack(
            WatchUiNode node,
            StringBuilder builder,
            int indent,
            WatchUiConversionDiagnostics diagnostics,
            WatchUiSwiftStateContext stateContext)
        {
            var destinationNodes = CollectNavigationDestinations(node, diagnostics);
            var linkedRoutes = CollectNavigationLinkRoutes(node);
            foreach (var route in linkedRoutes)
            {
                if (!string.IsNullOrWhiteSpace(route) && !destinationNodes.ContainsKey(route))
                {
                    diagnostics.Warnings.Add(
                        string.Format(
                            "WatchNavigationLink route '{0}' has no matching WatchNavigationDestination under the same WatchNavigationStack.",
                            route));
                }
            }

            var destinationRoutes = destinationNodes.Keys;

            EmitLine(builder, indent, "NavigationStack(path: $navigationPath) {");
            EmitLine(builder, indent + 1, "VStack(spacing: 0) {");

            if (node.children != null)
            {
                foreach (var child in node.children)
                {
                    if (child == null)
                    {
                        continue;
                    }

                    if (child.kind == "WatchNavigationDestination")
                    {
                        continue;
                    }

                    EmitNode(child, builder, indent + 2, diagnostics, stateContext);
                }
            }

            EmitLine(builder, indent + 1, "}");

            if (destinationNodes.Count > 0)
            {
                EmitNavigationDestinationModifier(
                    destinationNodes,
                    destinationRoutes,
                    builder,
                    indent + 1,
                    diagnostics,
                    stateContext);
            }
            EmitLine(builder, indent, "}");
        }

        private static List<string> CollectNavigationLinkRoutes(WatchUiNode node)
        {
            var routes = new List<string>();
            if (node == null || node.children == null)
            {
                return routes;
            }

            foreach (var child in node.children)
            {
                if (child == null)
                    continue;

                if (child.kind == "WatchNavigationLink")
                {
                    routes.Add((child.route ?? string.Empty).Trim());
                    continue;
                }

                if (child.children == null)
                {
                    continue;
                }

                routes.AddRange(CollectNavigationLinkRoutes(child));
            }

            return routes;
        }

        private static Dictionary<string, WatchUiNode> CollectNavigationDestinations(
            WatchUiNode node,
            WatchUiConversionDiagnostics diagnostics)
        {
            var destinationNodes = new Dictionary<string, WatchUiNode>();
            if (node == null || node.children == null)
            {
                return destinationNodes;
            }

            foreach (var child in node.children)
            {
                if (child == null || child.kind != "WatchNavigationDestination")
                {
                    continue;
                }

                var route = (child.route ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(route))
                {
                    diagnostics.Warnings.Add("WatchNavigationDestination requires non-empty route.");
                    continue;
                }

                if (destinationNodes.ContainsKey(route))
                {
                    diagnostics.Warnings.Add(string.Format("Duplicate WatchNavigationDestination route '{0}'. First one will be kept.", route));
                    continue;
                }

                destinationNodes[route] = child;
            }

            return destinationNodes;
        }

        private static void EmitNavigationDestinationModifier(
            Dictionary<string, WatchUiNode> destinationNodes,
            IEnumerable<string> linkedRoutes,
            StringBuilder builder,
            int indent,
            WatchUiConversionDiagnostics diagnostics,
            WatchUiSwiftStateContext stateContext)
        {
            EmitLine(builder, indent, ".navigationDestination(for: String.self) { route in");

            var destinationEmitted = false;
            var emittedRoutes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var route in linkedRoutes)
            {
                if (string.IsNullOrWhiteSpace(route) || !emittedRoutes.Add(route))
                {
                    continue;
                }

                if (!destinationNodes.TryGetValue(route, out var destination) || destination == null)
                {
                    diagnostics.Warnings.Add(string.Format("Navigation destination '{0}' is missing; route will resolve to missing-state fallback.", EscapeString(route)));
                    continue;
                }

                if (!destinationEmitted)
                {
                    EmitLine(builder, indent + 1, string.Format("if route == \"{0}\" {{", EscapeString(route)));
                }
                else
                {
                    EmitLine(builder, indent + 1, string.Format("}} else if route == \"{0}\" {{", EscapeString(route)));
                }

                EmitDestinationChildren(destination, builder, indent + 2, diagnostics, stateContext);
                destinationEmitted = true;
            }

            if (destinationEmitted)
            {
                EmitLine(builder, indent + 1, "} else {");
            }
            else
            {
                EmitLine(builder, indent + 1, "if false {");
            }

            EmitLine(builder, indent + 2, "AnyView(Text(\"Missing destination for route \\(route)\") )");
            EmitLine(builder, indent + 1, "}");
            EmitLine(builder, indent, "}");
        }

        private static void EmitDestinationChildren(
            WatchUiNode destinationNode,
            StringBuilder builder,
            int indent,
            WatchUiConversionDiagnostics diagnostics,
            WatchUiSwiftStateContext stateContext)
        {
            EmitLine(builder, indent, "AnyView(");
            EmitLine(builder, indent + 1, "VStack {");

            if (destinationNode.children == null || destinationNode.children.Length == 0)
            {
                EmitLine(builder, indent + 2, "Text(\"Empty destination\")");
            }
            else
            {
                foreach (var child in destinationNode.children)
                {
                    if (child == null)
                    {
                        continue;
                    }

                    EmitNode(child, builder, indent + 2, diagnostics, stateContext);
                }
            }

            EmitLine(builder, indent + 1, "}");
            EmitPresentation(destinationNode, builder, indent + 1);
            EmitLine(builder, indent, ")");
        }

        private static void EmitButton(
            WatchUiNode node,
            StringBuilder builder,
            int indent,
            WatchUiConversionDiagnostics diagnostics,
            WatchUiSwiftStateContext stateContext)
        {
            if (!node.enabled)
            {
                diagnostics.Warnings.Add(
                    string.Format(
                        "WatchButton '{0}' is disabled and will be emitted as disabled action stub.",
                        node.id ?? "<untitled>"));
                EmitLine(
                    builder,
                    indent,
                    FormatNodeExpression(
                        node,
                        string.Format(
                            "Button(action: {{ {0} }}) {{",
                            EmitActionCall(node, "WatchButton"))));
            }
            else
            {
                EmitLine(
                    builder,
                    indent,
                    FormatNodeExpression(
                        node,
                        string.Format(
                            "Button(action: {{ {0} }}) {{",
                            EmitActionCall(node, "WatchButton"))));
            }
            EmitControlLabel(node, node.label ?? "Button", builder, indent + 1, diagnostics, stateContext);
            EmitLine(builder, indent, "}");
            if (node.presentation != null || node.children?.Length > 0)
                EmitLine(builder, indent + 1, ".buttonStyle(.plain)");
        }

        private static void EmitNavigationLink(
            WatchUiNode node,
            StringBuilder builder,
            int indent,
            WatchUiConversionDiagnostics diagnostics,
            WatchUiSwiftStateContext stateContext)
        {
            var title = EscapeString(string.IsNullOrWhiteSpace(node.title) ? "Navigate" : node.title);
            var route = EscapeString((node.route ?? string.Empty).Trim());
            if (string.IsNullOrWhiteSpace(route))
            {
                EmitLine(builder, indent, string.Format("NavigationLink(\"{0}\") {{", title));
                EmitLine(builder, indent + 1, "Text(\"Missing route\")");
                EmitLine(builder, indent, "}");
                return;
            }

            EmitLine(
                builder,
                indent,
                FormatNodeExpression(
                    node,
                    string.Format("NavigationLink(value: \"{0}\") {{", route)));
            EmitControlLabel(node, string.IsNullOrWhiteSpace(node.title) ? "Navigate" : node.title, builder, indent + 1, diagnostics, stateContext);
            EmitLine(builder, indent, "}");
            if (node.presentation != null || node.children?.Length > 0)
                EmitLine(builder, indent + 1, ".buttonStyle(.plain)");
        }

        private static void EmitControlLabel(WatchUiNode node, string fallback, StringBuilder builder, int indent,
            WatchUiConversionDiagnostics diagnostics, WatchUiSwiftStateContext stateContext)
        {
            var children = node.children?.Where(child => child != null).ToArray();
            if (children == null || children.Length == 0)
                EmitLine(builder, indent, "Text(\"" + EscapeString(fallback) + "\")");
            else if (children.Length == 1)
                EmitNode(children[0], builder, indent, diagnostics, stateContext);
            else
            {
                EmitLine(builder, indent, "VStack(spacing: 0) {");
                EmitChildren(node, builder, indent + 1, diagnostics, stateContext);
                EmitLine(builder, indent, "}");
            }
            EmitPresentation(node, builder, indent + 1);
            if (node.presentation != null)
                EmitLine(builder, indent + 1, ".contentShape(Rectangle())");
        }

        private static void EmitGestureArea(WatchUiNode node, StringBuilder builder, int indent)
        {
            var label = EscapeString(node.label ?? "[WatchGestureArea]");
            var action = string.IsNullOrWhiteSpace(node.action)
                ? string.Format("unbound:{0}", string.IsNullOrWhiteSpace(node.id) ? "gesture" : node.id)
                : node.action;
            var gesture = (node.gesture ?? "tap").Trim().ToLowerInvariant();
            var minimumDistance = node.minimumDistance;
            var minimumDuration = node.minimumDuration;
            var maximumDistance = node.maximumDistance;
            var maximumDistanceText = float.IsNaN(maximumDistance) || float.IsInfinity(maximumDistance) || maximumDistance < 0f
                ? "20" : StyleNumber(maximumDistance);
            var minimumDistanceText = float.IsNaN(minimumDistance) || float.IsInfinity(minimumDistance) || minimumDistance < 0f
                ? "20"
                : string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0:0.###}",
                    Math.Max(0f, minimumDistance));
            var minimumDurationText = float.IsNaN(minimumDuration) || float.IsInfinity(minimumDuration) || minimumDuration < 0f
                ? "0.5"
                : string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0:0.###}",
                    Math.Max(0f, minimumDuration));

            EmitLine(
                builder,
                indent,
                FormatNodeExpression(node, string.Format("Text(\"{0}\") // watch-gesture-area", label)));

            EmitTouchSurfacePresentation(node, builder, indent + 1);

            switch (gesture)
            {
                case "tap":
                    EmitLine(
                        builder,
                        indent + 1,
                        string.Format(
                            " .onTapGesture {{ {0} }}",
                            EmitGestureActionCall(
                                node,
                                "WatchGestureArea/tap",
                                action,
                                "payload: [\"touchPress\": 1, \"touchPosX\": Float(0), \"touchPosY\": Float(0), \"tap\": 1]")));
                    break;
                case "doubletap":
                case "double-tap":
                    EmitLine(
                        builder,
                        indent + 1,
                        string.Format(
                            " .onTapGesture(count: 2) {{ {0} }}",
                            EmitGestureActionCall(
                                node,
                                "WatchGestureArea/doubleTap",
                                action,
                                "payload: [\"touchPress\": 1, \"touchPosX\": Float(0), \"touchPosY\": Float(0), \"doubleTap\": 1]")));
                    break;
                case "longpress":
                case "long-press":
                    EmitLine(
                        builder,
                        indent + 1,
                        string.Format(
                            " .onLongPressGesture(minimumDuration: {0}, maximumDistance: {1}) {{ {2} }}",
                            minimumDurationText,
                            maximumDistanceText,
                            EmitGestureActionCall(
                                node,
                                "WatchGestureArea/longPress",
                                action,
                                "payload: [\"touchPress\": 1, \"touchPosX\": Float(0), \"touchPosY\": Float(0), \"longPress\": 1]")));
                    break;
                case "swipeup":
                case "swipe-up":
                    EmitSwipeGesture(node, "up", minimumDistanceText, action, node.coordinateSpace, builder, indent);
                    break;
                case "swipe":
                    EmitSwipeGesture(node, "any", minimumDistanceText, action, node.coordinateSpace, builder, indent);
                    break;
                case "swipedown":
                case "swipe-down":
                    EmitSwipeGesture(node, "down", minimumDistanceText, action, node.coordinateSpace, builder, indent);
                    break;
                case "swipeleft":
                case "swipe-left":
                    EmitSwipeGesture(node, "left", minimumDistanceText, action, node.coordinateSpace, builder, indent);
                    break;
                case "swiperight":
                case "swipe-right":
                    EmitSwipeGesture(node, "right", minimumDistanceText, action, node.coordinateSpace, builder, indent);
                    break;
                case "drag":
                    EmitDragGesture(node, minimumDistanceText, action, node.coordinateSpace, builder, indent);
                    break;
                default:
                {
                    var fallbackTapAction = EmitGestureActionCall(
                        node,
                        "WatchGestureArea/fallbackTap",
                        action,
                        "payload: [\"touchPress\": 1, \"touchPosX\": Float(0), \"touchPosY\": Float(0)]");
                    EmitLine(
                        builder,
                        indent + 1,
                        string.Format(
                            " .onTapGesture {{ {0} }}",
                            fallbackTapAction));
                    break;
                }
            }
        }

        private static void EmitDragGesture(
            WatchUiNode node,
            string minimumDistance,
            string action,
            string coordinateSpace,
            StringBuilder builder,
            int indent)
        {
            var sourceId = ResolveSourceId(node, "WatchGestureArea");
            var coordinateSpaceExpression = ResolveGestureCoordinateSpace(coordinateSpace);
            var gestureOptions = string.IsNullOrWhiteSpace(coordinateSpaceExpression)
                ? string.Empty
                : string.Format(", coordinateSpace: {0}", coordinateSpaceExpression);

            EmitLine(
                builder,
                indent + 1,
                string.Format(" .gesture(DragGesture(minimumDistance: {0}{1})", minimumDistance, gestureOptions));
            EmitLine(builder, indent + 2, ".onEnded { value in");
            EmitLine(
                builder,
                indent + 3,
                EmitActionCall(
                    action,
                    sourceId,
                    "WatchGestureArea/drag",
                    "payload: [\"axis0\": Float(value.translation.width), \"touchPosX\": Float(value.location.x), \"touchPosY\": Float(value.location.y), \"touchPress\": 1]"));
            EmitLine(builder, indent + 2, "}");
            EmitLine(builder, indent + 2, ")");
        }

        private static void EmitSwipeGesture(
            WatchUiNode node,
            string direction,
            string minimumDistance,
            string action,
            string coordinateSpace,
            StringBuilder builder,
            int indent)
        {
            var coordinateSpaceExpression = ResolveGestureCoordinateSpace(coordinateSpace);
            var gestureOptions = string.IsNullOrWhiteSpace(coordinateSpaceExpression)
                ? string.Empty
                : string.Format(", coordinateSpace: {0}", coordinateSpaceExpression);

            EmitLine(
                builder,
                indent + 1,
                string.Format(" .gesture(DragGesture(minimumDistance: {0}{1})", minimumDistance, gestureOptions));
            EmitLine(builder, indent + 2, ".onEnded { value in");
            EmitLine(builder, indent + 3, "let dx = value.translation.width");
            EmitLine(builder, indent + 3, "let dy = value.translation.height");

            var sourceId = ResolveSourceId(node, "WatchGestureArea");

            if (direction == "any")
            {
                EmitLine(builder, indent + 3, $"guard max(abs(dx), abs(dy)) >= {minimumDistance}, dx != 0 || dy != 0 else {{ return }}");
                EmitLine(builder, indent + 3, "if abs(dx) >= abs(dy) {");
                EmitLine(builder, indent + 4, "if dx < 0 {");
                EmitLine(builder, indent + 5, EmitActionCall(action, sourceId, "WatchGestureArea/swipe-left",
                    "payload: [\"axis0\": Float(dx), \"touchPosX\": Float(value.location.x), \"touchPosY\": Float(value.location.y), \"swipeLeft\": 1]"));
                EmitLine(builder, indent + 4, "} else {");
                EmitLine(builder, indent + 5, EmitActionCall(action, sourceId, "WatchGestureArea/swipe-right",
                    "payload: [\"axis0\": Float(dx), \"touchPosX\": Float(value.location.x), \"touchPosY\": Float(value.location.y), \"swipeRight\": 1]"));
                EmitLine(builder, indent + 4, "}");
                EmitLine(builder, indent + 3, "} else if dy < 0 {");
                EmitLine(builder, indent + 4, EmitActionCall(action, sourceId, "WatchGestureArea/swipe-up",
                    "payload: [\"axis0\": Float(dy), \"touchPosX\": Float(value.location.x), \"touchPosY\": Float(value.location.y), \"swipeUp\": 1]"));
                EmitLine(builder, indent + 3, "} else {");
                EmitLine(builder, indent + 4, EmitActionCall(action, sourceId, "WatchGestureArea/swipe-down",
                    "payload: [\"axis0\": Float(dy), \"touchPosX\": Float(value.location.x), \"touchPosY\": Float(value.location.y), \"swipeDown\": 1]"));
                EmitLine(builder, indent + 3, "}");
                EmitLine(builder, indent + 2, "}");
                EmitLine(builder, indent + 2, ")");
                return;
            }

            var isHorizontal = direction == "left" || direction == "right";
            EmitLine(
                builder,
                indent + 3,
                string.Format(
                    "if {0} > {1} {{",
                    isHorizontal ? "abs(dx)" : "abs(dy)",
                    isHorizontal ? "abs(dy)" : "abs(dx)"));

            if (isHorizontal)
            {
                if (direction == "left")
                {
                    EmitLine(builder, indent + 4, "if dx < 0 {");
                    EmitLine(
                        builder,
                        indent + 5,
                        EmitActionCall(
                            action,
                            sourceId,
                            "WatchGestureArea/swipe-left",
                            "payload: [\"axis0\": Float(dx), \"touchPosX\": Float(value.location.x), \"touchPosY\": Float(value.location.y), \"swipeLeft\": 1]"));
                    EmitLine(builder, indent + 5, "} else {");
                    EmitLine(builder, indent + 6, "// no-op");
                    EmitLine(builder, indent + 4, "}");
                }
                else if (direction == "right")
                {
                    EmitLine(builder, indent + 4, "if dx > 0 {");
                    EmitLine(
                        builder,
                        indent + 5,
                        EmitActionCall(
                            action,
                            sourceId,
                            "WatchGestureArea/swipe-right",
                            "payload: [\"axis0\": Float(dx), \"touchPosX\": Float(value.location.x), \"touchPosY\": Float(value.location.y), \"swipeRight\": 1]"));
                    EmitLine(builder, indent + 5, "} else {");
                    EmitLine(builder, indent + 6, "// no-op");
                    EmitLine(builder, indent + 4, "}");
                }
            }
            else
            {
                if (direction == "up")
                {
                    EmitLine(builder, indent + 4, "if dy < 0 {");
                    EmitLine(
                        builder,
                        indent + 5,
                        EmitActionCall(
                            action,
                            sourceId,
                            "WatchGestureArea/swipe-up",
                            "payload: [\"axis0\": Float(dy), \"touchPosX\": Float(value.location.x), \"touchPosY\": Float(value.location.y), \"swipeUp\": 1]"));
                    EmitLine(builder, indent + 5, "} else {");
                    EmitLine(builder, indent + 6, "// no-op");
                    EmitLine(builder, indent + 4, "}");
                }
                else
                {
                    EmitLine(builder, indent + 4, "if dy > 0 {");
                    EmitLine(
                        builder,
                        indent + 5,
                        EmitActionCall(
                            action,
                            sourceId,
                            "WatchGestureArea/swipe-down",
                            "payload: [\"axis0\": Float(dy), \"touchPosX\": Float(value.location.x), \"touchPosY\": Float(value.location.y), \"swipeDown\": 1]"));
                    EmitLine(builder, indent + 5, "} else {");
                    EmitLine(builder, indent + 6, "// no-op");
                    EmitLine(builder, indent + 4, "}");
                }
            }

            EmitLine(builder, indent + 3, "}");

            EmitLine(builder, indent + 2, "}");
            EmitLine(builder, indent + 2, ")");
        }

        private static void EmitToggle(
            WatchUiNode node,
            StringBuilder builder,
            int indent,
            WatchUiSwiftStateContext stateContext)
        {
            var stateName = ResolveStateName(node, stateContext, "toggle");
            EmitLine(
                builder,
                indent,
                FormatNodeExpression(
                    node,
                    string.Format(
                        "Toggle(\"{0}\", isOn: ${1})",
                        EscapeString(node.label ?? "Toggle"),
                        stateName)));

            EmitLine(
                builder,
                indent + 1,
                    string.Format(
                    ".onChange(of: {0}) {{ _, newValue in {1} }}",
                    stateName,
                    EmitActionCall(
                        node,
                        "WatchToggle",
                        null,
                        "payload: [\"value\": (newValue ? 1 : 0)]")));
        }

        private static void EmitImage(
            WatchUiNode node,
            StringBuilder builder,
            int indent)
        {
            var imageExpression = ResolveImageExpression(node);
            if (string.IsNullOrWhiteSpace(imageExpression))
            {
                EmitLine(
                    builder,
                    indent,
                    "Text(\"[WatchImage missing source]\")");
                return;
            }

            EmitLine(builder, indent, imageExpression);

            if (string.IsNullOrWhiteSpace(node.foregroundColor))
                return;

            EmitLine(
                builder,
                indent + 1,
                string.Format(
                    ".foregroundStyle({0})",
                    ResolveColorExpression(node.foregroundColor)));
        }

        private static string ResolveColorExpression(string color)
        {
            if (string.IsNullOrWhiteSpace(color))
                return "Color.clear";

            if (UnityEngine.ColorUtility.TryParseHtmlString(color, out var parsedColor))
                return StyleColor(parsedColor);

            switch (color.Trim().ToLowerInvariant())
            {
                case "black":
                    return ".black";
                case "blue":
                    return ".blue";
                case "brown":
                    return ".brown";
                case "cyan":
                    return ".cyan";
                case "gray":
                    return ".gray";
                case "green":
                    return ".green";
                case "indigo":
                    return ".indigo";
                case "mint":
                    return ".mint";
                case "orange":
                    return ".orange";
                case "pink":
                    return ".pink";
                case "purple":
                    return ".purple";
                case "red":
                    return ".red";
                case "teal":
                    return ".teal";
                case "white":
                    return ".white";
                case "yellow":
                    return ".yellow";
                default:
                    return string.Format("Color(\"{0}\")", EscapeString(color.Trim()));
            }
        }

        private static string ResolveGestureCoordinateSpace(string coordinateSpace)
        {
            if (string.IsNullOrWhiteSpace(coordinateSpace))
            {
                return string.Empty;
            }

            switch (coordinateSpace.Trim().ToLowerInvariant())
            {
                case "global":
                    return ".global";
                case "local":
                    return ".local";
                default:
                    return string.Format(".named(\"{0}\")", EscapeString(coordinateSpace.Trim()));
            }
        }

        private static string ResolveImageExpression(WatchUiNode node)
        {
            var source = string.IsNullOrWhiteSpace(node.imageSystemName)
                ? null
                : node.imageSystemName;

            if (!string.IsNullOrWhiteSpace(source))
            {
                return string.Format("Image(systemName: \"{0}\")", EscapeString(source));
            }

            source = !string.IsNullOrWhiteSpace(node.imageName)
                ? node.imageName
                : node.imageSource;

            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            return string.Format("Image(\"{0}\")", EscapeString(source.Trim()));
        }

        private static void EmitSpacer(
            WatchUiNode node,
            StringBuilder builder,
            int indent)
        {
            if (!float.IsNaN(node.minLength) && !float.IsInfinity(node.minLength) && node.minLength >= 0f)
            {
                EmitLine(
                    builder,
                    indent,
                    string.Format(
                        "Spacer(minLength: {0})",
                        node.minLength.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                return;
            }

            EmitLine(builder, indent, "Spacer()");
        }

        private static void EmitLabel(
            WatchUiNode node,
            StringBuilder builder,
            int indent)
        {
            var title = EscapeString(node.title ?? string.Empty);
            var systemImage = (node.systemImage ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(title))
            {
                title = "Label";
            }

            if (string.IsNullOrWhiteSpace(systemImage))
            {
                EmitLine(builder, indent, string.Format("Text(\"{0}\")", title));
            }
            else
            {
                EmitLine(
                    builder,
                    indent,
                    string.Format(
                        "Label(\"{0}\", systemImage: \"{1}\")",
                        title,
                        EscapeString(systemImage)));
            }
        }

        private static void EmitSlider(
            WatchUiNode node,
            StringBuilder builder,
            int indent,
            WatchUiSwiftStateContext stateContext)
        {
            var stateName = ResolveStateName(node, stateContext, "slider");
            var minimum = node.minimum.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var maximum = node.maximum.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (node.step > 0f)
            {
                EmitLine(
                    builder,
                    indent,
                    FormatNodeExpression(
                        node,
                        string.Format(
                            "Slider(value: ${0}, in: {1}...{2}, step: {3}) {{ Text(\"{4}\") }}",
                            stateName,
                            minimum,
                            maximum,
                            node.step.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            EscapeString(node.label ?? "Slider"))));
            }
            else
            {
                EmitLine(
                    builder,
                    indent,
                    FormatNodeExpression(
                        node,
                        string.Format(
                            "Slider(value: ${0}, in: {1}...{2}) {{ Text(\"{3}\") }}",
                            stateName,
                            minimum,
                            maximum,
                            EscapeString(node.label ?? "Slider"))));
            }

            EmitLine(
                builder,
                indent + 1,
                string.Format(
                    ".onChange(of: {0}) {{ _, newValue in {1} }}",
                    stateName,
                    EmitActionCall(
                        node,
                        "WatchSlider",
                        null,
                        "payload: [\"value\": Float(newValue)]")));
        }

        private static void EmitStepper(
            WatchUiNode node,
            StringBuilder builder,
            int indent,
            WatchUiSwiftStateContext stateContext)
        {
            var stateName = ResolveStateName(node, stateContext, "stepper");
            var minimum = node.minimum.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var maximum = node.maximum.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var step = node.step.ToString(System.Globalization.CultureInfo.InvariantCulture);
            EmitLine(
                builder,
                indent,
                FormatNodeExpression(
                    node,
                    string.Format(
                        "Stepper(value: ${0}, in: {1}...{2}, step: {3}) {{ Text(\"{4}\") }}",
                        stateName,
                        minimum,
                        maximum,
                        step,
                        EscapeString(node.label ?? "Stepper"))));
            EmitLine(
                builder,
                indent + 1,
                string.Format(
                    ".onChange(of: {0}) {{ _, newValue in {1} }}",
                    stateName,
                    EmitActionCall(node, "WatchStepper", null, "payload: [\"value\": Float(newValue)]")));
        }

        private static void EmitPicker(
            WatchUiNode node,
            StringBuilder builder,
            int indent,
            WatchUiConversionDiagnostics diagnostics,
            WatchUiSwiftStateContext stateContext)
        {
            var stateName = ResolveStateName(node, stateContext, "picker");
            EmitLine(
                builder,
                indent,
                FormatNodeExpression(
                    node,
                    string.Format("Picker(\"{0}\", selection: ${1}) {{", EscapeString(node.label ?? "Picker"), stateName)));

            if (node.children != null)
            {
                foreach (var child in node.children)
                {
                    if (child?.kind == "WatchPickerOption")
                        EmitPickerOption(child, builder, indent + 1);
                }
            }

            EmitLine(builder, indent, "}");
            var style = (node.style ?? string.Empty).Trim().ToLowerInvariant();
            if (style == "wheel")
            {
                EmitLine(builder, indent + 1, ".pickerStyle(.wheel)");
            }
            else if (style == "segmented" || style == "menu")
            {
                diagnostics.Warnings.Add(string.Format(
                    "WatchPicker '{0}' style '{1}' is unavailable on watchOS and will use automatic style.",
                    node.id ?? "<untitled>",
                    style));
            }
            else if (!string.IsNullOrWhiteSpace(style) && style != "automatic")
            {
                diagnostics.Warnings.Add(string.Format(
                    "WatchPicker '{0}' style '{1}' is not recognized and will use automatic style.",
                    node.id ?? "<untitled>",
                    style));
            }

            if (!string.IsNullOrWhiteSpace(node.action))
            {
                EmitLine(
                    builder,
                    indent + 1,
                    string.Format(
                        ".onChange(of: {0}) {{ _, newValue in {1} }}",
                        stateName,
                        EmitActionCall(node, "WatchPicker", null, "payload: [\"value\": newValue]")));
            }
        }

        private static void EmitPickerOption(WatchUiNode node, StringBuilder builder, int indent)
        {
            var label = EscapeString(string.IsNullOrWhiteSpace(node.label) ? node.selection : node.label);
            var value = EscapeString(node.selection ?? string.Empty);
            var expression = string.IsNullOrWhiteSpace(node.systemImage)
                ? string.Format("Text(\"{0}\")", label)
                : string.Format("Label(\"{0}\", systemImage: \"{1}\")", label, EscapeString(node.systemImage));
            EmitLine(builder, indent, string.Format("{0}.tag(\"{1}\")", expression, value));
        }

        private static void EmitGauge(
            WatchUiNode node,
            StringBuilder builder,
            int indent,
            WatchUiConversionDiagnostics diagnostics)
        {
            var minimum = node.minimum.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var maximum = node.maximum.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var value = node.value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var label = EscapeString(node.label ?? "Gauge");
            var current = EscapeString(node.currentValueLabel ?? string.Empty);
            var currentLabel = string.IsNullOrWhiteSpace(current)
                ? string.Empty
                : string.Format(" currentValueLabel: {{ Text(\"{0}\") }}", current);
            EmitLine(
                builder,
                indent,
                FormatNodeExpression(
                    node,
                    string.Format(
                        "Gauge(value: {0}, in: {1}...{2}) {{ Text(\"{3}\") }}{4}",
                        value,
                        minimum,
                        maximum,
                        label,
                        currentLabel)));

            var style = (node.style ?? string.Empty).Trim().ToLowerInvariant();
            if (style == "circular")
            {
                EmitLine(builder, indent + 1, ".gaugeStyle(.accessoryCircular)");
            }
            else if (style == "linear")
            {
                EmitLine(builder, indent + 1, ".gaugeStyle(.linearCapacity)");
            }
            else if (!string.IsNullOrWhiteSpace(style) && style != "automatic")
            {
                diagnostics.Warnings.Add(string.Format(
                    "WatchGauge '{0}' style '{1}' is not recognized and will use automatic style.",
                    node.id ?? "<untitled>",
                    style));
            }
        }

        private static void EmitJoystick(WatchUiNode node, StringBuilder builder, int indent)
        {
            var kind = "WatchJoystick";
            var sourceId = ResolveSourceId(node, kind);
            var action = string.IsNullOrWhiteSpace(node.action)
                ? string.Format("unbound:{0}", sourceId)
                : node.action;
            var diameter = (Math.Max(1f, node.radius) * 2f).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var deadZone = Math.Max(0f, node.deadZone).ToString(System.Globalization.CultureInfo.InvariantCulture);

            EmitLine(builder, indent, FormatNodeExpression(node, "ZStack {"));
            EmitLine(builder, indent + 1, "Circle().stroke()");
            EmitLine(builder, indent + 1, string.Format("Text(\"{0}\")", EscapeString(node.label ?? "Joystick")));
            EmitLine(builder, indent, "}");
            EmitLine(builder, indent + 1, string.Format(".frame(width: {0}, height: {0})", diameter));
            EmitTouchSurfacePresentation(node, builder, indent + 1);
            if (!node.enabled)
                return;

            EmitLine(builder, indent + 1, ".gesture(");
            EmitLine(builder, indent + 2, string.Format("DragGesture(minimumDistance: {0})", deadZone));
            EmitLine(builder, indent + 2, ".onChanged { value in");
            EmitLine(builder, indent + 3, string.Format("let normalizedX = Float(max(-1, min(1, value.translation.width / {0})))", Math.Max(1f, node.radius).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            EmitLine(builder, indent + 3, string.Format("let normalizedY = Float(max(-1, min(1, value.translation.height / {0})))", Math.Max(1f, node.radius).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            EmitLine(
                builder,
                indent + 3,
                EmitActionCall(
                    action,
                    sourceId,
                    kind + "/changed",
                    "payload: [\"x\": normalizedX, \"y\": normalizedY, \"axis0\": normalizedX, \"touchPress\": 1]"));
            EmitLine(builder, indent + 2, "}");
            EmitLine(builder, indent + 2, ".onEnded { value in");
            var endX = node.springReturn
                ? "0"
                : string.Format("Float(max(-1, min(1, value.translation.width / {0})))", Math.Max(1f, node.radius).ToString(System.Globalization.CultureInfo.InvariantCulture));
            var endY = node.springReturn
                ? "0"
                : string.Format("Float(max(-1, min(1, value.translation.height / {0})))", Math.Max(1f, node.radius).ToString(System.Globalization.CultureInfo.InvariantCulture));
            EmitLine(
                builder,
                indent + 3,
                EmitActionCall(
                    action,
                    sourceId,
                    kind + "/ended",
                    string.Format("payload: [\"x\": {0}, \"y\": {1}, \"axis0\": {0}, \"touchPress\": 0]", endX, endY)));
            EmitLine(builder, indent + 2, "}");
            EmitLine(builder, indent + 1, ")");
        }

        private static void EmitTextField(
            WatchUiNode node,
            StringBuilder builder,
            int indent,
            WatchUiSwiftStateContext stateContext)
        {
            var stateName = ResolveStateName(node, stateContext, "text");
            var title = EscapeString(string.IsNullOrWhiteSpace(node.title) ? "Input" : node.title);
            var prompt = EscapeString(node.prompt ?? string.Empty);
            var expression = string.IsNullOrWhiteSpace(prompt)
                ? string.Format("TextField(\"{0}\", text: ${1})", title, stateName)
                : string.Format("TextField(\"{0}\", text: ${1}, prompt: Text(\"{2}\"))", title, stateName, prompt);

            EmitLine(builder, indent, FormatNodeExpression(node, expression));

            EmitLine(
                builder,
                indent + 1,
                string.Format(
                    ".onChange(of: {0}) {{ _, newValue in {1} }}",
                    stateName,
                    EmitActionCall(
                        node,
                        "WatchTextField/change",
                        null,
                        "payload: [\"value\": newValue]")));

            if (!string.IsNullOrWhiteSpace(node.action))
            {
                EmitLine(
                    builder,
                    indent + 1,
                    string.Format(
                        ".onSubmit {{ {0} }}",
                        EmitActionCall(node, "WatchTextField/submit")));
            }

            if (!node.autocorrection)
            {
                EmitLine(builder, indent + 1, ".autocorrectionDisabled()");
            }
        }

        private static void EmitNavigationDestination(
            WatchUiNode node,
            StringBuilder builder,
            int indent)
        {
            EmitLine(
                builder,
                indent,
                string.Format("// NavigationDestination '{0}' must be nested directly inside a WatchNavigationStack.", EscapeString(node.route ?? string.Empty)));
        }

        private static void EmitTouchpad(
            WatchUiNode node,
            StringBuilder builder,
            int indent)
        {
            var kind = "WatchTouchpad";
            var sourceId = ResolveSourceId(node, kind);
            var action = string.IsNullOrWhiteSpace(node.action)
                ? string.Format("unbound:{0}", sourceId)
                : node.action;

            EmitLine(
                builder,
                indent,
                FormatNodeExpression(
                    node,
                    string.Format("Text(\"{0}\")", EscapeString(node.label ?? "[WatchTouchpad]"))));

            EmitTouchSurfacePresentation(node, builder, indent + 1);

            if (!node.enabled)
            {
                return;
            }

            EmitLine(
                builder,
                indent + 1,
                string.Format(
                    ".onTapGesture {{ {0} }}",
                    EmitActionCall(action, sourceId, kind + "/tap", string.Format("payload: [\"touchPress\": 1, \"touchPosX\": Float(0), \"touchPosY\": Float(0), \"tap\": 1]"))));

            EmitLine(
                builder,
                indent + 1,
                string.Format(
                    ".onTapGesture(count: 2) {{ {0} }}",
                    EmitActionCall(
                        action,
                        sourceId,
                        kind + "/doubleTap",
                        string.Format("payload: [\"touchPress\": 1, \"touchPosX\": Float(0), \"touchPosY\": Float(0), \"doubleTap\": 1]"))));

            EmitLine(
                builder,
                indent + 1,
                string.Format(
                    ".onLongPressGesture(minimumDuration: 0.4) {{ {0} }}",
                    EmitActionCall(action, sourceId, kind + "/longPress", string.Format("payload: [\"longPress\": 1, \"touchPress\": 1]"))));

            EmitLine(builder, indent + 1, " .gesture(");
            EmitLine(
                builder,
                indent + 2,
                "DragGesture(minimumDistance: 0.0)");
            EmitLine(
                builder,
                indent + 2,
                ".onEnded { value in");
            EmitLine(
                builder,
                indent + 3,
                EmitActionCall(
                    action,
                    sourceId,
                    kind + "/drag",
                    "payload: [\"axis0\": Float(value.translation.width), \"touchPosX\": Float(value.location.x), \"touchPosY\": Float(value.location.y), \"touchPress\": 1]"));
            EmitLine(builder, indent + 2, "}");
            EmitLine(builder, indent + 2, ")");
        }

        private static void EmitCrown(
            WatchUiNode node,
            StringBuilder builder,
            int indent,
            WatchUiSwiftStateContext stateContext)
        {
            var kind = "WatchCrown";
            var sourceId = ResolveSourceId(node, kind);
            var action = string.IsNullOrWhiteSpace(node.action)
                ? string.Format("unbound:{0}", sourceId)
                : node.action;

            EmitLine(
                builder,
                indent,
                FormatNodeExpression(
                    node,
                    string.Format("Text(\"{0}\")", EscapeString(node.label ?? "[WatchCrown]"))));

            EmitTouchSurfacePresentation(node, builder, indent + 1);

            if (!node.enabled)
            {
                return;
            }

            var state = stateContext.ResolveCrownStateNames(node);
            EmitLine(builder, indent + 1, ".focusable()");
            EmitLine(builder, indent + 1, $".focused(${state.Focus})");
            EmitLine(builder, indent + 1, $".onTapGesture {{ {state.Focus} = true }}");
            EmitLine(builder, indent + 1, $".onAppear {{ {state.Focus} = true }}");

            EmitLine(
                builder,
                indent + 1,
                $".digitalCrownRotation(${state.Value})");
            EmitLine(
                builder,
                indent + 1,
                $".onChange(of: {state.Value}) {{ _, newValue in");
            EmitLine(
                builder,
                indent + 2,
                $"let delta = Float(newValue - {state.Previous})");
            EmitLine(
                builder,
                indent + 2,
                $"{state.Previous} = newValue");
            EmitLine(
                builder,
                indent + 2,
                EmitActionCall(
                    action,
                    sourceId,
                    kind + "/rotate",
                    "payload: [\"crown\": delta, \"axis0\": delta]"));
            EmitLine(builder, indent + 1, "}");
        }

        private static void EmitMotion(
            WatchUiNode node,
            StringBuilder builder,
            int indent)
        {
            var kind = "WatchMotion";
            var sourceId = ResolveSourceId(node, kind);
            var action = string.IsNullOrWhiteSpace(node.action)
                ? string.Format("unbound:{0}", sourceId)
                : node.action;
            var interval = ResolveUpdateInterval(node.updateInterval, 0.1f)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);

            EmitLine(
                builder,
                indent,
                FormatNodeExpression(
                    node,
                    string.Format(
                        "Button(\"{0}\") {{ WatchSensorBridge.shared.startMotion(sourceId: \"{1}\", action: \"{2}\", interval: {3}, includeAcceleration: {4}, includeGyro: {5}, includeAttitude: {6}) }}",
                        EscapeString(node.label ?? "[WatchMotion]"),
                        EscapeString(sourceId),
                        EscapeString(action),
                        interval,
                        node.includeAcceleration ? "true" : "false",
                        node.includeGyro ? "true" : "false",
                        node.includeAttitude ? "true" : "false")));

            EmitLine(
                builder,
                indent + 1,
                string.Format(".onDisappear {{ WatchSensorBridge.shared.stopMotion(sourceId: \"{0}\") }}", EscapeString(sourceId)));
        }

        private static void EmitMicrophone(
            WatchUiNode node,
            StringBuilder builder,
            int indent)
        {
            var kind = "WatchMicrophone";
            var sourceId = ResolveSourceId(node, kind);
            var action = string.IsNullOrWhiteSpace(node.action)
                ? string.Format("unbound:{0}", sourceId)
                : node.action;
            var interval = ResolveUpdateInterval(node.updateInterval, 0.2f)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);

            EmitLine(builder, indent, FormatNodeExpression(node, "VStack {"));
            EmitLine(
                builder,
                indent + 1,
                string.Format(
                    "Button(\"{0}\") {{ WatchSensorBridge.shared.requestAndStartMicrophone(sourceId: \"{1}\", action: \"{2}\", interval: {3}) }}",
                    EscapeString(node.label ?? "[WatchMicrophone]"),
                    EscapeString(sourceId),
                    EscapeString(action),
                    interval));
            EmitLine(
                builder,
                indent + 1,
                string.Format(
                    "Button(\"Send Recording\") {{ WatchSensorBridge.shared.stopAndTransferMicrophone(sourceId: \"{0}\") }}",
                    EscapeString(sourceId)));
            EmitLine(builder, indent, "}");

            EmitLine(
                builder,
                indent + 1,
                string.Format(".onDisappear {{ WatchSensorBridge.shared.stopMicrophone(sourceId: \"{0}\") }}", EscapeString(sourceId)));
        }

        private static bool HasWatchCrown(WatchUiNode node)
        {
            if (node == null)
            {
                return false;
            }

            if (node.kind == "WatchCrown")
            {
                return true;
            }

            if (node.children == null || node.children.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < node.children.Length; i++)
            {
                if (HasWatchCrown(node.children[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasKind(WatchUiNode node, string kind)
        {
            if (node == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(kind) && node.kind == kind)
            {
                return true;
            }

            if (node.children == null || node.children.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < node.children.Length; i++)
            {
                if (HasKind(node.children[i], kind))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EmitProgressView(
            WatchUiNode node,
            StringBuilder builder,
            int indent,
            WatchUiConversionDiagnostics diagnostics)
        {
            var value = node.value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var total = node.total.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var label = EscapeString(node.label ?? "Progress");
            var hasFiniteValue = !float.IsNaN(node.value);
            var expression = hasFiniteValue
                ? string.Format("ProgressView(\"{0}\", value: {1}, total: {2})", label, value, total)
                : string.Format("ProgressView(\"{0}\")", label);

            EmitLine(builder, indent, FormatNodeExpression(node, expression));

            var style = (node.style ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(style) || style == "default")
            {
                return;
            }

            if (style == "circular")
            {
                EmitLine(builder, indent + 1, ".progressViewStyle(CircularProgressViewStyle())");
                return;
            }

            if (style == "linear")
            {
                EmitLine(builder, indent + 1, ".progressViewStyle(LinearProgressViewStyle())");
                return;
            }

            diagnostics.Warnings.Add(
                string.Format(
                    "WatchProgressView '{0}' has unknown style '{1}' and will use default.",
                    node.id ?? "<untitled>",
                    style));
        }

        private static string FormatNodeExpression(WatchUiNode node, string expression)
        {
            var metadata = BuildNodeMetadata(node);
            return string.IsNullOrWhiteSpace(metadata)
                ? expression
                : string.Format("{0} // {1}", expression, metadata);
        }

        private static string BuildNodeMetadata(WatchUiNode node)
        {
            var metadata = new List<string>();
            if (!string.IsNullOrWhiteSpace(node.id))
            {
                metadata.Add(string.Format("id='{0}'", node.id));
            }

            if (node.classes != null && node.classes.Length > 0)
            {
                metadata.Add(string.Format("classes={0}", string.Join(",", node.classes)));
            }

            if (!node.enabled)
            {
                metadata.Add("disabled");
            }

            if (metadata.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(", ", metadata);
        }

        private static bool HasWatchNavigation(WatchUiNode node)
        {
            if (node == null)
            {
                return false;
            }

            if (node.kind == "WatchNavigationStack")
            {
                return true;
            }

            if (node.children == null || node.children.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < node.children.Length; i++)
            {
                if (HasWatchNavigation(node.children[i]))
                    return true;
            }

            return false;
        }

        private static bool IsInteractiveKind(string kind)
        {
            return kind == "WatchButton"
                   || kind == "WatchTouchpad"
                   || kind == "WatchGestureArea"
                || kind == "WatchCrown"
                || kind == "WatchMotion"
                || kind == "WatchMicrophone"
                   || kind == "WatchToggle"
                   || kind == "WatchSlider"
                   || kind == "WatchStepper"
                   || kind == "WatchPicker"
                   || kind == "WatchJoystick"
                   || kind == "WatchTextField"
                || kind == "WatchNavigationLink";
        }

        private static string ResolveStateName(WatchUiNode node, WatchUiSwiftStateContext stateContext, string controlKind)
        {
            if (stateContext == null || node == null)
            {
                return controlKind == "slider"
                    ? "sliderValue"
                    : (controlKind == "text" ? "textValue" : "toggleValue");
            }

            return controlKind switch
            {
                "slider" => stateContext.ResolveSliderStateName(node),
                "text" => stateContext.ResolveTextFieldStateName(node),
                "stepper" => stateContext.ResolveStepperStateName(node),
                "picker" => stateContext.ResolvePickerStateName(node),
                _ => stateContext.ResolveToggleStateName(node)
            };
        }

        private static void EmitLine(StringBuilder builder, int indent, string line)
        {
            for (var i = 0; i < indent; i++)
                builder.Append("    ");
            builder.AppendLine(line);
        }

        private static string EscapeString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var escaped = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\':
                        escaped.Append("\\\\");
                        break;
                    case '"':
                        escaped.Append("\\\"");
                        break;
                    case '\n':
                        escaped.Append("\\n");
                        break;
                    case '\r':
                        escaped.Append("\\r");
                        break;
                    case '\t':
                        escaped.Append("\\t");
                        break;
                    default:
                        if (character < 0x20)
                            escaped.AppendFormat("\\u{{{0:x}}}", (int)character);
                        else
                            escaped.Append(character);
                        break;
                }
            }

            return escaped.ToString();
        }

        private static string EmitActionCall(WatchUiNode node, string kind, string action = null, string payload = null)
        {
            var sourceId = string.IsNullOrWhiteSpace(node.id) ? kind : node.id;
            var resolvedAction = string.IsNullOrWhiteSpace(action) ? string.Format("unbound:{0}", sourceId) : action;

            return BuildActionCall(resolvedAction, sourceId, kind, payload);
        }

        private static string EmitActionCall(string action, string sourceId, string kind = "WatchGestureArea", string payload = null)
        {
            var resolvedAction = string.IsNullOrWhiteSpace(action) ? string.Format("unbound:{0}", sourceId) : action;
            var resolvedSource = string.IsNullOrWhiteSpace(sourceId) ? kind : sourceId;

            return BuildActionCall(resolvedAction, resolvedSource, kind, payload);
        }

        private static string BuildActionCall(string action, string sourceId, string kind, string payload = null)
        {
            var call = string.Format(
                "WatchUiActionSink.shared.send(action: \"{0}\", sourceId: \"{1}\", kind: \"{2}\")",
                EscapeString(action),
                EscapeString(sourceId),
                EscapeString(kind));

            if (string.IsNullOrWhiteSpace(payload))
            {
                return call;
            }

            return call.Insert(call.Length - 1, string.Format(", {0}", payload));
        }

        private static string ResolveSourceId(WatchUiNode node, string kindFallback)
        {
            if (!string.IsNullOrWhiteSpace(node.id))
                return EscapeString(node.id);

            if (string.IsNullOrWhiteSpace(kindFallback))
                return "unknown";

            return kindFallback;
        }

        private static string EmitGestureActionCall(WatchUiNode node, string gestureKind, string action = null, string payload = null)
        {
            return EmitActionCall(node, gestureKind, action, payload);
        }

        private static float ResolveUpdateInterval(float value, float fallback)
        {
            if (value > 0f && !float.IsNaN(value) && !float.IsInfinity(value))
            {
                return value;
            }

            return fallback;
        }
    }
}
