#!/bin/bash
git pull origin master
screen -dmS DiscordWarningBot dotnet run -- $1
