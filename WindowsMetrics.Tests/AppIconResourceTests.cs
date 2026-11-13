using System.IO;
using WindowsMetrics.Services;

namespace WindowsMetrics.Tests;

public class AppIconResourceTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WindowsMetrics.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate repository root (WindowsMetrics.slnx) from the test output directory.");
        }

        return dir.FullName;
    }

    // The embedded icon resource must be readable from the assembly.
    [Fact]
    public void OpenStream_ReturnsEmbeddedIcon()
    {
        using var stream = AppIconResource.OpenStream();

        Assert.NotNull(stream);
    }

    // The embedded bytes must be identical to the icon file kept in the repository.
    [Fact]
    public void EmbeddedIcon_MatchesAssetFile()
    {
        using var stream = AppIconResource.OpenStream();
        Assert.NotNull(stream);

        using var embedded = new MemoryStream();
        stream.CopyTo(embedded);

        var expected = File.ReadAllBytes(Path.Combine(FindRepoRoot(), "WindowsMetrics", "Assets", "app.ico"));

        Assert.Equal(expected, embedded.ToArray());
    }

    // The resource name constant must match an entry in the assembly manifest.
    [Fact]
    public void ResourceName_IsListedInAssemblyManifest()
    {
        Assert.Contains(AppIconResource.ResourceName, typeof(AppIconResource).Assembly.GetManifestResourceNames());
    }
}
