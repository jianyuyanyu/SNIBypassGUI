using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using static SNIBypassGUI.Common.Interop.Kernel32;
using static SNIBypassGUI.Common.LogManager;

namespace SNIBypassGUI.Common.IO
{
    public static class IniUtils
    {
        /// <summary>
        /// Writes a string value to the initialization (INI) file.
        /// </summary>
        /// <param name="section">The section name.</param>
        /// <param name="key">The key name.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="path">The full path to the INI file.</param>
        public static void WriteString(string section, string key, string value, string path)
            => WritePrivateProfileString(section, key, value, path);

        /// <summary>
        /// Reads a string value from the initialization (INI) file.
        /// </summary>
        /// <param name="section">The section name.</param>
        /// <param name="key">The key name.</param>
        /// <param name="path">The full path to the INI file.</param>
        /// <param name="defaultValue">The value to return if the key is not found (default is empty).</param>
        /// <returns>The value read from the file.</returns>
        public static string ReadString(string section, string key, string path, string defaultValue = "")
        {
            int size = 1024; // Initial buffer size
            StringBuilder temp = new(size);
            int ret = GetPrivateProfileString(section, key, defaultValue, temp, size, path);

            // If the return value is size - 1 (or size - 2 for some OS versions), it means the buffer was too small.
            // We expand the buffer until it fits or reaches a hard limit (64KB).
            while ((ret == size - 1 || ret == size - 2) && size < 65536)
            {
                size *= 2; // Double the buffer size
                temp.EnsureCapacity(size);
                ret = GetPrivateProfileString(section, key, defaultValue, temp, size, path);
            }

            // If it is still truncated at the maximum limit, log a warning.
            if (ret >= size - 1)
                WriteLog($"INI value for [{section}] {key} is too long and may be truncated.", LogLevel.Warning);

            return temp.ToString();
        }

        /// <summary>
        /// Writes a boolean value to the INI file (as "true" or "false").
        /// </summary>
        public static void WriteBool(string section, string key, bool value, string path)
            => WriteString(section, key, value.ToString().ToLower(), path);

        /// <summary>
        /// Syntactic sugar to explicitly set a key to "true".
        /// </summary>
        public static void SetTrue(string section, string key, string path)
            => WriteBool(section, key, true, path);

        /// <summary>
        /// Syntactic sugar to explicitly set a key to "false".
        /// </summary>
        public static void SetFalse(string section, string key, string path)
            => WriteBool(section, key, false, path);

        /// <summary>
        /// Reads a boolean value from the INI file.
        /// Supports "true", "1", "yes", "on" as true.
        /// </summary>
        /// <param name="defaultValue">The default boolean value if the key is missing or invalid.</param>
        public static bool ReadBool(string section, string key, string path, bool defaultValue = false)
        {
            string value = ReadString(section, key, path, defaultValue.ToString());

            if (string.IsNullOrWhiteSpace(value)) return defaultValue;

            // Normalize checks for common "true" indicators
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Retrieves all key names from a specific section.
        /// </summary>
        public static List<string> GetKeys(string section, string path)
        {
            List<string> keys = [];

            if (!File.Exists(path)) return keys;

            // Note: INI files are typically ANSI/ASCII encoded. 
            // Encoding.Default uses the system's current ANSI code page.
            Encoding fileEncoding = Encoding.Default;

            try
            {
                using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using StreamReader reader = new(fs, fileEncoding);

                string line;
                bool isInSection = false;
                string targetSectionHeader = $"[{section}]";

                while ((line = reader.ReadLine()) != null)
                {
                    string trimmedLine = line.Trim();

                    // Skip empty lines or comments
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith(";") || trimmedLine.StartsWith("#"))
                        continue;

                    // Check if we entered the target section
                    if (trimmedLine.Equals(targetSectionHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        isInSection = true;
                        continue;
                    }

                    // If we are in the section
                    if (isInSection)
                    {
                        // If we hit another section start like "[Other]", stop reading
                        if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                            break;

                        // Parse key
                        int equalIndex = trimmedLine.IndexOf('=');
                        if (equalIndex > 0)
                        {
                            string key = trimmedLine.Substring(0, equalIndex).Trim();
                            if (!string.IsNullOrEmpty(key)) keys.Add(key);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Failed to get keys from section [{section}].", LogLevel.Error, ex);
            }

            return keys;
        }

        /// <summary>
        /// Renames INI sections in a file (e.g., changes "[OldName]" to "[NewName]").
        /// </summary>
        /// <param name="oldNames">List of section names to find.</param>
        /// <param name="newNames">List of new names to replace with.</param>
        /// <param name="filePath">Path to the INI file.</param>
        public static void RenameSection(string[] oldNames, string[] newNames, string filePath)
        {
            if (oldNames == null || newNames == null || oldNames.Length != newNames.Length || string.IsNullOrEmpty(filePath))
            {
                WriteLog("Invalid arguments for RenameSection.", LogLevel.Warning);
                return;
            }

            if (!File.Exists(filePath))
            {
                WriteLog($"INI File {filePath} does not exist.", LogLevel.Warning);
                return;
            }

            string tempFilePath = filePath + ".temp";
            try
            {
                // Note: Using Encoding.Default to maintain compatibility with legacy INI formats (ANSI).
                using (var reader = new StreamReader(filePath, Encoding.Default))
                using (var writer = new StreamWriter(tempFilePath, false, Encoding.Default))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        bool replaced = false;
                        string trimmedLine = line.Trim();
                        if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                        {
                            string sectionName = trimmedLine.Substring(1, trimmedLine.Length - 2);
                            for (int i = 0; i < oldNames.Length; i++)
                            {
                                if (sectionName.Equals(oldNames[i], StringComparison.OrdinalIgnoreCase))
                                {
                                    writer.WriteLine($"[{newNames[i].Trim()}]");
                                    replaced = true;
                                    break;
                                }
                            }
                        }

                        if (!replaced) writer.WriteLine(line);
                    }
                }

                FileUtils.TryReplaceFile(tempFilePath, filePath);
            }
            catch (Exception ex)
            {
                WriteLog("Exception occurred while renaming INI sections.", LogLevel.Error, ex);
                FileUtils.TryDelete(tempFilePath);
            }
        }

        /// <summary>
        /// Sanitizes a string to be used as a safe INI key or section name.
        /// Removes characters that conflict with INI syntax (e.g., [], =).
        /// </summary>
        /// <param name="originalName">The original string.</param>
        /// <returns>A sanitized string safe for INI usage.</returns>
        public static string SanitizeKey(string originalName)
        {
            if (string.IsNullOrEmpty(originalName))
                return Guid.NewGuid().ToString("N");

            // Optimized: Using StringBuilder to avoid multiple string allocations
            StringBuilder sb = new(originalName.Length);
            foreach (char c in originalName)
            {
                switch (c)
                {
                    case ' ':
                    case '(':
                    case ')':
                    case '[':
                    case ']':
                    case '=':
                        continue; // Skip these INI-sensitive or unwanted characters
                    default:
                        sb.Append(c);
                        break;
                }
            }

            string result = sb.ToString().Trim();

            // Fallback if the string becomes empty after sanitization (e.g. input was "[]")
            return string.IsNullOrEmpty(result) ? Guid.NewGuid().ToString("N") : result;
        }
    }
}
