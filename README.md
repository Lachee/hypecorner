# Hype Corner
This bot uses Emgu OpenCV to watch streams and find players of [Rainbow Six: Siege](https://www.ubisoft.com/en-au/game/rainbow-six/siege) that are on match point.

### Check it out on [HypeCorner.tv](https://hypecorner.tv/)!

## Orchestra
By default, the bot will use the OrchestraHost to orchestrate with a external API the channels. 
This external API can be found at [github.com/lachee/hypecorner-api](https://github.com/Lachee/hypecorner-api) and will require authentication in order to use.

In code, it is possible to change the hosting provider so it uses twitch directly instead. However, going forward in the development the bot will be written for the Orchestra API which will be improved to handle all aspects of running [Hypecorner.TV](https://hypecorner.tv/), including automatic twitch hosting. 

I wish to have this enviroment with micro-services instead of a giant monolithic service (which I have done in the past) and the orchestra enables that.

## Usage
For best chance of getting this working, make sure you _read **all**_ of Emgu install requirements, install ffmpeg (and set it up as a path) and cry. I have had a lot of difficulties getting this working, and it has only been confirmed to work on **Windows 10**, **Windows Server 2016*** and **Ubuntu 18.04**; Because of this I will be not providing support when it comes to building and running the application.

_*Windows Server 2016 required several C++ redistrubutables installed that are not listed in any dependencies._

## Troubleshoot
`Protocol 'rtp' not on whitelist 'file,crypto'!`
* Please ensure you have the following enviroment variable set: `OPENCV_FFMPEG_CAPTURE_OPTIONS` = `protocol_whitelist;file,rtp,udp`

`libcvextern.dll failed to load (or exception)`
* You have not correctly installed Emgu. **Ubuntu**, please check the `install-ubuntu-18.04.sh` on all the enviroment settings you need to set. **Windows** this is a bit harder, but its likely you are using the wrong runtime. Please build like so: `dotnet build --runtime win-x86`. If that still doesnt work, then copy everything in the `x86` folder into the root directory. It took a lot of fiddling to get this working on Windows Server 2016.

`... OpenCV fails CMAKE ...`
* **Ubuntu** users may have a compile error when building emgucv. **THIS ONLY WORKS ON UBUNTU 18.04 OR 20.04**. Sorry, LTS 16 users. Additionally, the install script is only available on 18.04, you will need to copy it and modify so it builds emgucv for 20.04.
