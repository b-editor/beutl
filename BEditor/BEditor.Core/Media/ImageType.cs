using System;

namespace BEditor.Core.Media {
#nullable enable
    public readonly struct ImageType : IEquatable<ImageType> {
        public int Value { get; }

        public ImageType(int value) => Value = value;

        public static implicit operator int(ImageType type) => type.Value;
        public static implicit operator OpenTK.Graphics.OpenGL.PixelType(ImageType type) {
            OpenTK.Graphics.OpenGL.PixelType s = OpenTK.Graphics.OpenGL.PixelType.Bitmap;
            switch (type.Depth) {
                case Byte:
                    s = OpenTK.Graphics.OpenGL.PixelType.UnsignedByte;
                    break;
                case Char:
                    s = OpenTK.Graphics.OpenGL.PixelType.Byte;
                    break;
                case UShort:
                    s = OpenTK.Graphics.OpenGL.PixelType.UnsignedShort;
                    break;
                case Short:
                    s = OpenTK.Graphics.OpenGL.PixelType.Short;
                    break;
                case Int:
                    s = OpenTK.Graphics.OpenGL.PixelType.Int;
                    break;
                case Float:
                    s = OpenTK.Graphics.OpenGL.PixelType.Float;
                    break;
                case Double:
                    break;
                case CV_USRTYPE1:
                    break;
                default:
                    break;
            }

            return s;
        }
        public static implicit operator OpenTK.Graphics.OpenGL.PixelInternalFormat(ImageType type) => type.Channels switch
        {
            1 => OpenTK.Graphics.OpenGL.PixelInternalFormat.One,
            2 => OpenTK.Graphics.OpenGL.PixelInternalFormat.Rg8,
            3 => OpenTK.Graphics.OpenGL.PixelInternalFormat.Rgb,
            4 => OpenTK.Graphics.OpenGL.PixelInternalFormat.Rgba,
            _ => throw new Exception(),
        };
        public static implicit operator OpenTK.Graphics.OpenGL.PixelFormat(ImageType type) => type.Channels switch
        {
            1 => OpenTK.Graphics.OpenGL.PixelFormat.Red,
            2 => OpenTK.Graphics.OpenGL.PixelFormat.Rg,
            3 => OpenTK.Graphics.OpenGL.PixelFormat.Bgr,
            4 => OpenTK.Graphics.OpenGL.PixelFormat.Bgra,
            _ => throw new Exception(),
        };

        public static implicit operator ImageType(int value) => new ImageType(value);

        public static ImageType FromInt32(int value) => new ImageType(value);

        public int Depth => Value & (DepthMax - 1);

        public bool IsInteger => Depth < Float;

        public int Channels => (Value >> ChannelShift) + 1;

        public int Bits {
            get {
                return Depth switch
                {
                    Byte => 8,
                    Char => 8,
                    UShort => 16,
                    Short => 16,
                    Int => 32,
                    Float => 32,
                    Double => 64,
                    CV_USRTYPE1 => 8,
                    _ => throw new Exception(),
                };
            }
        }


        public bool Equals(ImageType type) => Value == type.Value;


        public override bool Equals(object? type) {
            if (type is null) {
                return false;
            }

            if (type.GetType() != typeof(ImageType)) {
                return false;
            }

            return Equals((ImageType)type);
        }

        public static bool operator ==(ImageType left, ImageType right) => left.Equals(right);

        public static bool operator !=(ImageType left, ImageType right) => !left.Equals(right);


        public override int GetHashCode() => Value.GetHashCode();


        public override string ToString() {
            string s;
            switch (Depth) {
                case Byte:
                    s = "Byte";
                    break;
                case Char:
                    s = "Char";
                    break;
                case UShort:
                    s = "UShort";
                    break;
                case Short:
                    s = "Short";
                    break;
                case Int:
                    s = "Int";
                    break;
                case Float:
                    s = "Float";
                    break;
                case Double:
                    s = "Double";
                    break;
                case CV_USRTYPE1:
                    s = "CV_USRTYPE1";
                    break;
                default:
                    return $"Unsupported type value ({Value})";
            }

            var ch = Channels;
            if (ch <= 4) {
                return s + "Channel" + ch;
            }
            else {
                return s + "Channel(" + ch + ")";
            }
        }

        private const int ChannelMax = 512,
            ChannelShift = 3,
            DepthMax = 1 << ChannelShift;


        public const int
            Byte = 0,
            Char = 1,
            UShort = 2,
            Short = 3,
            Int = 4,
            Float = 5,
            Double = 6,
            CV_USRTYPE1 = 7;


        public static readonly ImageType
            ByteChannel1 = ByteChannel(1),
            ByteChannel2 = ByteChannel(2),
            ByteChannel3 = ByteChannel(3),
            ByteChannel4 = ByteChannel(4),
            CharChannel1 = CharChannel(1),
            CharChannel2 = CharChannel(2),
            CharChannel3 = CharChannel(3),
            CharChannel4 = CharChannel(4),
            UShortChannel1 = UShortChannel(1),
            UShortChannel2 = UShortChannel(2),
            UShortChannel3 = UShortChannel(3),
            UShortChannel4 = UShortChannel(4),
            ShortChannel1 = ShortChannel(1),
            ShortChannel2 = ShortChannel(2),
            ShortChannel3 = ShortChannel(3),
            ShortChannel4 = ShortChannel(4),
            IntChannel1 = IntChannel(1),
            IntChannel2 = IntChannel(2),
            IntChannel3 = IntChannel(3),
            IntChannel4 = IntChannel(4),
            FloatChannel1 = FloatChannel(1),
            FloatChannel2 = FloatChannel(2),
            FloatChannel3 = FloatChannel(3),
            FloatChannel4 = FloatChannel(4),
            DoubleChannel1 = DoubleChannel(1),
            DoubleChannel2 = DoubleChannel(2),
            DoubleChannel3 = DoubleChannel(3),
            DoubleChannel4 = DoubleChannel(4);

        #region

        public static ImageType ByteChannel(int ch) => MakeType(Byte, ch);

        public static ImageType CharChannel(int ch) => MakeType(Char, ch);

        public static ImageType UShortChannel(int ch) => MakeType(UShort, ch);

        public static ImageType ShortChannel(int ch) => MakeType(Short, ch);

        public static ImageType IntChannel(int ch) => MakeType(Int, ch);

        public static ImageType FloatChannel(int ch) => MakeType(Float, ch);

        public static ImageType DoubleChannel(int ch) => MakeType(Double, ch);

        #endregion

        public static ImageType MakeType(int depth, int channels) {
            if (channels <= 0 || channels >= ChannelMax) {
                throw new Exception("Channels count should be 1.." + (ChannelMax - 1));
            }

            if (depth < 0 || depth >= DepthMax) {
                throw new Exception("Data type depth should be 0.." + (DepthMax - 1));
            }

            return (depth & (DepthMax - 1)) + ((channels - 1) << ChannelShift);
        }
    }
}
