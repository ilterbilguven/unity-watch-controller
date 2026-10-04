import Foundation
import WatchConnectivity

private struct WatchControllerFrame
{
    @WatchContinuousValue var primaryX: Float = 0
    @WatchContinuousValue var primaryY: Float = 0
    var primaryButton: UInt8 = 0

    var touchPress: UInt8 = 0
    var tap: UInt8 = 0
    var doubleTap: UInt8 = 0
    var primaryAction: UInt8 = 0
    var longPress: UInt8 = 0
    var drag: UInt8 = 0
    var swipeUp: UInt8 = 0
    var swipeDown: UInt8 = 0
    var swipeLeft: UInt8 = 0
    var swipeRight: UInt8 = 0

    var crown: Float = 0

    @WatchContinuousValue var accelerationX: Float = 0
    @WatchContinuousValue var accelerationY: Float = 0
    @WatchContinuousValue var accelerationZ: Float = 0

    @WatchContinuousValue var gyroX: Float = 0
    @WatchContinuousValue var gyroY: Float = 0
    @WatchContinuousValue var gyroZ: Float = 0

    @WatchContinuousValue var microphoneLevel: Float = 0
    @WatchContinuousValue var microphonePeak: Float = 0

    @WatchContinuousValue var touchPosX: Float = 0
    @WatchContinuousValue var touchPosY: Float = 0

    @WatchContinuousValue var axis0: Float = 0

    var attitude = WatchAttitude()
    @WatchContinuousValue var motionSampleInterval: Float = 0

    // Native-only monotonic clock; only the elapsed queue age crosses the bridge.
    var nativeReceivedAt = ContinuousClock.now
    var nativeQueueAgeSeconds: Float = 0

    mutating func clearStaleContinuousInput(at now: ContinuousClock.Instant, after timeout: Duration)
    {
        _primaryX.clearIfStale(at: now, after: timeout)
        _primaryY.clearIfStale(at: now, after: timeout)
        _accelerationX.clearIfStale(at: now, after: timeout)
        _accelerationY.clearIfStale(at: now, after: timeout)
        _accelerationZ.clearIfStale(at: now, after: timeout)
        _gyroX.clearIfStale(at: now, after: timeout)
        _gyroY.clearIfStale(at: now, after: timeout)
        _gyroZ.clearIfStale(at: now, after: timeout)
        _microphoneLevel.clearIfStale(at: now, after: timeout)
        _microphonePeak.clearIfStale(at: now, after: timeout)
        _touchPosX.clearIfStale(at: now, after: timeout)
        _touchPosY.clearIfStale(at: now, after: timeout)
        _axis0.clearIfStale(at: now, after: timeout)
        attitude.clearIfStale(at: now, after: timeout)
        _motionSampleInterval.clearIfStale(at: now, after: timeout)
    }

    mutating func updateMotion(from message: [String: Any])
    {
        // An omitted attitude must not renew the retained pose's timestamp.
        if message["attitude"] != nil
        {
            attitude = WatchAttitude(message: message)
        }
        if let interval = message["motionSampleInterval"] as? NSNumber
        {
            let seconds = interval.floatValue
            motionSampleInterval = seconds.isFinite && seconds >= 0 ? seconds : 0
        }
    }
}

private struct WatchAttitude
{
    private(set) var x: Float = 0
    private(set) var y: Float = 0
    private(set) var z: Float = 0
    private(set) var w: Float = 1
    private(set) var tracked = false
    private(set) var reference: UInt32 = 0
    private var updatedAt = ContinuousClock.now

    init() {}

    init(message: [String: Any])
    {
        guard let raw = message["attitude"] as? [NSNumber], raw.count == 4,
              let rawReference = message["attitudeReference"] as? NSNumber else { return }
        let referenceValue = rawReference.doubleValue
        guard referenceValue.isFinite, referenceValue >= 1,
              referenceValue <= Double(UInt32.max), referenceValue.rounded(.towardZero) == referenceValue else { return }
        let components = raw.map(\.doubleValue)
        guard components.allSatisfy(\.isFinite),
              let scale = components.map({ abs($0) }).max(), scale > 0 else { return }
        // Scale first so even finite values near Double.greatestFiniteMagnitude
        // normalize without overflowing. This is still raw CoreMotion handedness.
        let scaled = components.map { $0 / scale }
        let length = sqrt(scaled.reduce(0) { $0 + $1 * $1 })
        x = Float(scaled[0] / length)
        y = Float(scaled[1] / length)
        z = Float(scaled[2] / length)
        w = Float(scaled[3] / length)
        tracked = true
        reference = UInt32(referenceValue)
    }

    mutating func clearIfStale(at now: ContinuousClock.Instant, after timeout: Duration)
    {
        if updatedAt.duration(to: now) >= timeout
        {
            x = 0
            y = 0
            z = 0
            w = 1
            tracked = false
            reference = 0
        }
    }

    func ageSeconds(at now: ContinuousClock.Instant) -> Float
    {
        guard tracked else { return 0 }
        let age = updatedAt.duration(to: now).components
        return Float(max(0, Double(age.seconds) + Double(age.attoseconds) / 1e18))
    }
}

@propertyWrapper
private struct WatchContinuousValue
{
    private var value: Float
    private var updatedAt = ContinuousClock.now

    init(wrappedValue: Float)
    {
        value = wrappedValue
    }

    var wrappedValue: Float
    {
        get { value }
        set
        {
            value = newValue
            updatedAt = .now
        }
    }

    mutating func clearIfStale(at now: ContinuousClock.Instant, after timeout: Duration)
    {
        // Copying a retained snapshot or expiring it must not renew its lifetime.
        if updatedAt.duration(to: now) >= timeout
        {
            value = 0
        }
    }
}

private func hasDiscreteInput(_ frame: WatchControllerFrame) -> Bool
{
    return frame.primaryButton > 0
        || frame.touchPress > 0
        || frame.tap > 0
        || frame.doubleTap > 0
        || frame.primaryAction > 0
        || frame.longPress > 0
        || frame.drag > 0
        || frame.swipeUp > 0
        || frame.swipeDown > 0
        || frame.swipeLeft > 0
        || frame.swipeRight > 0
        || frame.crown != 0
}

private func clearingDiscreteInput(from frame: WatchControllerFrame) -> WatchControllerFrame
{
    var continuous = frame
    continuous.primaryButton = 0
    continuous.touchPress = 0
    continuous.tap = 0
    continuous.doubleTap = 0
    continuous.primaryAction = 0
    continuous.longPress = 0
    continuous.drag = 0
    continuous.swipeUp = 0
    continuous.swipeDown = 0
    continuous.swipeLeft = 0
    continuous.swipeRight = 0
    continuous.crown = 0
    return continuous
}

