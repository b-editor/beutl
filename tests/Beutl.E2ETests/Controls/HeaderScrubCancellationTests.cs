using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Beutl.Configuration;
using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests.Controls;

// A header scrub hides the cursor until it ends. These tests drop the release in the ways the app can
// lose it and check that the scrub still ends: the cursor comes back, the value reached so far is
// confirmed once, and later moves over the header leave the value alone.
[TestFixture, NonParallelizable]
public class HeaderScrubCancellationTests
{
    public sealed record EditorCase(string Name, Func<PropertyEditor> Create, Func<PropertyEditor, object> Read)
    {
        public override string ToString() => Name;
    }

    private static readonly EditorCase[] s_editors =
    [
        new("Number", () => new NumberEditor<int> { Header = "N" }, e => ((NumberEditor<int>)e).Value),
        new("Rational", () => new RationalEditor { Header = "R", Value = new Rational(30, 1) }, e => ((RationalEditor)e).Value),
        new("Vector2", () => new Vector2Editor<int> { Header = "V2" },
            e => (((Vector2Editor<int>)e).FirstValue, ((Vector2Editor<int>)e).SecondValue)),
        new("Vector3", () => new Vector3Editor<float> { Header = "V3" },
            e => (((Vector3Editor<float>)e).FirstValue, ((Vector3Editor<float>)e).SecondValue,
                ((Vector3Editor<float>)e).ThirdValue)),
        new("Vector4", () => new Vector4Editor<float> { Header = "V4" },
            e => (((Vector4Editor<float>)e).FirstValue, ((Vector4Editor<float>)e).SecondValue,
                ((Vector4Editor<float>)e).ThirdValue, ((Vector4Editor<float>)e).FourthValue)),
    ];

    private bool _pointerLock;

    // Pointer lock reads the real mouse on macOS and Windows, which a headless drag never moves.
    [SetUp]
    public void DisablePointerLock()
    {
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        _pointerLock = config.EnablePointerLockInProperty;
        config.EnablePointerLockInProperty = false;
    }

    [TearDown]
    public void RestorePointerLock()
    {
        GlobalConfiguration.Instance.EditorConfig.EnablePointerLockInProperty = _pointerLock;
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(s_editors))]
    public void Another_element_taking_the_pointer_mid_drag_ends_the_scrub(EditorCase editorCase)
    {
        using var scrub = Scrub.Start(editorCase);

        // As a dialog or another window does when it takes the pointer.
        scrub.Pointer.Capture(scrub.Host.Sink);
        HeadlessTestHelpers.Settle();
        scrub.Pointer.Capture(null);
        scrub.MoveOverHeader(RawInputModifiers.None);
        scrub.MoveOverHeader(RawInputModifiers.LeftMouseButton);

        scrub.AssertEndedOnce();
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(s_editors))]
    public void Removing_the_editor_mid_drag_ends_the_scrub(EditorCase editorCase)
    {
        using var scrub = Scrub.Start(editorCase);

        ((StackPanel)scrub.Host.Window.Content!).Children.Remove(scrub.Host.Editor);
        HeadlessTestHelpers.Settle();

        scrub.AssertEndedOnce();
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(s_editors))]
    public void A_move_with_the_button_up_ends_the_scrub(EditorCase editorCase)
    {
        using var scrub = Scrub.Start(editorCase);

        // The release happened where the header never heard of it.
        scrub.MoveOverHeader(RawInputModifiers.None);
        scrub.MoveOverHeader(RawInputModifiers.None);

        scrub.AssertEndedOnce();
    }

    private sealed class Scrub : IDisposable
    {
        private readonly EditorCase _editorCase;
        private readonly List<PropertyEditorValueChangedEventArgs> _confirmed = [];
        private readonly int _activeBefore;
        private readonly object _initial;
        private readonly object _dragged;
        private readonly TextBlock _header;
        private double _offset = 40;

        private Scrub(EditorCase editorCase)
        {
            _editorCase = editorCase;
            Host = new EditorTestHost<PropertyEditor>(editorCase.Create());
            Host.Editor.ValueConfirmed += (_, e) => _confirmed.Add(e);
            _header = Host.Require<TextBlock>("PART_HeaderTextBlock");
            _activeBefore = HeaderScrubGesture.ActiveCount;
            _initial = editorCase.Read(Host.Editor);

            Pointer = Host.PressAndDrag(_header, _offset);
            _dragged = editorCase.Read(Host.Editor);
            Assert.That(_dragged, Is.Not.EqualTo(_initial), "precondition: the drag changed the value");
            Assert.That(HeaderScrubGesture.ActiveCount, Is.EqualTo(_activeBefore + 1), "precondition: the scrub hid the cursor");
        }

        public EditorTestHost<PropertyEditor> Host { get; }

        public IPointer Pointer { get; }

        public static Scrub Start(EditorCase editorCase) => new(editorCase);

        public void MoveOverHeader(RawInputModifiers modifiers)
        {
            _offset += 40;
            Point? point = _header.TranslatePoint(new Point(_header.Bounds.Width / 2 + _offset, _header.Bounds.Height / 2), Host.Window);
            Host.Window.MouseMove(point ?? default, modifiers);
            HeadlessTestHelpers.Settle();
        }

        public void AssertEndedOnce()
        {
            Assert.Multiple(() =>
            {
                Assert.That(HeaderScrubGesture.ActiveCount, Is.EqualTo(_activeBefore), "the cursor is shown again");
                Assert.That(_editorCase.Read(Host.Editor), Is.EqualTo(_dragged), "later moves leave the value alone");
                Assert.That(_confirmed, Has.Count.EqualTo(1), "the value reached so far is confirmed once");
            });
            PropertyEditorValueChangedEventArgs args = _confirmed[0];
            Assert.That(args.NewValue, Is.EqualTo(_dragged));
            // Only the typed subclass carries the old value.
            Assert.That(args.GetType().GetProperty("OldValue")!.GetValue(args), Is.EqualTo(_initial));
        }

        public void Dispose() => Host.Dispose();
    }
}
