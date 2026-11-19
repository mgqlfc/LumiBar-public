import Accelerate
import CoreAudio
import Foundation
import Synchronization

@MainActor
final class Spectrum {
    private var tap = AudioObjectID(kAudioObjectUnknown)
    private var device = AudioObjectID(kAudioObjectUnknown)
    private var callback: AudioDeviceIOProcID?
    private var running = false
    private var analyzer: Analyzer?

    var levels: [Double] { analyzer?.levels ?? Array(repeating: 0, count: 8) }

    func start(pid: pid_t) -> Bool {
        stop()
        var pid = pid
        var process: AudioObjectID = 0
        var size = UInt32(MemoryLayout<AudioObjectID>.size)
        var address = AudioObjectPropertyAddress(mSelector: kAudioHardwarePropertyTranslatePIDToProcessObject,
                                                mScope: kAudioObjectPropertyScopeGlobal,
                                                mElement: kAudioObjectPropertyElementMain)
        guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address,
                                         UInt32(MemoryLayout<pid_t>.size), &pid, &size, &process) == noErr,
              process != kAudioObjectUnknown else { return false }
        let description = CATapDescription(monoMixdownOfProcesses: [process])
        description.name = "LumiBar Spectrum"
        description.isPrivate = true
        description.muteBehavior = .unmuted
        guard AudioHardwareCreateProcessTap(description, &tap) == noErr else { return false }

        var format = AudioStreamBasicDescription()
        size = UInt32(MemoryLayout<AudioStreamBasicDescription>.size)
        address.mSelector = kAudioTapPropertyFormat
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
