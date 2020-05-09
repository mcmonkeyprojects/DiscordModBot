using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Discord.Net;
using Discord;
using Discord.WebSocket;
using System.Diagnostics;
using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticDataSyntax;
using FreneticUtilities.FreneticToolkit;
using DiscordBotBase;
using DiscordBotBase.CommandHandlers;

namespace DiscordModBot
{
    /// <summary>
    /// Represents a Discord user, containing handling for warning them.
    /// </summary>
    public class WarnableUser
    {
        /// <summary>
        /// The configuration section for this user's warning file.
        /// </summary>
        public FDSSection WarningFileSection;

        /// <summary>
        /// The user's Discord ID.
        /// </summary>
        public ulong UserID;

        /// <summary>
        /// ID of the relevant Discord guild/server.
        /// </summary>
        public ulong ServerID;

        public IEnumerable<KeyValuePair<string, DateTimeOffset>> OldNames()
        {
            FDSSection names_section = WarningFileSection.GetSection("seen_names");
            if (names_section == null)
            {
                yield break;
            }
            foreach (string key in names_section.GetRootKeys())
            {
                FDSSection nameSection = names_section.GetRootData(key).Internal as FDSSection;
                DateTimeOffset time = StringConversionHelper.StringToDateTime(nameSection.GetString("first_seen_time")).Value;
                yield return new KeyValuePair<string, DateTimeOffset>(FDSUtility.UnEscapeKey(key), time);
            }
        }

        /// <summary>
        /// Gets or sets the muted status for this user.
        /// Setting does not save.
        /// </summary>
        public bool IsMuted
        {
            get
            {
                return WarningFileSection.GetBool("is_muted", false).Value;
            }
            set
            {
                WarningFileSection.Set("is_muted", value);
            }
        }

        /// <summary>
        /// Gets or sets the do-not-support status for this user.
        /// Setting does not save.
        /// </summary>
        public bool IsDoNotSupport
        {
            get
            {
                return WarningFileSection.GetBool("is_nosupport", false).Value;
            }
            set
            {
                WarningFileSection.Set("is_nosupport", value);
            }
        }

        /// <summary>
        /// Gets all warnings for this user, starting at most recent and going back in time.
        /// </summary>
        public IEnumerable<Warning> GetWarnings()
        {
            long? currentId = WarningFileSection.GetLong("current_id", null);
            if (currentId == null)
            {
                yield break;
            }
            long currentValue = currentId.Value;
            for (long i = currentValue; i > 0; i--)
            {
                if (WarningFileSection.HasKey("warnings." + i))
                {
                    yield return Warning.FromSection(WarningFileSection.GetSection("warnings." + i), UserID);
                }
            }
        }

        /// <summary>
        /// Adds a new warning to this user and saves the warning file.
        /// </summary>
        public void AddWarning(Warning warn)
        {
            long currentId = WarningFileSection.GetLong("current_id", 0).Value + 1;
            WarningFileSection.Set("current_id", currentId);
            FDSSection newSection = new FDSSection();
            warn.SaveToSection(newSection);
            WarningFileSection.Set("warnings." + currentId, newSection);
            Save();
            SocketGuild guild = DiscordBotBaseHelper.CurrentBot.Client.GetGuild(ServerID);
            string warnPostfix = warn.Level == WarningLevel.NOTE ? "" : " warning";
            string reason = (warn.Reason.Length > 250) ? (warn.Reason.Substring(0, 250) + "(... trimmed ...)") : warn.Reason;
            reason = UserCommands.EscapeUserInput(reason);
            string message = $"User <@{UserID}> received a {warn.Level}{warnPostfix} from moderator <@{warn.GivenBy}>.\n\nReason: `{reason}`\n\n[Click For Details]({warn.Link})";
            Color color = warn.Level == WarningLevel.NOTE ? new Color(255, 255, 0) : new Color(255, 0, 0);
            ModBotLoggers.SendEmbedToAllFor(guild, DiscordModBot.ModLogsChannel, new EmbedBuilder().WithColor(color).WithTitle("User Warning/Note Applied").WithDescription(message).Build());
        }

        /// <summary>
        /// Gets or sets the last known username for this user.
        /// Setting does not save.
        /// </summary>
        public string LastKnownUsername
        {
            get
            {
                return WarningFileSection.GetString("last_known_username");
            }
            set
            {
                WarningFileSection.Set("last_known_username", value);
            }
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
            LastKnownUsername = name;
            string escapedName = FDSUtility.EscapeKey(name);
            FDSSection names_section = WarningFileSection.GetSection("seen_names");
            if (names_section == null)
            {
                names_section = new FDSSection();
                WarningFileSection.Set("seen_names", names_section);
            }
            if (!names_section.HasRootKey(escapedName))
            {
                FDSSection nameSection = new FDSSection();
                nameSection.SetRoot("first_seen_time", StringConversionHelper.DateTimeToString(DateTimeOffset.Now, false));
                names_section.SetRoot(escapedName, nameSection);
            }
            Save();
            return lastName != null;
        }

        /// <summary>
        /// Adds a comment to a base key if needed and able.
        /// </summary>
        public void AddCommentIfNeeded(string key, string comment)
        {
            FDSData data = WarningFileSection.GetData(key);
            if (data != null && data.PrecedingComments.Count == 0)
            {
                data.AddComment(comment);
            }
        }

        /// <summary>
        /// Adds (if needed) default comments for each base level key.
        /// </summary>
        public void DefaultComments()
        {
            AddCommentIfNeeded("current_id", "Current warning ID number. Equal to the number of warnings currently listed.");
            AddCommentIfNeeded("warnings", "All warnings listed for this user.");
            AddCommentIfNeeded("last_known_username", "Last username seen attached to this user.");
            AddCommentIfNeeded("seen_names", "All names seen from this user.");
            AddCommentIfNeeded("is_muted", "Whether this user is muted (or should be).");
        }

        public static Object SaveLock = new Object();

        /// <summary>
        /// Save the warning file.
        /// </summary>
        public void Save()
        {
            DefaultComments();
            lock (SaveLock)
            {
                Directory.CreateDirectory("./warnings/");
                Directory.CreateDirectory("./warnings/" + ServerID + "/");
                FDSUtility.SaveToFile(WarningFileSection, "./warnings/" + ServerID + "/" + UserID + ".fds");
            }
        }
    }
}
