using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace HypeCorner.Hosting
{
    /// <summary>
    /// Handles hosting channels
    /// </summary>
    public interface IHostProvider
    {
        /// <summary>
        /// Hosts a channel
        /// </summary>
        /// <param name="channelName"></param>
        public Task HostAsync(string channelName);

        /// <summary>
        /// Can the provider host the channel?
        /// </summary>
        /// <param name="channelName"></param>
        /// <returns></returns>
        public Task<bool> CanHostAsync(string channelName);
    }
}