private let AppleWatchInputFrameByteLength = 216

private func appendFloat(_ value: Float, into bytes: inout [UInt8], _ offset: Int)
{
    var safeValue = value
    if safeValue.isFinite == false
    {
        safeValue = 0
    }

    let valueBytes = withUnsafeBytes(of: safeValue) { Array($0) }
    if bytes.count < offset + valueBytes.count
    {
        return
    }

    for i in 0..<valueBytes.count
    {
        bytes[offset + i] = valueBytes[i]
    }
}

private func encodeWatchControllerFrame(_ frame: WatchControllerFrame) -> [UInt8]
{
    var bytes = [UInt8](repeating: 0, count: AppleWatchInputFrameByteLength)

    appendFloat(frame.primaryX, into: &bytes, 0)
    appendFloat(frame.primaryY, into: &bytes, 4)
    bytes[8] = frame.primaryButton > 0 ? 1 : 0
    bytes[9] = frame.touchPress > 0 ? 1 : 0
    bytes[10] = frame.tap > 0 ? 1 : 0
    bytes[11] = frame.doubleTap > 0 ? 1 : 0
    bytes[12] = frame.primaryAction > 0 ? 1 : 0
    bytes[13] = frame.longPress > 0 ? 1 : 0
    bytes[18] = frame.drag > 0 ? 1 : 0
    bytes[14] = frame.swipeUp > 0 ? 1 : 0
    bytes[15] = frame.swipeDown > 0 ? 1 : 0
    bytes[16] = frame.swipeLeft > 0 ? 1 : 0
    bytes[17] = frame.swipeRight > 0 ? 1 : 0

    appendFloat(frame.crown, into: &bytes, 20)
    appendFloat(frame.accelerationX, into: &bytes, 24)
    appendFloat(frame.accelerationY, into: &bytes, 28)
    appendFloat(frame.accelerationZ, into: &bytes, 32)

    appendFloat(frame.gyroX, into: &bytes, 36)
    appendFloat(frame.gyroY, into: &bytes, 40)
    appendFloat(frame.gyroZ, into: &bytes, 44)

    appendFloat(frame.microphoneLevel, into: &bytes, 48)
    appendFloat(frame.microphonePeak, into: &bytes, 52)
    appendFloat(frame.touchPosX, into: &bytes, 56)
    appendFloat(frame.touchPosY, into: &bytes, 60)
    appendFloat(frame.axis0, into: &bytes, 64)
    appendFloat(frame.nativeQueueAgeSeconds, into: &bytes, 68)
    appendFloat(frame.attitude.x, into: &bytes, 72)
    appendFloat(frame.attitude.y, into: &bytes, 76)
    appendFloat(frame.attitude.z, into: &bytes, 80)
    appendFloat(frame.attitude.w, into: &bytes, 84)
    bytes[88] = frame.attitude.tracked ? 1 : 0
    let reference = frame.attitude.reference
    for index in 0..<4
    {
        bytes[92 + index] = UInt8(truncatingIfNeeded: reference >> (index * 8))
    }
    appendFloat(frame.motionSampleInterval, into: &bytes, 96)
    // Includes time before this packet was queued, and time spent in the queue.
    // Managed consumers must not subtract nativeQueueAgeSeconds a second time.
    appendFloat(frame.attitude.ageSeconds(at: .now), into: &bytes, 100)

    return bytes
}

private final class WatchTouchBuffer
{
    private let FrameQueueCapacity = 64
    private var pendingFrames: [WatchControllerFrame] = []
    private var deferredContinuousFrame: WatchControllerFrame?
    private var continuousFrame = WatchControllerFrame()
    private var emitNeutralAfterOverflow = false
    private var staleTimeout: Duration = .milliseconds(250)
    private let lock = NSLock()

    func setStaleTimeout(_ seconds: Float)
    {
        guard seconds.isFinite, seconds > 0 else { return }
        lock.lock()
        defer { lock.unlock() }
        staleTimeout = .seconds(Double(max(0.001, seconds)))
    }

    func currentContinuousFrame() -> WatchControllerFrame
    {
        lock.lock()
        defer { lock.unlock() }
        continuousFrame.clearStaleContinuousInput(at: .now, after: staleTimeout)
        return continuousFrame
    }

    func push(_ frame: WatchControllerFrame)
    {
        let receivedAt = ContinuousClock.now
        lock.lock()
        defer { lock.unlock() }

        var frame = frame
        frame.nativeReceivedAt = receivedAt
        frame.nativeQueueAgeSeconds = 0
        frame.clearStaleContinuousInput(at: receivedAt, after: staleTimeout)
        continuousFrame = clearingDiscreteInput(from: frame)
        let isDiscrete = hasDiscreteInput(frame)

        if !isDiscrete,
           pendingFrames.count >= FrameQueueCapacity,
           !pendingFrames.contains(where: { !hasDiscreteInput($0) })
        {
            deferredContinuousFrame = frame
            return
        }

        if !makeRoomForIncomingFrame(isDiscrete: isDiscrete)
        {
            return
        }

        // This accepted frame supersedes any older deferred continuous snapshot.
        deferredContinuousFrame = nil

        if !isDiscrete,
           let last = pendingFrames.last,
           !hasDiscreteInput(last)
        {
            pendingFrames[pendingFrames.count - 1] = frame
            return
        }

        pendingFrames.append(frame)
    }

    func pop() -> WatchControllerFrame?
    {
        lock.lock()
        defer { lock.unlock() }

        if emitNeutralAfterOverflow
        {
            emitNeutralAfterOverflow = false
            return WatchControllerFrame()
        }

        guard !pendingFrames.isEmpty else { return nil }
        var frame = pendingFrames.removeFirst()
        let now = ContinuousClock.now
        frame.clearStaleContinuousInput(at: now, after: staleTimeout)
        let age = frame.nativeReceivedAt.duration(to: now).components
        frame.nativeQueueAgeSeconds = Float(Double(age.seconds) + Double(age.attoseconds) / 1e18)
        if pendingFrames.isEmpty,
           let deferredContinuousFrame
        {
            pendingFrames.append(deferredContinuousFrame)
            self.deferredContinuousFrame = nil
        }
        return frame
    }

