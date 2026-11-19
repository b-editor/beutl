using Beutl.Configuration;
using Beutl.Extensibility;
using Beutl.Media.Decoding;
using Reactive.Bindings;

namespace Beutl.UnitTests.TestInfrastructure;

public sealed class TestEditorContext(CoreObject obj, GlobalConfiguration? configuration = null, Beutl.Services.INotificationService? notifications = null) : IEditorContext
{
    private readonly Dictionary<Type, object> _services = new() { [typeof(DecoderRegistry)] = new DecoderRegistry(configuration?.ExtensionConfig ?? new()), [typeof(GlobalConfiguration)] = configuration ?? new GlobalConfiguration(), [typeof(Beutl.Services.INotificationService)] = notifications ?? new Beutl.Services.NotificationService() };

    public static Moq.Mock<IEditorContext> CreateMock(Beutl.Services.INotificationService? notifications = null)
    {
        var context = new Moq.Mock<IEditorContext>();
        context.Setup(x => x.GetService(typeof(DecoderRegistry))).Returns(new DecoderRegistry(new()));
        context.Setup(x => x.GetService(typeof(Beutl.Services.INotificationService)))
            .Returns(notifications ?? new Beutl.Services.NotificationService());
        return context;
    }

    public CoreObject Object { get; } = obj;

    public EditorExtension Extension => null!;

    public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

    public void AddService<T>(T service)
        where T : notnull
    {
        _services[typeof(T)] = service;
    }

    public object? GetService(Type serviceType)
    {
        return _services.GetValueOrDefault(serviceType)
            ?? (serviceType == typeof(Beutl.Media.FontManager) ? TestFonts.Manager : null);
    }

    public T? FindToolTab<T>(Func<T, bool> condition)
        where T : IToolContext
    {
        return default;
    }

    public T? FindToolTab<T>()
        where T : IToolContext
    {
        return default;
    }

    public bool OpenToolTab(IToolContext item)
    {
        return false;
    }

    public void CloseToolTab(IToolContext item)
    {
    }
}
