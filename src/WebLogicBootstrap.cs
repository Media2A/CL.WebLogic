using CL.Common;
using CL.GitHelper;
using CL.NetUtils;
using CL.Storage;
using CodeLogic;

namespace CL.WebLogic;

public static class WebLogicBootstrap
{
    public static Task LoadRecommendedLibrariesAsync(bool includeOptionalInfrastructure = false) =>
        LoadRecommendedLibrariesAsync(new WebLogicBootstrapOptions
        {
            IncludeCommon = includeOptionalInfrastructure,
            IncludeGitHelper = includeOptionalInfrastructure,
            IncludeStorage = includeOptionalInfrastructure,
            IncludeNetUtils = includeOptionalInfrastructure
        });

    public static async Task LoadRecommendedLibrariesAsync(WebLogicBootstrapOptions options)
    {
        if (options.IncludeCommon)
            await Libraries.LoadAsync<CommonLibrary>().ConfigureAwait(false);

        if (options.IncludeGitHelper)
            await Libraries.LoadAsync<GitHelperLibrary>().ConfigureAwait(false);

        if (options.IncludeStorage)
            await Libraries.LoadAsync<StorageLibrary>().ConfigureAwait(false);

        if (options.IncludeNetUtils)
            await Libraries.LoadAsync<NetUtilsLibrary>().ConfigureAwait(false);

        await Libraries.LoadAsync<WebLogicLibrary>().ConfigureAwait(false);
    }
}

public sealed class WebLogicBootstrapOptions
{
    public bool IncludeCommon { get; set; }
    public bool IncludeGitHelper { get; set; }
    public bool IncludeStorage { get; set; }
    public bool IncludeNetUtils { get; set; }
}