    private func makeRoomForIncomingFrame(isDiscrete: Bool) -> Bool
    {
        guard pendingFrames.count >= FrameQueueCapacity else { return true }

        if let coalescibleIndex = pendingFrames.firstIndex(where: { !hasDiscreteInput($0) })
        {
            pendingFrames.remove(at: coalescibleIndex)
            return true
        }

        if isDiscrete
        {
            pendingFrames.removeAll(keepingCapacity: true)
            deferredContinuousFrame = nil
            continuousFrame = WatchControllerFrame()
            emitNeutralAfterOverflow = true
            return false
        }

        return false
    }
}

private final class WatchRecordingPathBuffer
{
    private let capacity = 16
    private var paths: [String] = []
    private let lock = NSLock()

    func push(_ path: String)
    {
        lock.lock()
        defer { lock.unlock() }
        if paths.count >= capacity
        {
            paths.removeFirst()
        }
        paths.append(path)
    }

    func pushFront(_ path: String)
    {
        lock.lock()
        defer { lock.unlock() }
        if paths.count >= capacity
        {
            paths.removeLast()
        }
        paths.insert(path, at: 0)
    }

    func pop() -> String?
    {
        lock.lock()
        defer { lock.unlock() }
        guard !paths.isEmpty else { return nil }
        return paths.removeFirst()
    }
}

private final class WatchSessionManager: NSObject, WCSessionDelegate
{
    private static var sharedInstance: WatchSessionManager?
    private let touchBuffer = WatchTouchBuffer()
    private let recordingPathBuffer = WatchRecordingPathBuffer()

    static var shared: WatchSessionManager
    {
        if let instance = sharedInstance
        {
            return instance
        }

        let created = WatchSessionManager()
        sharedInstance = created
        return created
    }

    func start()
    {
        guard WCSession.isSupported() else { return }
        let session = WCSession.default
        session.delegate = self
        session.activate()
    }

    func setStaleTimeout(_ seconds: Float)
    {
        touchBuffer.setStaleTimeout(seconds)
    }

    func session(_ session: WCSession, activationDidCompleteWith activationState: WCSessionActivationState, error: (any Error)?)
    {
    }

    func popFrame(
        _ x: UnsafeMutablePointer<Float>,
        _ y: UnsafeMutablePointer<Float>,
        _ button: UnsafeMutablePointer<UInt8>,
        _ touchPress: UnsafeMutablePointer<UInt8>,
        _ tap: UnsafeMutablePointer<UInt8>,
        _ doubleTap: UnsafeMutablePointer<UInt8>,
        _ primaryAction: UnsafeMutablePointer<UInt8>,
        _ longPress: UnsafeMutablePointer<UInt8>,
        _ drag: UnsafeMutablePointer<UInt8>,
        _ swipeUp: UnsafeMutablePointer<UInt8>,
        _ swipeDown: UnsafeMutablePointer<UInt8>,
        _ swipeLeft: UnsafeMutablePointer<UInt8>,
        _ swipeRight: UnsafeMutablePointer<UInt8>,
        _ crown: UnsafeMutablePointer<Float>,
        _ accelerationX: UnsafeMutablePointer<Float>,
        _ accelerationY: UnsafeMutablePointer<Float>,
        _ accelerationZ: UnsafeMutablePointer<Float>,
        _ gyroX: UnsafeMutablePointer<Float>,
        _ gyroY: UnsafeMutablePointer<Float>,
        _ gyroZ: UnsafeMutablePointer<Float>,
        _ microphoneLevel: UnsafeMutablePointer<Float>,
        _ microphonePeak: UnsafeMutablePointer<Float>,
        _ touchPosX: UnsafeMutablePointer<Float>,
        _ touchPosY: UnsafeMutablePointer<Float>,
        _ axis0: UnsafeMutablePointer<Float>) -> UInt8
    {
        guard let sample = touchBuffer.pop() else { return 0 }

        x.pointee = sample.primaryX
        y.pointee = sample.primaryY
        button.pointee = sample.primaryButton
        touchPress.pointee = sample.touchPress
        tap.pointee = sample.tap
        doubleTap.pointee = sample.doubleTap
        primaryAction.pointee = sample.primaryAction
        longPress.pointee = sample.longPress
        drag.pointee = sample.drag
        swipeUp.pointee = sample.swipeUp
        swipeDown.pointee = sample.swipeDown
        swipeLeft.pointee = sample.swipeLeft
        swipeRight.pointee = sample.swipeRight
        crown.pointee = sample.crown
        accelerationX.pointee = sample.accelerationX
        accelerationY.pointee = sample.accelerationY
        accelerationZ.pointee = sample.accelerationZ
        gyroX.pointee = sample.gyroX
        gyroY.pointee = sample.gyroY
        gyroZ.pointee = sample.gyroZ
        microphoneLevel.pointee = sample.microphoneLevel
        microphonePeak.pointee = sample.microphonePeak
        touchPosX.pointee = sample.touchPosX
        touchPosY.pointee = sample.touchPosY
        axis0.pointee = sample.axis0

        return 1
    }

    func popFrameBytes(
        _ bytesOut: UnsafeMutablePointer<UInt8>,
        _ maxByteCount: Int) -> Int
    {
        guard maxByteCount >= AppleWatchInputFrameByteLength else
        {
            return 0
        }

        guard let sample = touchBuffer.pop() else { return 0 }
        let encoded = encodeWatchControllerFrame(sample)
        encoded.withUnsafeBufferPointer
        { buffer in
            guard let source = buffer.baseAddress else { return }
            bytesOut.update(from: source, count: AppleWatchInputFrameByteLength)
        }

        return AppleWatchInputFrameByteLength
    }

    func popRecordingPath(_ bytesOut: UnsafeMutablePointer<UInt8>, _ maxByteCount: Int) -> Int
    {
        guard let path = recordingPathBuffer.pop() else { return 0 }
        let utf8 = Array(path.utf8)
        let requiredByteCount = utf8.count + 1
        guard maxByteCount >= requiredByteCount else
        {
            recordingPathBuffer.pushFront(path)
            return -requiredByteCount
        }

        for index in utf8.indices
        {
            bytesOut[index] = utf8[index]
        }
        bytesOut[utf8.count] = 0
        return utf8.count
    }

    private func readNumber(_ value: Any?) -> Float?
    {
        guard let number = value as? NSNumber else { return nil }
        let floatValue = number.floatValue
        return floatValue.isFinite ? floatValue : nil
    }

