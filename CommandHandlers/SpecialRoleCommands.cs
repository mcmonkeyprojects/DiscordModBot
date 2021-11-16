using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Discord;
using Discord.WebSocket;
using DiscordBotBase;
using DiscordBotBase.CommandHandlers;
using ModBot.Database;
using ModBot.WarningHandlers;
using ModBot.Core;
using FreneticUtilities.FreneticExtensions;

namespace ModBot.CommandHandlers
{
    /// <summary>Command handler for special role commands.</summary>
    public class SpecialRoleCommands : UserCommands
    {
        /// <summary>User command to add a special role to a user.</summary>
        public void CMD_AddSpecialRole(GuildConfig.SpecialRole role, CommandData command)
        {
            SocketGuild guild = (command.Message.Channel as SocketGuildChannel).Guild;
            if (!DiscordModBot.IsModerator(command.Message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(command.Message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            if (!DiscordModBot.WarningCommandHandler.GetTargetUser(command, true, true, out ulong userID))
            {
                return;
            }
            SocketGuildUser guildUser = guild.GetUser(userID);
            WarnableUser warnable = WarningUtilities.GetWarnableUser(guild.Id, userID);
            if (warnable.SeenNames.IsEmpty())
            {
                SendErrorMessageReply(command.Message, "Invalid Input", "Cannot alter that user: user has never been seen before.");
                return;
            }
            if (guildUser != null)
            {
                SocketRole socketRole = guild.GetRole(role.RoleID);
                if (socketRole == null)
                {
                    SendErrorMessageReply(command.Message, "Failed To Apply", "Cannot apply special role: no matching role found.");
                    return;
                }
                guildUser.AddRoleAsync(socketRole).Wait();
            }
            bool alreadyHadRole = warnable.SpecialRoles.Contains(role.Name);
            if (alreadyHadRole)
            {
                SendGenericNegativeMessageReply(command.Message, "Cannot Apply Special Role", $"User <@{userID}> is already marked with special role `{role.Name}`.");
                return;
            }
            warnable.SpecialRoles.Add(role.Name);
            warnable.Save();
            string hadSameRoleBefore = "";
            if (!string.IsNullOrWhiteSpace(role.AddWarnText) && warnable.Warnings.Any(w => w.Reason == role.AddWarnText))
            {
                hadSameRoleBefore = $"\n\nUser has previously had the special role `{role.Name}` applied.";
            }
            IUserMessage sentMessage = command.Message.Channel.SendMessageAsync(text: $"<@{userID}>", embed: new EmbedBuilder().WithTitle("Special Role Applied").WithDescription($"<@{command.Message.Author.Id}> has given <@{userID}> special role `{role.Name}`.\n{role.AddExplanation}\n{warnable.GetPastWarningsText()}{hadSameRoleBefore}").Build()).Result;
            if (!string.IsNullOrWhiteSpace(role.AddWarnText))
            {
                Warning warning = new() { GivenTo = userID, GivenBy = command.Message.Author.Id, TimeGiven = DateTimeOffset.UtcNow, Level = role.AddLevel, Reason = role.AddWarnText };
                warning.Link = LinkToMessage(sentMessage);
                warnable.AddWarning(warning);
            }
        }

        /// <summary>User command to remove a special role from a user.</summary>
        public void CMD_RemoveSpecialRole(GuildConfig.SpecialRole role, CommandData command)
        {
            SocketGuild guild = (command.Message.Channel as SocketGuildChannel).Guild;
            if (!DiscordModBot.IsModerator(command.Message.Author as SocketGuildUser))
            {
                SendErrorMessageReply(command.Message, "Not Authorized", "You're not allowed to do that.");
                return;
            }
            if (!DiscordModBot.WarningCommandHandler.GetTargetUser(command, true, true, out ulong userID))
            {
                return;
            }
            SocketGuildUser guildUser = guild.GetUser(userID);
            WarnableUser warnable = WarningUtilities.GetWarnableUser(guild.Id, userID);
            if (warnable.SeenNames.IsEmpty())
            {
                SendErrorMessageReply(command.Message, "Invalid Input", "Cannot alter that user: user has never been seen before.");
                return;
            }
            bool hasRole = warnable.SpecialRoles.Contains(role.Name);
            if (!hasRole)
            {
                SendGenericNegativeMessageReply(command.Message, "Cannot Remove Special Role", $"User <@{userID}> does not have the special role `{role.Name}`.");
                return;
            }
            if (guildUser != null)
            {
                SocketRole socketRole = guild.GetRole(role.RoleID);
                if (socketRole == null)
                {
                    SendErrorMessageReply(command.Message, "Failed To Remove", "Cannot remove special role: no matching role found.");
                    return;
                }
                guildUser.RemoveRoleAsync(socketRole).Wait();
            }
            warnable.SpecialRoles.Remove(role.Name);
            warnable.Save();
            IUserMessage sentMessage = command.Message.Channel.SendMessageAsync(embed: new EmbedBuilder().WithTitle("Special Role Removed").WithDescription($"<@{command.Message.Author.Id}> has removed the special role `{role.Name}` from <@{userID}>.\n{role.RemoveExplanation}").Build()).Result;
            if (!string.IsNullOrWhiteSpace(role.RemoveWarnText))
            {
                Warning warning = new() { GivenTo = userID, GivenBy = command.Message.Author.Id, TimeGiven = DateTimeOffset.UtcNow, Level = role.RemoveLevel, Reason = role.RemoveWarnText };
                warning.Link = LinkToMessage(sentMessage);
                warnable.AddWarning(warning);
            }
        }
    }
}
