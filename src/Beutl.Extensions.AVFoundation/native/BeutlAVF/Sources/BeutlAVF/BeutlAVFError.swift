import Foundation

enum BeutlAVFError: Error {
    case invalidHandle
    case invalidArgument(String)
    case bufferTooSmall(required: Int, actual: Int)
    case fileNotFound(String)
    case noVideoTrack
    case noAudioTrack
    case readerFailed(String)
    case endOfStream
    case writerFailed(String)
    case unknown(String)

    var code: Int32 {
        switch self {
        case .invalidHandle: return -1
        case .invalidArgument: return -2
        case .bufferTooSmall: return -3
        case .fileNotFound: return -100
        case .noVideoTrack: return -101
        case .noAudioTrack: return -102
        case .readerFailed: return -103
        case .endOfStream: return -104
        case .writerFailed: return -200
        case .unknown: return -999
        }
    }

    var message: String {
        switch self {
        case .invalidHandle: return "Invalid handle."
        case .invalidArgument(let m): return "Invalid argument: \(m)"
        case .bufferTooSmall(let r, let a): return "Buffer too small (required=\(r), actual=\(a))."
        case .fileNotFound(let p): return "File not found: \(p)"
        case .noVideoTrack: return "No video track."
        case .noAudioTrack: return "No audio track."
        case .readerFailed(let m): return "Reader failed: \(m)"
        case .endOfStream: return "End of stream."
        case .writerFailed(let m): return "Writer failed: \(m)"
        case .unknown(let m): return "Unknown error: \(m)"
        }
    }
}

func describeNativeError(_ error: Error) -> String {
    let nativeError = error as NSError
    var message = "\(nativeError.localizedDescription) (domain=\(nativeError.domain), code=\(nativeError.code))"
    if let underlyingError = nativeError.userInfo[NSUnderlyingErrorKey] as? NSError {
        message += "; underlying: \(describeNativeError(underlyingError))"
    }
    return message
}

// Thread-local storage for the last error message.
private final class LastErrorBox {
    var message: String = ""
}

private let lastErrorKey = "beutl.avf.lastError"

private func lastErrorBox() -> LastErrorBox {
    if let existing = Thread.current.threadDictionary[lastErrorKey] as? LastErrorBox {
        return existing
    }
    let box = LastErrorBox()
    Thread.current.threadDictionary[lastErrorKey] = box
    return box
}

func setLastErrorMessage(_ message: String) {
    lastErrorBox().message = message
}

func getLastErrorMessage() -> String {
    return lastErrorBox().message
}

// Exports are called from .NET threads, which drain no autorelease pool, so each call drains its own. Otherwise the
// reader a seek replaces stays alive, and after a few hundred seeks AVFoundation can no longer decode.
@discardableResult
func withErrorHandling(_ body: () throws -> Void) -> Int32 {
    do {
        try autoreleasepool(invoking: body)
        setLastErrorMessage("")
        return 0
    } catch let err as BeutlAVFError {
        setLastErrorMessage(err.message)
        return err.code
    } catch {
        setLastErrorMessage(String(describing: error))
        return BeutlAVFError.unknown(String(describing: error)).code
    }
}
