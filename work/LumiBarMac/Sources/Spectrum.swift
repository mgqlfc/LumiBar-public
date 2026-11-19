import Accelerate
import CoreAudio
import Foundation
import Synchronization

enum AudioPlayback {
    static func object(for pid: pid_t) -> AudioObjectID? {
        var pid = pid
        var process = AudioObjectID(kAudioObjectUnknown)
        var size = UInt32(MemoryLayout<AudioObjectID>.size)
        var address = property(kAudioHardwarePropertyTranslatePIDToProcessObject)
        guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address,
                                         UInt32(MemoryLayout<pid_t>.size), &pid, &size, &process) == noErr,
              process != kAudioObjectUnknown else { return nil }
        return process
    }

    static func applicationObjects(pid: pid_t, bundleID: String) -> [AudioObjectID] {
        let main = object(for: pid)
        var propertySize: UInt32 = 0
        var address = property(kAudioHardwarePropertyProcessObjectList)
        let system = AudioObjectID(kAudioObjectSystemObject)
        guard AudioObjectGetPropertyDataSize(system, &address, 0, nil, &propertySize) == noErr,
              propertySize > 0 else { return main.map { [$0] } ?? [] }
        let count = Int(propertySize) / MemoryLayout<AudioObjectID>.stride
        var processes = [AudioObjectID](repeating: 0, count: count)
        let status = processes.withUnsafeMutableBytes {
            AudioObjectGetPropertyData(system, &address, 0, nil, &propertySize, $0.baseAddress!)
        }
        guard status == noErr else { return main.map { [$0] } ?? [] }
        let matching = processes.filter { process in
            if let main, process == main { return true }
            guard let id = identifier(process) else { return false }
            return id == bundleID || id.hasPrefix(bundleID + ".")
        }
        return matching.isEmpty ? main.map { [$0] } ?? [] : matching
    }

    static func hasOutput(_ processes: [AudioObjectID]) -> Bool {
        processes.contains { process in
            var value: UInt32 = 0
            var size = UInt32(MemoryLayout<UInt32>.size)
            var address = property(kAudioProcessPropertyIsRunningOutput)
            return AudioObjectGetPropertyData(process, &address, 0, nil, &size, &value) == noErr && value == 1
        }
    }

    private static func identifier(_ process: AudioObjectID) -> String? {
        var value: Unmanaged<CFString>?
        var size = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
        var address = property(kAudioProcessPropertyBundleID)
        guard AudioObjectGetPropertyData(process, &address, 0, nil, &size, &value) == noErr else { return nil }
        return value.map { $0.takeRetainedValue() as String }
    }

    private static func property(_ selector: AudioObjectPropertySelector) -> AudioObjectPropertyAddress {
        .init(mSelector: selector, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain)
    }
}

@MainActor
final class Spectrum {
    private var tap = AudioObjectID(kAudioObjectUnknown)
    private var device = AudioObjectID(kAudioObjectUnknown)
    private var callback: AudioDeviceIOProcID?
    private var running = false
    private var analyzer: Analyzer?

    var levels: [Double] { analyzer?.levels ?? Array(repeating: 0, count: 8) }

    func start(pid: pid_t, bundleID: String) -> Bool {
        stop()
        let processes = AudioPlayback.applicationObjects(pid: pid, bundleID: bundleID)
        guard !processes.isEmpty else { return false }
        let description = CATapDescription(monoMixdownOfProcesses: processes)
        description.name = "LumiBar Spectrum"
        description.isPrivate = true
        description.muteBehavior = .unmuted
        guard AudioHardwareCreateProcessTap(description, &tap) == noErr else { return false }

        var format = AudioStreamBasicDescription()
        var size = UInt32(MemoryLayout<AudioStreamBasicDescription>.size)
        var address = AudioObjectPropertyAddress(mSelector: kAudioTapPropertyFormat,
                                                mScope: kAudioObjectPropertyScopeGlobal,
                                                mElement: kAudioObjectPropertyElementMain)
        guard AudioObjectGetPropertyData(tap, &address, 0, nil, &size, &format) == noErr,
              format.mFormatID == kAudioFormatLinearPCM, format.mFormatFlags & kAudioFormatFlagIsFloat != 0,
              format.mBitsPerChannel == 32, format.mChannelsPerFrame == 1,
              let analyzer = Analyzer(sampleRate: format.mSampleRate) else {
            stop()
            return false
        }

        let properties: [String: Any] = [
            kAudioAggregateDeviceNameKey: "LumiBar Spectrum",
            kAudioAggregateDeviceUIDKey: UUID().uuidString,
            kAudioAggregateDeviceIsPrivateKey: true,
            kAudioAggregateDeviceTapAutoStartKey: true,
            kAudioAggregateDeviceTapListKey: [[kAudioSubTapUIDKey: description.uuid.uuidString,
                                              kAudioSubTapDriftCompensationKey: true]]
        ]
        guard AudioHardwareCreateAggregateDevice(properties as CFDictionary, &device) == noErr,
              AudioDeviceCreateIOProcIDWithBlock(&callback, device, nil, { _, input, _, _, _ in
                  let buffers = UnsafeMutableAudioBufferListPointer(UnsafeMutablePointer(mutating: input))
                  for buffer in buffers {
                      guard let samples = buffer.mData?.assumingMemoryBound(to: Float.self) else { continue }
                      analyzer.add(UnsafeBufferPointer(start: samples, count: Int(buffer.mDataByteSize) / 4))
                  }
              }) == noErr,
              let callback, AudioDeviceStart(device, callback) == noErr else {
            stop()
            return false
        }
        self.analyzer = analyzer
        running = true
        return true
    }

