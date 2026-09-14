using System.IO;
using System.Xml.Linq;

namespace WindowsMetrics.Tests;

public class PublishConfigurationTests
{
    private static readonly XDocument ProjectXml = XDocument.Load(FindCsprojPath());

    private static string FindCsprojPath()
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

        return Path.Combine(dir.FullName, "WindowsMetrics", "WindowsMetrics.csproj");
    }

    private static string? GetPropertyValue(string propertyName) =>
        ProjectXml.Descendants(propertyName).FirstOrDefault()?.Value;

    private static string? GetContentMetadata(string includePath, string metadataName) =>
        ProjectXml.Descendants("Content")
            .FirstOrDefault(e => (string?)e.Attribute("Include") == includePath)
            ?.Element(metadataName)?.Value;

    [Theory]
    [InlineData("PublishRuntimeIdentifier", "win-x64")]
    [InlineData("PublishSelfContained", "true")]
    [InlineData("PublishSingleFile", "true")]
    [InlineData("EnableCompressionInSingleFile", "true")]
    [InlineData("IncludeNativeLibrariesForSelfExtract", "true")]
    [InlineData("PublishTrimmed", "false")]
    [InlineData("PublishReadyToRun", "true")]
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
    public void AssetContent_IsExcludedFromSingleFileBundle(string includePath)
    {
        // Without ExcludeFromSingleFile, the single-file bundler silently absorbs
        // Assets\app.ico instead of copying it next to the exe (verified via
        // `dotnet publish -v:diag`), which breaks TrayIconService's
        // AppContext.BaseDirectory-relative File.Exists lookup at runtime.
        Assert.Equal("true", GetContentMetadata(includePath, "ExcludeFromSingleFile"));
    }
}
