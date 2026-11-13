using System.IO;

namespace WindowsMetrics.Services;

/// <summary>Reads the application icon embedded in the assembly as a manifest resource.</summary>
public static class AppIconResource
{
    /// <summary>Manifest resource name; must match the LogicalName in WindowsMetrics.csproj.</summary>
    public const string ResourceName = "WindowsMetrics.Assets.app.ico";

    /// <summary>Opens the embedded app.ico, or returns null if the resource is missing.</summary>
    public static Stream? OpenStream() =>
        typeof(AppIconResource).Assembly.GetManifestResourceStream(ResourceName);
}
