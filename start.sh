#!/bin/bash
git pull origin master
git submodule update --init --recursive
screen -dmS DiscordWarningBot dotnet run -- $1
