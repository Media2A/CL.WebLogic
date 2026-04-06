using CL.WebLogic;
using CL.WebLogic.AspNetCore;
using CL.WebLogic.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class WebLogicServiceCollectionExtensions
    {
        public static IServiceCollection AddCodeLogicWebLogic(this IServiceCollection services)
        {
            services.AddHttpContextAccessor();
            services.AddSignalR();
            services.AddSingleton<IWebLogicRealtimeBroadcaster, SignalRWebLogicRealtimeBroadcaster>();
            return services;
        }
    }
}

namespace Microsoft.AspNetCore.Builder
{
    public static class WebLogicApplicationBuilderExtensions
    {
        public static WebApplication UseCodeLogicWebLogic(this WebApplication app)
        {
            var webLogic = WebLogicLibrary.GetRequired();
            if (webLogic.Runtime is null)
                throw new InvalidOperationException("CL.WebLogic runtime is not available.");

            var broadcaster = app.Services.GetRequiredService<IWebLogicRealtimeBroadcaster>();
            webLogic.RealtimeBridge?.AttachBroadcaster(broadcaster);

            app.MapHub<WebLogicRealtimeHub>("/weblogic-hubs/events");
            app.Map("/{**path}", async context =>
            {
                await webLogic.Runtime.HandleRequestAsync(context).ConfigureAwait(false);
            });

            return app;
        }
    }
}
