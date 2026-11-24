import AppKit
import Darwin

@MainActor
enum DisplaySpaces {
    private typealias Connection = @convention(c) () -> Int32
    private typealias CopyDisplays = @convention(c) (Int32) -> Unmanaged<CFArray>?

    private static let windowServer: (connection: Connection, copyDisplays: CopyDisplays)? = {
        guard let library = dlopen("/System/Library/PrivateFrameworks/SkyLight.framework/SkyLight", RTLD_LAZY | RTLD_LOCAL) else {
            return nil
        }
        guard let connection = dlsym(library, "CGSMainConnectionID"),
              let copy = dlsym(library, "CGSCopyManagedDisplaySpaces") else {
            dlclose(library)
            return nil
        }
        return (unsafeBitCast(connection, to: Connection.self), unsafeBitCast(copy, to: CopyDisplays.self))
    }()

    private static var fullScreenByDisplay: [CGDirectDisplayID: Bool] = [:]

    static func refresh() {
        fullScreenByDisplay.removeAll()
        guard let windowServer,
              let displays = windowServer.copyDisplays(windowServer.connection())?.takeRetainedValue() as? [[String: Any]] else { return }
        var spaces: [String: Bool] = [:]
        for display in displays {
            guard let id = display["Display Identifier"] as? String,
                  let space = display["Current Space"] as? [String: Any], let type = space["type"] as? Int else { continue }
            spaces[id.uppercased()] = type == 4
        }
        for screen in NSScreen.screens {
            guard let id = displayID(screen), let uuid = CGDisplayCreateUUIDFromDisplayID(id)?.takeRetainedValue() else { continue }
            let key = CFUUIDCreateString(nil, uuid) as String
            fullScreenByDisplay[id] = spaces[key.uppercased()] ?? spaces["MAIN"]
        }
    }

    static func isFullScreen(on screen: NSScreen) -> Bool {
        // An accessory app may receive empty presentation options while another app is full-screen.
        if let id = displayID(screen), let fullScreen = fullScreenByDisplay[id] { return fullScreen }
        return NSApp.currentSystemPresentationOptions.contains(.fullScreen)
    }

    private static func displayID(_ screen: NSScreen) -> CGDirectDisplayID? {
        screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? CGDirectDisplayID
    }
}
