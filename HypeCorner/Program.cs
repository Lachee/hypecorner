using HypeCorner.Hosting;
using HypeCorner.Logging;
using HypezoneTwitch.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Models;

namespace HypeCorner
{
    class Program
    {
        static void Main(string[] args)
        {
            const string configPath = "config.json";
            TerminateFFEMPG();

            //Load COnfiguration
            Configuration config;
            if (!File.Exists(configPath))
            {
                config = new Configuration();
                File.WriteAllText(configPath, JsonConvert.SerializeObject(config, Formatting.Indented));
                Console.WriteLine("initial configuration file created. Please edit {0}", configPath);
                return;
            } 
            else
            {
                string json = File.ReadAllText(configPath);
                config = JsonConvert.DeserializeObject<Configuration>(json);
            }


            //Create and run the HypeZone
            Console.WriteLine("Starting HypeCorner");
            var hypezone = new HypeWatcher(config)
            {
                Logger = new ConsoleLogger(config.LogLevel, true)
            };

            try
            {
                //Wait and terminate after
                hypezone.WatchAsync().Wait();
                Console.WriteLine("HypeZone terminated");
            }
            finally
            {
                //Finally clean up ffmpeg
                TerminateFFEMPG();
            }
        }

        private static void TerminateFFEMPG()
        {
            //Clear previous FFMPEG, just in case
            foreach (var p in Process.GetProcessesByName("ffmpeg"))
                p.Kill();
        }
    }

}
