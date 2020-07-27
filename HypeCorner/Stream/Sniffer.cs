using Emgu.CV.Quality;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace HypeCorner.Stream
{
    /// <summary>
    /// Fetches the stream links from twitch.
    /// </summary>
    class Sniffer
    {
        /// <summary>
        /// Borrowed from streamlink https://github.com/streamlink/streamlink/blob/76880e46589d2765bf030927169debd295958040/src/streamlink/plugins/twitch.py#L47
        /// </summary>
        private const string TwitchPrivateKey = "kimne78kx3ncx6brgo4mv6wki5h1ko";
        private static HttpClient HttpClient = new HttpClient();
        private static int GlobalNonce = 0;
        
        /// <summary>
        /// Gets available streams
        /// </summary>
        /// <param name="channelName"></param>
        /// <returns></returns>
        public static async Task<Stream[]> GetStreamsAsync(string channelName)
        {
            var accessToken = await GetAccessTokenAsync(channelName);
            var queryParams = new NameValueCollection()
            {
                { "player", "twitchweb" },
                { "p", (GlobalNonce++).ToString() },
                { "type", "any" },
                { "allow_source", "true" },
                { "allow_audio_only", "false" },
                { "allow_spectre", "false" },
                { "token", accessToken.Token },
                { "sig", accessToken.Signature },
            };

            string url = string.Format("https://usher.ttvnw.net/api/channel/hls/{0}.m3u8?{1}", channelName, ToQueryString(queryParams));
            var playlistRaw = await HttpClient.GetStringAsync(url);
            return ParsePlaylist(playlistRaw);
        }

        /// <summary>
        /// Gets the token used to access the endpoint
        /// </summary>
        /// <param name="channelName"></param>
        /// <returns></returns>
        private static async Task<AccessToken> GetAccessTokenAsync(string channelName)
        {
            string url = $"https://api.twitch.tv/api/channels/{channelName}/access_token?client_id={TwitchPrivateKey}";
            var json = await HttpClient.GetStringAsync(url);
            var obj = JObject.Parse(json);
            return new AccessToken
            {
                Token = obj["token"].Value<string>(),
                Signature = obj["sig"].Value<string>()
            };
        }

        /// <summary>
        /// Parses the playlist
        /// </summary>
        /// <param name="playlist"></param>
        /// <returns></returns>
        private static Stream[] ParsePlaylist(string playlist)
        {
            var parsed = new List<Stream>();
            var lines = playlist.Split('\n');
            for (int i = 4; i < lines.Length - 1; i += 3)
            {
                var stream = new Stream()
                {
                    Quality = lines[i - 2].Split("NAME=\"")[1].Split("\"")[0],
                    Resolution = (lines[i - 1].IndexOf("RESOLUTION") != -1 ? lines[i - 1].Split("RESOLUTION=")[1].Split(",")[0] : null),
                    Url = lines[i]
                };

                //Set the stream quality if we can
                int indexofP = stream.Quality.IndexOf('p');
                if (indexofP > 0)
                    stream.QualityNo = int.Parse(stream.Quality.Substring(0, indexofP));

                //Add to our list
                parsed.Add(stream);
            }

            //Return the ordered list
            return parsed.OrderByDescending(k => k.QualityNo).ToArray();
        }


        /// <summary>
        /// Converts NameValueCollection into a query string
        /// </summary>
        /// <param name="nvc"></param>
        /// <returns></returns>
        private static string ToQueryString(NameValueCollection nvc)
        {
            //https://stackoverflow.com/questions/829080/how-to-build-a-query-string-for-a-url-in-c
            var array = (
                from key in nvc.AllKeys
                from value in nvc.GetValues(key)
                select string.Format(
            "{0}={1}",
            HttpUtility.UrlEncode(key),
            HttpUtility.UrlEncode(value))
                ).ToArray();
            return string.Join("&", array);
        }

        /// <summary>
        /// Access Token
        /// </summary>
        struct AccessToken
        {
            public string Token;
            public string Signature;
        }

    }
}
