﻿using HypeCorner.Logging;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;

namespace HypeCorner
{
    class Program
    {
        static void Main(string[] args)
        {
            const string configPath = "config.json";

            //We first need to setup the enviroment then terminate any instances of FFMPEG that might be left over from a crash.
            if (!HypeCorner.Stream.OCRCapture.ValidateEnviromentVariables()) {
                Console.WriteLine("Enviroment Variables are not setup! Set '{0}' to '{1}'", "OPENCV_FFMPEG_CAPTURE_OPTIONS", "protocol_whitelist;file,rtp,udp");
                return;
            }

            TerminateFFEMPG();

            //Load Configuration
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
            ILogger logger = config.LogFile ?
                                (ILogger) new FileLogger("hypecorner.log", config.LogLevel) : 
                                (ILogger) new ConsoleLogger(config.LogLevel, true);

            var hypezone = new HypeWatcher(config, logger);

            try
            {
                //Wait and terminate after
                hypezone.RunAsync().Wait();
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
