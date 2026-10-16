namespace OpenWrtStudio.Tests;

internal static class TestPaths
{
    public static string RepositoryRoot
    {
        get
        {
            var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "OpenWrtStudio.csproj")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new System.IO.DirectoryNotFoundException("Cannot find OpenWrtStudio.csproj above the test output.");
        }
    }
}

internal sealed class LiveRouterFactAttribute : Xunit.FactAttribute
{
    public LiveRouterFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENWRT_TEST_HOST")) ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENWRT_TEST_PASSWORD")))
            Skip = "Set OPENWRT_TEST_HOST and OPENWRT_TEST_PASSWORD to opt into live router tests.";
    }
}

internal sealed class LiveGitHubFactAttribute : Xunit.FactAttribute
{
    public LiveGitHubFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("OPENWRT_TEST_GITHUB") != "1")
            Skip = "Set OPENWRT_TEST_GITHUB=1 to opt into live GitHub tests.";
    }
}
