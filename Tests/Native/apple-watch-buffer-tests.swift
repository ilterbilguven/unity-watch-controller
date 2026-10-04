import Foundation

// Run from any directory: xcrun swift Tests/Native/apple-watch-buffer-tests.swift
// Compile the exact production declarations with these tests in one file so the
// private buffer stays private. No WatchConnectivity runtime or paired device is needed.
let repository = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
let bridge = repository.appendingPathComponent("Runtime/Plugins/iOS/WatchBridge.swift")
let temporaryDirectory = FileManager.default.temporaryDirectory
    .appendingPathComponent("apple-watch-buffer-tests-\(UUID().uuidString)", isDirectory: true)

let tests = #"""

private struct TestFailure: Error, CustomStringConvertible
{
    let description: String
}

private func expect<T: Equatable>(_ actual: T, _ expected: T, _ message: String) throws
{
    if actual != expected
    {
        throw TestFailure(description: "\(message): expected \(expected), got \(actual)")
    }
}

private func input(_ x: Float, pulse: Float? = nil) -> WatchControllerFrame
{
    var frame = WatchControllerFrame()
    frame.primaryX = x
    if let pulse
    {
        frame.tap = 1
        frame.axis0 = pulse
    }
    return frame
}

private func fillWithPulses(_ buffer: WatchTouchBuffer)
{
    for marker in 1...64
    {
        buffer.push(input(-0.5, pulse: Float(marker)))
    }
}

private func drain(_ buffer: WatchTouchBuffer) throws -> [WatchControllerFrame]
{
    var frames: [WatchControllerFrame] = []
    while let frame = buffer.pop()
    {
        frames.append(frame)
        if frames.count > 65
        {
            throw TestFailure(description: "Queue exceeded its bounded drain length")
        }
    }
    return frames
}

private func encodedFloat(_ bytes: [UInt8], at offset: Int) -> Float
{
    let bits = UInt32(bytes[offset])
        | (UInt32(bytes[offset + 1]) << 8)
        | (UInt32(bytes[offset + 2]) << 16)
        | (UInt32(bytes[offset + 3]) << 24)
    return Float(bitPattern: bits)
}

private var failures = 0
private var passes = 0

private func test(_ name: String, _ body: () throws -> Void)
{
    do
    {
        try body()
        passes += 1
        print("PASS: \(name)")
    }
    catch
    {
        failures += 1
        print("FAIL: \(name): \(error)")
    }
}

test("new continuous input supersedes a deferred snapshot")
{
    let buffer = WatchTouchBuffer()
    fillWithPulses(buffer)
    buffer.push(input(0))
    try expect(buffer.pop()?.axis0, 1, "First pulse")
    buffer.push(input(0.9))

    let frames = try drain(buffer)
    try expect(frames.filter { $0.tap > 0 }.map(\.axis0), (2...64).map { Float($0) }, "Remaining pulses stay in FIFO order")
    try expect(frames.filter { $0.tap == 0 }.map(\.primaryX), [0.9], "Old deferred input must not replay after the newer snapshot")
    try expect(buffer.currentContinuousFrame().primaryX, 0.9, "Retained continuous input")
}

test("an untracked attitude encodes a finite identity quaternion")
{
    let bytes = encodeWatchControllerFrame(WatchControllerFrame())
    try expect((72...84).filter { $0 % 4 == 0 }.map { encodedFloat(bytes, at: $0) },
               [0, 0, 0, 1], "Untracked attitude is identity, not a zero quaternion")
    try expect(bytes[88], 0, "No fabricated tracking before first motion sample")
}

test("new pulse-bearing input supersedes a deferred snapshot")
{
    let buffer = WatchTouchBuffer()
    fillWithPulses(buffer)
    buffer.push(input(0))
    try expect(buffer.pop()?.axis0, 1, "First pulse")
    buffer.push(input(0.9, pulse: 65))

    let frames = try drain(buffer)
    try expect(frames.map(\.axis0), (2...65).map { Float($0) }, "Pulses stay in FIFO order with no stale snapshot appended")
    try expect(frames.last?.primaryX, 0.9, "Newest pulse carries the final continuous state")
    try expect(buffer.currentContinuousFrame().tap, 0, "Retained state clears pulse controls")
}

