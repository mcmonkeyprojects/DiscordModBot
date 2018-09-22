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

namespace WarningBot
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
        }

        /// <summary>
        /// Save the warning file.
        /// </summary>
        public void Save()
        {
            Directory.CreateDirectory("./warnings/");
            FDSUtility.SaveToFile(WarningFileSection, "./warnings/" + UserID + ".fds");
        }
    }
}
