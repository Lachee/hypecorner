#!/bin/bash

echo "Building Ubuntu 18. This may take a while"
SCRIPTPATH="$( cd "$(dirname "$0")" >/dev/null 2>&1 ; pwd -P )"
echo "SCRIPTPATH=$SCRIPTPATH"

# Setup DotNET
echo "Setting up DPKG"
wget https://packages.microsoft.com/config/ubuntu/18.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

echo "Installing DOTNET"
sudo apt-get update;
sudo apt-get install -y apt-transport-https && \
  	sudo apt-get update && \
 	sudo apt-get install -y dotnet-sdk-3.1

# Setup FFMPEG
echo "Installing FFMPEG"
y | sudo apt-get install ffmpeg

# Locate EMGU and clone it if nessary
if [ -z "$EMGUCV" ]
then
	echo "Cloning EMGU"
	git clone https://github.com/emgucv/emgucv emgucv
	cd emgucv
	git submodule update --init --recursive
	EMGUCV="$SCRIPTPATH/emgucv"
fi

# Time to build EMGU
cd "$EMGUCV/platforms/ubuntu/18.04/"
echo "Resolving Deps"
./apt_install_dependency

echo "Building EMGU"
./cmake_configure

# Hopefully this completes without a hitch
export LD_LIBRARY_PATH="$EMGUCV/platforms/ubuntu/18.04/build/bin/x64:$EMGUCV/libs/x64"

# BUild the DOTNET
echo "Building DOTNET"
cd "$SCRIPTPATH"
dotnet build

# Set the enviroment
export OPENCV_FFMPEG_CAPTURE_OPTIONS="protocol_whitelist;file,rtp,udp"

# Run the script
echo "Running"
dotnet run --project HypeCorner


