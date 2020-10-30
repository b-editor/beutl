#pragma once

namespace BEditor {
	namespace CLI {
		namespace Graphics { 
			public enum class PixelFormat {
                UnsignedShort = 5123,
                UnsignedInt = 5125,
                ColorIndex = 6400,
                StencilIndex = 6401,
                DepthComponent = 6402,
                Red = 6403,
                RedExt = 6403,
                Green = 6404,
                Blue = 6405,
                Alpha = 6406,
                Rgb = 6407,
                Rgba = 6408,
                Luminance = 6409,
                LuminanceAlpha = 6410,
                AbgrExt = 0x8000,
                CmykExt = 32780,
                CmykaExt = 32781,
                Bgr = 32992,
                Bgra = 32993,
                Ycrcb422Sgix = 33211,
                Ycrcb444Sgix = 33212,
                Rg = 33319,
                RgInteger = 33320,
                R5G6B5IccSgix = 33894,
                R5G6B5A8IccSgix = 33895,
                Alpha16IccSgix = 33896,
                Luminance16IccSgix = 33897,
                Luminance16Alpha8IccSgix = 33899,
                DepthStencil = 34041,
                RedInteger = 36244,
                GreenInteger = 36245,
                BlueInteger = 36246,
                AlphaInteger = 36247,
                RgbInteger = 36248,
                RgbaInteger = 36249,
                BgrInteger = 36250,
                BgraInteger = 36251
			};
		}
	}
}