    private enum WatchUiActionKind: Int
    {
        case unknown = 0
        case watchButton = 1
        case watchTouchpadTap = 2
        case watchTouchpadDoubleTap = 3
        case watchTouchpadLongPress = 4
        case watchTouchpadDrag = 5
        case watchCrownPress = 6
        case watchCrownRotate = 7
        case watchGestureAreaTap = 8
        case watchGestureAreaDoubleTap = 9
        case watchGestureAreaLongPress = 10
        case watchGestureAreaSwipeUp = 11
        case watchGestureAreaSwipeDown = 12
        case watchGestureAreaSwipeLeft = 13
        case watchGestureAreaSwipeRight = 14
        case watchGestureAreaDrag = 15
        case watchToggle = 16
        case watchSlider = 17
        case watchTextFieldChange = 18
        case watchTextFieldSubmit = 19
    }

    private func readNumber(_ message: [String: Any], key: String) -> Float?
    {
        guard let value = message[key] else { return nil }

        if let boolValue = value as? Bool
        {
            return boolValue ? 1.0 : 0.0
        }

        if let number = value as? NSNumber
        {
            let floatValue = number.floatValue
            return floatValue.isFinite ? floatValue : nil
        }

        return readFloatString(value)
    }

    private func readFloatString(_ raw: Any?) -> Float?
    {
        guard let text = raw as? String else { return nil }
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return nil }
        guard let value = Float(trimmed) else { return nil }
        return value.isFinite ? value : nil
    }

    private func readInt(_ raw: Any?) -> Int?
    {
        if let boolValue = raw as? Bool
        {
            return boolValue ? 1 : nil
        }

        if let number = raw as? NSNumber
        {
            // Keep exact integer boundaries before a Double can round them.
            // Int(exactly:) rejects fractional, nonfinite and overflow values.
            return Int(number.stringValue) ?? Int(exactly: number.doubleValue)
        }
        if let text = raw as? String
        {
            let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
            return Int(trimmed)
        }

        return nil
    }

    private func readInt(_ message: [String: Any], key: String) -> Int?
    {
        guard let raw = message[key] else { return nil }
        return readInt(raw)
    }

    private func resolveWatchUiActionKind(_ normalizedKind: String, _ kindId: Int?) -> WatchUiActionKind
    {
        if let kindId, kindId >= 1, let mapped = WatchUiActionKind(rawValue: kindId)
        {
            return mapped
        }

        return switch normalizedKind
        {
        case "watchbutton":
            .watchButton
        case "watchtouchpad/tap":
            .watchTouchpadTap
        case "watchtouchpad/doubletap":
            .watchTouchpadDoubleTap
        case "watchtouchpad/longpress":
            .watchTouchpadLongPress
        case "watchtouchpad/drag":
            .watchTouchpadDrag
        case "watchcrown/press":
            .watchCrownPress
        case "watchcrown/rotate":
            .watchCrownRotate
        case "watchgesturearea/tap":
            .watchGestureAreaTap
        case "watchgesturearea/doubletap":
            .watchGestureAreaDoubleTap
        case "watchgesturearea/longpress":
            .watchGestureAreaLongPress
        case "watchgesturearea/swipe-up":
            .watchGestureAreaSwipeUp
        case "watchgesturearea/swipe-down":
            .watchGestureAreaSwipeDown
        case "watchgesturearea/swipe-left":
            .watchGestureAreaSwipeLeft
        case "watchgesturearea/swipe-right":
            .watchGestureAreaSwipeRight
        case "watchgesturearea/drag":
            .watchGestureAreaDrag
        case "watchtoggle":
            .watchToggle
        case "watchslider":
            .watchSlider
        case "watchtextfield/change":
            .watchTextFieldChange
        case "watchtextfield/submit":
            .watchTextFieldSubmit
        default:
            .unknown
        }
    }

    private func readBoolLike(_ raw: Any?) -> Bool?
    {
        if let boolValue = raw as? Bool
        {
            return boolValue;
        }

        if let numberValue = readNumber(raw)
        {
            return numberValue != 0;
        }

        guard let textValue = raw as? String else { return nil }
        let normalized = textValue.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        if normalized == "1" || normalized == "true" || normalized == "yes" || normalized == "on"
        {
            return true;
        }
        if normalized == "0" || normalized == "false" || normalized == "no" || normalized == "off"
        {
            return false;
        }

        return nil;
    }

    private func readFlag(_ message: [String: Any], keys: [String], defaultValue: UInt8 = 0) -> UInt8
    {
        for key in keys
        {
            if let value = readBoolLike(message[key])
            {
                return value ? 1 : 0
            }
        }

        return defaultValue
    }

    private func readVector3(_ raw: Any?) -> (Float, Float, Float)?
    {
        guard
            let values = raw as? [Any],
            values.count == 3
        else
        {
            return nil
        }

        let first = readNumber(values[0])
        let second = readNumber(values[1])
        let third = readNumber(values[2])
        if let first, let second, let third,
           first.isFinite && second.isFinite && third.isFinite
        {
            return (first, second, third)
        }

        return nil
    }

    private func readVector3FromMessage(
        _ message: [String: Any],
        keys: [String],
        xKeys: [String],
        yKeys: [String],
        zKeys: [String]
    ) -> (Float, Float, Float)?
    {
        for key in keys
        {
            if let vector = readVector3(message[key])
            {
                return vector
            }
        }

        for key in keys
        {
            guard let components = message[key] as? [String: Any] else
            {
                continue
            }

            var x: Float?
            var y: Float?
            var z: Float?
            for alias in xKeys
            {
                if let value = readNumber(components[alias])
                {
                    x = value
                    break
                }
            }

            for alias in yKeys
            {
                if let value = readNumber(components[alias])
                {
                    y = value
                    break
                }
            }

            for alias in zKeys
            {
                if let value = readNumber(components[alias])
                {
                    z = value
                    break
                }
            }

            if let x, let y, let z,
               x.isFinite && y.isFinite && z.isFinite
            {
                return (x, y, z)
            }
        }

        var x: Float?
        var y: Float?
        var z: Float?

        for alias in xKeys
        {
            if let value = readNumber(message, key: alias)
            {
                x = value
                break
            }
        }

        for alias in yKeys
        {
            if let value = readNumber(message, key: alias)
            {
                y = value
                break
            }
        }

        for alias in zKeys
        {
            if let value = readNumber(message, key: alias)
            {
                z = value
                break
            }
            if let vectorAlias = readNumber(message, key: alias + "s")
            {
                z = vectorAlias
                break
            }
        }

        if let x, let y, let z,
           x.isFinite && y.isFinite && z.isFinite
        {
            return (x, y, z)
        }

        return nil;
    }

    private func readVector2(_ raw: Any?) -> (Float, Float)?
    {
        guard
            let values = raw as? [Any],
            values.count == 2
        else
        {
            return nil
        }

        let first = readNumber(values[0])
        let second = readNumber(values[1])
        if let first, let second, first.isFinite && second.isFinite
        {
            return (first, second)
        }

        return nil;
    }

    private func readVector2FromMessage(_ message: [String: Any], keys: [String]) -> (Float, Float)?
    {
        for key in keys
        {
            let vector = readVector2(message[key])
            if let vector,
               vector.0.isFinite && vector.1.isFinite
            {
                return vector
            }
        }

        return nil
    }

    private func readVector2FromMessage(
        _ message: [String: Any],
        keys: [String],
        xKeys: [String],
        yKeys: [String]
    ) -> (Float, Float)?
    {
        for key in keys
        {
            if let vector = readVector2(message[key])
            {
                return vector
            }

            guard let components = message[key] as? [String: Any] else
            {
                continue
            }

            var x: Float?
            var y: Float?

            for key in xKeys
            {
                if let value = readNumber(components[key])
                {
                    x = value
                    break
                }
            }

            for key in yKeys
            {
                if let value = readNumber(components[key])
                {
                    y = value
                    break
                }
            }

            if let x, let y, x.isFinite && y.isFinite
            {
                return (x, y)
            }
        }

        return nil
    }

    private func readVector2FromMessage(
        _ message: [String: Any],
        xKeys: [String],
        yKeys: [String]
    ) -> (Float, Float)?
    {
        var x: Float?
        var y: Float?

        for alias in xKeys
        {
            if let value = readNumber(message, key: alias)
            {
                x = value
                break
            }
        }

        for alias in yKeys
        {
            if let value = readNumber(message, key: alias)
            {
                y = value
                break
            }
        }

        if let x, let y, x.isFinite && y.isFinite
        {
            return (x, y)
        }

        return nil
    }

    private func readVector3FromMessage(_ message: [String: Any], keys: [String]) -> (Float, Float, Float)?
    {
        return readVector3FromMessage(
            message,
            keys: keys,
            xKeys: ["x", "accelX", "roll"],
            yKeys: ["y", "accelY", "pitch"],
            zKeys: ["z", "accelZ", "yaw"]
        )
    }

    private func readString(_ message: [String: Any], key: String) -> String?
    {
        guard let value = message[key] else { return nil }

        if let text = value as? String
        {
            return text.isEmpty ? nil : text
        }

        if let number = value as? NSNumber
        {
            return number.stringValue
        }

        if let boolValue = value as? Bool
        {
            return boolValue ? "true" : "false"
        }

        return nil
    }

    private func readAxis0Value(_ message: [String: Any], aliases: [String]) -> Float?
    {
        for key in aliases
        {
            let number = readNumber(message, key: key)
            if let number
            {
                return number
            }

            if let stringValue = message[key], let number = readFloatString(stringValue)
            {
                return number
            }
        }

        return nil
    }

    private func readTouchPositionFromMessage(
        _ message: [String: Any],
        vectorKeys: [String],
        xKeys: [String],
        yKeys: [String]
    ) -> (Float, Float)?
    {
        if let vector = readVector2FromMessage(message, keys: vectorKeys)
        {
            return vector
        }

        var touchPosX: Float?
        var touchPosY: Float?

        for key in xKeys
        {
            if let value = readNumber(message, key: key)
            {
                touchPosX = value
                break
            }
        }

        for key in yKeys
        {
            if let value = readNumber(message, key: key)
            {
                touchPosY = value
                break
            }
        }

        guard let x = touchPosX, let y = touchPosY, x.isFinite && y.isFinite else
        {
            return nil
        }

        return (x, y)
    }

    private func isWatchActionMessage(_ message: [String: Any]) -> Bool
    {
        if case let boolValue as Bool = message["watch-action"], boolValue
        {
            return true
        }

        if let kindId = readInt(message, key: "kind-id"),
            let mapped = WatchUiActionKind(rawValue: kindId),
            mapped != .unknown
        {
            return true
        }

        guard let kind = readString(message, key: "kind")?.trimmingCharacters(in: .whitespacesAndNewlines),
              !kind.isEmpty else
        {
            return false
        }

        return kind.hasPrefix("watch") || kind.hasPrefix("Watch")
    }

    private func applyActionAsControls(_ message: [String: Any], to frame: inout WatchControllerFrame)
    {
        let kind = readString(message, key: "kind")?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        let action = readString(message, key: "action")?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        let normalized = kind.lowercased()
        let normalizedAction = action.lowercased()
        let kindId = readInt(message, key: "kind-id")
        let resolvedKind = resolveWatchUiActionKind(normalized, kindId)
        func applyGestureAreaPayload(to frame: inout WatchControllerFrame)
        {
            if let touchPosition = self.readTouchPositionFromMessage(
                message,
                vectorKeys: ["touchPosition", "touchPos", "touch"],
                xKeys: ["touchPosX", "touchPositionX", "touchX", "x"],
                yKeys: ["touchPosY", "touchPositionY", "touchY", "y"]
            )
            {
                frame.touchPosX = touchPosition.0
                frame.touchPosY = touchPosition.1
            }

            if let axis = self.readAxis0Value(message, aliases: ["axis0", "value"])
            {
                frame.axis0 = axis
            }
        }

        switch resolvedKind
        {
        case .watchButton:
            frame.primaryButton = 1
            return

        case .watchTouchpadTap:
            frame.touchPress = readFlag(message, keys: ["touchPress", "touch-press", "isTouchPress", "tap"], defaultValue: 1)
            frame.tap = readFlag(message, keys: ["tap", "isTap", "is-tap"], defaultValue: 1)
            frame.axis0 = readAxis0Value(message, aliases: ["axis0", "axis"]) ?? 0
            if let touchPosition = readTouchPositionFromMessage(
                message,
                vectorKeys: ["touchPosition", "touchPos", "touch"],
                xKeys: ["touchPosX", "touchPositionX", "touchX", "x"],
                yKeys: ["touchPosY", "touchPositionY", "touchY", "y"]
            )
            {
                frame.touchPosX = touchPosition.0
                frame.touchPosY = touchPosition.1
            }
            return

        case .watchTouchpadDoubleTap:
            frame.touchPress = readFlag(message, keys: ["touchPress", "touch-press", "isTouchPress", "doubleTap"], defaultValue: 1)
            frame.doubleTap = readFlag(message, keys: ["doubleTap", "isDoubleTap", "is-double-tap"], defaultValue: 1)
            frame.axis0 = readAxis0Value(message, aliases: ["axis0", "axis"]) ?? 0
            if let touchPosition = readTouchPositionFromMessage(
                message,
                vectorKeys: ["touchPosition", "touchPos", "touch"],
                xKeys: ["touchPosX", "touchPositionX", "touchX", "x"],
                yKeys: ["touchPosY", "touchPositionY", "touchY", "y"]
            )
            {
                frame.touchPosX = touchPosition.0
                frame.touchPosY = touchPosition.1
            }
            return

        case .watchTouchpadLongPress:
            frame.touchPress = readFlag(message, keys: ["touchPress", "touch-press", "isTouchPress", "longPress"], defaultValue: 1)
            frame.longPress = readFlag(message, keys: ["longPress", "isLongPress", "is-long-press"], defaultValue: 1)
            frame.axis0 = readAxis0Value(message, aliases: ["axis0", "axis"]) ?? 0
            if let touchPosition = readTouchPositionFromMessage(
                message,
                vectorKeys: ["touchPosition", "touchPos", "touch"],
                xKeys: ["touchPosX", "touchPositionX", "touchX", "x"],
                yKeys: ["touchPosY", "touchPositionY", "touchY", "y"]
            )
            {
                frame.touchPosX = touchPosition.0
                frame.touchPosY = touchPosition.1
            }
            return

        case .watchTouchpadDrag:
            frame.drag = readFlag(message, keys: ["drag", "isDrag", "is-drag"], defaultValue: 1)
            frame.touchPress = readFlag(message, keys: ["touchPress", "touch-press", "isTouchPress"], defaultValue: 1)
            frame.axis0 = readAxis0Value(message, aliases: ["axis0", "axis"]) ?? 0
            if let touchPosition = readTouchPositionFromMessage(
                message,
                vectorKeys: ["touchPosition", "touchPos", "touch"],
                xKeys: ["touchPosX", "touchPositionX", "touchX", "x"],
                yKeys: ["touchPosY", "touchPositionY", "touchY", "y"]
            )
            {
                frame.touchPosX = touchPosition.0
                frame.touchPosY = touchPosition.1
            }
            return

        case .watchCrownPress:
            frame.primaryButton = 1
            return

        case .watchCrownRotate:
            frame.crown = readAxis0Value(message, aliases: ["crown", "axis0"]) ?? frame.crown
            if let axis = readAxis0Value(message, aliases: ["axis0", "crown"])
            {
                frame.axis0 = axis
            }
            return

        case .watchGestureAreaTap:
            frame.touchPress = 1
            applyGestureAreaPayload(to: &frame)
            return

        case .watchGestureAreaDoubleTap:
            frame.doubleTap = 1
            applyGestureAreaPayload(to: &frame)
            return

        case .watchGestureAreaLongPress:
            frame.longPress = 1
            applyGestureAreaPayload(to: &frame)
            return

        case .watchGestureAreaDrag:
            frame.drag = 1
            frame.touchPress = 1
            applyGestureAreaPayload(to: &frame)
            return

        case .watchGestureAreaSwipeUp:
            frame.swipeUp = 1
            applyGestureAreaPayload(to: &frame)
            return

        case .watchGestureAreaSwipeDown:
            frame.swipeDown = 1
            applyGestureAreaPayload(to: &frame)
            return

        case .watchGestureAreaSwipeLeft:
            frame.swipeLeft = 1
            applyGestureAreaPayload(to: &frame)
            return

        case .watchGestureAreaSwipeRight:
            frame.swipeRight = 1
            applyGestureAreaPayload(to: &frame)
            return

        case .watchToggle:
            frame.primaryAction = 1
            if let value = readAxis0Value(message, aliases: ["value", "axis0", "axis"])
            {
                frame.axis0 = value
            }
            return

        case .watchSlider:
            if let value = readAxis0Value(message, aliases: ["value", "axis0", "axis"])
            {
                frame.axis0 = value
            }
            return

        case .watchTextFieldChange:
            frame.primaryAction = 1
            if let value = readAxis0Value(message, aliases: ["value"])
            {
                frame.axis0 = value
            }
            else if let text = readString(message, key: "value"), !text.isEmpty
            {
                frame.axis0 = 1
            }
            return

        case .watchTextFieldSubmit:
            frame.primaryAction = 1
            if let value = readAxis0Value(message, aliases: ["value"])
            {
                frame.axis0 = value
            }
            return

        case .unknown:
            break
        }


        if normalized == "watchbutton"
            || normalized.hasPrefix("watchbutton/")
            || normalizedAction == "primary"
        {
            frame.primaryButton = 1
            return
        }

        if normalized == "watchtouchpad" || normalized == "watchtouchpad/press"
        {
            frame.primaryButton = 1
            return
        }

        if normalized == "watchtouchpad/tap" || normalized == "watchtouchpad/doubletap" || normalized == "watchtouchpad/longpress" || normalized == "watchtouchpad/drag"
        {
            let isDrag = normalized == "watchtouchpad/drag" || normalizedAction == "drag"
            frame.drag = isDrag ? 1 : 0
            frame.touchPress = 1
            frame.tap = readFlag(message, keys: ["tap"])
            frame.doubleTap = readFlag(message, keys: ["doubleTap"])
            frame.longPress = readFlag(message, keys: ["longPress"])
            frame.axis0 = readAxis0Value(message, aliases: ["axis0", "axis"]) ?? 0
            if let touchPosition = readTouchPositionFromMessage(
                message,
                vectorKeys: ["touchPosition", "touchPos", "touch"],
                xKeys: ["touchPosX", "touchPositionX", "touchX", "x"],
                yKeys: ["touchPosY", "touchPositionY", "touchY", "y"]
            )
            {
                frame.touchPosX = touchPosition.0
                frame.touchPosY = touchPosition.1
            }
            return
        }

        if normalized == "watchcrown/press"
        {
            frame.primaryButton = 1
            return
        }

        if normalized == "watchcrown/rotate"
        {
            frame.crown = readAxis0Value(message, aliases: ["crown", "axis0"]) ?? frame.crown
            if let axis = readAxis0Value(message, aliases: ["axis0", "crown"])
            {
                frame.axis0 = axis
            }
            return
        }

        if normalized.hasPrefix("watchcrown")
            && normalizedAction == "primary"
        {
            frame.primaryButton = 1
            return
        }

        if normalized.hasPrefix("watchgesturearea")
        {
            if normalized.contains("double")
            {
                frame.doubleTap = 1
            }
            else if normalized.contains("long")
            {
                frame.longPress = 1
            }
            else if normalized.contains("drag")
            {
                frame.drag = 1
                frame.touchPress = 1
            }
            else if normalized.contains("swipe-up") || normalized.contains("swipeup")
            {
                frame.swipeUp = 1
            }
            else if normalized.contains("swipe-down") || normalized.contains("swipedown")
            {
                frame.swipeDown = 1
            }
            else if normalized.contains("swipe-left") || normalized.contains("swipeleft")
            {
                frame.swipeLeft = 1
            }
            else if normalized.contains("swipe-right") || normalized.contains("swiperight")
            {
                frame.swipeRight = 1
            }
            else
            {
                frame.touchPress = 1
            }

            if let touchPosition = readTouchPositionFromMessage(
                message,
                vectorKeys: ["touchPosition", "touchPos", "touch"],
                xKeys: ["touchPosX", "touchPositionX", "touchX", "x"],
                yKeys: ["touchPosY", "touchPositionY", "touchY", "y"]
            )
            {
                frame.touchPosX = touchPosition.0
                frame.touchPosY = touchPosition.1
            }
            if let axis = readAxis0Value(message, aliases: ["axis0", "value"])
            {
                frame.axis0 = axis
            }

            return;
        }

        if normalized.contains("double")
        {
            frame.doubleTap = 1
            return;
        }

        if normalized.contains("long")
        {
            frame.longPress = 1
            return;
        }

        if normalized.hasPrefix("watchtoggle")
        {
            frame.primaryAction = 1
            if let value = readAxis0Value(message, aliases: ["value", "axis0", "axis"])
            {
                frame.axis0 = value
            }

            return;
        }

        if normalized.hasPrefix("watchslider")
        {
            if let value = readAxis0Value(message, aliases: ["value", "axis0", "axis"])
            {
                frame.axis0 = value
            }
            return;
        }

        if normalized == "watchtextfield/change"
        {
            frame.primaryAction = 1
            if let value = readAxis0Value(message, aliases: ["value"])
            {
                frame.axis0 = value
            }
            else if let text = readString(message, key: "value"), !text.isEmpty
            {
                frame.axis0 = 1
            }
            return;
        }

        if normalized == "watchtextfield/submit"
        {
            frame.primaryAction = 1
            if let value = readAxis0Value(message, aliases: ["value"])
            {
                frame.axis0 = value
            }
        }
    }

    private func pushFrameFromMessage(_ message: [String: Any]?)
    {
        guard let message else { return }

        var frame = touchBuffer.currentContinuousFrame()

        if let x = readNumber(message, key: "x") ?? readNumber(message, key: "primaryX")
        {
            frame.primaryX = x
        }
        if let y = readNumber(message, key: "y") ?? readNumber(message, key: "primaryY")
        {
            frame.primaryY = y
        }

        if let touchPosition = readTouchPositionFromMessage(
            message,
            vectorKeys: ["touchPosition", "touchPos", "touch"],
            xKeys: ["touchPosX", "touchPositionX", "touchX", "x"],
            yKeys: ["touchPosY", "touchPositionY", "touchY", "y"]
        )
        {
            frame.touchPosX = touchPosition.0
            frame.touchPosY = touchPosition.1
        }

        if let acceleration = readVector3FromMessage(
            message,
            keys: ["acceleration", "linearAcceleration", "userAcceleration", "accel"]
        )
        {
            frame.accelerationX = acceleration.0
            frame.accelerationY = acceleration.1
            frame.accelerationZ = acceleration.2
        }

        if let gyro = readVector3FromMessage(
            message,
            keys: ["gyro", "gyroscope", "rotationRate", "angularVelocity"]
        )
        {
            frame.gyroX = gyro.0
            frame.gyroY = gyro.1
            frame.gyroZ = gyro.2
        }

        frame.primaryButton = readFlag(message, keys: ["button", "primaryButton"])
        frame.touchPress = readFlag(message, keys: ["touchPress"])
        frame.drag = readFlag(message, keys: ["drag", "isDragging"])
        frame.tap = readFlag(message, keys: ["tap", "ordinaryTap"])
        frame.doubleTap = readFlag(message, keys: ["doubleTap", "doubleTapGesture"])
        frame.primaryAction = readFlag(message, keys: ["primaryAction", "action"])
        frame.longPress = readFlag(message, keys: ["longPress"])
        frame.swipeUp = readFlag(message, keys: ["swipeUp", "swipeUpPulse"])
        frame.swipeDown = readFlag(message, keys: ["swipeDown"])
        frame.swipeLeft = readFlag(message, keys: ["swipeLeft"])
        frame.swipeRight = readFlag(message, keys: ["swipeRight"])

        if let axis0 = readNumber(message, key: "axis0") ?? readNumber(message, key: "axis")
        {
            frame.axis0 = axis0
        }
        if let crown = readNumber(message, key: "crown")
        {
            frame.crown = crown
        }
        if let level = readNumber(message, key: "microphoneLevel") ?? readNumber(message, key: "microphone")
        {
            frame.microphoneLevel = level
        }
        if let peak = readNumber(message, key: "microphonePeak") ?? readNumber(message, key: "peakMicrophone")
        {
            frame.microphonePeak = peak
        }

        if isWatchActionMessage(message)
        {
            applyActionAsControls(message, to: &frame)
        }

        frame.updateMotion(from: message)
        touchBuffer.push(frame)
    }

    func handleWatchMessage(_ message: [String: Any]?)
    {
        pushFrameFromMessage(message)
    }

    func session(_ session: WCSession, didReceiveMessage message: [String : Any])
    {
        handleWatchMessage(message)
    }

    func session(_ session: WCSession, didReceiveMessage message: [String : Any], replyHandler: @escaping ([String : Any]) -> Void)
    {
        handleWatchMessage(message)
    }

    func session(_ session: WCSession, didReceiveMessageData messageData: Data)
    {
        guard let message = try? JSONSerialization.jsonObject(with: messageData) as? [String: Any] else { return }
        handleWatchMessage(message)
    }

    func session(_ session: WCSession, didReceiveMessageData messageData: Data, replyHandler: @escaping (Data) -> Void)
    {
        guard let message = try? JSONSerialization.jsonObject(with: messageData) as? [String: Any] else
        {
            replyHandler(Data())
            return
        }

        handleWatchMessage(message)
        replyHandler(Data())
    }

    func session(_ session: WCSession, didReceive file: WCSessionFile)
    {
        guard file.metadata?["kind"] as? String == "watch-audio-recording" else { return }

        do
        {
            let fileManager = FileManager.default
            guard let applicationSupport = fileManager.urls(for: .applicationSupportDirectory, in: .userDomainMask).first else { return }
            let recordingsDirectory = applicationSupport.appendingPathComponent("AppleWatchRecordings", isDirectory: true)
            try fileManager.createDirectory(at: recordingsDirectory, withIntermediateDirectories: true)
            let fileExtension = file.fileURL.pathExtension.isEmpty ? "m4a" : file.fileURL.pathExtension
            let destination = recordingsDirectory
                .appendingPathComponent(UUID().uuidString)
                .appendingPathExtension(fileExtension)
            try fileManager.moveItem(at: file.fileURL, to: destination)
            recordingPathBuffer.push(destination.path)
        }
        catch
        {
            return
        }
    }

