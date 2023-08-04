namespace Beutl.Extensibility.Services;

public interface INotificationService
{
    public void Show(Notification notification);
}

public enum NotificationType
{
    Information = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

public record struct Notification(
    string Title,
    string Message,
    NotificationType Type = NotificationType.Information,
    TimeSpan? Expiration = null,
    Action? OnClick = null,
    Action? OnClose = null);
