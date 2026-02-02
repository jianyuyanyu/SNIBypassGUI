using System;
using System.Linq;

namespace SNIBypassGUI.Common.Text
{
    /// <summary>
    /// Utilities for parsing command line arguments.
    /// </summary>
    public static class ArgumentUtils
    {
        /// <summary>
        /// Tries to get the value of a specific argument.
        /// e.g. If args contains "-config" and "file.ini", searching for "-config" returns "file.ini".
        /// </summary>
        /// <param name="args">The argument array.</param>
        /// <param name="argName">The name of the argument to look for.</param>
        /// <param name="value">The value of the argument if found.</param>
        /// <returns>True if the argument and its value exist; otherwise, false.</returns>
        public static bool TryGetArgumentValue(string[] args, string argName, out string value)
        {
            value = null;

            // Basic validation
            if (args == null || args.Length == 0) return false;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals(argName, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    value = args[i + 1];
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if the argument array contains a specific flag/argument.
        /// </summary>
        /// <param name="args">The argument array.</param>
        /// <param name="argName">The name of the argument to check.</param>
        /// <returns>True if the argument exists; otherwise, false.</returns>
        public static bool ContainsArgument(string[] args, string argName)
        {
            if (args == null) return false;
            return args.Contains(argName, StringComparer.OrdinalIgnoreCase);
        }
    }
}
