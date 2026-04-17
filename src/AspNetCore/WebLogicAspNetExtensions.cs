using System.Net;
using CL.WebLogic;
using CL.WebLogic.AspNetCore;
using CL.WebLogic.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
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
            if (config?.Security.TrustForwardedHeaders == true)
            {
                var forwardedHeadersOptions = new ForwardedHeadersOptions
                {
                    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
                    ForwardLimit = 2
                };

                forwardedHeadersOptions.KnownIPNetworks.Clear();
                forwardedHeadersOptions.KnownProxies.Clear();

                var proxies = config.Security.TrustedProxies ?? [];
                var networks = config.Security.TrustedNetworks ?? [];
                var hasExplicit = proxies.Length > 0 || networks.Length > 0;

                foreach (var proxyIp in proxies)
                {
                    if (IPAddress.TryParse(proxyIp.Trim(), out var ip))
                        forwardedHeadersOptions.KnownProxies.Add(ip);
                }

                foreach (var cidr in networks)
                {
                    if (TryParseIPNetwork(cidr.Trim(), out var network))
                        forwardedHeadersOptions.KnownIPNetworks.Add(network);
                }

                if (!hasExplicit)
                {
                    forwardedHeadersOptions.KnownProxies.Add(IPAddress.Loopback);
                    forwardedHeadersOptions.KnownProxies.Add(IPAddress.IPv6Loopback);
                    forwardedHeadersOptions.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
                    forwardedHeadersOptions.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
                    forwardedHeadersOptions.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("192.168.0.0"), 16));

                    Console.Error.WriteLine(
                        "[CL.WebLogic] WARNING: TrustForwardedHeaders is enabled but no TrustedProxies or TrustedNetworks are configured. " +
                        "Falling back to loopback + RFC1918 private ranges. " +
                        "Set security.trustedProxies or security.trustedNetworks in config to your actual proxy IPs/CIDRs for production.");
                }

                app.UseForwardedHeaders(forwardedHeadersOptions);
            }

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

        private static bool TryParseIPNetwork(string cidr, out System.Net.IPNetwork network)
        {
            network = new System.Net.IPNetwork(IPAddress.None, 0);
            if (string.IsNullOrWhiteSpace(cidr)) return false;
            var slash = cidr.IndexOf('/');
            if (slash < 0) return false;
            if (!IPAddress.TryParse(cidr[..slash], out var addr)) return false;
            if (!int.TryParse(cidr[(slash + 1)..], out var prefix)) return false;
            if (prefix < 0 || prefix > (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32))
                return false;
            network = new System.Net.IPNetwork(addr, prefix);
            return true;
        }
    }
}
