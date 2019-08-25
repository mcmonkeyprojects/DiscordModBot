#!/bin/bash
git pull origin master
git submodule update --init --recursive
screen -dmS DiscordModBot dotnet run -- $1
