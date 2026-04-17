import Foundation

// Wrap a Swift reference type so that an opaque C pointer owns a +1 retain count.
// The paired ``release`` call balances the retain performed by ``retain``.
enum HandleRegistry {
    static func retain<T: AnyObject>(_ object: T) -> OpaquePointer {
        let unmanaged = Unmanaged.passRetained(object)
        return OpaquePointer(unmanaged.toOpaque())
    }

    static func borrow<T: AnyObject>(_ handle: OpaquePointer?) -> T? {
        guard let handle = handle else { return nil }
        return Unmanaged<T>.fromOpaque(UnsafeRawPointer(handle)).takeUnretainedValue()
    }

    static func release<T: AnyObject>(_ handle: OpaquePointer?, as _: T.Type) {
        guard let handle = handle else { return }
        Unmanaged<T>.fromOpaque(UnsafeRawPointer(handle)).release()
    }
}