#if !os(watchOS)
    func sessionDidBecomeInactive(_ session: WCSession)
    {
    }

    func sessionDidDeactivate(_ session: WCSession)
    {
        if WCSession.isSupported()
        {
            session.activate()
        }
    }
#endif
}

@_cdecl("WatchBridge_Start")
public func WatchBridge_Start()
{
    WatchSessionManager.shared.start()
}

@_cdecl("WatchBridge_SetStaleTimeout")
public func WatchBridge_SetStaleTimeout(_ seconds: Float)
{
    WatchSessionManager.shared.setStaleTimeout(seconds)
}

@_cdecl("WatchBridge_PopTouch")
public func WatchBridge_PopTouch(_ x: UnsafeMutablePointer<Float>, _ y: UnsafeMutablePointer<Float>, _ button: UnsafeMutablePointer<UInt8>) -> UInt8
{
    var touchPress: UInt8 = 0
    var tap: UInt8 = 0
    var doubleTap: UInt8 = 0
    var primaryAction: UInt8 = 0
    var longPress: UInt8 = 0
    var drag: UInt8 = 0
    var swipeUp: UInt8 = 0
    var swipeDown: UInt8 = 0
    var swipeLeft: UInt8 = 0
    var swipeRight: UInt8 = 0
    var crown: Float = 0
    var accelerationX: Float = 0
    var accelerationY: Float = 0
    var accelerationZ: Float = 0
    var gyroX: Float = 0
    var gyroY: Float = 0
    var gyroZ: Float = 0
    var microphoneLevel: Float = 0
    var microphonePeak: Float = 0
    var touchPosX: Float = 0
    var touchPosY: Float = 0
    var axis0: Float = 0

    let hadFrame = WatchSessionManager.shared.popFrame(
        x,
        y,
        button,
        &touchPress,
        &tap,
        &doubleTap,
        &primaryAction,
        &longPress,
        &drag,
        &swipeUp,
        &swipeDown,
        &swipeLeft,
        &swipeRight,
        &crown,
        &accelerationX,
        &accelerationY,
        &accelerationZ,
        &gyroX,
        &gyroY,
        &gyroZ,
        &microphoneLevel,
        &microphonePeak,
        &touchPosX,
        &touchPosY,
        &axis0)

    return hadFrame
}

