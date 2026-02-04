using System;

namespace SNIBypassGUI.Common.Extensions
{
    public static class LongExtensions
    {
        /// <summary>
        /// Converts bytes to a human-readable string (e.g., "1.52 MB").
        /// </summary>
        /// <param name="byteCount">The size in bytes.</param>
        /// <param name="decimals">Number of decimal places.</param>
        public static string ToReadableSize(this long byteCount, int decimals = 2)
        {
            if (byteCount == 0) return "0 B";

            string[] suffixes = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));

            if (place >= suffixes.Length) place = suffixes.Length - 1;

            double num = Math.Round(bytes / Math.Pow(1024, place), decimals);
            string sign = byteCount < 0 ? "-" : "";

            return $"{sign}{num} {suffixes[place]}";
        }
    }
}
