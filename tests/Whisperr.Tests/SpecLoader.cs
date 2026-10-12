using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Whisperr.Tests;

internal static class SpecLoader
{
    private const string WireSpecUrl = "https://raw.githubusercontent.com/WhisperrAI/whisperr-spec/main/conformance/wire.json";
    private const string BehaviorSpecUrl = "https://raw.githubusercontent.com/WhisperrAI/whisperr-spec/main/conformance/behavior.json";
    private const string V2SpecUrl = "https://raw.githubusercontent.com/WhisperrAI/whisperr-spec/main/conformance/v2.json";

    public static Task<string> LoadWireSpecAsync()
    {
        var explicitPath = Environment.GetEnvironmentVariable("WHISPERR_SPEC_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return ReadAllTextAsync(explicitPath);
        }

        var siblingPath = FindSiblingSpec("wire.json");
        if (siblingPath != null)
        {
            return ReadAllTextAsync(siblingPath);
        }

        return FetchAsync(WireSpecUrl);
    }

    public static Task<string> LoadBehaviorSpecAsync()
    {
        return LoadSiblingSpecAsync("WHISPERR_BEHAVIOR_SPEC_PATH", "behavior.json", BehaviorSpecUrl);
    }

    public static Task<string> LoadV2SpecAsync()
    {
        return LoadSiblingSpecAsync("WHISPERR_V2_SPEC_PATH", "v2.json", V2SpecUrl);
    }

    private static Task<string> LoadSiblingSpecAsync(string explicitPathVariable, string fileName, string url)
    {
        var explicitPath = Environment.GetEnvironmentVariable(explicitPathVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return ReadAllTextAsync(explicitPath);
        }

        var wirePath = Environment.GetEnvironmentVariable("WHISPERR_SPEC_PATH");
        if (!string.IsNullOrWhiteSpace(wirePath))
        {
            var specPath = Path.Combine(Path.GetDirectoryName(wirePath) ?? string.Empty, fileName);
            if (File.Exists(specPath))
            {
                return ReadAllTextAsync(specPath);
            }
        }

        var siblingPath = FindSiblingSpec(fileName);
        if (siblingPath != null)
        {
            return ReadAllTextAsync(siblingPath);
        }

        return FetchAsync(url);
    }

    private static Task<string> ReadAllTextAsync(string path)
    {
#if NET8_0_OR_GREATER
        return File.ReadAllTextAsync(path);
#else
        return Task.FromResult(File.ReadAllText(path));
#endif
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
