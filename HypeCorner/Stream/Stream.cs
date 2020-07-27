using System;
using System.Collections.Generic;
using System.Text;

namespace HypeCorner.Stream
{

    /// <summary>
    /// The available stream from twitch
    /// </summary>
    public class Stream
    {
        /// <summary>
        /// The quality of the stream. Ranges from values such as "360p", "480p" to "720p60" and "1080p60 (source)"
        /// </summary>
        public string Quality { get; set; }

        public int QualityNo { get; set; }

        /// <summary>
        /// Pixel Resolution
        /// </summary>
        public string Resolution { get; set; }

        /// <summary>
        /// URL to the m3u8
        /// </summary>
        public string Url { get; set; }
    }

}
