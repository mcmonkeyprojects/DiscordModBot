using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using DiscordBotBase.CommandHandlers;
using Discord;
using Discord.WebSocket;

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
        public void CMD_TestName(string[] cmds, IUserMessage message)
        {
            if (!DiscordModBot.IsBotCommander(message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            string name = NameUtilities.GenerateAsciiName(string.Join(" ", cmds));
            SendGenericPositiveMessageReply(message, "Test Name", $"Test of ASCII-Name-Rule name generator: {name}");
        }

        /// <summary>
        /// User command to sweep through all current names.
        /// </summary>
        public void CMD_Sweep(string[] cmds, IUserMessage message)
        {
            if (!DiscordModBot.IsBotCommander(message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            SocketGuildChannel channel = message.Channel as SocketGuildChannel;
            channel.Guild.DownloadUsersAsync();
            foreach (SocketGuildUser user in channel.Guild.Users)
            {
                if (NameUtilities.AsciiNameRuleCheck(message, user))
                {
                    Thread.Sleep(400);
                }
            }
        }
    }
}
