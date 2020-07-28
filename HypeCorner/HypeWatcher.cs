using HypeCorner.Hosting;
using HypeCorner.Logging;
using HypeCorner.Stream;
using HypeCorner.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TwitchLib.Api;

namespace HypeCorner
{
    /// <summary>
    /// This is the main hyperzone. This determines what to watch and links all the systems together
    /// </summary>
    public class HypeWatcher : IDisposable
    {
        #region constants & statics
        private static System.Net.Http.HttpClient http = new System.Net.Http.HttpClient();
        private static Random random = new Random();

        /// <summary>
        /// Name of the game to find
        /// </summary>
        const string GAME_NAME = "Tom Clancy's Rainbow Six: Siege";
        const int MINIMUM_LIST_OPTIONS = 4;
        const string LOG_APP = "HYPE";
        #endregion

        /// <summary>
        /// Current logger
        /// </summary>
        public ILogger Logger { get; set; } = new NullLogger();

        public Configuration Configuration { get; }
        public Orchestra Orchestra { get; }

        private TwitchAPI _twitch;
        private IHostProvider _host;
        private OCRCapture _capture;
        private volatile bool _skip = false;

        //Handles how long ago a channel was last hosted
        private Dictionary<string, DateTime> _channelHistory;
        private TimeSpan _repeatChannelTimer;

        //List of streams we can pick from
        List<TwitchLib.Api.V5.Models.Streams.Stream> _availableStreams;

        public HypeWatcher(Configuration configuration, ILogger logger)
        {
            //Setup the configuration
            //Set the configuration
            Configuration = configuration;
            Logger = logger;
            Logger.Level = Configuration.LogLevel;

            //Setup Orchestra
            Orchestra = new Orchestra(configuration.ApiName, configuration.ApiPassword, configuration.ApiBaseUrl) { Logger = Logger };
            Orchestra.OnSkip += (s, e) => { Logger.Info("skipping channel", LOG_APP); _skip = true; };

            //Setup the TWitch API
            _twitch = new TwitchAPI();
            _twitch.Settings.ClientId = configuration.TwitchClientId;

            //Setup the Host
            //https://twitchapps.com/tmi/
            //var chatCredentials = new ConnectionCredentials("<username>", "<oauth token>");
            //using var host = new TwitchHost(chatCredentials, "<username>");
            _host = new OrchestratedHost(Orchestra);

            //Setup teh channel limits
            _channelHistory = new Dictionary<string, DateTime>();
            _repeatChannelTimer = TimeSpan.FromMinutes(configuration.RepeatChannelTimer);
        }
    
        /// <summary>
        /// Watches 
        /// </summary>
        /// <returns></returns>
        public async Task WatchAsync()
        {
            Logger.Info("Starting Watch", LOG_APP);

            while (true)
            {
                Logger.Trace("Finding a channel to check...", LOG_APP);

                //Refresh the available streams
                if (_availableStreams == null || _availableStreams.Count < MINIMUM_LIST_OPTIONS)
                    _availableStreams = await GetAvailableStreams();

                //Get the stream we should check
                int index = random.Next(_availableStreams.Count);
                var stream = _availableStreams[index];

                //Add them to the checklist.
                //Remove them from our check list so we dont search them again
                _channelHistory[stream.Channel.Name] = DateTime.UtcNow;
                _availableStreams.RemoveAt(index);

                //Before we are even allowed to start OCR, lets validate we can host this channel
                Logger.Info("Checking Channel {0}", LOG_APP, stream.Channel.Name);
                if (await _host.CanHostAsync(stream.Channel.Name))
                {

                    //Cleanup the previous capture and create a new one
                    Logger.Trace("Creating a channel capture", LOG_APP);
                    using (_capture = new OCRCapture(stream.Channel.Name) { Logger = Logger })
                    {
                        //Begin capturing
                        _capture.Begin();
                        await Task.Delay(10);

                        try
                        {
                            //Scan the captured channel when it becomes available after some time
                            await TryHostCurrentCapture();
                        }
                        catch (Exception e)
                        {
                            Logger.Error("Failed to scan channel {0}, {1}", LOG_APP, stream.Channel.Name, e.Message);
                        }

                        //End capturing
                        _capture.End();
                        Logger.Trace("Capture Ended", LOG_APP);
                    }

                    //Set capture to null
                    _capture = null;
                    await Task.Delay(100);
                }
            }

        }

