using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using WindowsMetrics.Services;

namespace WindowsMetrics.Tests;

public class PublishConfigurationTests
{
    private static readonly XDocument ProjectXml = XDocument.Load(FindCsprojPath());

    private static string FindCsprojPath() =>
        Path.Combine(FindRepoRoot(), "WindowsMetrics", "WindowsMetrics.csproj");

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

    private static string? GetPropertyValue(string propertyName) =>
        ProjectXml.Descendants(propertyName).FirstOrDefault()?.Value;

    private static XElement? FindItem(string itemType, string includePath) =>
        ProjectXml.Descendants(itemType)
            .FirstOrDefault(e => (string?)e.Attribute("Include") == includePath);

    [Theory]
    [InlineData("PublishRuntimeIdentifier", "win-x64")]
    [InlineData("PublishSelfContained", "true")]
    [InlineData("PublishSingleFile", "true")]
    [InlineData("EnableCompressionInSingleFile", "true")]
    [InlineData("IncludeNativeLibrariesForSelfExtract", "true")]
    [InlineData("IncludeAllContentForSelfExtract", "true")]
    [InlineData("PublishTrimmed", "false")]
    [InlineData("PublishReadyToRun", "true")]
    [InlineData("CopyOutputSymbolsToPublishDirectory", "false")]
    public void PublishOnlyProperty_HasExpectedValue(string propertyName, string expectedValue)
    {
        Assert.Equal(expectedValue, GetPropertyValue(propertyName));
    }

    [Fact]
    public void BareRuntimeIdentifier_IsNotSet()
    {
        // A bare RuntimeIdentifier affects `dotnet build` too, not just `dotnet publish`.
        Assert.Null(GetPropertyValue("RuntimeIdentifier"));
    }

    [Fact]
    public void BareSelfContained_IsNotSet()
    {
        // A bare SelfContained affects `dotnet build` too, not just `dotnet publish`.
        Assert.Null(GetPropertyValue("SelfContained"));
    }

    [Theory]
    [InlineData(@"Assets\app.ico")]
    [InlineData(@"Assets\app.png")]
    public void AssetFiles_AreNotPublishedAsLooseContent(string includePath)
    {
        // Loose Content files would be published next to the exe and break the single-exe output.
        Assert.Null(FindItem("Content", includePath));
    }

    [Fact]
    public void AppIcon_IsEmbeddedWithExpectedLogicalName()
    {
        // The icon is read at runtime by LogicalName, so the csproj and AppIconResource must agree.
        var item = FindItem("EmbeddedResource", @"Assets\app.ico");
        Assert.NotNull(item);
        Assert.Equal(AppIconResource.ResourceName, (string?)item.Attribute("LogicalName"));
    }

    [Theory]
    [InlineData("WindowsMetrics/WindowsMetrics.csproj")]
    [InlineData("WindowsMetrics.Tests/WindowsMetrics.Tests.csproj")]
    public void TargetFramework_IsNet11Windows(string relativeCsprojPath)
    {
        // Both the app and its test project must target .NET 11 on Windows.
        var doc = XDocument.Load(Path.Combine(FindRepoRoot(), relativeCsprojPath));
        Assert.Equal("net11.0-windows", doc.Descendants("TargetFramework").FirstOrDefault()?.Value);
    }

    [Theory]
    [InlineData("System.Management")]
    [InlineData("Microsoft.Extensions.Hosting")]
    [InlineData("Microsoft.Extensions.Http")]
    public void RuntimeCoupledPackage_TargetsDotNet11(string packageId)
    {
        // Runtime-coupled packages must be on the 11.x line to match the .NET 11 runtime.
        var reference = ProjectXml.Descendants("PackageReference")
            .FirstOrDefault(e => (string?)e.Attribute("Include") == packageId);
        Assert.NotNull(reference);
        var version = (string?)reference.Attribute("Version");
        Assert.NotNull(version);
        Assert.StartsWith("11.", version);
    }

    [Fact]
    public void GlobalJson_PinsSdkMajor11()
    {
        // global.json pins the SDK to the 11.x feature band without allowing prereleases.
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepoRoot(), "global.json")));
        var sdk = json.RootElement.GetProperty("sdk");
        Assert.StartsWith("11.", sdk.GetProperty("version").GetString());
        Assert.Equal("latestFeature", sdk.GetProperty("rollForward").GetString());
        Assert.False(sdk.GetProperty("allowPrerelease").GetBoolean());
    }
}
