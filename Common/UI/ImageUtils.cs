using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using static SNIBypassGUI.Common.LogManager;

namespace SNIBypassGUI.Common.UI
{
    public static class ImageUtils
    {
        /// <summary>
        /// Loads an image from a file path with support for caching and dynamic decoding size.
        /// </summary>
        public static BitmapImage LoadImage(string imagePath, Dictionary<string, BitmapImage> cache = null, int? maxDecodeSize = null)
        {
            if (cache != null && cache.TryGetValue(imagePath, out BitmapImage cachedImage)) return cachedImage;

            BitmapImage image = new();
            try
            {
                image.BeginInit();
                image.UriSource = new Uri(imagePath);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache | BitmapCreateOptions.IgnoreColorProfile;

                if (maxDecodeSize.HasValue)
                {
                    var (w, h) = GetImageSize(imagePath);
                    if (w > maxDecodeSize || h > maxDecodeSize)
                    {
                        double ratio = Math.Min((double)maxDecodeSize.Value / w, (double)maxDecodeSize.Value / h);
                        image.DecodePixelWidth = (int)(w * ratio);
                        image.DecodePixelHeight = (int)(h * ratio);
                    }
                }

                image.EndInit();
                image.Freeze();

                cache?[imagePath] = image;
            }
            catch (FileNotFoundException)
            {
                WriteLog($"Image file {imagePath} not found!", LogLevel.Warning);
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred loading image {imagePath}.", LogLevel.Error, ex);
            }

            return image;
        }

        /// <summary>
        /// Converts a Base64 string to a BitmapImage.
        /// </summary>
        public static BitmapImage Base64ToBitmapImage(string base64String)
        {
            if (string.IsNullOrEmpty(base64String))
            {
                WriteLog("Input Base64 string is empty.", LogLevel.Warning);
                return null;
            }

            try
            {
                byte[] imageBytes = Convert.FromBase64String(base64String);
                using var memoryStream = new MemoryStream(imageBytes);
                memoryStream.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                return bitmapImage;
            }
            catch (Exception ex)
            {
                WriteLog("Exception occurred converting Base64 to BitmapImage.", LogLevel.Error, ex);
                return null;
            }
        }

        /// <summary>
        /// Gets the dimensions of an image file without fully loading it.
        /// </summary>
        public static (int Width, int Height) GetImageSize(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                return (frame.PixelWidth, frame.PixelHeight);
            }
            catch (Exception ex)
            {
                WriteLog($"Exception occurred getting image size for {path}.", LogLevel.Error, ex);
                return (0, 0);
            }
        }
    }
}
