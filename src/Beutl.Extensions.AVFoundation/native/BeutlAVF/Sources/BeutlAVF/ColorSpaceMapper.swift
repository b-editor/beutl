import AVFoundation
import CBeutlAVFTypes
import CoreMedia
import Foundation

// Tags must stay in sync with the BEUTL_TRANSFER_*/BEUTL_PRIMARIES_* constants in
// BeutlAVFTypes.h and the BeutlTransferFunction/BeutlColorPrimaries enums on the C# side.
struct ColorSpaceTags: Equatable {
    var isHdr: Bool
    var transfer: Int32
    var primaries: Int32
}

enum ColorSpaceMapper {
    static func extract(from track: AVAssetTrack) -> ColorSpaceTags {
        guard let desc = track.formatDescriptions.first else {
            return ColorSpaceTags(isHdr: false,
                                  transfer: Int32(BEUTL_TRANSFER_UNKNOWN),
                                  primaries: Int32(BEUTL_PRIMARIES_UNKNOWN))
        }
        let fmt = desc as! CMFormatDescription
        let transferValue = CMFormatDescriptionGetExtension(
            fmt, extensionKey: kCMFormatDescriptionExtension_TransferFunction) as? String
        let primariesValue = CMFormatDescriptionGetExtension(
            fmt, extensionKey: kCMFormatDescriptionExtension_ColorPrimaries) as? String

        let transfer = mapTransfer(transferValue)
        let primaries = mapPrimaries(primariesValue)
        let isHdr = transfer == Int32(BEUTL_TRANSFER_PQ) || transfer == Int32(BEUTL_TRANSFER_HLG)
        return ColorSpaceTags(isHdr: isHdr, transfer: transfer, primaries: primaries)
    }

    private static func mapTransfer(_ value: String?) -> Int32 {
        guard let value = value as CFString? else { return Int32(BEUTL_TRANSFER_UNKNOWN) }

        // Transfer function CFString constants defined by CoreMedia.
        if CFEqual(value, kCMFormatDescriptionTransferFunction_SMPTE_ST_2084_PQ) {
            return Int32(BEUTL_TRANSFER_PQ)
        }
        if CFEqual(value, kCMFormatDescriptionTransferFunction_ITU_R_2100_HLG) {
            return Int32(BEUTL_TRANSFER_HLG)
        }
        if CFEqual(value, kCMFormatDescriptionTransferFunction_ITU_R_709_2) {
            return Int32(BEUTL_TRANSFER_BT709)
        }
        if CFEqual(value, kCMFormatDescriptionTransferFunction_ITU_R_2020) {
            return Int32(BEUTL_TRANSFER_REC2020)
        }
        if CFEqual(value, kCMFormatDescriptionTransferFunction_SMPTE_240M_1995) {
            return Int32(BEUTL_TRANSFER_SMPTE240M)
        }
        if CFEqual(value, kCMFormatDescriptionTransferFunction_SMPTE_ST_428_1) {
            return Int32(BEUTL_TRANSFER_SMPTE428)
        }
        if CFEqual(value, kCMFormatDescriptionTransferFunction_UseGamma) {
            return Int32(BEUTL_TRANSFER_TWO_DOT_TWO)
        }
        if CFEqual(value, kCMFormatDescriptionTransferFunction_sRGB) {
            return Int32(BEUTL_TRANSFER_SRGB)
        }
        if CFEqual(value, kCMFormatDescriptionTransferFunction_Linear) {
            return Int32(BEUTL_TRANSFER_LINEAR)
        }
        return Int32(BEUTL_TRANSFER_UNKNOWN)
    }

    private static func mapPrimaries(_ value: String?) -> Int32 {
        guard let value = value as CFString? else { return Int32(BEUTL_PRIMARIES_UNKNOWN) }

        if CFEqual(value, kCMFormatDescriptionColorPrimaries_ITU_R_709_2) {
            return Int32(BEUTL_PRIMARIES_BT709)
        }
        if CFEqual(value, kCMFormatDescriptionColorPrimaries_ITU_R_2020) {
            return Int32(BEUTL_PRIMARIES_REC2020)
        }
        if CFEqual(value, kCMFormatDescriptionColorPrimaries_EBU_3213) {
            return Int32(BEUTL_PRIMARIES_EBU3213)
        }
        if CFEqual(value, kCMFormatDescriptionColorPrimaries_SMPTE_C) {
            return Int32(BEUTL_PRIMARIES_SMPTE170M)
        }
        if CFEqual(value, kCMFormatDescriptionColorPrimaries_DCI_P3) {
            return Int32(BEUTL_PRIMARIES_SMPTE431)
        }
        if CFEqual(value, kCMFormatDescriptionColorPrimaries_P3_D65) {
            return Int32(BEUTL_PRIMARIES_DCIP3)
        }
        if CFEqual(value, kCMFormatDescriptionColorPrimaries_P22) {
            return Int32(BEUTL_PRIMARIES_BT470M)
        }
        return Int32(BEUTL_PRIMARIES_UNKNOWN)
    }
}
