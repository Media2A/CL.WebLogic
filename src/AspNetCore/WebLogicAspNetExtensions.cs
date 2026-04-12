using CL.WebLogic;
using CL.WebLogic.AspNetCore;
using CL.WebLogic.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.ResponseCompression;
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

            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat([
                    "application/javascript",
                    "application/json",
                    "text/css",
                    "text/html",
                    "text/plain",
                    "text/xml",
                    "image/svg+xml"
                ]);
            });

            services.Configure<BrotliCompressionProviderOptions>(options =>
            {
                options.Level = System.IO.Compression.CompressionLevel.Fastest;
            });

            services.Configure<GzipCompressionProviderOptions>(options =>
            {
                options.Level = System.IO.Compression.CompressionLevel.Fastest;
            });

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

            var config = webLogic.GetConfig();
            if (config?.Security.EnableCompression != false)
            {
                app.UseResponseCompression();
            }

            app.MapHub<WebLogicRealtimeHub>("/weblogic-hubs/events");
            app.Map("/{**path}", async context =>
            {
                await webLogic.Runtime.HandleRequestAsync(context).ConfigureAwait(false);
            });

            return app;
        }
    }
}
