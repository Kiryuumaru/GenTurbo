using ApplicationBuilderHelpers.Extensions;
using Microsoft.Extensions.Configuration;

namespace Application.Configuration.Extensions;

public static class GenTurboConfigurationExtensions
{
    private const string RetentionHoursKey = "GEN_TURBO_RETENTION_HOURS";
    private const string OrchestratorUrlKey = "GEN_TURBO_ORCHESTRATOR_URL";

    extension(IConfiguration configuration)
    {
        public int RetentionHours
        {
            get
            {
                var value = configuration.GetRefValueOrDefault(RetentionHoursKey, "24");
                return int.Parse(value);
            }
            set => configuration[RetentionHoursKey] = value.ToString();
        }

        public string OrchestratorUrl
        {
            get
            {
                return configuration.GetRefValueOrDefault(OrchestratorUrlKey, "https://gen-turbo.local.net");
            }
            set => configuration[OrchestratorUrlKey] = value;
        }
    }
}
