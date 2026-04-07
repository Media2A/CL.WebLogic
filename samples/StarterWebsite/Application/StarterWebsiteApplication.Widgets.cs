using CL.WebLogic.Routing;
using CL.WebLogic.Theming;
using System.Text.Json;

namespace StarterWebsite.Application;

public sealed partial class StarterWebsiteApplication
{
    private void RegisterWidgetsAndAreas(WebRegistrationContext context)
    {
        context.RegisterWidget("starter.quick-links", new WebWidgetOptions
        {
            Description = "Renders a themed grid of starter demo links.",
            Tags = ["starter", "widget", "links"],
            SampleParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Jump around the starter"
            }
        }, _ => Task.FromResult(WebWidgetResult.Template(
            "widgets/quick-links.html",
            new Dictionary<string, object?>
            {
                ["title"] = _.GetParameter("title", "Jump around the starter") ?? "Jump around the starter",
                ["links"] = BuildQuickLinks()
            })));

        context.RegisterWidget("starter.request-glance", new WebWidgetOptions
        {
            Description = "Compact card showing the current request and RBAC identity.",
            Tags = ["starter", "widget", "context"],
            SampleParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Current request"
            }
        }, widgetContext => Task.FromResult(WebWidgetResult.Template(
            "widgets/request-glance.html",
            new Dictionary<string, object?>
            {
                ["title"] = widgetContext.GetParameter("title", "Current request") ?? "Current request",
                ["user"] = string.IsNullOrWhiteSpace(widgetContext.Request.UserId) ? "anonymous" : widgetContext.Request.UserId,
                ["path"] = widgetContext.Request.Path,
                ["method"] = widgetContext.Request.Method,
                ["groups"] = widgetContext.Request.AccessGroups.Count == 0 ? "(none)" : string.Join(", ", widgetContext.Request.AccessGroups)
            })));

        context.RegisterWidget("starter.counter-panel", new WebWidgetOptions
        {
            Description = "Interactive counter widget backed by persisted widget instance settings.",
            Tags = ["starter", "widget", "actions", "data"],
            SampleParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Counter panel",
                ["count"] = "0"
            },
            DataHandler = async widgetContext =>
            {
                var settings = await widgetContext.GetInstanceSettingsAsync().ConfigureAwait(false);
                var title = widgetContext.GetParameter("title", "Counter panel") ?? "Counter panel";
                var countText = widgetContext.GetParameter("count", "0") ?? "0";
                _ = int.TryParse(countText, out var count);

                return new
                {
                    widget = widgetContext.Name,
                    widgetContext.InstanceId,
                    title,
                    count,
                    updatedUtc = settings?.UpdatedUtc
                };
            },
            ActionHandler = async actionContext =>
            {
                var title = actionContext.Widget.GetParameter("title", "Counter panel") ?? "Counter panel";
                var countText = actionContext.Widget.GetParameter("count", "0") ?? "0";
                _ = int.TryParse(countText, out var count);

                switch (actionContext.ActionName)
                {
                    case "increment":
                        count++;
                        break;
                    case "reset":
                        count = 0;
                        break;
                    default:
                        return WebWidgetActionResult.Ok(new { message = "Unknown action ignored." });
                }

                await actionContext.SaveSettingsAsync(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["title"] = title,
                    ["count"] = count.ToString()
                }).ConfigureAwait(false);

                var activityInstanceId = ResolveRelatedActivityInstanceId(actionContext.Widget.InstanceId);
                var activityEntries = await LoadActivityEntriesAsync(actionContext, activityInstanceId).ConfigureAwait(false);
                activityEntries.Insert(0, new DashboardActivityEntry(
                    $"starter.counter.{actionContext.ActionName}",
                    actionContext.ActionName.Equals("increment", StringComparison.OrdinalIgnoreCase) ? "Counter incremented" : "Counter reset",
                    $"The dashboard counter is now {count}.",
                    DateTimeOffset.UtcNow.ToString("u")));

                if (activityEntries.Count > 6)
                    activityEntries.RemoveRange(6, activityEntries.Count - 6);

                await actionContext.SaveSettingsAsync(
                    activityInstanceId,
                    "starter.activity-stream",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["title"] = actionContext.Widget.InstanceId?.Contains(".starter-main.", StringComparison.OrdinalIgnoreCase) == true
                            ? $"Activity for {actionContext.Widget.Request.UserId}"
                            : "Dashboard activity",
                        ["entries_json"] = JsonSerializer.Serialize(activityEntries)
                    }).ConfigureAwait(false);

                return WebWidgetActionResult.Ok(
                    new
                    {
                        title,
                        count
                    },
                    refresh: new WebWidgetRefreshPlan
                    {
                        WidgetInstances = [actionContext.Widget.InstanceId ?? string.Empty, activityInstanceId],
                        WidgetAreas = ["dashboard.main", "dashboard.sidebar"],
                        RefreshWidgetAreas = true
                    },
                    messages:
                    [
                        new WebWidgetClientMessage
                        {
                            Level = WebWidgetClientMessageLevel.Success,
                            Title = "Counter updated",
                            Detail = $"The dashboard counter is now {count}."
                        }
                    ],
                    events:
                    [
                        new WebWidgetClientEvent
                        {
                            Channel = "dashboard.counter.updated",
                            Name = actionContext.ActionName,
                            Payload = new
                            {
                                count,
                                widget = actionContext.Widget.Name,
                                instanceId = actionContext.Widget.InstanceId
                            }
                        }
                    ]);
            },
            Actions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["increment"] = "Increase the counter by one.",
                ["reset"] = "Reset the counter to zero."
            }
        }, async widgetContext =>
        {
            var settings = await widgetContext.GetInstanceSettingsAsync().ConfigureAwait(false);
            var title = widgetContext.GetParameter("title", "Counter panel") ?? "Counter panel";
            var countText = widgetContext.GetParameter("count", "0") ?? "0";
            _ = int.TryParse(countText, out var count);

            return WebWidgetResult.Template(
                "widgets/counter-panel.html",
                new Dictionary<string, object?>
                {
                    ["title"] = title,
                    ["count"] = count,
                    ["instance_id"] = widgetContext.InstanceId ?? string.Empty,
                    ["widget_name"] = widgetContext.Name,
                    ["updated_utc"] = settings?.UpdatedUtc.ToString("u") ?? "not saved yet"
                });
        });

        context.RegisterWidget("starter.activity-stream", new WebWidgetOptions
        {
            Description = "Shows widget action activity so dashboard widgets can react to each other through saved state and refresh channels.",
            Tags = ["starter", "widget", "activity", "messaging"],
            SampleParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Activity stream"
            },
            DataHandler = async widgetContext =>
            {
                var settings = await widgetContext.GetInstanceSettingsAsync().ConfigureAwait(false);
                return new
                {
                    widget = widgetContext.Name,
                    widgetContext.InstanceId,
                    title = widgetContext.GetParameter("title", "Activity stream") ?? "Activity stream",
                    entries = ParseActivityEntries(settings?.Settings.GetValueOrDefault("entries_json"))
                };
            }
        }, async widgetContext =>
        {
            var settings = await widgetContext.GetInstanceSettingsAsync().ConfigureAwait(false);
            return WebWidgetResult.Template(
                "widgets/activity-stream.html",
                new Dictionary<string, object?>
                {
                    ["title"] = widgetContext.GetParameter("title", "Activity stream") ?? "Activity stream",
                    ["entries"] = ParseActivityEntries(settings?.Settings.GetValueOrDefault("entries_json")),
                    ["instance_id"] = widgetContext.InstanceId ?? string.Empty,
                    ["widget_name"] = widgetContext.Name
                });
        });

        context.RegisterWidgetArea("dashboard.main", "starter.quick-links", new WebWidgetAreaOptions
        {
            Description = "Starter quick links in the main dashboard area.",
            Order = 10,
            InstanceId = "dashboard.main.quicklinks",
            IncludeRoutePatterns = ["/dashboard", "/"],
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Starter navigation"
            }
        });

        context.RegisterWidgetArea("dashboard.main", "starter.counter-panel", new WebWidgetAreaOptions
        {
            Description = "Interactive counter widget in the dashboard main area.",
            Order = 20,
            InstanceId = "dashboard.main.counter",
            IncludeRoutePatterns = ["/dashboard"],
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Dashboard counter",
                ["count"] = "1"
            }
        });

        context.RegisterWidgetArea("dashboard.sidebar", "starter.request-glance", new WebWidgetAreaOptions
        {
            Description = "Starter request card in the sidebar dashboard area.",
            Order = 10,
            InstanceId = "dashboard.sidebar.request",
            IncludeRoutePatterns = ["/dashboard", "/about", "/template-lab"],
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Sidebar request view"
            }
        });

        context.RegisterWidgetArea("dashboard.sidebar", "starter.activity-stream", new WebWidgetAreaOptions
        {
            Description = "Dashboard activity widget showing widget-to-widget refresh flow.",
            Order = 20,
            InstanceId = "dashboard.sidebar.activity",
            IncludeRoutePatterns = ["/dashboard"],
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Dashboard activity"
            }
        });

        context.RegisterWidgetArea("site.sidebar", "starter.request-glance", new WebWidgetAreaOptions
        {
            Description = "Shared sidebar request widget for selected pages.",
            Order = 10,
            InstanceId = "site.sidebar.request",
            IncludeRoutePatterns = ["/about", "/template-lab", "/dashboard"],
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Page sidebar"
            }
        });

        context.RegisterWidgetArea("member.sidebar", "starter.quick-links", new WebWidgetAreaOptions
        {
            Description = "Authenticated quick-links sidebar for member areas.",
            Order = 20,
            InstanceId = "member.sidebar.quicklinks",
            IncludeRoutePatterns = ["/rbac", "/rbac/*"],
            AllowAnonymous = false,
            RequiredAccessGroups = ["member", "editor", "staff", "admin", "beta"],
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Member navigation"
            }
        });
    }
}
