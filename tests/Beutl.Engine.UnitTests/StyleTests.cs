using Beutl.Animation;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Styling;

using Microsoft.Extensions.Logging;

using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Beutl.Graphics.UnitTests;

#nullable disable

public class StyleTests
{
    private StyleableObject _obj;
    private RectShape _obj2;
    private Style[] _styles1;
    private Style[] _styles2;

    [SetUp]
    public void Setup()
    {
        BeutlApplication.Current.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());

        _obj = new StyleableObject();
        Style style1 = new Style<StyleableObject>
        {
            Setters =
            {
                new Setter<string>(StyleableObject.String1Property, "first1"),
                new Setter<string>(StyleableObject.String2Property, "first2"),
            }
        };
        Style style2 = new Style<StyleableObject>
        {
            Setters =
            {
                new Setter<string>(StyleableObject.String2Property, "second1"),
                new Setter<string>(StyleableObject.String3Property, "second2"),
            }
        };
        Style style3 = new Style<StyleableObject>
        {
            Setters =
            {
                new Setter<string>(StyleableObject.String3Property, "third1"),
                new Setter<string>(StyleableObject.String1Property, "third2"),
            }
        };

        _styles1 = [style1, style2, style3];

        _obj2 = new RectShape
        {
            Fill = Brushes.White
        };
        style1 = new Style<RectShape>
        {
            Setters =
            {
                new Setter<IBrush>(Drawable.FillProperty, new SolidColorBrush
                {
                    Color = Colors.Red,
                    Opacity = 50
                })
            }
        };
        style2 = new Style<RectShape>
        {
            Setters =
            {
                new Setter<IBrush>(Drawable.FillProperty, new SolidColorBrush
                {
                    Color = Colors.White,
                    Opacity = 100
                })
            }
        };

        _styles2 = [style1, style2];
    }

    [Test]
    public void Apply()
    {
        _obj.Styles.Replace(_styles1);

        IStyleInstance instance = _obj.Styles.Instance(_obj);
        (_obj as IStyleable).StyleApplied(instance);
        instance.Apply(new Clock());

        ClassicAssert.AreEqual("third2", _obj.String1);
        ClassicAssert.AreEqual("second1", _obj.String2);
        ClassicAssert.AreEqual("third1", _obj.String3);
    }

    [Test]
    public void Apply2()
    {
        int count1 = 0;
        int count2 = 0;
        int count3 = 0;
        using (SolidColorBrush.ColorProperty.Changed.Subscribe(e =>
        {
            // White -> Red -> White
            if (e.Sender == _obj2.Fill && ++count1 > 2)
            {
                ClassicAssert.Fail();
            }
        }))
        using (Brush.OpacityProperty.Changed.Subscribe(e =>
        {
            // 1.0 -> 0.5 -> 1.0
            if (e.Sender == _obj2.Fill && ++count2 > 2)
            {
                ClassicAssert.Fail();
            }
        }))
        using (Drawable.FillProperty.Changed.Subscribe(e =>
        {
            // ImmutableSolidColorBrush(White) -> SolidColorBrush(Transparent)
            if (e.Sender == _obj2 && ++count3 > 1)
            {
                ClassicAssert.Fail();
            }
        }))
        {
            _obj2.Styles.Replace(_styles2);

            IStyleInstance instance = _obj2.Styles.Instance(_obj2);
            (_obj2 as IStyleable).StyleApplied(instance);
            instance.Begin();
            instance.Apply(new Clock());
            instance.End();

            ClassicAssert.AreEqual(Colors.White, ((ISolidColorBrush)_obj2.Fill).Color);
            ClassicAssert.AreEqual(100, _obj2.Fill.Opacity);
        }
    }

    public class Clock : IClock
    {
        public TimeSpan CurrentTime { get; }

        public TimeSpan AudioStartTime { get; }

        public TimeSpan BeginTime { get; }

        public TimeSpan DurationTime { get; }

        public IClock GlobalClock => this;
    }

    public class InheritStyleableObject : StyleableObject
    {
    }

    public class StyleableObject : Styleable
    {
        public static readonly CoreProperty<string> String1Property = ConfigureProperty<string, StyleableObject>(nameof(String1)).Register();
        public static readonly CoreProperty<string> String2Property = ConfigureProperty<string, StyleableObject>(nameof(String2)).Register();
        public static readonly CoreProperty<string> String3Property = ConfigureProperty<string, StyleableObject>(nameof(String3)).Register();

        public string String1
        {
            get => GetValue(String1Property);
            set => SetValue(String1Property, value);
        }

        public string String2
        {
            get => GetValue(String2Property);
            set => SetValue(String2Property, value);
        }

        public string String3
        {
            get => GetValue(String3Property);
            set => SetValue(String3Property, value);
        }
    }
}
