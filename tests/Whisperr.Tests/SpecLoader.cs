using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Whisperr.Tests;

internal static class SpecLoader
{
    private const string WireSpecUrl = "https://raw.githubusercontent.com/WhisperrAI/whisperr-spec/main/conformance/wire.json";
    private const string BehaviorSpecUrl = "https://raw.githubusercontent.com/WhisperrAI/whisperr-spec/main/conformance/behavior.json";

    public static Task<string> LoadWireSpecAsync()
    {
        var explicitPath = Environment.GetEnvironmentVariable("WHISPERR_SPEC_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return File.ReadAllTextAsync(explicitPath);
        }

        var siblingPath = FindSiblingSpec("wire.json");
        if (siblingPath != null)
        {
            return File.ReadAllTextAsync(siblingPath);
        }

        return FetchAsync(WireSpecUrl);
    }

    public static Task<string> LoadBehaviorSpecAsync()
    {
        var explicitPath = Environment.GetEnvironmentVariable("WHISPERR_BEHAVIOR_SPEC_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return File.ReadAllTextAsync(explicitPath);
        }

        var wirePath = Environment.GetEnvironmentVariable("WHISPERR_SPEC_PATH");
        if (!string.IsNullOrWhiteSpace(wirePath))
        {
            var behaviorPath = Path.Combine(Path.GetDirectoryName(wirePath) ?? string.Empty, "behavior.json");
            if (File.Exists(behaviorPath))
            {
                return File.ReadAllTextAsync(behaviorPath);
            }
        }

        var siblingPath = FindSiblingSpec("behavior.json");
        if (siblingPath != null)
        {
            return File.ReadAllTextAsync(siblingPath);
        }

        return FetchAsync(BehaviorSpecUrl);
    }

    private static string? FindSiblingSpec(string fileName)
    {
        var root = FindRepositoryRoot();
        if (root == null)
        {
            return null;
        }

        var candidate = Path.GetFullPath(Path.Combine(root, "..", "whisperr-spec", "conformance", fileName));
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Whisperr.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static async Task<string> FetchAsync(string url)
    {
        using var http = new HttpClient();
        return await http.GetStringAsync(url).ConfigureAwait(false);
    }
}
