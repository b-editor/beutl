using System;
using System.Collections.Generic;
using System.Text;

namespace BEditor.Graphics {
    public enum ReadBufferMode {
        None = 0,
        FrontLeft = 0x400,
        FrontRight = 1025,
        BackLeft = 1026,
        BackRight = 1027,
        Front = 1028,
        Back = 1029,
        Left = 1030,
        Right = 1031,
        FrontAndBack = 1032,
        Aux0 = 1033,
        Aux1 = 1034,
        Aux2 = 1035,
        Aux3 = 1036,
        ColorAttachment0 = 36064,
        ColorAttachment1 = 36065,
        ColorAttachment2 = 36066,
        ColorAttachment3 = 36067,
        ColorAttachment4 = 36068,
        ColorAttachment5 = 36069,
        ColorAttachment6 = 36070,
        ColorAttachment7 = 36071,
        ColorAttachment8 = 36072,
        ColorAttachment9 = 36073,
        ColorAttachment10 = 36074,
        ColorAttachment11 = 36075,
        ColorAttachment12 = 36076,
        ColorAttachment13 = 36077,
        ColorAttachment14 = 36078,
        ColorAttachment15 = 36079
    }
}