@_cdecl("WatchBridge_PopFrame")
public func WatchBridge_PopFrame(
    _ x: UnsafeMutablePointer<Float>,
    _ y: UnsafeMutablePointer<Float>,
    _ button: UnsafeMutablePointer<UInt8>,
    _ touchPress: UnsafeMutablePointer<UInt8>,
    _ tap: UnsafeMutablePointer<UInt8>,
    _ doubleTap: UnsafeMutablePointer<UInt8>,
    _ primaryAction: UnsafeMutablePointer<UInt8>,
    _ longPress: UnsafeMutablePointer<UInt8>,
    _ drag: UnsafeMutablePointer<UInt8>,
    _ swipeUp: UnsafeMutablePointer<UInt8>,
    _ swipeDown: UnsafeMutablePointer<UInt8>,
    _ swipeLeft: UnsafeMutablePointer<UInt8>,
    _ swipeRight: UnsafeMutablePointer<UInt8>,
    _ crown: UnsafeMutablePointer<Float>,
    _ accelerationX: UnsafeMutablePointer<Float>,
    _ accelerationY: UnsafeMutablePointer<Float>,
    _ accelerationZ: UnsafeMutablePointer<Float>,
    _ gyroX: UnsafeMutablePointer<Float>,
    _ gyroY: UnsafeMutablePointer<Float>,
    _ gyroZ: UnsafeMutablePointer<Float>,
    _ microphoneLevel: UnsafeMutablePointer<Float>,
    _ microphonePeak: UnsafeMutablePointer<Float>,
    _ touchPosX: UnsafeMutablePointer<Float>,
    _ touchPosY: UnsafeMutablePointer<Float>,
    _ axis0: UnsafeMutablePointer<Float>) -> UInt8
{
    return WatchSessionManager.shared.popFrame(
        x,
        y,
        button,
        touchPress,
        tap,
        doubleTap,
        primaryAction,
        longPress,
        drag,
        swipeUp,
        swipeDown,
        swipeLeft,
        swipeRight,
        crown,
        accelerationX,
        accelerationY,
        accelerationZ,
        gyroX,
        gyroY,
        gyroZ,
        microphoneLevel,
        microphonePeak,
        touchPosX,
        touchPosY,
        axis0)
}

@_cdecl("WatchBridge_PopFrameBytes")
public func WatchBridge_PopFrameBytes(_ frameBuffer: UnsafeMutablePointer<UInt8>?, _ maxByteCount: Int32) -> Int32
{
    guard let frameBuffer else
    {
        return 0
    }

    return Int32(WatchSessionManager.shared.popFrameBytes(frameBuffer, Int(maxByteCount)))
}

@_cdecl("WatchBridge_PopRecordingPath")
public func WatchBridge_PopRecordingPath(_ pathBuffer: UnsafeMutablePointer<UInt8>?, _ maxByteCount: Int32) -> Int32
{
    guard let pathBuffer else { return 0 }
    return Int32(WatchSessionManager.shared.popRecordingPath(pathBuffer, Int(maxByteCount)))
}
