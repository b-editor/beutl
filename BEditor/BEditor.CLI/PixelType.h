#pragma once

namespace BEditor {
	namespace CLI {
		namespace Graphics {
			public enum class PixelType {
                Byte = 5120,
                UnsignedByte = 5121,
                Short = 5122,
                UnsignedShort = 5123,
                Int = 5124,
                UnsignedInt = 5125,
                Float = 5126,
                HalfFloat = 5131,
                Bitmap = 6656,
                UnsignedByte332 = 32818,
                UnsignedByte332Ext = 32818,
                UnsignedShort4444 = 32819,
                UnsignedShort4444Ext = 32819,
                UnsignedShort5551 = 32820,
                UnsignedShort5551Ext = 32820,
                UnsignedInt8888 = 32821,
                UnsignedInt8888Ext = 32821,
                UnsignedInt1010102 = 32822,
                UnsignedInt1010102Ext = 32822,
                UnsignedByte233Reversed = 33634,
                UnsignedShort565 = 33635,
                UnsignedShort565Reversed = 33636,
                UnsignedShort4444Reversed = 33637,
                UnsignedShort1555Reversed = 33638,
                UnsignedInt8888Reversed = 33639,
                UnsignedInt2101010Reversed = 33640,
                UnsignedInt248 = 34042,
                UnsignedInt10F11F11FRev = 35899,
                UnsignedInt5999Rev = 35902,
                Float32UnsignedInt248Rev = 36269
			};
		}
	}
}