    func stop() {
        if let callback {
            if running { AudioDeviceStop(device, callback) }
            AudioDeviceDestroyIOProcID(device, callback)
        }
        if device != kAudioObjectUnknown { AudioHardwareDestroyAggregateDevice(device) }
        if tap != kAudioObjectUnknown { AudioHardwareDestroyProcessTap(tap) }
        running = false
        callback = nil
        device = kAudioObjectUnknown
        tap = kAudioObjectUnknown
        analyzer = nil
    }
}

// The Core Audio callback owns the FFT buffers. The view reads the packed levels atomically.
private final class Analyzer: @unchecked Sendable {
    private static let count = 2048
    private let setup: FFTSetup
    private let window: [Float]
    private let bands: [Range<Int>]
    private let packed = Atomic<UInt64>(0)
    private var real = [Float](repeating: 0, count: count)
    private var imaginary = [Float](repeating: 0, count: count)
    private var cursor = 0

    init?(sampleRate: Double) {
        guard sampleRate > 0, let setup = vDSP_create_fftsetup(11, FFTRadix(kFFTRadix2)) else { return nil }
        self.setup = setup
        window = (0..<Self.count).map { Float(0.5 - 0.5 * cos(2 * .pi * Double($0) / Double(Self.count - 1))) }
        bands = (0..<8).map { band in
            let low = 45 * pow(16000.0 / 45, Double(band) / 8)
            let high = 45 * pow(16000.0 / 45, Double(band + 1) / 8)
            let start = min(Self.count / 2 - 1, max(1, Int(ceil(low * Double(Self.count) / sampleRate))))
            let end = min(Self.count / 2, max(start + 1, Int(ceil(high * Double(Self.count) / sampleRate))))
            return start..<end
        }
    }

    deinit { vDSP_destroy_fftsetup(setup) }

    var levels: [Double] {
        let snapshot = packed.load(ordering: .relaxed)
        return (0..<8).map { Double((snapshot >> ($0 * 8)) & 255) / 255 }
    }

    func add(_ samples: UnsafeBufferPointer<Float>) {
        for sample in samples {
            real[cursor] = sample.isFinite ? sample * window[cursor] : 0
            imaginary[cursor] = 0
            cursor += 1
            if cursor == Self.count {
                analyze()
                cursor = 0
            }
        }
    }

    private func analyze() {
        real.withUnsafeMutableBufferPointer { real in
            imaginary.withUnsafeMutableBufferPointer { imaginary in
                var values = DSPSplitComplex(realp: real.baseAddress!, imagp: imaginary.baseAddress!)
                vDSP_fft_zip(setup, &values, 1, 11, FFTDirection(FFT_FORWARD))
            }
        }
        var frame: UInt64 = 0
        for (index, band) in bands.enumerated() {
            var power: Float = 0
            for bin in band { power = max(power, real[bin] * real[bin] + imaginary[bin] * imaginary[bin]) }
            let decibels = 20 * log10(sqrt(power) / Float(Self.count) + 1e-9)
            let level = max(0, min(1, (decibels + 75) / 65))
            frame |= UInt64(level * 255) << (index * 8)
        }
        packed.store(frame, ordering: .relaxed)
    }
}
