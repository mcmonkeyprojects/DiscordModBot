using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using Discord;
using Discord.WebSocket;
using ModBot.Database;
using ModBot.Core;

namespace ModBot.WarningHandlers
{
    /// <summary>
    /// Utilities related to warning and mute management.
    /// </summary>
    public class WarningUtilities
    {
        /// <summary>
        /// Returns whether a Discord user is current muted (checks via configuration value for the mute role name).
        /// </summary>
        public static bool IsMuted(IGuildUser user)
        {
            GuildConfig config = DiscordModBot.GetConfig(user.GuildId);
            if (!config.MuteRole.HasValue)
            {
                return false;
            }
            return (user as SocketGuildUser).Roles.Any((role) => role.Id == config.MuteRole.Value);
        }

        /// <summary>
        /// Gets the <see cref="WarnableUser"/> object for a Discord user (by Discord ID).
        /// </summary>
        public static WarnableUser GetWarnableUser(ulong guildId, ulong id)
        {
            ModBotDatabaseHandler.Guild guildData = DiscordModBot.DatabaseHandler.GetDatabase(guildId);
            lock (guildData)
            {
                WarnableUser user = guildData.Users.FindById(unchecked((long)id));
                if (user == null)
                {
                    user = guildData.Users_Outdated.FindById(id);
                    if (user == null)
                    {
                        user = new WarnableUser() { DB_ID_Signed = unchecked((long)id), GuildID = guildId };
                    }
                }
                if (user.DB_ID_Signed != unchecked((long)id) || user.RawUserID > 1000UL)
                {
                    user.DB_ID_Signed = unchecked((long)id);
                    user.RawUserID = 0;
                    user.Ensure();
                    user.Save();
                }
                return user;
            }
        }
    }
}
