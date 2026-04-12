using CodeLogic.Core.Configuration;

namespace MiniBlog.Admin.Plugin.Config;

public sealed class AdminPluginConfig : ConfigModelBase
{
    public string DashboardTitle { get; set; } = "Editorial control room";
    public string EditorHeadline { get; set; } = "Shape the next story";
}
