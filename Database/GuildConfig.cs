using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using ModBot.WarningHandlers;

namespace ModBot.Database
{
    /// <summary>Represents per-guild configuration options.</summary>
    public class GuildConfig
    {
        /// <summary>The ID of a mute role (if any).</summary>
        public ulong? MuteRole { get; set; }

        /// <summary>The IDs of roles that are allowed moderator access.</summary>
        public List<ulong> ModeratorRoles { get; set; }

        /// <summary>What text to use to 'get attention' when a mute is given (eg. an @ mention to an admin).</summary>
        public string AttentionNotice { get; set; }

        /// <summary>The ID of the incident notice channel(s).</summary>
        public List<ulong> IncidentChannel { get; set; }

        /// <summary>The ID of the join log message channel(s).</summary>
        public List<ulong> JoinNotifChannel { get; set; }

        /// <summary>The ID of the voicechannel join/leave log message channel(s).</summary>
        public List<ulong> VoiceChannelJoinNotifs { get; set; }

        /// <summary>The ID of the role-change log message channel(s).</summary>
        public List<ulong> RoleChangeNotifChannel { get; set; }

        /// <summary>The ID of the nickname-change log message channel(s).</summary>
        public List<ulong> NameChangeNotifChannel { get; set; }

        /// <summary>The ID of the moderation activity (warns, bans, etc) log message channel(s).</summary>
        public List<ulong> ModLogsChannel { get; set; }

        /// <summary>
        /// Channels to log, mapping from (channel being logged) to (channel that shows the logs).
        /// Key value 0 means log all unspecified to there.
        /// </summary>
        public Dictionary<ulong, ulong> LogChannels { get; set; }

        /// <summary>Whether the ASCII name rule should be enforced by the bot.</summary>
        public bool EnforceAsciiNameRule { get; set; }

        /// <summary>Whether the A-Z first character name rule should be enforced by the bot.</summary>
        public bool EnforceNameStartRule { get; set; }

        /// <summary>If true, the name start rule (when enabled by <see cref="EnforceNameStartRule"/>) is more lenient and allows unicode symbols.</summary>
        public bool NameStartRuleLenient { get; set; }

        /// <summary>Whether warnings are enabled.</summary>
        public bool WarningsEnabled { get; set; }

        /// <summary>Whether (temp)bans are enabled.</summary>
        public bool BansEnabled { get; set; }

        /// <summary>The maximum ban duration allowed (if any).</summary>
        public string MaxBanDuration { get; set; }

        /// <summary>Whether to notify users about warnings received via a DM.</summary>
        public bool NotifyWarnsInDM { get; set; }

        /// <summary>Whether to automatically mute known spambots.</summary>
        public bool AutomuteSpambots { get; set; }

        /// <summary>Roles that definitely aren't spambots.</summary>
        public List<ulong> NonSpambotRoles { get; set; }

        /// <summary>Map of post IDs to react role data for automatic free roles from a react.</summary>
        public Dictionary<ulong, ReactRoleData> ReactRoles { get; set; }

        /// <summary>A map of special roles that persist across rejoins. Keys are names, values are data.</summary>
        public Dictionary<string, SpecialRole> SpecialRoles { get; set; }

        /// <summary>Data related to <see cref="ReactRoles"/>.</summary>
        public class ReactRoleData
        {
            /// <summary>A map from reaction ID to the role ID to be added.</summary>
            public Dictionary<string, ulong> ReactToRole { get; set; }
        }

        /// <summary>Represents a special role that a user can be stuck with.</summary>
        public class SpecialRole
        {
            /// <summary>The (modbot-side) name of the special role (not the same as the Discord role name).</summary>
            public string Name { get; set; }

            /// <summary>The ID of the role.</summary>
            public ulong RoleID { get; set; }

            /// <summary>The explanation text to display when adding the role to a user.</summary>
            public string AddExplanation { get; set; }

            /// <summary>The explanation text to display when removing the role from a user.</summary>
            public string RemoveExplanation { get; set; }

            /// <summary>The warning text to apply when adding the role (if any).</summary>
            public string AddWarnText { get; set; }

            /// <summary>The warning text to apply when removing the role (if any).</summary>
            public string RemoveWarnText { get; set; }

            /// <summary>The level of warning to apply when adding the role (if any).</summary>
            public WarningLevel AddLevel { get; set; }

            /// <summary>The level of warning to apply when removing the role (if any).</summary>
            public WarningLevel RemoveLevel { get; set; }

            /// <summary>A list of moderator commands that add this role to a user.</summary>
            public List<string> AddCommands { get; set; }

            /// <summary>A list of moderator commands that remove this role from a user.</summary>
            public List<string> RemoveCommands { get; set; }
        }

        /// <summary>Ensure all configuration options are either set properly, or are default.</summary>
        public void Ensure()
        {
            if (ModeratorRoles == null)
            {
                ModeratorRoles = new List<ulong>();
            }
            if (IncidentChannel == null)
            {
                IncidentChannel = new List<ulong>();
            }
            if (JoinNotifChannel == null)
            {
                JoinNotifChannel = new List<ulong>();
            }
            if (VoiceChannelJoinNotifs == null)
            {
                VoiceChannelJoinNotifs = new List<ulong>();
            }
            if (RoleChangeNotifChannel == null)
            {
                RoleChangeNotifChannel = new List<ulong>();
            }
            if (NameChangeNotifChannel == null)
            {
                NameChangeNotifChannel = new List<ulong>();
            }
            if (ModLogsChannel == null)
            {
                ModLogsChannel = new List<ulong>();
            }
            if (LogChannels == null)
            {
                LogChannels = new Dictionary<ulong, ulong>();
            }
            if (SpecialRoles == null)
            {
                SpecialRoles = new Dictionary<string, SpecialRole>();
            }
            if (NonSpambotRoles == null)
            {
                NonSpambotRoles = new List<ulong>();
            }
        }
    }
}
