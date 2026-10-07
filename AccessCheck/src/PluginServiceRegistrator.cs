using Jellyfin.Data.Events.Users;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.AccessCheck;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(
                BasicAuthenticationHandler.SchemeName, null);

        serviceCollection.AddAuthorization(options =>
            options.AddPolicy(BasicAuthenticationHandler.SchemeName, policy =>
                policy.AddAuthenticationSchemes(BasicAuthenticationHandler.SchemeName)
                      .RequireAuthenticatedUser()));

        serviceCollection.AddScoped<IEventConsumer<UserUpdatedEventArgs>, UserEventConsumer>();
        serviceCollection.AddScoped<IEventConsumer<UserPasswordChangedEventArgs>, UserEventConsumer>();
        serviceCollection.AddScoped<IEventConsumer<UserDeletedEventArgs>, UserEventConsumer>();
        serviceCollection.AddScoped<IEventConsumer<UserLockedOutEventArgs>, UserEventConsumer>();
    }
}
