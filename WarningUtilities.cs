using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using Discord;
using Discord.WebSocket;
using FreneticUtilities.FreneticDataSyntax;

namespace DiscordModBot
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
            return (user as SocketGuildUser).Roles.Any((role) => role.Name.ToLowerInvariant() == DiscordModBot.MuteRoleName);
        }

        /// <summary>
        /// Lock object for warning handling.
        /// </summary>
        public static Object WarnLock = new Object();

        /// <summary>
        /// Warns a user (by Discord ID and pre-completed warning object).
        /// </summary>
        public static WarnableUser Warn(ulong serverId, ulong id, Warning warn)
        {
            lock (WarnLock)
            {
                WarnableUser user = GetWarnableUser(serverId, id);
                user.AddWarning(warn);
                return user;
            }
        }

        /// <summary>
        /// Gets the <see cref="WarnableUser"/> object for a Discord user (by Discord ID).
        /// </summary>
        public static WarnableUser GetWarnableUser(ulong serverId, ulong id)
        {
            string fname = "./warnings/" + serverId + "/" + id + ".fds";
            return new WarnableUser() { UserID = id, ServerID = serverId, WarningFileSection = File.Exists(fname) ? FDSUtility.ReadFile(fname) : new FDSSection() };
        }
    }
}
