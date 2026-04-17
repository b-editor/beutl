#ifndef BeutlAVFTypes_h
#define BeutlAVFTypes_h

#include <stdint.h>

#pragma pack(push, 4)

typedef struct {
    int32_t width;
    int32_t height;
    int32_t codecFourCC;
    int32_t frameRateNum;
    int32_t frameRateDen;
    int64_t durationNum;
    int64_t durationDen;
    int64_t nominalFrameCount;
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
    int32_t codec;                 // 0=Default, 1=H264, 2=JPEG
    int32_t bitrate;               // -1 = unspecified
    int32_t keyframeInterval;      // -1 = unspecified
    int32_t profileLevelH264;      // 0=Default
    int32_t frameRateNum;
    int32_t frameRateDen;
    float   jpegQuality;           // < 0 = unspecified
    int32_t _padding;
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
