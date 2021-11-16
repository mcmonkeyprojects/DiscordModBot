using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace ModBot.WarningHandlers
{
    /// <summary>Represents a single warning given to a user.</summary>
    public class Warning
    {
        /// <summary>The time the warning was given.</summary>
        public DateTimeOffset TimeGiven { get; set; }

        /// <summary>Who the warning was given by (Discord ID).</summary>
        public ulong GivenBy { get; set; }

        /// <summary>Who the warning was given to (Discord ID).</summary>
        public ulong GivenTo { get; set; }

        /// <summary>The warning reason (as input by a helper).</summary>
        public string Reason { get; set; }

        /// <summary>The warning level (as input by a helper).</summary>
        public WarningLevel Level { get; set; }

        /// <summary>A generated link to where the warning was given.</summary>
        public string Link { get; set; }
    }
}
