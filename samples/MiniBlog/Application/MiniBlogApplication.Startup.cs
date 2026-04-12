using CL.MySQL2;
using CL.WebLogic;
using CL.WebLogic.Routing;
using CL.WebLogic.Security;
using CodeLogic;
using CodeLogic.Framework.Application;
using CodeLogic.Framework.Libraries;
using MiniBlog.Config;
using MiniBlog.Shared;
using MiniBlog.Shared.Infrastructure;
using MiniBlog.Shared.Services;

namespace MiniBlog;

public sealed partial class MiniBlogApplication
{
    public Task OnConfigureAsync(ApplicationContext context)
    {
        context.Configuration.Register<MiniBlogConfig>();
        return Task.CompletedTask;
    }

    public Task OnInitializeAsync(ApplicationContext context)
    {
        _config = context.Configuration.Get<MiniBlogConfig>();
        context.Logger.Info($"MiniBlog initialized for '{_config.SiteTitle}'");
        return Task.CompletedTask;
    }

    public async Task OnStartAsync(ApplicationContext context)
    {
        var web = WebLogicLibrary.GetRequired();
        if (web.IdentityStore is MiniBlogMySqlIdentityStore appIdentityStore)
            await appIdentityStore.InitializeAsync().ConfigureAwait(false);

        await web.RegisterContributorAsync(new WebContributorDescriptor
        {
            Id = Manifest.Id,
            Name = Manifest.Name,
            Kind = "Application",
            Description = Manifest.Description ?? string.Empty
        }, this).ConfigureAwait(false);

        if (Libraries.Get<MySQL2Library>() is null)
        {
            context.Logger.Warning("MiniBlog started without CL.MySQL2. The sample will fall back to seeded in-memory content.");
            return;
        }

        if (web.IdentityStore is MiniBlogMySqlIdentityStore identityStore)
        {
            await identityStore.SeedUsersAsync(
                MiniBlogDemoData.Users.Select(user => new WebIdentitySeed
                {
                    UserId = user.UserId,
                    DisplayName = user.DisplayName,
                    Email = user.Email,
                    Password = user.Password,
                    AccessGroups = user.AccessGroups,
                    IsActive = true
                })).ConfigureAwait(false);
        }

        var data = new MiniBlogDataService(_config.ConnectionId);
        await data.InitializeAsync().ConfigureAwait(false);
        await data.SeedPostsAsync(MiniBlogDemoData.Posts).ConfigureAwait(false);
    }

    public Task OnStopAsync() => Task.CompletedTask;
}
