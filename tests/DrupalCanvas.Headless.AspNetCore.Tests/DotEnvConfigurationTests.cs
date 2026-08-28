using Microsoft.Extensions.Configuration;

namespace DrupalCanvas.Headless.AspNetCore.Tests;

public class DotEnvConfigurationTests
{
    private static Dictionary<string, string> ParseLines(params string[] lines)
        => DotEnvConfiguration.Parse(lines)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

    [Fact]
    public void Parses_plain_quoted_and_exported_assignments()
    {
        var values = ParseLines(
            "# The Drupal site",
            "CANVAS_SITE_URL=https://drupal.example",
            "QUOTED=\"with spaces\"",
            "SINGLE='single quoted'",
            "export EXPORTED=yes",
            "",
            "TRAILING=value # a comment",
            "EQUALS=a=b=c");

        Assert.Equal("https://drupal.example", values["CANVAS_SITE_URL"]);
        Assert.Equal("with spaces", values["QUOTED"]);
        Assert.Equal("single quoted", values["SINGLE"]);
        Assert.Equal("yes", values["EXPORTED"]);
        Assert.Equal("value", values["TRAILING"]);
        Assert.Equal("a=b=c", values["EQUALS"]);
        Assert.Equal(6, values.Count);
    }

    [Fact]
    public void Ignores_malformed_lines_instead_of_failing()
    {
        var values = ParseLines("no separator here", "=no key", "OK=1");
        Assert.Equal(["OK"], values.Keys);
    }

    [Fact]
    public void A_quoted_value_keeps_its_hash_characters()
        => Assert.Equal(
            "secret#not-a-comment",
            ParseLines("V=\"secret#not-a-comment\"")["V"]);

    [Fact]
    public void Loads_the_file_into_configuration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotenv-{Guid.NewGuid()}.env");
        File.WriteAllLines(path, ["FROM_DOTENV_FILE=file-value"]);
        try
        {
            var configuration = new ConfigurationBuilder().AddDotEnvFile(path).Build();
            Assert.Equal("file-value", configuration["FROM_DOTENV_FILE"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_real_environment_wins_over_the_file()
    {
        var name = $"DOTENV_TEST_{Guid.NewGuid():N}";
        var path = Path.Combine(Path.GetTempPath(), $"dotenv-{Guid.NewGuid()}.env");
        File.WriteAllLines(path, [$"{name}=file-value"]);
        Environment.SetEnvironmentVariable(name, "environment-value");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddDotEnvFile(path)
                .AddEnvironmentVariables()
                .Build();
            Assert.Equal("environment-value", configuration[name]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
            File.Delete(path);
        }
    }

    [Fact]
    public void A_missing_file_is_fine_when_optional_and_an_error_when_not()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"dotenv-{Guid.NewGuid()}.env");
        _ = new ConfigurationBuilder().AddDotEnvFile(missing).Build();
        Assert.Throws<FileNotFoundException>(
            () => new ConfigurationBuilder().AddDotEnvFile(missing, optional: false));
    }
}
