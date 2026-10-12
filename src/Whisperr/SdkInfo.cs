using System.Reflection;

namespace Whisperr;

/// <summary>
/// The SDK identity that every event carries in its context.
/// </summary>
internal static class SdkInfo
{
    public const string Name = "whisperr-dotnet";

    public static readonly string Version = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = typeof(SdkInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // SourceLink appends "+<commit sha>". The package version is the part before it.
            var plus = informational!.IndexOf('+');
            return plus >= 0 ? informational.Substring(0, plus) : informational;
        }

        var version = assembly.GetName().Version;
        return version == null ? "0.0.0" : version.ToString(3);
    }
}
