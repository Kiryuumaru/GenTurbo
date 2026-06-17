using ApplicationBuilderHelpers.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Application.Logger.Extensions;

public static class LoggerConfigurationExtensions
{
    private const string LoggerLevelKey = "RUNTIME_LOGGER_LEVEL";

    extension(IConfiguration configuration)
    {
        public LogLevel LoggerLevel
        {
            get
            {
                var loggerLevel = configuration.GetRefValueOrDefault(LoggerLevelKey, LogLevel.Information.ToString());
                return Enum.Parse<LogLevel>(loggerLevel);
            }
            set => configuration[LoggerLevelKey] = value.ToString();
        }
    }
}