test("full pulse queue preserves every pulse and only the latest deferred snapshot")
{
    let buffer = WatchTouchBuffer()
    fillWithPulses(buffer)
    buffer.push(input(0.2))
    buffer.push(input(0.7))

    let frames = try drain(buffer)
    try expect(frames.filter { $0.tap > 0 }.map(\.axis0), (1...64).map { Float($0) }, "Deferred updates must not evict pulses")
    try expect(frames.filter { $0.tap == 0 }.map(\.primaryX), [0.7], "Deferred updates coalesce to the latest value")
    try expect(frames.last?.primaryX, 0.7, "Deferred update follows all earlier pulses")
}

test("adjacent continuous updates coalesce without crossing a pulse")
{
    let buffer = WatchTouchBuffer()
    buffer.push(input(0.1))
    buffer.push(input(0.3))
    buffer.push(input(0.4, pulse: 1))
    buffer.push(input(0.6))
    buffer.push(input(0.8))

    let frames = try drain(buffer)
    try expect(frames.map(\.primaryX), [0.3, 0.4, 0.8], "Coalescing preserves the pulse boundary")
    try expect(frames.map(\.tap), [0, 1, 0], "Pulse emits exactly once")
}

test("incoming pulse evicts continuous input before any queued pulse")
{
    let buffer = WatchTouchBuffer()
    buffer.push(input(0.1))
    for marker in 1...64
    {
        buffer.push(input(0.5, pulse: Float(marker)))
    }

    let frames = try drain(buffer)
    try expect(frames.map(\.axis0), (1...64).map { Float($0) }, "Every pulse survives continuous eviction")
    try expect(frames.map(\.tap), [UInt8](repeating: 1, count: 64), "Only pulse frames remain")
}

test("discrete overflow clears deferred input and emits one neutral before recovery")
{
    let buffer = WatchTouchBuffer()
    fillWithPulses(buffer)
    buffer.push(input(0.7))
    buffer.push(input(0.8, pulse: 65))
    try expect(buffer.currentContinuousFrame().primaryX, 0, "Overflow clears retained state")
    buffer.push(input(0.9, pulse: 66))

    let frames = try drain(buffer)
    try expect(frames.map(\.primaryX), [0, 0.9], "Neutral precedes recovery without stale deferred input")
    try expect(frames.map(\.tap), [0, 1], "Neutral has no pulse and recovery emits once")
}

test("native queue age includes time spent deferred behind a pulse backlog")
{
    let buffer = WatchTouchBuffer()
    fillWithPulses(buffer)
    buffer.push(input(0.7))
    Thread.sleep(forTimeInterval: 0.03)

    let frames = try drain(buffer)
    try expect(frames.count, 65, "All pulses and deferred state are delivered")
    for frame in frames
    {
        let bytes = encodeWatchControllerFrame(frame)
        try expect(bytes.count, 216, "Wire frame size stays compatible")
        try expect(encodedFloat(bytes, at: 0), frame.primaryX, "Existing primaryX offset stays compatible")
        try expect(encodedFloat(bytes, at: 64), frame.axis0, "Existing axis0 offset stays compatible")
        let age = encodedFloat(bytes, at: 68)
        try expect(age.isFinite && age >= 0.02, true, "Each dequeued frame carries its real native backlog age, including deferred state")
        try expect(Array(bytes[104...]), [UInt8](repeating: 0, count: 112), "Remaining reserved bytes stay zero")
    }
}

test("a Crown delta is not inherited by the next unrelated motion update")
{
    let buffer = WatchTouchBuffer()
    var crown = input(0.5)
    crown.crown = 0.25
    buffer.push(crown)
    try expect(buffer.pop()?.crown, 0.25, "Original Crown event")

    var motion = buffer.currentContinuousFrame()
    motion.accelerationX = 0.75
    buffer.push(motion)
    let frames = try drain(buffer)
    try expect(frames.map(\.crown), [0], "Unrelated motion must not repeat the Crown delta")
    try expect(frames.map(\.primaryX), [0.5], "True continuous controls stay retained")
    try expect(frames.map(\.accelerationX), [0.75], "New motion value is delivered")
}

test("consecutive Crown deltas survive coalescing and queue pressure exactly once")
{
    let buffer = WatchTouchBuffer()
    buffer.push(input(0.1))
    for marker in 1...64
    {
        var crown = input(0.5)
        crown.crown = Float(marker) / 100
        buffer.push(crown)
    }

    let frames = try drain(buffer)
    try expect(frames.map(\.crown), (1...64).map { Float($0) / 100 }, "Each Crown event is preserved in FIFO order")
    try expect(buffer.currentContinuousFrame().crown, 0, "Crown is absent from retained continuous state")
}

