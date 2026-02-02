using System;
using System.Linq;

namespace SNIBypassGUI.Common.Text
{
    public static class StringUtils
    {
        #region Delimited String Operations

        /// <summary>
        /// Swaps the positions of two items in a delimited string.
        /// </summary>
        [Obsolete]
        public static string SwapListItems(string inputString, string itemA, string itemB, char separator = ',')
        {
            if (string.IsNullOrEmpty(inputString)) return string.Empty;

            var list = inputString.Split([separator], StringSplitOptions.RemoveEmptyEntries).ToList();

            int indexA = list.IndexOf(itemA);
            int indexB = list.IndexOf(itemB);

            if (indexA != -1 && indexB != -1)
                (list[indexA], list[indexB]) = (list[indexB], list[indexA]);

            return string.Join(separator.ToString(), list);
        }

        /// <summary>
        /// Adds an item to the end of the string. If it already exists, it is moved to the end.
        /// </summary>
        [Obsolete]
        public static string AddOrMoveItemToEnd(string inputString, string item, char separator = ',')
        {
            string cleanedString = RemoveItem(inputString, item, separator);
            return string.IsNullOrEmpty(cleanedString)
                ? item
                : $"{cleanedString}{separator}{item}";
        }

        /// <summary>
        /// Removes a specific item from a delimited string.
        /// </summary>
        [Obsolete]
        public static string RemoveItem(string inputString, string itemToRemove, char separator = ',')
        {
            if (string.IsNullOrEmpty(inputString)) return string.Empty;

            var items = inputString.Split([separator], StringSplitOptions.RemoveEmptyEntries)
                .Where(x => x != itemToRemove);

            return string.Join(separator.ToString(), items);
        }

        /// <summary>
        /// Replaces a specific item in a delimited string with a new one.
        /// </summary>
        [Obsolete]
        public static string ReplaceItem(string inputString, string oldItem, string newItem, char separator = ',')
        {
            if (string.IsNullOrEmpty(inputString)) return string.Empty;

            var items = inputString.Split([separator], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x == oldItem ? newItem : x);

            return string.Join(separator.ToString(), items);
        }

        #endregion

        #region String Manipulation

        /// <summary>
        /// Joins non-empty strings with a specified separator.
        /// </summary>
        [Obsolete]
        public static string JoinNonEmpty(string separator, params string[] args) =>
            string.Join(separator, args.Where(arg => !string.IsNullOrEmpty(arg)));

        /// <summary>
        /// Splits a string by separators and removes empty entries.
        /// </summary>
        [Obsolete]
        public static string[] SplitToNonEmptyArray(string input, params string[] separators)
        {
            if (string.IsNullOrEmpty(input)) return [];
            return [.. input.Split(separators, StringSplitOptions.RemoveEmptyEntries).Where(arg => !string.IsNullOrEmpty(arg))];
        }
        #endregion
    }
}
