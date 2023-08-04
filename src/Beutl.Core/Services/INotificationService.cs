namespace Beutl.Services;

public interface INotificationService
{
    void Show(Notification notification);
}

public enum NotificationType
{
    Information = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

public record Notification(
    string Title,
    string Message,
    NotificationType Type = NotificationType.Information,
    TimeSpan? Expiration = null,
    Action? OnClose = null,
    Action? OnActionButtonClick = null,
    string? ActionButtonText = null);
