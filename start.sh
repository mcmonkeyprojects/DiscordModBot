#!/bin/bash
git pull origin master
git submodule update --init --recursive
dotnet build DiscordModBot.sln --configuration Release -o ./bin/live_release
screen -dmS DiscordModBot dotnet bin/live_release/DiscordModBot.dll $1
