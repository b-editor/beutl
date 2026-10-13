using Beutl.FFmpegWorker.Decoding;
using FFmpeg.AutoGen.Abstractions;

namespace Beutl.FFmpegWorker;

// The decoding and encoding AccelerationOptions enums declare the same members in the same order,
// so both pass their value as an int to this one table.
internal static class HardwareAcceleration
{
    public static AVHWDeviceType? ToHWDeviceType(int acceleration)
    {
        return (FFmpegDecodingSettings.AccelerationOptions)acceleration switch
        {
            FFmpegDecodingSettings.AccelerationOptions.Software => AVHWDeviceType.AV_HWDEVICE_TYPE_NONE,
            FFmpegDecodingSettings.AccelerationOptions.VDPAU => AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU,
            FFmpegDecodingSettings.AccelerationOptions.CUDA => AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA,
            FFmpegDecodingSettings.AccelerationOptions.VAAPI => AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI,
            FFmpegDecodingSettings.AccelerationOptions.DXVA2 => AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2,
            FFmpegDecodingSettings.AccelerationOptions.QSV => AVHWDeviceType.AV_HWDEVICE_TYPE_QSV,
            FFmpegDecodingSettings.AccelerationOptions.VideoToolbox => AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX,
            FFmpegDecodingSettings.AccelerationOptions.D3D11VA => AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
            FFmpegDecodingSettings.AccelerationOptions.DRM => AVHWDeviceType.AV_HWDEVICE_TYPE_DRM,
            FFmpegDecodingSettings.AccelerationOptions.OpenCL => AVHWDeviceType.AV_HWDEVICE_TYPE_OPENCL,
            FFmpegDecodingSettings.AccelerationOptions.MediaCodec => AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC,
            FFmpegDecodingSettings.AccelerationOptions.Vulkan => AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN,
            _ => null
        };
    }
}
