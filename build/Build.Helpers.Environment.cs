using Domain.AppEnvironment.Constants;
using Nuke.Common;
using Nuke.Common.IO;
using Serilog;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

partial class Build
{
    public override string[] EnvironmentBranches =>
        [.. AppEnvironments.AllValues.Select(e => e.Tag)];

    public override string MainEnvironmentBranch =>
        AppEnvironments.AllValues.Last().Tag;

    Target Clean => _ => _
        .Executes(() =>
        {
            foreach (var path in RootDirectory.GetFiles("**", 99).Where(i => i.Name.EndsWith(".csproj")))
            {
                if (path.Name == "_build.csproj") continue;
                Log.Information("Cleaning {path}", path);
                (path.Parent / "bin").DeleteDirectory();
                (path.Parent / "obj").DeleteDirectory();
            }
            (RootDirectory / ".vs").DeleteDirectory();
        });

    Target Init => _ => _
        .Executes(() =>
        {
            GenerateEmbeddedConfig();
        });

    Target GithubWorkflow => _ => _
        .Executes(() =>
        {
            GenerateEmbeddedConfig();
        });

    void GenerateEmbeddedConfig()
    {
        var configPath = RootDirectory / "embedded-config.json";

        if (File.Exists(configPath))
        {
            Log.Information("embedded-config.json already exists at {path}", configPath);
            return;
        }

        Log.Information("Generating embedded-config.json at {path}", configPath);

        var envObject = new JsonObject();
        var configObject = new JsonObject
        {
            ["shared"] = new JsonObject
            {
                ["orchestrator_url"] = "https://gen-turbo.local.net",
            },
            ["environments"] = envObject,
        };

        foreach (var env in AppEnvironments.AllValues)
        {
            envObject[env.Tag] = new JsonObject
            {
                ["hf_token"] = $"{env.Short}-change-me",
                ["civitai_api_key"] = $"{env.Short}-change-me",
            };
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(configPath, configObject.ToJsonString(options));
        Log.Information("embedded-config.json generated with branches: {branches}", string.Join(", ", AppEnvironments.AllValues.Select(e => e.Tag)));
    }
}
