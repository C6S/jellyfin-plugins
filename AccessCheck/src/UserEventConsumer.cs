using Jellyfin.Data.Events.Users;
using MediaBrowser.Controller.Events;

namespace Jellyfin.Plugin.AccessCheck;

public class UserEventConsumer :
    IEventConsumer<UserUpdatedEventArgs>,
    IEventConsumer<UserPasswordChangedEventArgs>,
    IEventConsumer<UserDeletedEventArgs>,
    IEventConsumer<UserLockedOutEventArgs>
{
    public Task OnEvent(UserUpdatedEventArgs eventArgs)
    {
        Plugin.Instance?.AuthCache.Invalidate(eventArgs.Argument.Username);
        return Task.CompletedTask;
    }

    public Task OnEvent(UserPasswordChangedEventArgs eventArgs)
    {
        Plugin.Instance?.AuthCache.Invalidate(eventArgs.Argument.Username);
        return Task.CompletedTask;
    }

    public Task OnEvent(UserDeletedEventArgs eventArgs)
    {
        Plugin.Instance?.AuthCache.Invalidate(eventArgs.Argument.Username);
        return Task.CompletedTask;
    }

    // Lockout disables the account without a UserUpdated event.
    public Task OnEvent(UserLockedOutEventArgs eventArgs)
    {
        Plugin.Instance?.AuthCache.Invalidate(eventArgs.Argument.Username);
        return Task.CompletedTask;
    }
}
