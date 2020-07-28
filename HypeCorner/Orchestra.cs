﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using HypeCorner.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HypeCorner
{
    /// <summary>
    /// Handles API access to the API
    /// </summary>
    public class Orchestra : IDisposable
    {
        const string LOG_ORC = "ORCHE";
        const string EVENT_ORCHESTRA_SKIP      = "ORCHESTRA_SKIP";     //Sent to tell the OCR that we wish to skip this nonsense and please find us a new channel.
        const string EVENT_ORCHESTRA_PREROLL   = "ORCHESTRA_PREROLL";  //Sent to tell the embed client and OBS clients to run a preroll for a specified duration as we are about to change.
        const string EVENT_ORCHESTRA_CHANGE    = "ORCHESTRA_CHANGE";   //Sent to tell the embed clients to switch channel
        const string EVENT_ORCHESTRA_SCORE     = "ORCHESTRA_SCORE";    //Sent every now and again to tell the users what we think the score is. More a reminder to stay connected.
        const string EVENT_BLACKLIST_ADD       = "BLACKLIST_ADDED";    //Invoked when a user is added to the blacklist
        const string EVENT_BLACKLIST_REMOVE    = "BLACKLIST_REMOVED";  //Invoked when a user is removed from the blacklist


        //Set once the orchestra has been disposed.
        private bool _disposed = false;
        private HttpClient http;
        private WebSocketSharp.WebSocket websocket;

        /// <summary>
        /// Current logger
        /// </summary>
        public ILogger Logger { get; set; } = new NullLogger();

        /// <summary>
        /// Event when it should skip
        /// </summary>
        public event EventHandler OnSkip;

        private struct ChannelNamePayload
        {
            public string name;
        }

        /// <summary>
        /// Creates a new Orchestra instance
        /// </summary>
        /// <param name="username"></param>
        /// <param name="password"></param>
        /// <param name="baseUrl"></param>
        public Orchestra(string username, string password, string baseUrl = "http://localhost:3000/api/")
        {
            #region HTTP initialization
            http = new HttpClient();
            http.BaseAddress = new Uri(baseUrl);
            http.DefaultRequestHeaders.Authorization =
              new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(
                    System.Text.ASCIIEncoding.ASCII.GetBytes(
                       $"{username}:{password}")));

            #endregion

            #region Websocket Initialization
            websocket = new WebSocketSharp.WebSocket(baseUrl + "gateway");
            websocket.OnClose += (sender, e) =>
            {
                if (_disposed) return;
                Logger.Warning("Orchestra closed", LOG_ORC);
                OpenWebsocket();
            }; 
            websocket.OnOpen += (sender, e) =>
            {
                if (_disposed) return;
                Logger.Info("Orchestra opened", LOG_ORC);
            };
            websocket.OnMessage += (sender, e) =>
            {
                if (_disposed) return;
                Logger.Trace("Orchestra Message: {0}", LOG_ORC, e.Data);

                //Parse the data and switch based of the event
                var jobj = JObject.Parse(e.Data);
                switch(jobj.Value<string>("e"))
                {
                    //We dont care about all the events
                    default: break;

                    //Skip events are important to us.
                    case EVENT_ORCHESTRA_SKIP:
                        Logger.Info("Orchestra has been requested to skip this channel.", LOG_ORC);
                        OnSkip?.Invoke(this, EventArgs.Empty);
                        break;
                }
            };
            #endregion
        }

        /// <summary>Opens the websocket</summary>
        private void OpenWebsocket() {
            Logger.Info("Opening Orchestra WS", LOG_ORC);
            websocket.ConnectAsync();
        }

        /// <summary>
        /// Gets the current blacklist
        /// </summary>
        /// <returns></returns>
        public async Task<IReadOnlyDictionary<string, string>> GetBlacklistAsync()
        {
            var json = await http.GetStringAsync("blacklist");
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
        }

        /// <summary>
        /// Gets the blacklist for a particular channel
        /// </summary>
        /// <param name="channel">The channel name</param>
        /// <returns>Returns the reason of the blacklist, otherwise null.</returns>
        public async Task<string> GetBlacklistReasonAsync(string channel)
        {
            try
            {
                //return the reason
                return await http.GetStringAsync($"blacklist/{channel}");
            } catch(HttpRequestException _)
            {
                //Threw an error code, probably has no blacklist
                return null;
            }
        }

        /// <summary>
        /// Changes the channel smoothly.
        /// </summary>
        /// <param name="channel">Name of the channel to switch too</param>
        /// <param name="prerollDuration">How long to wait for the preroll. Clients should be able to handle any amount and is only there for safety.</param>
        /// <returns></returns>
        public async Task ChangeChannelPrerollAsync(string channel, int prerollDuration = 100)
        {
            //Call the preroll, wait a bit, then change the channel
            await PrerollAsync(channel);
            await Task.Delay(prerollDuration);
            await ChangeChannelAsync(channel);
        }

        /// <summary>
        /// Changes the channel.
        /// </summary>
        /// <param name="channel"></param>
        /// <returns></returns>
        public async Task ChangeChannelAsync(string channel) {
            var json = JsonConvert.SerializeObject(new ChannelNamePayload() { name = channel });
            var contents = new StringContent(json);
            await http.PostAsync("orchestra/change", contents);
        }

        /// <summary>
        /// Calls the preroll. Use this to trigger client's splash screens before changing.
        /// </summary>
        /// <returns></returns>
        public async Task PrerollAsync(string channel) {
            var json = JsonConvert.SerializeObject(new ChannelNamePayload() { name = channel });
            var contents = new StringContent(json);
            await http.PostAsync("orchestra/preroll", contents);
        }
       
        /// <summary>
        /// Tells the clients to skip (that would be this, and it would be pointless).
        /// </summary>
        /// <returns></returns>
        [System.Obsolete("There is literally no use to do this, because you will get the callback")]
        public async Task SkipAsync() {
            var contents = new StringContent("");
            await http.PostAsync("orchestra/skip", contents);
        }

        /// <summary>
        /// Updates the displayed scores.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public Task UpdateScoresAsync(int left, int right)
        {
            return UpdateScoresAsync(new int[] { left, right });
        }

        /// <summary>
        /// Updates the displayed scores.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public async Task UpdateScoresAsync(int[] score)
        {
            var json = JsonConvert.SerializeObject(score);
            var contents = new StringContent(json);
            await http.PostAsync("orchestra/score", contents);
        }


        public void Dispose()
        {
            _disposed = true;

            websocket?.Close();
            websocket = null;

            http?.Dispose();
            http = null;
        }
    }
}
