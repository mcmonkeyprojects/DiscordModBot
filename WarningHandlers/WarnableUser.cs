using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using Discord;
using Discord.WebSocket;
using DiscordBotBase;
using DiscordBotBase.CommandHandlers;
using ModBot.Core;
using LiteDB;

namespace ModBot.WarningHandlers
{
    /// <summary>
    /// Represents a Discord user, containing handling for warning them.
    /// </summary>
    public class WarnableUser
    {
        /// <summary>
        /// The user's Discord ID, for database storage.
        /// This is an unchecked cast from ulong to long (last bit becomes a sign) to force the database to store it properly.
        /// </summary>
        public long DB_ID_Signed { get; set; }

        /// <summary>
        /// Gets the Discord user ID.
        /// </summary>
        public ulong UserID()
        {
            return unchecked((ulong)DB_ID_Signed);
        }

        /// <summary>
        /// ID of the relevant Discord guild/server.
        /// </summary>
        public ulong GuildID { get; set; }

        /// <summary>
        /// A list of the user's previous names.
        /// </summary>
        public List<OldName> SeenNames { get; set; }

        /// <summary>
        /// Helper class that represents a user's previous name.
        /// </summary>
        public class OldName
        {
            /// <summary>
            /// The old name's actual text.
            /// </summary>
            public string Name { get; set; }

            /// <summary>
            /// The first time the old name was seen.
            /// </summary>
            public DateTimeOffset FirstSeen { get; set; }
        }

        /// <summary>
        /// Whether the user is currently muted.
        /// </summary>
        public bool IsMuted { get; set; }

        /// <summary>
        /// A list of special roles applied to the user (special role names only, refer to per-server config for details of the role).
        /// </summary>
        public List<string> SpecialRoles { get; set; }

        /// <summary>
        /// A list of warnings and notes for this user.
        /// </summary>
        public List<Warning> Warnings { get; set; }

        /// <summary>
        /// The last known username for this user.
        /// </summary>
        public string LastKnownUsername { get; set; }

        /// <summary>
        /// Ensures the warnable user instance has all fields containing either a real value, or the default.
        /// </summary>
        public void Ensure()
        {
            if (Warnings == null)
            {
                Warnings = new List<Warning>();
            }
            if (SpecialRoles == null)
            {
                SpecialRoles = new List<string>();
            }
            if (SeenNames == null)
            {
                SeenNames = new List<OldName>();
            }
        }

        /// <summary>
        /// Returns a short single-line string indicating if a user has previous warnings or notes.
        /// Returns an empty string if none.
        /// </summary>
        public string GetPastWarningsText()
        {
            int warns = 0, notes = 0;
            foreach (Warning warn in Warnings)
            {
                if (warn.Level == WarningLevel.NOTE)
                {
                    notes++;
                }
                else
                {
                    warns++;
                }
            }
            if (warns > 0 && notes > 0)
            {
                return $"User has {warns} previous warnings and {notes} previous notes.";
            }
            else if (warns > 0)
            {
                return $"User has {warns} previous warnings.";
            }
            else if (notes > 0)
            {
                return $"User has {notes} previous notes.";
            }
            return "";
        }

        /// <summary>
        /// Adds a new warning to this user and saves the warning file.
        /// </summary>
        public void AddWarning(Warning warn)
        {
            Warnings.Insert(0, warn);
            Save();
            SocketGuild guild = DiscordBotBaseHelper.CurrentBot.Client.GetGuild(GuildID);
            string warnPostfix = warn.Level == WarningLevel.NOTE ? "" : " warning";
            string reason = (warn.Reason.Length > 250) ? (warn.Reason.Substring(0, 250) + "(... trimmed ...)") : warn.Reason;
            reason = UserCommands.EscapeUserInput(reason);
            string message = $"User <@{UserID()}> received a {warn.Level}{warnPostfix} from moderator <@{warn.GivenBy}>.\n\nReason: `{reason}`\n\n[Click For Details]({warn.Link})";
            Color color = warn.Level == WarningLevel.NOTE ? new Color(255, 255, 0) : new Color(255, 0, 0);
            ModBotLoggers.SendEmbedToAllFor(guild, DiscordModBot.GetConfig(GuildID).ModLogsChannel, new EmbedBuilder().WithColor(color).WithTitle("User Warning/Note Applied").WithDescription(message).Build());
        }

        /// <summary>
        /// Marks a username seen for the user. If the username has changed, returns the previous username.
        /// Returns whether the username is new (if 'true', the name is new and is in the out variable - if 'false', the name is old OR the this is the first known name for the user).
        /// </summary>
        public bool SeenUsername(string name, out string lastName)
        {
            lastName = LastKnownUsername;
            if (lastName == name)
            {
                return false;
            }
            Ensure();
            Console.WriteLine($"User ID {UserID()} in {GuildID} changed base username from {LastKnownUsername} to {name}");
            LastKnownUsername = name;
            if (!SeenNames.Any(n => n.Name == name))
            {
                SeenNames.Add(new OldName() { Name = name, FirstSeen = DateTimeOffset.Now });
            }
            Save();
            return lastName != null;
        }

        /// <summary>
        /// Saves the warnable user back to database.
        /// </summary>
        public void Save()
        {
            DiscordModBot.DatabaseHandler.GetDatabase(GuildID).Users.Upsert(DB_ID_Signed, this);
        }
    }
}
