using OBSWebsocketDotNet;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Models;

namespace HypeCorner.Hosting
{
    class EmbedHost : IHostProvider
    {
        private HttpClient http;
        private OBSWebsocket _obs;

        public string API { get; }

        public EmbedHost(string username, string password, string api = "http://localhost:3000")
        {
            API = api;

            http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
              new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(
                    System.Text.ASCIIEncoding.ASCII.GetBytes(
                       $"{username}:{password}")));

            _obs = new OBSWebsocketDotNet.OBSWebsocket();
            _obs.Connect("ws://localhost:4444", "");
        }

        public async Task HostAsync(string channelName)
        {
            //If we are connected to OBS, then we will switch scenes and wait a bit
            if (_obs.IsConnected) {
                _obs.SetCurrentScene("goodbye");

                var properties = _obs.GetTextGDIPlusProperties("streamnametxt");
                properties.Text = channelName;
                properties.TextColor = 16777215;
                properties.BackgroundColor = 0;
                _obs.SetTextGDIPlusProperties(properties);

                await Task.Delay(1500);
            }

            //Prepare the endpoint
            string url = string.Format("{0}/api/channel/{1}", API, channelName);

            //Post. It doesn't actually care if the content has data
            var content = new StringContent(channelName);
            await http.PostAsync(url, content);

            //If we are connected to OBS, we will return scene
            if (_obs.IsConnected)
            {
                //Await a few seconds before switching to prepare
                await Task.Delay(5000);
                _obs.SetCurrentScene("prepare");
            }
        }

        public Task<bool> CanHostAsync(string channelName)
        {
            return Task.FromResult(true);
        }
    }
}
