import AudioToolbox
import CoreAudio

enum SystemOutputVolume {
    static var isAdjustable: Bool { outputControl() != nil }

    static func step(up: Bool) {
        guard let (device, address) = outputControl() else { return }
        var property = address
        var level: Float32 = 0
        var size = UInt32(MemoryLayout<Float32>.size)
        guard AudioObjectGetPropertyData(device, &property, 0, nil, &size, &level) == noErr, level.isFinite else { return }
        level = min(1, max(0, level + (up ? 1 : -1) / 16))
        _ = AudioObjectSetPropertyData(device, &property, 0, nil, size, &level)
    }

    private static func outputControl() -> (AudioDeviceID, AudioObjectPropertyAddress)? {
        var device = AudioDeviceID(kAudioObjectUnknown)
        var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        var output = AudioObjectPropertyAddress(mSelector: kAudioHardwarePropertyDefaultOutputDevice,
                                               mScope: kAudioObjectPropertyScopeGlobal,
                                               mElement: kAudioObjectPropertyElementMain)
        guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &output, 0, nil, &size, &device) == noErr,
              device != kAudioObjectUnknown else { return nil }
        for selector in [kAudioHardwareServiceDeviceProperty_VirtualMainVolume, kAudioDevicePropertyVolumeScalar] {
            var property = AudioObjectPropertyAddress(mSelector: selector, mScope: kAudioDevicePropertyScopeOutput,
                                                      mElement: kAudioObjectPropertyElementMain)
            var writable = DarwinBoolean(false)
            if AudioObjectHasProperty(device, &property),
               AudioObjectIsPropertySettable(device, &property, &writable) == noErr, writable.boolValue {
                return (device, property)
            }
        }
        return nil
    }
}
