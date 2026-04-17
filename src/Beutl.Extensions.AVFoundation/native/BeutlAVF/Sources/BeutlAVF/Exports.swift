import CBeutlAVFTypes
import Foundation

// Increment on any breaking ABI change (struct layout, function signature).
private let abiVersion: Int32 = 1

@_cdecl("beutl_avf_version")
public func beutl_avf_version() -> Int32 {
    return abiVersion
}

@_cdecl("beutl_avf_last_error_message")
public func beutl_avf_last_error_message(
    _ buffer: UnsafeMutablePointer<CChar>?,
    _ capacity: Int
) {
    guard let buffer = buffer, capacity > 0 else { return }
    let message = getLastErrorMessage()
    let bytes = Array(message.utf8)
    let copyCount = min(bytes.count, capacity - 1)
    for i in 0..<copyCount {
        buffer[i] = CChar(bitPattern: bytes[i])
    }
    buffer[copyCount] = 0
}

// MARK: - Reader

@_cdecl("beutl_avf_reader_open")
public func beutl_avf_reader_open(
    _ pathPtr: UnsafePointer<CChar>?,
    _ modeFlags: Int32,
    _ optionsPtr: UnsafePointer<BeutlReaderOptions>?,
    _ outHandle: UnsafeMutablePointer<OpaquePointer?>?
) -> Int32 {
    return withErrorHandling {
        guard let outHandle = outHandle else {
            throw BeutlAVFError.invalidArgument("outHandle is null")
        }
        outHandle.pointee = nil

        guard let pathPtr = pathPtr else {
            throw BeutlAVFError.invalidArgument("path is null")
        }

        let options: BeutlReaderOptions = optionsPtr?.pointee ?? BeutlReaderOptions(
            maxVideoBufferSize: 4,
            maxAudioBufferSize: 20,
            thresholdFrameCount: 30,
            thresholdSampleCount: 30000)

        let path = String(cString: pathPtr)
        let reader = try Reader(path: path, modeFlags: modeFlags, options: options)
        outHandle.pointee = HandleRegistry.retain(reader)
    }
}

@_cdecl("beutl_avf_reader_has_video")
public func beutl_avf_reader_has_video(
    _ handle: OpaquePointer?,
    _ outHasVideo: UnsafeMutablePointer<Int32>?
) -> Int32 {
    return withErrorHandling {
        guard let reader: Reader = HandleRegistry.borrow(handle) else {
            throw BeutlAVFError.invalidHandle
        }
        outHasVideo?.pointee = reader.videoContext != nil ? 1 : 0
    }
}

@_cdecl("beutl_avf_reader_has_audio")
public func beutl_avf_reader_has_audio(
    _ handle: OpaquePointer?,
    _ outHasAudio: UnsafeMutablePointer<Int32>?
) -> Int32 {
    return withErrorHandling {
        guard let reader: Reader = HandleRegistry.borrow(handle) else {
            throw BeutlAVFError.invalidHandle
        }
        outHasAudio?.pointee = reader.audioContext != nil ? 1 : 0
    }
}

@_cdecl("beutl_avf_reader_get_video_info")
public func beutl_avf_reader_get_video_info(
    _ handle: OpaquePointer?,
    _ outInfo: UnsafeMutablePointer<BeutlVideoInfo>?
) -> Int32 {
    return withErrorHandling {
        guard let reader: Reader = HandleRegistry.borrow(handle) else {
            throw BeutlAVFError.invalidHandle
        }
        guard let videoContext = reader.videoContext else {
            throw BeutlAVFError.noVideoTrack
        }
        guard let outInfo = outInfo else {
            throw BeutlAVFError.invalidArgument("outInfo is null")
        }
        outInfo.pointee = videoContext.info
    }
}

@_cdecl("beutl_avf_reader_get_audio_info")
public func beutl_avf_reader_get_audio_info(
    _ handle: OpaquePointer?,
    _ outInfo: UnsafeMutablePointer<BeutlAudioInfo>?
) -> Int32 {
    return withErrorHandling {
        guard let reader: Reader = HandleRegistry.borrow(handle) else {
            throw BeutlAVFError.invalidHandle
        }
        guard let audioContext = reader.audioContext else {
            throw BeutlAVFError.noAudioTrack
        }
        guard let outInfo = outInfo else {
            throw BeutlAVFError.invalidArgument("outInfo is null")
        }
        outInfo.pointee = audioContext.info
    }
}

@_cdecl("beutl_avf_reader_read_video")
public func beutl_avf_reader_read_video(
    _ handle: OpaquePointer?,
    _ frameIndex: Int64,
    _ outBuffer: UnsafeMutableRawPointer?,
    _ capacityBytes: Int32,
    _ rowBytes: Int32
) -> Int32 {
    return withErrorHandling {
        guard let reader: Reader = HandleRegistry.borrow(handle) else {
            throw BeutlAVFError.invalidHandle
        }
        guard let videoContext = reader.videoContext else {
            throw BeutlAVFError.noVideoTrack
        }
        guard let outBuffer = outBuffer else {
            throw BeutlAVFError.invalidArgument("outBuffer is null")
        }
        try videoContext.readFrame(
            index: Int(frameIndex),
            outBuffer: outBuffer,
            capacityBytes: Int(capacityBytes),
            rowBytes: Int(rowBytes))
    }
}

