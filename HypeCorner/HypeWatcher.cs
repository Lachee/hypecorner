
using HypeCorner.Logging;
using HypeCorner.Stream;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using TwitchLib.Api;
using HypeCorner.Exceptions;

using TwitchStream = TwitchLib.Api.V5.Models.Streams.Stream ;

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
        private OCRCapture _capture;
        private volatile bool _skip = false;

        //Handles how long ago a channel was last hosted
        private Dictionary<string, DateTime> _channelHistory;
        private TimeSpan _repeatChannelTimer;

        /// <summary>List of streams waiting to be checked</summary>
        Queue<TwitchStream> _streamQueue;

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

            //Setup teh channel limits
            _channelHistory = new Dictionary<string, DateTime>();
            _repeatChannelTimer = TimeSpan.FromMinutes(configuration.RepeatChannelTimer);
        }

        /// <summary>
        /// Begins watching for match points
        /// </summary>
        /// <returns></returns>
        public async Task RunAsync()
        {
            Logger.Info("Starting Watch", LOG_APP);

            while (true)
            {
                //Find a stream
                Logger.Trace("Finding a channel to check...", LOG_APP);
                var stream = await GetStreamAsync();

                //Before we are even allowed to start OCR, lets validate we can host this channel
                Logger.Info("Checking Channel {0}", LOG_APP, stream.Channel.Name);
                if (await CanHostAsync(stream.Channel.Name))
                {

                    //Cleanup the previous capture and create a new one
                    Logger.Trace("Creating a channel capture", LOG_APP);
                    using (_capture = new OCRCapture(stream.Channel.Name) { Logger = Logger })
                    {
                        //_capture.ShowWindows();

                        //Begin capturing
                        _capture.Begin();
                        await Task.Delay(10);

                        try
                        {
                            //Scan the captured channel when it becomes available after some time
                            await WatchCaptureAsync();
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

        /// <summary>Watches the current capture. Throws an exception if the capture isn't worth watching.</summary>
        private async Task WatchCaptureAsync()
        {
            Stopwatch timer = new Stopwatch();
            timer.Start();

            //Reset the skip
            _skip = false;

            //Wait for it to be reading (or until 10s has past)
            Logger.Trace("Waiting for FFMPEG", LOG_APP);
            while (_capture.IsRunning && _capture.FrameCount < 10) {
                if (timer.ElapsedMilliseconds >= 10000000)
                    throw new WatchException("Stream timed out");

                await Task.Delay(100);
            }

            //Try to get the scoreboard
            Logger.Trace("Waiting for Scoreboard", LOG_APP);
            while (_capture.IsRunning)
            {
                if (_capture.IsScoreboardVisible()) break;  //Break the loop if we are valid
                if (timer.ElapsedMilliseconds >= 10000)     //Terminate the host if we see no scoreboard
                    throw new WatchException("Scoreboard not visible");

                await Task.Delay(1000);
            }

            //If we are on match point, continue!
            //await Task.Delay(1000);
            if (!_capture.IsMatchPoint())
                throw new WatchException("Not on match point");

            //Host the channel, if we are still allowed to
            Logger.Info("Attempting to host channel", LOG_APP);
            if (!await HostCaptureAsync())
                throw new WatchException("Cannot host the channel");


            //Main loop that we will continue while we are hosting this channel
            //Reset the timer. We are offically reading the contents of the stream now.
            Logger.Info("Watching stream for end of game", LOG_APP);
            timer.Restart();

            int previousLeft = -1;
            int previousRight = -1;
            int previousFrameCount = _capture.FrameCount;
            while (_capture.IsRunning && !_skip)
            {
                //We were able to validate we are still on the scoreboard and still match point, so lets continue
                // We are also checking if the framecount has changed here as its important that we make sure we are still actually getting frames.
                if (_capture.IsScoreboardVisible() && _capture.IsMatchPoint() && _capture.FrameCount != previousFrameCount)
                {
                    //Reset the timer as we are on the scoreboard.
                    timer.Restart();
                }

                //Update the frame count
                previousFrameCount = _capture.FrameCount;

                //If the score changed, then we will tell the orchestra.
                var scores = _capture.GetScores();
                if (scores[0] != previousLeft || scores[1] != previousRight)
                {
                    previousLeft = scores[0]; previousRight = scores[1];
                    await Orchestra.UpdateScoresAsync(scores);
                }

                //Skip from key or _skip
                if (Console.KeyAvailable || _skip) {
                    while (Console.KeyAvailable) Console.ReadKey(true);
                    throw new WatchException("Watch terminated by external source");
                }

                //If execeded, lets find someone else
                if (timer.ElapsedMilliseconds >= 20000)
                    throw new Exception("Failed to maintain the scoreboard visibility");

                //Wait some time
                await Task.Delay(1000);
            }
            Logger.Trace("Game Ended", LOG_APP);
        }

        /// <summary>
        /// Hosts the current capture
        /// </summary>
        /// <returns>Returns if we can host the caputre</returns>
        private async Task<bool> HostCaptureAsync()
        {
            //not sure if we should run a CanHostAsync. Think its just slowing stuff down really.
            //Upload the thumbnail of hte current channel.
            await Orchestra.UploadThumbnail(_capture.ChannelName, _capture.GetJpegData());

            //Host the channel
            await HostAsync(_capture.ChannelName);
            return true;
        }

        /// <summary>
        /// Hosts a channel
        /// </summary>
        /// <param name="channel"></param>
        /// <returns></returns>
        private async Task HostAsync(string channel)
        {
            //Tell orchestra to preroll change
            Logger.Info("Hosting Channel {0}", LOG_APP, channel);
            await Orchestra.HostChannelAsync(channel, Configuration.PrerollDuration);
        }

        /// <summary>
        /// Checks if we are allowed to host this channel
        /// </summary>
        /// <param name="channel"></param>
        /// <returns></returns>
        private async Task<bool> CanHostAsync(string channel)
        {
            //Make sure they dont have a blacklist
            var reason = await Orchestra.GetBlacklistReasonAsync(_capture.ChannelName);
            if (!string.IsNullOrWhiteSpace(reason))
            {
                Logger.Warning("Cannot host channel {0}: {1}", LOG_APP, channel, reason);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Finds a stream to check
        /// </summary>
        /// <returns></returns>
        private async Task<TwitchStream> GetStreamAsync() {

            //If we have too little streams, get some new ones.
            if (_streamQueue == null || _streamQueue.Count == 0)
            {
                //Get a list of eligible streams and turn them into a queue
                var eligible = await GetEligibleStreamsAsync();
                _streamQueue = new Queue<TwitchStream>(eligible.OrderBy(s => s.Viewers));
            }

            //Find a stream.
            // it has been determined purely random guessing isn't appropriate.
            // int index = random.Next(_availableStreams.Count); 
            //var stream = _availableStreams[index];
            //_availableStreams.RemoveAt(index);

            //Instead, we will always just get hte first and shift it back down.
            var stream = _streamQueue.Dequeue();
            Logger.Info("Dequeued {0} with {1} views", LOG_APP, stream.Channel.Name, stream.Viewers);

            //Add them to the checklist.
            //Remove them from our check list so we dont search them again
            _channelHistory[stream.Channel.Name] = DateTime.UtcNow;
            return stream;
        }


        /// <summary>Gets a list of available channels from twitch. It runs through several predicates to determine if the channel is actually allowed and populates the list</summary>
        /// <returns></returns>
        private async Task<TwitchStream[]> GetEligibleStreamsAsync()
        {
            //Get a list of blacklisted channels
            var webBlacklist = await Orchestra?.GetBlacklistAsync();

            //Get the streams
            Logger.Info("Updating list of available channels", LOG_APP);
            TwitchLib.Api.V5.Models.Streams.LiveStreams allStreams;
            TwitchStream[] eligibleStreams;

            const int MAX_PAGE = 20;
            int pageOffset = 0;

            do
            {
                //Get all the streams at a random offset
                int page = Math.Max(MAX_PAGE - pageOffset, 0);
                int offset = page > 0 ? random.Next(page) : 0;
                allStreams = await _twitch.V5.Streams.GetLiveStreamsAsync(game: GAME_NAME, offset: offset, limit: 100);

                //Query through acceptable streams
                IEnumerable<TwitchLib.Api.V5.Models.Streams.Stream> query = allStreams.Streams;
                query = query.Where(s => !s.Channel.Name.StartsWith("rainbow"));                                                                        //Skip offical channels
                query = query.Where(s => s.Channel.BroadcasterLanguage.StartsWith("en"));                                                               //English only broadcasters
                query = query.Where(s => !_channelHistory.TryGetValue(s.Channel.Name, out var dt) || (DateTime.UtcNow - dt) > _repeatChannelTimer);     //Channels that havn't been streamed recently

                //Skip web blacklist
                if (webBlacklist != null)
                    query = query.Where(s => !webBlacklist.ContainsKey(s.Channel.Name.ToLowerInvariant()));

                //Turn it into an array
                eligibleStreams = query.ToArray();
                Logger.Info("Found {0} new valid streams", LOG_APP, eligibleStreams.Length);

                //Increment the page and determine if we should go again
                pageOffset++;

                //If we are above the max, then just wait for a long time before trying again
                if (pageOffset >= MAX_PAGE) {
                    Logger.Error("Failed to find enough in {0} pages. Waiting a minute...", LOG_APP, pageOffset);
                    await Task.Delay(TimeSpan.FromMinutes(1));
                    pageOffset = 0;
                }

                // Keep looping until we have enough streams
            } while (eligibleStreams.Length < MINIMUM_LIST_OPTIONS);

            //Return the eligible streams
            return eligibleStreams;
        }

        public void Dispose()
        {
            Orchestra?.Dispose();
            _capture?.Dispose();
        }
    }
}
