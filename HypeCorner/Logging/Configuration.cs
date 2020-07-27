using HypeCorner.Hosting;
using HypezoneTwitch.Logging;
using TwitchLib.Api;

namespace HypeCorner.Logging
{
    public class Configuration
    {
        public string TwitchName { get; set; }
        public string TwitchClientId { get; set; }
        public string TwitchOAuth2 { get; set; }
        public string ApiName { get; set; } = "api";
        public string ApiPassword { get; set; } = "pass";
        public string ApiEndpoint { get; set; } = "https://mixy.lu.je";
        public int RepeatChannelTimer { get; set; } = 60;
        public LogLevel LogLevel { get; set; } = LogLevel.Info;

    }
}
