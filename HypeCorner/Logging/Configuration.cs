using HypeCorner.Hosting;
using HypeCorner.Logging;
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
        public string ApiBaseUrl { get; set; } = "http://localhost:3000/api/";
        public int RepeatChannelTimer { get; set; } = 60;
        public LogLevel LogLevel { get; set; } = LogLevel.Info;

    }
}
