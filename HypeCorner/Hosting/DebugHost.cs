using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace HypeCorner.Hosting
{
    /// <summary>
    /// Handles hosting to a FFPlay instance
    /// </summary>
    class DebugHost : IHostProvider
    {
        public Task<bool> CanHostAsync(string channelName)
        {
            return Task.FromResult(true);
        }

        public Task HostAsync(string channelName)
        {
            Console.WriteLine("HOSTING " + channelName);
            return Task.CompletedTask;
        }
    }
}