@_cdecl("beutl_avf_reader_read_audio")
public func beutl_avf_reader_read_audio(
    _ handle: OpaquePointer?,
    _ startSample: Int64,
    _ lengthSamples: Int32,
    _ outBuffer: UnsafeMutableRawPointer?,
    _ capacityBytes: Int32
) -> Int32 {
    return withErrorHandling {
        guard let reader: Reader = HandleRegistry.borrow(handle) else {
            throw BeutlAVFError.invalidHandle
        }
        guard let audioContext = reader.audioContext else {
            throw BeutlAVFError.noAudioTrack
        }
        guard let outBuffer = outBuffer else {
            throw BeutlAVFError.invalidArgument("outBuffer is null")
        }
        try audioContext.readSamples(
            startSample: Int(startSample),
            length: Int(lengthSamples),
            outBuffer: outBuffer,
            capacityBytes: Int(capacityBytes))
    }
}

@_cdecl("beutl_avf_reader_close")
public func beutl_avf_reader_close(_ handle: OpaquePointer?) {
    HandleRegistry.release(handle, as: Reader.self)
}

// MARK: - Writer

@_cdecl("beutl_avf_writer_create")
public func beutl_avf_writer_create(
    _ pathPtr: UnsafePointer<CChar>?,
    _ videoConfigPtr: UnsafePointer<BeutlVideoEncoderConfig>?,
    _ audioConfigPtr: UnsafePointer<BeutlAudioEncoderConfig>?,
    _ outHandle: UnsafeMutablePointer<OpaquePointer?>?
) -> Int32 {
    return withErrorHandling {
        guard let outHandle = outHandle else {
            throw BeutlAVFError.invalidArgument("outHandle is null")
        }
        outHandle.pointee = nil
        guard let pathPtr = pathPtr else {
            throw BeutlAVFError.invalidArgument("path is null")
        }
        let path = String(cString: pathPtr)
        let writer = try Writer(
            path: path,
            videoConfig: videoConfigPtr?.pointee,
            audioConfig: audioConfigPtr?.pointee)
        outHandle.pointee = HandleRegistry.retain(writer)
    }
}

@_cdecl("beutl_avf_writer_start")
public func beutl_avf_writer_start(_ handle: OpaquePointer?) -> Int32 {
    return withErrorHandling {
        guard let writer: Writer = HandleRegistry.borrow(handle) else {
            throw BeutlAVFError.invalidHandle
        }
        try writer.start()
    }
}

@_cdecl("beutl_avf_writer_append_video")
public func beutl_avf_writer_append_video(
    _ handle: OpaquePointer?,
    _ bgra: UnsafeRawPointer?,
    _ width: Int32,
    _ height: Int32,
    _ rowBytes: Int32,
    _ ptsNum: Int64,
    _ ptsDen: Int32
) -> Int32 {
    return withErrorHandling {
        guard let writer: Writer = HandleRegistry.borrow(handle) else {
            throw BeutlAVFError.invalidHandle
        }
        guard let bgra = bgra else {
            throw BeutlAVFError.invalidArgument("bgra is null")
        }
        try writer.appendVideo(
            bgra: bgra,
            width: Int(width),
            height: Int(height),
            rowBytes: Int(rowBytes),
            ptsNum: ptsNum,
            ptsDen: ptsDen)
    }
}

@_cdecl("beutl_avf_writer_append_audio")
public func beutl_avf_writer_append_audio(
    _ handle: OpaquePointer?,
    _ pcm: UnsafeMutableRawPointer?,
    _ numSamples: Int32,
    _ ptsSamples: Int64,
    _ sampleRate: Int32
) -> Int32 {
    return withErrorHandling {
        guard let writer: Writer = HandleRegistry.borrow(handle) else {
            throw BeutlAVFError.invalidHandle
        }
        guard let pcm = pcm else {
            throw BeutlAVFError.invalidArgument("pcm is null")
        }
        try writer.appendAudio(
            pcm: pcm,
            numSamples: Int(numSamples),
            ptsSamples: ptsSamples,
            sampleRate: Int(sampleRate))
    }
}

@_cdecl("beutl_avf_writer_finish")
public func beutl_avf_writer_finish(_ handle: OpaquePointer?) -> Int32 {
    return withErrorHandling {
        guard let writer: Writer = HandleRegistry.borrow(handle) else {
            throw BeutlAVFError.invalidHandle
        }
        try writer.finish()
    }
}

@_cdecl("beutl_avf_writer_close")
public func beutl_avf_writer_close(_ handle: OpaquePointer?) {
    HandleRegistry.release(handle, as: Writer.self)
}
