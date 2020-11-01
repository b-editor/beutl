using System;

namespace BEditor.Core.Media {
#nullable enable
    public readonly struct ImageType : IEquatable<ImageType> {
        public int Value { get; }

        public ImageType(in int value) => Value = value;

        public static implicit operator int(in ImageType type) => type.Value;
        public static implicit operator OpenTK.Graphics.OpenGL.PixelType(in ImageType type) {
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
        public static implicit operator OpenTK.Graphics.OpenGL.PixelInternalFormat(in ImageType type) => type.Channels switch
        {
            1 => OpenTK.Graphics.OpenGL.PixelInternalFormat.One,
            2 => OpenTK.Graphics.OpenGL.PixelInternalFormat.Rg8,
            3 => OpenTK.Graphics.OpenGL.PixelInternalFormat.Rgb,
            4 => OpenTK.Graphics.OpenGL.PixelInternalFormat.Rgba,
            _ => throw new Exception(),
        };
        public static implicit operator OpenTK.Graphics.OpenGL.PixelFormat(in ImageType type) => type.Channels switch
        {
            1 => OpenTK.Graphics.OpenGL.PixelFormat.Red,
            2 => OpenTK.Graphics.OpenGL.PixelFormat.Rg,
            3 => OpenTK.Graphics.OpenGL.PixelFormat.Bgr,
            4 => OpenTK.Graphics.OpenGL.PixelFormat.Bgra,
            _ => throw new Exception(),
        };

        public static implicit operator ImageType(in int value) => new ImageType(value);

        public static ImageType FromInt32(in int value) => new ImageType(value);

        public int Depth => Value & (DepthMax - 1);

        public bool IsInteger => Depth < Float;

        public int Channels => (Value >> ChannelShift) + 1;

        public int Bits {
            get {
                var depth = Depth;
                if (depth is Byte or Char or CV_USRTYPE1) return 8;
                else if (depth is UShort or Short) return 16;
                else if (depth is Int or Float) return 32;
                else return 64;
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

        public static bool operator ==(in ImageType left, in ImageType right) => left.Equals(right);

        public static bool operator !=(in ImageType left, in ImageType right) => !left.Equals(right);


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
            ByteCh1 = ByteChannel(1),
            ByteCh2 = ByteChannel(2),
            ByteCh3 = ByteChannel(3),
            ByteCh4 = ByteChannel(4),
            CharCh1 = CharChannel(1),
            CharCh2 = CharChannel(2),
            CharCh3 = CharChannel(3),
            CharCh4 = CharChannel(4),
            UShortCh1 = UShortChannel(1),
            UShortCh2 = UShortChannel(2),
            UShortCh3 = UShortChannel(3),
            UShortCh4 = UShortChannel(4),
            ShortCh1 = ShortChannel(1),
            ShortCh2 = ShortChannel(2),
            ShortCh3 = ShortChannel(3),
            ShortCh4 = ShortChannel(4),
            IntCh1 = IntChannel(1),
            IntCh2 = IntChannel(2),
            IntCh3 = IntChannel(3),
            IntCh4 = IntChannel(4),
            FloatCh1 = FloatChannel(1),
            FloatCh2 = FloatChannel(2),
            FloatCh3 = FloatChannel(3),
            FloatCh4 = FloatChannel(4),
            DoubleCh1 = DoubleChannel(1),
            DoubleCh2 = DoubleChannel(2),
            DoubleCh3 = DoubleChannel(3),
            DoubleCh4 = DoubleChannel(4);

        #region

        public static ImageType ByteChannel(int ch) => MakeType(Byte, ch);

        public static ImageType CharChannel(in int ch) => MakeType(Char, ch);

        public static ImageType UShortChannel(in int ch) => MakeType(UShort, ch);

        public static ImageType ShortChannel(in int ch) => MakeType(Short, ch);

        public static ImageType IntChannel(in int ch) => MakeType(Int, ch);

        public static ImageType FloatChannel(in int ch) => MakeType(Float, ch);

        public static ImageType DoubleChannel(in int ch) => MakeType(Double, ch);

        #endregion

        public static ImageType MakeType(in int depth, in int channels) {
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