        /// <summary>Checks if hte current capture is host worthy.</summary>
        private async Task TryHostCurrentCapture()
        {
            Stopwatch timer = new Stopwatch();
            timer.Start();

            //Reset the skip
            _skip = false;

            //Wait for it to be reading (or until 10s has past)
            Logger.Trace("Waiting for FFMPEG", LOG_APP);
            while (_capture.IsRunning && _capture.FrameCount < 10) {
                if (timer.ElapsedMilliseconds >= 10000000)
                    throw new Exception("Took too long to get the first few frames.");
                await Task.Delay(100);
            }

            //Try to get the scoreboard
            Logger.Trace("Waiting for Scoreboard", LOG_APP);
            while (_capture.IsRunning)
            {             
                if (_capture.IsScoreboardVisible()) break;  //Break the loop if we are valid
                if (timer.ElapsedMilliseconds >= 10000)     //Terminate the host if we see no scoreboard
                    throw new Exception("Took too long to find the scoreboard");
                await Task.Delay(1000);
            }

            //If we are on match point, continue!
            //await Task.Delay(1000);
            if (!_capture.IsMatchPoint())
                throw new Exception("Not on match point when scoreboard was found");

            //Host the channel, if we are still allowed to
            Logger.Info("Attempting to host channel", LOG_APP);
            if (!await _host.CanHostAsync(_capture.ChannelName))
                throw new Exception("Cannot host the channel");
            await _host.HostAsync(_capture.ChannelName);

            //Main loop that we will continue while we are hosting this channel
            //Reset the timer. We are offically reading the contents of the stream now.
            Logger.Info("Watching stream for end of game", LOG_APP);
            timer.Restart();

            int previousLeft = -1;
            int previousRight = -1;

            while (_capture.IsRunning && !_skip)
            {
                //We were able to validate we are still on the scoreboard and still match point, so lets continue
                if (_capture.IsScoreboardVisible() && _capture.IsMatchPoint())
                {
                    //Reset the timer as we are on the scoreboard.
                    timer.Restart();

                    //If the score changed, then we will tell the orchestra.
                    var scores = _capture.GetScores();
                    if (scores[0] != previousLeft || scores[1] != previousRight)
                    {
                        previousLeft = scores[0]; previousRight = scores[1];
                        await Orchestra.UpdateScoresAsync(scores);
                    }
                }
                
                if (Console.KeyAvailable) {
                    while (Console.KeyAvailable) Console.ReadKey(true);
                    throw new Exception("Requested Cancellation");
                }

                //If execeded, lets find someone else
                if (timer.ElapsedMilliseconds >= 15000)
                    throw new Exception("Failed to maintain the scoreboard visibility");

                //Wait some time
                await Task.Delay(1000);
            }
            Logger.Trace("Game Ended", LOG_APP);
        }

        /// <summary>Gets available channels</summary>
        /// <returns></returns>
        private async Task<List<TwitchLib.Api.V5.Models.Streams.Stream>> GetAvailableStreams()
        {
            //Get a list of blacklisted channels
            var webBlacklist = await Orchestra?.GetBlacklistAsync();

            //Get the streams
            Logger.Info("Updating list of available channels", LOG_APP);
            TwitchLib.Api.V5.Models.Streams.LiveStreams gameStreams;
            List<TwitchLib.Api.V5.Models.Streams.Stream> validStreams;
            do
            {
                //Get a random offset
                int offset = random.Next(20);
                gameStreams = await _twitch.V5.Streams.GetLiveStreamsAsync(game: GAME_NAME, offset: offset, limit: 100);

                //Query through acceptable streams
                IEnumerable<TwitchLib.Api.V5.Models.Streams.Stream> query = gameStreams.Streams;
                query = query.Where(s => !s.Channel.Name.StartsWith("rainbow"));                                                                    //Skip offical channels
                query = query.Where(s => s.Channel.BroadcasterLanguage.StartsWith("en"));                                                           //English only broadcasters
                query = query.Where(s => !_channelHistory.TryGetValue(s.Channel.Name, out var dt) || (DateTime.UtcNow - dt) > _repeatChannelTimer);  //Channels that havn't been streamed recently

                //Skip web blacklist
                if (webBlacklist != null)
                    query = query.Where(s => !webBlacklist.ContainsKey(s.Channel.Name.ToLowerInvariant()));

                //Turn it into a list
                validStreams = query.ToList();
                Logger.Info("Found {0} new valid streams", LOG_APP, validStreams.Count);
            } while (validStreams.Count < MINIMUM_LIST_OPTIONS);

            return validStreams;
        }

        public void Dispose()
        {
            Orchestra?.Dispose();
            _capture?.Dispose();
        }
    }
}
