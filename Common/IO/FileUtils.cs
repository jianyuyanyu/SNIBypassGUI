using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static SNIBypassGUI.Common.LogManager;

namespace SNIBypassGUI.Common.IO
{
    public static class FileUtils
    {
        #region Basic Read/Write

        public static async Task<string> ReadAllTextAsync(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        public static Task WriteAllTextAsync(string path, string contents) =>
            WriteAllTextAsync(path, contents, new UTF8Encoding(false));

        public static async Task WriteAllTextAsync(string path, string contents, Encoding encoding)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
            using var writer = new StreamWriter(stream, encoding);
            await writer.WriteAsync(contents);
        }

        /// <summary>
        /// Prepends content to the beginning of a specified file.
        /// </summary>
        public static void PrependToFile(string filePath, string[] linesToAdd)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    File.WriteAllLines(filePath, linesToAdd);
                    return;
                }

                // Read all existing lines, combine with new lines, and rewrite
                string[] existingLines = File.ReadAllLines(filePath);
                var allLines = new string[linesToAdd.Length + existingLines.Length];
                linesToAdd.CopyTo(allLines, 0);
                existingLines.CopyTo(allLines, linesToAdd.Length);
                File.WriteAllLines(filePath, allLines);
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred while prepending to file {filePath}.", LogLevel.Error, ex);
                throw;
            }
        }

        public static void PrependToFile(string filePath, List<string> linesToAdd) => PrependToFile(filePath, linesToAdd.ToArray());
        public static void PrependToFile(string filePath, string lineToAdd) => PrependToFile(filePath, new[] { lineToAdd });

        /// <summary>
        /// Appends content to the end of a specified file.
        /// </summary>
        public static void AppendToFile(string filePath, string[] linesToAdd, Encoding encoding = null)
        {
            try
            {
                encoding ??= Encoding.UTF8;
                if (!File.Exists(filePath))
                {
                    File.WriteAllLines(filePath, linesToAdd);
                    return;
                }
                using StreamWriter writer = new(filePath, true, encoding);
                foreach (string line in linesToAdd) writer.WriteLine(line);
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred while appending to file {filePath}.", LogLevel.Error, ex);
                throw;
            }
        }

        public static void AppendToFile(string filePath, List<string> linesToAdd, Encoding encoding = null) => AppendToFile(filePath, linesToAdd.ToArray(), encoding);
        public static void AppendToFile(string filePath, string lineToAdd, Encoding encoding = null) => AppendToFile(filePath, new[] { lineToAdd }, encoding);

        #endregion

        #region Block/Section Manipulation

        /// <summary>
        /// Removes a block of text delimited by custom markers: "# [Tab] sectionName Start" to "# [Tab] sectionName End".
        /// </summary>
        [Obsolete]
        public static void RemoveSection(string filePath, string sectionName)
        {
            if (!File.Exists(filePath))
            {
                WriteLog($"File {filePath} not found!", LogLevel.Warning);
                return;
            }

            string startMarker = $"#\t{sectionName} Start";
            string endMarker = $"#\t{sectionName} End";
            string tempFilePath = Path.GetTempFileName();

            try
            {
                bool isRemoving = false;

                using (StreamReader reader = new(filePath))
                using (StreamWriter writer = new(tempFilePath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line == startMarker)
                        {
                            isRemoving = true;
                            continue;
                        }
                        else if (line == endMarker)
                        {
                            isRemoving = false;
                            continue;
                        }

                        if (!isRemoving) writer.WriteLine(line);
                    }
                }
                TryReplaceFile(tempFilePath, filePath);
                WriteLog($"Successfully removed section '{sectionName}' from {filePath}.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred while removing section '{sectionName}' from {filePath}.", LogLevel.Error, ex);
                throw;
            }
        }

        /// <summary>
        /// Removes multiple blocks of text delimited by custom markers.
        /// </summary>
        [Obsolete]
        public static void RemoveSection(string filePath, string[] sectionNames)
        {
            if (!File.Exists(filePath))
            {
                WriteLog($"File {filePath} not found!", LogLevel.Warning);
                return;
            }

            string tempFilePath = Path.GetTempFileName();
            try
            {
                using (StreamReader reader = new(filePath))
                using (StreamWriter writer = new(tempFilePath))
                {
                    string line;
                    bool isRemoving = false;
                    string currentSectionToRemove = null;

                    while ((line = reader.ReadLine()) != null)
                    {
                        // Check for start markers
                        bool isStartMarker = false;
                        foreach (string sectionName in sectionNames)
                        {
                            if (line == $"#\t{sectionName} Start")
                            {
                                isStartMarker = true;
                                isRemoving = true;
                                currentSectionToRemove = sectionName;
                                break;
                            }
                        }

                        if (isStartMarker) continue;

                        if (isRemoving)
                        {
                            // Check for end marker of the current section
                            foreach (string sectionName in sectionNames)
                            {
                                if (line == $"#\t{sectionName} End" && currentSectionToRemove == sectionName)
                                {
                                    isRemoving = false;
                                    currentSectionToRemove = null;
                                    break;
                                }
                            }
                            // If we hit a matching end marker, we broke out of the loop and set isRemoving to false.
                            // We should verify if 'isRemoving' became false to execute the 'continue' logic correctly.
                            // Logic: If we found the end marker, we skip writing this line.
                            if (!isRemoving && currentSectionToRemove == null) continue;

                            // If still removing, skip writing the content
                            continue;
                        }

                        writer.WriteLine(line);
                    }
                }
                TryReplaceFile(tempFilePath, filePath);
                WriteLog($"Successfully removed multiple sections from {filePath}.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred while removing multiple sections from {filePath}.", LogLevel.Error, ex);
                throw;
            }
        }

        /// <summary>
        /// Retrieves the content of a block delimited by custom markers: "# [Tab] sectionName Start" to "# [Tab] sectionName End".
        /// </summary>
        [Obsolete]
        public static string[] GetSection(string filePath, string sectionName)
        {
            if (!File.Exists(filePath))
            {
                WriteLog($"File {filePath} not found!", LogLevel.Warning);
                return [];
            }

            string startMarker = $"#\t{sectionName} Start";
            string endMarker = $"#\t{sectionName} End";
            bool isInSection = false;
            List<string> sectionLines = [];

            try
            {
                using (StreamReader reader = new(filePath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!isInSection)
                        {
                            if (line == startMarker)
                            {
                                isInSection = true;
                                sectionLines.Add(line);
                            }
                        }
                        else
                        {
                            sectionLines.Add(line);
                            if (line == endMarker) break;
                        }
                    }
                }

                if (!isInSection || sectionLines.Count == 0 || sectionLines[sectionLines.Count - 1] != endMarker)
                {
                    if (isInSection)
                        WriteLog($"Found start marker for '{sectionName}' in {filePath} but reached EOF without end marker.", LogLevel.Warning);
                }

                return [.. sectionLines];
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred while getting section '{sectionName}' from {filePath}.", LogLevel.Error, ex);
                throw;
            }
        }

        #endregion

        #region Directory & File Management

        public static void EnsureDirectoryExists(string path)
        {
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred while creating directory {path}.", LogLevel.Error, ex);
                throw;
            }
        }

        public static void EnsureFileExists(string filePath)
        {
            try
            {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) EnsureDirectoryExists(dir);
                if (!File.Exists(filePath)) File.Create(filePath).Dispose();
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred while creating file {filePath}.", LogLevel.Error, ex);
                throw;
            }
        }

        public static void ClearFolder(string folderPath, bool deleteFilesIndividually = false)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    var ex = new DirectoryNotFoundException($"Directory not found: {folderPath}");
                    WriteLog($"Directory path {folderPath} does not exist.", LogLevel.Error, ex);
                    throw ex;
                }

                if (deleteFilesIndividually) EmptyFolderRecursive(folderPath);
                else
                {
                    Directory.Delete(folderPath, true);
                    Directory.CreateDirectory(folderPath);
                }
            }
            catch (Exception ex)
            {
                if (ex is not DirectoryNotFoundException)
                    WriteLog($"Exception occurred while clearing directory {folderPath}.", LogLevel.Error, ex);
                throw;
            }
        }

        private static void EmptyFolderRecursive(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return;

            foreach (string file in Directory.GetFiles(folderPath))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    WriteLog($"Exception occurred while deleting file {file}.", LogLevel.Error, ex);
                    throw;
                }
            }

            foreach (string dir in Directory.GetDirectories(folderPath))
            {
                try
                {
                    EmptyFolderRecursive(dir);
                    Directory.Delete(dir, true);
                }
                catch (Exception ex)
                {
                    WriteLog($"Exception occurred while deleting directory {dir}.", LogLevel.Error, ex);
                    throw;
                }
            }
        }

        public static void TryDelete(string path, int maxRetries = 5, int delayMilliseconds = 500)
        {
            int retryCount = 0;
            while (retryCount < maxRetries)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    else if (Directory.Exists(path)) Directory.Delete(path, true);
                    return;
                }
                catch (IOException ex)
                {
                    WriteLog($"IOException deleting {path}. Retry: {retryCount + 1}/{maxRetries}.", LogLevel.Warning, ex);
                    Thread.Sleep(delayMilliseconds);
                    retryCount++;
                }
                catch (Exception ex)
                {
                    WriteLog($"Exception occurred while deleting {path}.", LogLevel.Error, ex);
                    throw;
                }
            }
            WriteLog($"Failed to delete {path} after max retries.", LogLevel.Warning);
            throw new IOException($"Failed to delete {path} after max retries.");
        }

        public static void TryDelete(string[] paths) { foreach (var p in paths) TryDelete(p); }
        public static void TryDelete(List<string> paths) => TryDelete(paths.ToArray());

        /// <summary>
        /// Tries to replace a file. Handles cross-volume replacements by copy-delete fallback.
        /// </summary>
        public static bool TryReplaceFile(string sourceFilePath, string destinationFilePath)
        {
            try
            {
                string sourceRoot = Path.GetPathRoot(sourceFilePath);
                string destRoot = Path.GetPathRoot(destinationFilePath);

                if (string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase))
                {
                    File.Replace(sourceFilePath, destinationFilePath, null);
                }
                else
                {
                    string tempFilePath = Path.Combine(destRoot, Guid.NewGuid().ToString() + ".tmp");
                    try
                    {
                        File.Copy(sourceFilePath, tempFilePath, true);
                        File.Replace(tempFilePath, destinationFilePath, null);
                        File.Delete(sourceFilePath);
                    }
                    catch
                    {
                        if (File.Exists(tempFilePath)) TryDelete(tempFilePath);
                        throw;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                WriteLog("Exception occurred while trying to replace file.", LogLevel.Error, ex);
                return false;
            }
        }

        #endregion

        #region Resources & Metrics

        public static void ExtractResourceToFile(byte[] resource, string path)
        {
            using FileStream file = new(path, FileMode.Create);
            file.Write(resource, 0, resource.Length);
        }

        public static long GetFileSize(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return 0;
                return new FileInfo(filePath).Length;
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred getting size for {filePath}.", LogLevel.Error, ex);
                throw;
            }
        }

        public static long GetDirectorySize(string directoryPath)
        {
            long size = 0;
            try
            {
                if (!Directory.Exists(directoryPath))
                {
                    WriteLog($"Directory path {directoryPath} not found.", LogLevel.Warning);
                    return 0;
                }
                string[] files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
                foreach (string file in files) size += GetFileSize(file);
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred getting size for directory {directoryPath}.", LogLevel.Error, ex);
                throw;
            }
            return size;
        }

        public static long GetTotalSize(string[] paths)
        {
            long totalSize = 0;
            foreach (var path in paths)
            {
                if (File.Exists(path)) totalSize += GetFileSize(path);
                else if (Directory.Exists(path)) totalSize += GetDirectorySize(path);
                else WriteLog($"Path {path} does not exist.", LogLevel.Warning);
            }
            return totalSize;
        }

        public static string CalculateFileHash(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                WriteLog("File not found for hash calculation.", LogLevel.Warning);
                return null;
            }
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = new BufferedStream(File.OpenRead(filePath), 1024 * 1024);
                byte[] hashBytes = sha256.ComputeHash(stream);

                StringBuilder hashStringBuilder = new(64);
                foreach (byte b in hashBytes) hashStringBuilder.Append(b.ToString("x2"));
                return hashStringBuilder.ToString();
            }
            catch (Exception ex)
            {
                WriteLog("Exception occurred while calculating file hash.", LogLevel.Error, ex);
                return null;
            }
        }

        #endregion
    }
}
