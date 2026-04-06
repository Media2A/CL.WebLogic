using CL.Common;
using CL.GitHelper;
using CL.MySQL2;
using CL.NetUtils;
using CL.StorageS3;
using CodeLogic;

namespace CL.WebLogic;

public static class WebLogicBootstrap
{
    public static Task LoadRecommendedLibrariesAsync(bool includeOptionalInfrastructure = false) =>
        LoadRecommendedLibrariesAsync(new WebLogicBootstrapOptions
        {
            IncludeCommon = includeOptionalInfrastructure,
            IncludeGitHelper = includeOptionalInfrastructure,
            IncludeStorageS3 = includeOptionalInfrastructure,
            IncludeMySql = includeOptionalInfrastructure,
            IncludeNetUtils = includeOptionalInfrastructure
        });

    public static async Task LoadRecommendedLibrariesAsync(WebLogicBootstrapOptions options)
    {
        if (options.IncludeCommon)
            await Libraries.LoadAsync<CommonLibrary>().ConfigureAwait(false);

        if (options.IncludeGitHelper)
            await Libraries.LoadAsync<GitHelperLibrary>().ConfigureAwait(false);

        if (options.IncludeStorageS3)
            await Libraries.LoadAsync<StorageS3Library>().ConfigureAwait(false);

        if (options.IncludeMySql)
            await Libraries.LoadAsync<MySQL2Library>().ConfigureAwait(false);

        if (options.IncludeNetUtils)
            await Libraries.LoadAsync<NetUtilsLibrary>().ConfigureAwait(false);

        await Libraries.LoadAsync<WebLogicLibrary>().ConfigureAwait(false);
    }
}

public sealed class WebLogicBootstrapOptions
{
    public bool IncludeCommon { get; set; }
    public bool IncludeGitHelper { get; set; }
    public bool IncludeStorageS3 { get; set; }
    public bool IncludeMySql { get; set; }
    public bool IncludeNetUtils { get; set; }
}
