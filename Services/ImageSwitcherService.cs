using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SNIBypassGUI.Common.Extensions;
using SNIBypassGUI.Common.IO;
using SNIBypassGUI.Common.UI;
using SNIBypassGUI.Consts;

namespace SNIBypassGUI.Services
{
    public class ImageSwitcherService : INotifyPropertyChanged
    {
        private readonly Dictionary<string, string> _hashToImagePath = [];
        private readonly Dictionary<string, BitmapImage> _pathToImageCache = [];
        private readonly object _reloadLock = new();
        private readonly Random _random = new();

        private DispatcherTimer _timer;
        private List<string> _imageOrder = [];
        private int _currentIndex = -1;

        public event PropertyChangedEventHandler PropertyChanged;

        public ImageSource CurrentImage { get; private set; }
        public string ChangeMode { get; private set; }
        public int ChangeInterval { get; private set; }

        public ImageSwitcherService() => InitializeService();

        private void InitializeService()
        {
            ReloadConfig();

            if (_imageOrder.Count > 1)
            {
                _timer = new DispatcherTimer();
                UpdateTimer();
                _timer.Tick += Timer_Tick;
                _timer.Start();
            }

            if (_imageOrder.Count > 0)
            {
                LoadInitialImage();
                OnPropertyChanged(nameof(CurrentImage));
            }
        }

        private void LoadInitialImage()
        {
            int targetIndex;
            if (ChangeMode == ConfigConsts.SequentialMode)
                targetIndex = _hashToImagePath.ContainsKey(_imageOrder.First()) ? 0 : _imageOrder.IndexOf(_hashToImagePath.First().Key);
            else
            {
                int randomIdx = _random.Next(_imageOrder.Count);
                targetIndex = _hashToImagePath.ContainsKey(_imageOrder[randomIdx]) ? randomIdx : _imageOrder.IndexOf(_hashToImagePath.First().Key);
            }

            if (targetIndex >= 0 && targetIndex < _imageOrder.Count && _hashToImagePath.TryGetValue(_imageOrder[targetIndex], out string path))
            {
                CurrentImage = ImageUtils.LoadImage(path, _pathToImageCache, AppConsts.MaxDecodeSize);
                _currentIndex = targetIndex;
            }
        }

        public void ValidateCurrentImage()
        {
            if (CurrentImage == null) return;

            string currentPath = ((BitmapImage)CurrentImage).UriSource.AbsolutePath;

            if (!_hashToImagePath.Values.Contains(currentPath))
            {
                if (_imageOrder.Count > 0)
                {
                    string firstPath = _hashToImagePath[_imageOrder[0]];
                    CurrentImage = ImageUtils.LoadImage(firstPath, _pathToImageCache, AppConsts.MaxDecodeSize);
                    OnPropertyChanged(nameof(CurrentImage));
                }
                else
                {
                    CurrentImage = null;
                    OnPropertyChanged(nameof(CurrentImage));
                }
            }
        }

        public void ReloadConfig()
        {
            lock (_reloadLock)
            {
                var bgConfig = ConfigManager.Instance.Settings.Background;
                ChangeMode = bgConfig.ChangeMode;
                ChangeInterval = Math.Max(1, bgConfig.ChangeInterval);

                // Directly copy list
                _imageOrder = [.. bgConfig.ImageOrder];

                _hashToImagePath.Clear();

                if (Directory.Exists(PathConsts.BackgroundDirectory))
                {
                    var validExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
                    var files = Directory.EnumerateFiles(PathConsts.BackgroundDirectory, "*.*", SearchOption.AllDirectories)
                        .Where(file => validExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

                    foreach (var filePath in files)
                    {
                        string hash = FileUtils.CalculateFileHash(filePath);
                        _hashToImagePath[hash] = filePath;
                    }
                }

                _currentIndex = Math.Min(_currentIndex, _imageOrder.Count - 1);
                UpdateTimer();
            }
        }

        private void UpdateTimer()
        {
            if (_timer == null) return;
            _timer.Interval = TimeSpan.FromSeconds(ChangeInterval);
            if (_imageOrder.Count > 1) _timer.Start();
            else _timer.Stop();
        }

        private void ResetImageOrder()
        {
            if (!Directory.Exists(PathConsts.BackgroundDirectory)) return;

            var validExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
            var files = validExtensions.SelectMany(ext => Directory.GetFiles(PathConsts.BackgroundDirectory, $"*{ext}"));

            var config = ConfigManager.Instance.Settings.Background;
            config.ImageOrder.Clear();
            config.ImageOrder.AddRange(files.Select(FileUtils.CalculateFileHash));

            ConfigManager.Instance.Save();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_imageOrder.Count == 0) return;

            int nextIndex = CalculateNextIndex();

            if (_hashToImagePath.TryGetValue(_imageOrder[nextIndex], out string nextImagePath))
            {
                var nextImage = ImageUtils.LoadImage(nextImagePath, _pathToImageCache, AppConsts.MaxDecodeSize);

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    CurrentImage = nextImage;
                    OnPropertyChanged(nameof(CurrentImage));
                    _currentIndex = nextIndex;
                });
            }
            else
            {
                ResetImageOrder();
                ReloadConfig();
            }
        }

        private int CalculateNextIndex()
        {
            const int MAX_ATTEMPTS = 100;

            if (ChangeMode == ConfigConsts.SequentialMode)
                return (_currentIndex + 1) % _imageOrder.Count;

            if (_imageOrder.Count <= 1) return 0;

            int newIndex;
            int attempts = 0;

            do
            {
                newIndex = _random.Next(_imageOrder.Count);
                attempts++;
            } while (newIndex == _currentIndex && attempts < MAX_ATTEMPTS);

            return newIndex == _currentIndex ? (newIndex + 1) % _imageOrder.Count : newIndex;
        }

        public void Cleanup()
        {
            _timer?.Stop();
            CurrentImage = null;
            _pathToImageCache.Clear();
        }

        public void CleanAllCache() => _pathToImageCache.Clear();

        public void CleanCacheByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var keysToRemove = _pathToImageCache.Keys.Where(k => string.Equals(k, path, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var key in keysToRemove) _pathToImageCache.Remove(key);
        }

        public void ReloadByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            CleanCacheByPath(path);

            if (_imageOrder.Count > 0 && _currentIndex >= 0 && _currentIndex < _imageOrder.Count)
            {
                if (string.Equals(_hashToImagePath[_imageOrder[_currentIndex]], path, StringComparison.OrdinalIgnoreCase))
                {
                    var newImage = ImageUtils.LoadImage(path, _pathToImageCache, AppConsts.MaxDecodeSize);
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        CurrentImage = newImage;
                        OnPropertyChanged(nameof(CurrentImage));
                    });
                }
                else _pathToImageCache[path] = ImageUtils.LoadImage(path, _pathToImageCache, AppConsts.MaxDecodeSize);
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
