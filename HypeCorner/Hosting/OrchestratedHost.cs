using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace HypeCorner.Hosting
{
    class OrchestratedHost : IHostProvider
    {
        private HttpClient http;

        public Orchestra Orchestra { get; }

        public OrchestratedHost(Orchestra orchestra)
        {
            Orchestra = orchestra;            
        }

        public async Task HostAsync(string channelName)
        {
            //If we are connected to OBS, then we will switch scenes and wait a bit
            // The Orchestrated Host no longer deals with OBS. That is now dedicated to a seperate program.
            // if (_obs.IsConnected) {
            //     _obs.SetCurrentScene("goodbye");
            // 
            //     var properties = _obs.GetTextGDIPlusProperties("streamnametxt");
            //     properties.Text = channelName;
            //     properties.TextColor = 16777215;
            //     properties.BackgroundColor = 0;
            //     _obs.SetTextGDIPlusProperties(properties);
            // 
            //     await Task.Delay(1500);
            // }

            //Change the channel, with a preroll too.
            await Orchestra.ChangeChannelPrerollAsync(channelName);
        }

        public async Task<bool> CanHostAsync(string channelName)
        {
            //If they have no reason, they are not blacklisted
            var reason = await Orchestra.GetBlacklistReasonAsync(channelName);
            return string.IsNullOrWhiteSpace(reason);
        }
    }
}