test("negative Crown deltas remain ordered events")
{
    let buffer = WatchTouchBuffer()
    for delta: Float in [-0.25, 0.125, -0.5]
    {
        var frame = input(0)
        frame.crown = delta
        buffer.push(frame)
    }
    buffer.push(buffer.currentContinuousFrame())

    let frames = try drain(buffer)
    try expect(frames.map(\.crown), [-0.25, 0.125, -0.5, 0], "Crown direction changes must not drop or replay events")
}

test("an unrelated button cannot resurrect a joystick after native silence")
{
    let buffer = WatchTouchBuffer()
    buffer.push(input(1))
    _ = try drain(buffer)
    Thread.sleep(forTimeInterval: 0.27)

    var button = buffer.currentContinuousFrame()
    button.primaryButton = 1
    buffer.push(button)
    let frames = try drain(buffer)
    try expect(frames.map(\.primaryX), [0], "Expired joystick must not be inherited by a new button")
    try expect(frames.map(\.primaryButton), [1], "Fresh button pulse is preserved")
}

test("unrelated microphone traffic does not renew other continuous channels")
{
    let buffer = WatchTouchBuffer()
    var initial = input(1)
    initial.primaryY = 0.5
    initial.touchPosX = 0.75
    initial.touchPosY = 0.25
    initial.accelerationX = 1
    initial.accelerationY = 2
    initial.accelerationZ = 3
    initial.gyroX = 4
    initial.gyroY = 5
    initial.gyroZ = 6
    initial.microphonePeak = 0.8
    initial.axis0 = 0.9
    buffer.push(initial)
    _ = try drain(buffer)

    for _ in 0..<6
    {
        Thread.sleep(forTimeInterval: 0.05)
        var microphone = buffer.currentContinuousFrame()
        microphone.microphoneLevel = 0.7
        buffer.push(microphone)
        _ = try drain(buffer)
    }

    let retained = buffer.currentContinuousFrame()
    let stopped = [retained.primaryX, retained.primaryY, retained.touchPosX, retained.touchPosY,
                   retained.accelerationX, retained.accelerationY, retained.accelerationZ,
                   retained.gyroX, retained.gyroY, retained.gyroZ, retained.microphonePeak, retained.axis0]
    try expect(stopped, [Float](repeating: 0, count: 12), "Only explicitly updated channels may remain fresh")
    try expect(retained.microphoneLevel, 0.7, "Fresh microphone level remains active")
}

test("an explicit repeated value refreshes only its own continuous field")
{
    let buffer = WatchTouchBuffer()
    var initial = input(1)
    initial.primaryY = 0.5
    buffer.push(initial)
    _ = try drain(buffer)
    Thread.sleep(forTimeInterval: 0.15)

    var repeated = buffer.currentContinuousFrame()
    repeated.primaryX = 1
    buffer.push(repeated)
    _ = try drain(buffer)
    Thread.sleep(forTimeInterval: 0.15)

    let retained = buffer.currentContinuousFrame()
    try expect(retained.primaryX, 1, "The same explicit joystick value renews its freshness")
    try expect(retained.primaryY, 0, "An omitted component keeps its original expiry")
}

test("a dequeued snapshot clears continuous fields that expired while queued")
{
    let buffer = WatchTouchBuffer()
    buffer.push(input(1, pulse: 1))
    Thread.sleep(forTimeInterval: 0.27)

    let frames = try drain(buffer)
    try expect(frames.map(\.primaryX), [0], "Queue waiting must not extend a channel lifetime")
    try expect(frames.map(\.tap), [1], "Pulse disposition remains the managed age filter's responsibility")
}

test("the configured native timeout replaces the default for snapshot retention")
{
    let buffer = WatchTouchBuffer()
    buffer.setStaleTimeout(0.02)
    buffer.push(input(1))
    _ = try drain(buffer)
    Thread.sleep(forTimeInterval: 0.03)
    try expect(buffer.currentContinuousFrame().primaryX, 0, "Shorter receiver timeout is honored")

    buffer.setStaleTimeout(0.5)
    buffer.push(input(1))
    _ = try drain(buffer)
    Thread.sleep(forTimeInterval: 0.27)
    try expect(buffer.currentContinuousFrame().primaryX, 1, "Longer receiver timeout is honored")
}

