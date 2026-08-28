using Microsoft.Extensions.Configuration;

namespace DrupalCanvas.Headless.AspNetCore;

/// <summary>
/// Minimal .env support, so .NET Canvas frontends configure CANVAS_SITE_URL
/// the way every JavaScript template does. The JS frameworks load .env
/// natively; ASP.NET Core does not, so this fills the gap as a configuration
/// source.
///
/// Precedence follows dotenv convention: a variable already present in the
/// real environment wins over the file. (Configuration sources added later
/// would normally override earlier ones, so file keys that exist in the
/// environment are skipped instead of added.)
/// </summary>
public static class DotEnvConfiguration
{
    /// <summary>
    /// Loads KEY=VALUE pairs from a .env file into configuration. The path
    /// resolves against the current directory (the project directory under
    /// <c>dotnet run</c>, the deploy directory in production). A missing file
    /// is fine unless <paramref name="optional"/> is false.
    /// </summary>
    public static IConfigurationBuilder AddDotEnvFile(
        this IConfigurationBuilder builder,
        string path = ".env",
        bool optional = true)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return optional
                ? builder
                : throw new FileNotFoundException($"The .env file was not found: {fullPath}", fullPath);
        }

        var values = new Dictionary<string, string?>();
        foreach (var (key, value) in Parse(File.ReadLines(fullPath)))
        {
            // dotenv convention: the real environment wins over the file.
            if (Environment.GetEnvironmentVariable(key) is null)
            {
                values[key] = value;
            }
        }
        return builder.AddInMemoryCollection(values);
    }

    /// <summary>
    /// Parses .env lines: <c>KEY=VALUE</c> with optional <c>export</c>
    /// prefix, <c>#</c> comment lines, matching single or double quotes
    /// stripped from values, and unquoted trailing <c># comments</c> removed.
    /// Malformed lines are ignored rather than fatal — a .env file is
    /// hand-edited developer input.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> Parse(IEnumerable<string> lines)
    {
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }
            var key = line[..separator].TrimEnd();
            var value = line[(separator + 1)..].TrimStart();

            if (value.Length >= 2
                && (value[0] == '"' || value[0] == '\'')
                && value[^1] == value[0])
            {
                value = value[1..^1];
            }
            else
            {
                var comment = value.IndexOf(" #", StringComparison.Ordinal);
                if (comment >= 0)
                {
                    value = value[..comment];
                }
                value = value.TrimEnd();
            }

            yield return new KeyValuePair<string, string>(key, value);
        }
    }
}
