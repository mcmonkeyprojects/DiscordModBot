using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using DiscordBotBase.CommandHandlers;
using Discord;
using Discord.WebSocket;
using DiscordBotBase;

namespace DiscordModBot.CommandHandlers
{
    /// <summary>
    /// Commands for admin usage only.
    /// </summary>
    public class AdminCommands : UserCommands
    {
        /// <summary>
        /// Outputs an ASCII name rule test name.
        /// </summary>
        public void CMD_TestName(CommandData command)
        {
            if (!DiscordModBot.IsBotCommander(command.Message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(command.Message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            string name = NameUtilities.GenerateAsciiName(string.Join(" ", command.CleanedArguments));
            SendGenericPositiveMessageReply(command.Message, "Test Name", $"Test of ASCII-Name-Rule name generator: {name}");
        }

        /// <summary>
        /// User command to sweep through all current names.
        /// </summary>
        public void CMD_Sweep(CommandData command)
        {
            if (!DiscordModBot.IsBotCommander(command.Message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(command.Message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            SocketGuildChannel channel = command.Message.Channel as SocketGuildChannel;
            channel.Guild.DownloadUsersAsync();
            foreach (SocketGuildUser user in channel.Guild.Users)
            {
                if (NameUtilities.AsciiNameRuleCheck(command.Message, user))
                {
                    Thread.Sleep(400);
                }
            }
        }
    }
}
