using Plutonium.Testing;
using Xunit;

namespace Plutonium.Packaging.Tests;

/// <summary>
/// Packs the solution once and stages the offline dependency closure used by
/// every package assertion and consumer run.
/// </summary>
public sealed class PackagingFixture : IAsyncLifetime
{
    /// <summary>Gets the repository layout.</summary>
    public RepositoryLayout Layout { get; private set; } = null!;

    /// <summary>Gets the resolved .NET CLI.</summary>
    public DotNetCli DotNet { get; private set; } = null!;

    /// <summary>Gets the workspace that owns the feed and consumer outputs.</summary>
    public TempWorkspace Workspace { get; private set; } = null!;

    /// <summary>Gets the package version pinned in <c>src/Directory.Build.props</c>.</summary>
    public string Version { get; private set; } = string.Empty;

    /// <summary>Gets the pack result.</summary>
    public LocalFeed Feed { get; private set; } = null!;

    /// <summary>Gets the dependency-closure staging result.</summary>
    public FeedStagingResult Staging { get; private set; } = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        Layout = RepositoryLayout.Locate();
        DotNet = DotNetCli.Locate(Layout);
        Version = Layout.ReadBuilderVersion();
        Assert.False(
            string.IsNullOrWhiteSpace(Version),
            "src/Directory.Build.props must declare the package Version.");
        Workspace = TempWorkspace.Create("packaging");

        var builder = new LocalFeedBuilder(Layout, DotNet);
        Feed = await builder.PackAsync(Workspace, Version);
        Staging = Feed.Succeeded
            ? builder.StagePackageClosure(Feed, "ApplicationBuilderHelpers", "4.1.160")
            : new FeedStagingResult([], "Packing failed; the dependency closure was not staged.");
    }

    /// <inheritdoc/>
    public Task DisposeAsync()
    {
        Workspace?.Dispose();
        return Task.CompletedTask;
    }
}