test("invalid native timeout values do not disable the freshness limit")
{
    let buffer = WatchTouchBuffer()
    buffer.setStaleTimeout(0.02)
    for timeout: Float in [0, -1, .nan, .infinity]
    {
        buffer.setStaleTimeout(timeout)
    }
    buffer.push(input(1))
    _ = try drain(buffer)
    Thread.sleep(forTimeInterval: 0.03)
    try expect(buffer.currentContinuousFrame().primaryX, 0, "Invalid configuration preserves the last valid timeout")
}

test("raw attitude normalizes atomically and preserves wire offsets and reference")
{
    var frame = input(0.5, pulse: 1)
    frame.gyroZ = -2
    frame.updateMotion(from: ["attitude": [0.0, 0.0, -3.0, 4.0],
                              "attitudeReference": UInt32(0xf1234567), "motionSampleInterval": 0.05])
    let bytes = encodeWatchControllerFrame(clearingDiscreteInput(from: frame))
    try expect(encodedFloat(bytes, at: 0), 0.5, "Old primaryX offset")
    try expect(encodedFloat(bytes, at: 44), -2, "Gyro remains raw radians per second")
    try expect((72...84).filter { $0 % 4 == 0 }.map { encodedFloat(bytes, at: $0) },
               [0, 0, -0.6, 0.8], "Quaternion preserves raw CoreMotion signs")
    try expect(bytes[88], 1, "Tracked byte")
    try expect(Array(bytes[92..<96]), [0x67, 0x45, 0x23, 0xf1], "UInt32 reference uses little endian")
    try expect(encodedFloat(bytes, at: 96), 0.05, "Sample interval uses seconds")
}

test("invalid attitude neutralizes all components and never creates NaNs")
{
    let invalid: [[Double]] = [[0, 0, 0, 0], [0, 0, .nan, 1], [0, .infinity, 0, 1], [0, 1, 2]]
    for components in invalid
    {
        var frame = input(0)
        frame.updateMotion(from: ["attitude": [0, 0, 1, 0], "attitudeReference": 7])
        frame.updateMotion(from: ["attitude": components, "attitudeReference": 7,
                                  "motionSampleInterval": Double.nan])
        let bytes = encodeWatchControllerFrame(frame)
        try expect((72...84).filter { $0 % 4 == 0 }.map { encodedFloat(bytes, at: $0) },
                   [0, 0, 0, 1], "An invalid component invalidates the whole pose")
        try expect(bytes[88], 0, "Invalid attitude is not tracked")
        try expect(encodedFloat(bytes, at: 96), 0, "Invalid interval is neutral")
    }
}

test("huge finite quaternions normalize without overflow")
{
    var frame = input(0)
    let huge = Double.greatestFiniteMagnitude
    frame.updateMotion(from: ["attitude": [huge, -huge, huge, -huge], "attitudeReference": 2])
    let bytes = encodeWatchControllerFrame(frame)
    try expect((72...84).filter { $0 % 4 == 0 }.map { encodedFloat(bytes, at: $0) },
               [0.5, -0.5, 0.5, -0.5], "No overflow and no axis conversion")
    try expect(bytes[88], 1, "Huge finite attitude remains valid")
}

test("copied attitude expires independently during unrelated traffic")
{
    let buffer = WatchTouchBuffer()
    var initial = input(0)
    initial.updateMotion(from: ["attitude": [0, 1, 0, 0], "attitudeReference": 3, "motionSampleInterval": 0.05])
    buffer.push(initial)
    _ = buffer.pop()
    for _ in 0..<6
    {
        Thread.sleep(forTimeInterval: 0.05)
        var microphone = buffer.currentContinuousFrame()
        microphone.microphoneLevel = 0.7
        microphone.updateMotion(from: ["gyro": [1, 2, 3]])
        buffer.push(microphone)
        _ = buffer.pop()
    }
    let bytes = encodeWatchControllerFrame(buffer.currentContinuousFrame())
    try expect(bytes[88], 0, "Copies and unrelated messages cannot renew attitude")
    try expect(encodedFloat(bytes, at: 84), 1, "Expired attitude becomes identity")
    try expect(encodedFloat(bytes, at: 96), 0, "Expired sample interval is cleared")
    try expect(buffer.pop() == nil, true, "Cached state is never returned as a new queue sample")
}

test("invalid attitude reference cannot claim a tracked pose")
{
    for reference: Double in [0, -1, 1.5, .nan, .infinity, Double(UInt32.max) + 1]
    {
        var frame = input(0)
        frame.updateMotion(from: ["attitude": [0, 0, 0, 1], "attitudeReference": reference])
        try expect(encodeWatchControllerFrame(frame)[88], 0, "Invalid reference must not be tracked")
    }
}

