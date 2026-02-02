using System;
using System.Text.RegularExpressions;

namespace SNIBypassGUI.Common.Extensions
{
    /// <summary>
    /// Provides extension methods for string manipulation and conversion.
    /// </summary>
    public static class StringExtensions
    {
        /// <summary>
        /// Returns a value indicating whether a specified substring occurs within this string, using the specified comparison rules.
        /// (Backport for .NET Framework versions older than .NET Core 2.1)
        /// </summary>
        /// <param name="source">The string to search in.</param>
        /// <param name="toCheck">The string to seek.</param>
        /// <param name="comp">One of the enumeration values that specifies the rules for the search.</param>
        /// <returns><c>true</c> if the value parameter occurs within this string, or if value is the empty string; otherwise, <c>false</c>.</returns>
        public static bool Contains(this string source, string toCheck, StringComparison comp)
        {
            if (string.IsNullOrEmpty(source)) return false;
            if (string.IsNullOrEmpty(toCheck)) return true;

            return source.IndexOf(toCheck, comp) >= 0;
        }

        /// <summary>
        /// Converts the string representation of a number to an 32-bit signed integer.
        /// Returns the specified default value if the conversion fails.
        /// </summary>
        /// <param name="input">The string to convert.</param>
        /// <param name="defaultValue">The value to return if conversion fails. Defaults to 0.</param>
        /// <returns>The integer value of the string if successful; otherwise, the default value.</returns>
        public static int ToInt(this string input, int defaultValue = 0) =>
            int.TryParse(input, out int result) ? result : defaultValue;

        /// <summary>
        /// Converts the string to a boolean value.
        /// Returns <c>true</c> for "true", "1", "yes", "on" (case-insensitive); otherwise <c>false</c>.
        /// </summary>
        /// <param name="input">The string to convert.</param>
        /// <returns>The boolean representation of the string.</returns>
        public static bool ToBool(this string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;

            string cleanInput = input.Trim();
            return cleanInput.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   cleanInput.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   cleanInput.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   cleanInput.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sanitizes the string to be used as a safe identifier (e.g., for control names or filenames).
        /// Removes all characters except letters, digits, and underscores.
        /// </summary>
        /// <param name="input">The input string to sanitize.</param>
        /// <returns>A string containing only alphanumeric characters and underscores.</returns>
        public static string ToSafeIdentifier(this string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return Regex.Replace(input, @"[^a-zA-Z0-9_]", "");
        }
    }
}
