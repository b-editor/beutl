#ifndef BeutlAVFTypes_h
#define BeutlAVFTypes_h

#include <stdint.h>

#pragma pack(push, 4)

// --- Transfer function tags (mirrors BitmapColorSpaceTransferFn enum on the C# side). ---
#define BEUTL_TRANSFER_UNKNOWN      0
#define BEUTL_TRANSFER_SRGB         1
#define BEUTL_TRANSFER_LINEAR       2
#define BEUTL_TRANSFER_BT709        3
#define BEUTL_TRANSFER_PQ           4
#define BEUTL_TRANSFER_HLG          5
#define BEUTL_TRANSFER_REC2020      6
#define BEUTL_TRANSFER_TWO_DOT_TWO  7
#define BEUTL_TRANSFER_GAMMA28      8
#define BEUTL_TRANSFER_SMPTE240M    9
#define BEUTL_TRANSFER_SMPTE428    10

// --- YCbCr matrix tags (mirrors AVVideoYCbCrMatrix_* strings on the Swift side). ---
#define BEUTL_MATRIX_UNKNOWN       0
#define BEUTL_MATRIX_BT709         1
#define BEUTL_MATRIX_BT601         2
#define BEUTL_MATRIX_REC2020       3
#define BEUTL_MATRIX_SMPTE240M     4

// --- Color primaries tags (mirrors BitmapColorSpaceXyz enum on the C# side). ---
#define BEUTL_PRIMARIES_UNKNOWN    0
#define BEUTL_PRIMARIES_SRGB       1
#define BEUTL_PRIMARIES_BT709      2
#define BEUTL_PRIMARIES_BT470M     3
#define BEUTL_PRIMARIES_BT470BG    4
#define BEUTL_PRIMARIES_SMPTE170M  5
#define BEUTL_PRIMARIES_SMPTE240M  6
#define BEUTL_PRIMARIES_FILM       7
#define BEUTL_PRIMARIES_REC2020    8
#define BEUTL_PRIMARIES_XYZ        9
#define BEUTL_PRIMARIES_SMPTE431  10
#define BEUTL_PRIMARIES_DCIP3     11
#define BEUTL_PRIMARIES_EBU3213   12

typedef struct {
    int32_t width;
    int32_t height;
    int32_t codecFourCC;
    int32_t frameRateNum;
    int32_t frameRateDen;
    int64_t durationNum;
    int64_t durationDen;
    int64_t nominalFrameCount;
    int32_t isHdr;                 // 0 = SDR, 1 = HDR (PQ or HLG transfer)
    int32_t transferFunction;      // BEUTL_TRANSFER_*
    int32_t colorPrimaries;        // BEUTL_PRIMARIES_*
    int32_t bytesPerPixel;         // 4 (Bgra8888) or 8 (Rgba16161616)
} BeutlVideoInfo;

typedef struct {
    int32_t sampleRate;
    int32_t channelCount;
    int32_t codecFourCC;
    int64_t durationNum;
    int64_t durationDen;
    int64_t nominalSampleCount;
} BeutlAudioInfo;

typedef struct {
    int32_t maxVideoBufferSize;
    int32_t maxAudioBufferSize;
    int32_t thresholdFrameCount;
    int32_t thresholdSampleCount;
} BeutlReaderOptions;

typedef struct {
    int32_t width;
    int32_t height;
    int32_t sourceWidth;
    int32_t sourceHeight;
    int32_t codec;                 // 0=Default, 1=H264, 2=JPEG, 3=HEVC
    int32_t bitrate;               // -1 = unspecified
    int32_t keyframeInterval;      // -1 = unspecified
    int32_t profileLevelH264;      // 0=Default
    int32_t frameRateNum;
    int32_t frameRateDen;
    float   jpegQuality;           // < 0 = unspecified
    int32_t isHdr;                 // 0 = SDR, 1 = HDR (forces HEVC Main10 and 10+ bit pipeline)
    int32_t colorTransfer;         // BEUTL_TRANSFER_*
    int32_t colorPrimaries;        // BEUTL_PRIMARIES_*
    int32_t yCbCrMatrix;           // BEUTL_MATRIX_*
} BeutlVideoEncoderConfig;

typedef struct {
    int32_t sampleRate;
    int32_t channelCount;
    int32_t formatFourCC;
    int32_t bitrate;                  // -1 = unspecified
    int32_t quality;                  // -1 = Default
    int32_t sampleRateConverterQuality;
    int32_t linearPcmBitDepth;
    int32_t linearPcmFlags;           // bit0=Float, bit1=BigEndian, bit2=NonInterleaved
} BeutlAudioEncoderConfig;

#pragma pack(pop)

#endif /* BeutlAVFTypes_h */
