using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Models;

namespace HypeCorner.Hosting
{
    class TwitchHost : IHostProvider, IDisposable
    {
        private TwitchClient client;
        private string selfChannel;
        private string previousChannel;

        public TwitchHost(ConnectionCredentials credentials, string channel)
        {
            selfChannel = channel;
            client = new TwitchClient();
            client.Initialize(credentials);
            client.Connect();

            client.OnConnected += (s, e) =>
            {
                //Join our own channel
                client.JoinChannel(selfChannel);
            };

            client.OnJoinedChannel += (s, e) =>
            {
                //set the previous channel to this one. This way when we join we will be able to say goodbye
                Console.WriteLine("Joined Channel {0}", e.Channel);
                if (e.Channel != selfChannel)
                {
                    previousChannel = e.Channel;
                    //client.SendMessage(previousChannel, "Hello o/ How are you.");
                }
            };

            client.OnLeftChannel += (s, e) =>
            {
                Console.WriteLine("Left Channel {0}", e.Channel);
            };
        }

        public Task HostAsync(string channelName)
        {
            //Tell the previous channel we are leaving
            if (previousChannel != null && previousChannel != selfChannel)
            {
                Console.WriteLine("Leaving Channel {0}", previousChannel);
                //client.SendMessage(previousChannel, "See you around o/");
                client.LeaveChannel(previousChannel);
            }

            //Set the host
            client.SendMessage(selfChannel, "/host " + channelName);
            client.SendMessage(selfChannel, "hosting " + channelName);
            client.JoinChannel(channelName);
            return Task.CompletedTask;
        }


        public void Dispose()
        {
            client.Disconnect();
            client = null;
        }


        public Task<bool> CanHostAsync(string channelName)
        {
            return Task.FromResult(true);
        }
    }
}
