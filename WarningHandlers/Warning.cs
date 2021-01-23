using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace ModBot.WarningHandlers
{
    /// <summary>
    /// Represents a single warning given to a user.
    /// </summary>
    public class Warning
    {
        /// <summary>
        /// The time the warning was given.
        /// </summary>
        public DateTimeOffset TimeGiven;

        /// <summary>
        /// Who the warning was given by (Discord ID).
        /// </summary>
        public ulong GivenBy;

        /// <summary>
        /// Who the warning was given to (Discord ID).
        /// </summary>
        public ulong GivenTo;

        /// <summary>
        /// The warning reason (as input by a helper).
        /// </summary>
        public string Reason;

        /// <summary>
        /// The warning level (as input by a helper).
        /// </summary>
        public WarningLevel Level;

        /// <summary>
        /// A generated link to where the warning was given.
        /// </summary>
        public string Link;
    }
}