test("attitude wire age includes inherited sample age and queue delay exactly once")
{
    let buffer = WatchTouchBuffer()
    buffer.setStaleTimeout(5)
    var initial = input(0)
    initial.updateMotion(from: ["attitude": [0, 0, 1, 0], "attitudeReference": 3])
    buffer.push(initial)
    _ = buffer.pop()
    Thread.sleep(forTimeInterval: 0.03)

    var microphone = buffer.currentContinuousFrame()
    microphone.microphoneLevel = 0.7
    buffer.push(microphone)
    Thread.sleep(forTimeInterval: 0.02)
    let frames = try drain(buffer)
    try expect(frames.count, 1, "Only the unrelated microphone packet is queued")
    let bytes = encodeWatchControllerFrame(frames[0])
    let age = encodedFloat(bytes, at: 100)
    let queueAge = encodedFloat(bytes, at: 68)
    try expect(age.isFinite && age >= 0.045, true, "Pose sample age includes both waits")
    try expect(age - queueAge >= 0.025, true, "Pose age retains time before the unrelated packet arrived")
    try expect(bytes[88], 1, "Pose remains valid within the configured five-second timeout")
}

test("explicit repeated equal attitude refreshes only its own encoded sample age")
{
    var frame = input(0)
    let payload: [String: Any] = ["attitude": [0, 1, 0, 0], "attitudeReference": 7]
    frame.updateMotion(from: payload)
    Thread.sleep(forTimeInterval: 0.03)
    let oldAge = encodedFloat(encodeWatchControllerFrame(frame), at: 100)
    frame.updateMotion(from: ["microphoneLevel": 0.7])
    let copiedAge = encodedFloat(encodeWatchControllerFrame(clearingDiscreteInput(from: frame)), at: 100)
    try expect(copiedAge >= oldAge && oldAge >= 0.025, true, "Unrelated updates and copies do not renew attitude")
    frame.updateMotion(from: payload)
    let freshAge = encodedFloat(encodeWatchControllerFrame(frame), at: 100)
    try expect(freshAge < oldAge, true, "An equal explicit pose is still a new sample")
}

print("Apple Watch buffer tests: \(passes) passed, \(failures) failed")
exit(failures == 0 ? 0 : 1)
"""#

func run(_ executable: URL, _ arguments: [String]) throws -> Int32
{
    let process = Process()
    process.executableURL = executable
    process.arguments = arguments
    try process.run()
    process.waitUntilExit()
    return process.terminationStatus
}

do
{
    let source = try String(contentsOf: bridge, encoding: .utf8)
    guard let start = source.range(of: "private struct WatchControllerFrame\n"),
          let end = source.range(of: "private final class WatchRecordingPathBuffer\n"),
          start.lowerBound < end.lowerBound else
    {
        throw NSError(domain: "AppleWatchBufferTests", code: 1, userInfo: [
            NSLocalizedDescriptionKey: "Production buffer declaration boundaries were not found in \(bridge.path)"
        ])
    }

    try FileManager.default.createDirectory(at: temporaryDirectory, withIntermediateDirectories: false)
    defer { try? FileManager.default.removeItem(at: temporaryDirectory) }
    let main = temporaryDirectory.appendingPathComponent("main.swift")
    let executable = temporaryDirectory.appendingPathComponent("buffer-tests")
    let productionDeclarations = source[start.lowerBound..<end.lowerBound]
    try ("import Foundation\n" + productionDeclarations + tests).write(to: main, atomically: true, encoding: .utf8)

    let compileStatus = try run(URL(fileURLWithPath: "/usr/bin/xcrun"), [
        "--sdk", "macosx", "swiftc", "-warnings-as-errors", main.path, "-o", executable.path
    ])
    guard compileStatus == 0 else
    {
        throw NSError(domain: "AppleWatchBufferTests", code: Int(compileStatus), userInfo: [
            NSLocalizedDescriptionKey: "Production buffer test compilation failed"
        ])
    }
    let status = try run(executable, [])
    if status != 0
    {
        throw NSError(domain: "AppleWatchBufferTests", code: Int(status), userInfo: [
            NSLocalizedDescriptionKey: "Production buffer regression tests failed"
        ])
    }
}
catch
{
    FileHandle.standardError.write(Data("ERROR: \(error.localizedDescription)\n".utf8))
    exit(1)
}
