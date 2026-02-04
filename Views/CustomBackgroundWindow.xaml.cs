using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SNIBypassGUI.Common.Extensions;
using SNIBypassGUI.Common.IO;
using SNIBypassGUI.Common.UI;
using SNIBypassGUI.Consts;
using SNIBypassGUI.Models;
using SNIBypassGUI.Services;
using static SNIBypassGUI.Common.LogManager;
using MessageBox = HandyControl.Controls.MessageBox;

namespace SNIBypassGUI.Views
{
    public partial class CustomBackgroundWindow : Window
    {
        public ImageSwitcherService BackgroundService => MainWindow.BackgroundService;

        private string _currentImagePath;
        private readonly Dictionary<string, string> _hashToPathMap = [];

        public CustomBackgroundWindow()
        {
            InitializeComponent();

            TopBar.MouseLeftButtonDown += (o, e) => DragMove();
            DataContext = this;

            LoadImagesToList();

            BackgroundService.PropertyChanged += OnBackgroundChanged;
            CurrentImage.Source = BackgroundService.CurrentImage;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SyncControlsFromConfig();
            FadeIn();
        }

        private void LoadImagesToList()
        {
            ImageListBox.ItemsSource = null;
            _hashToPathMap.Clear();

            // Direct List Access
            var imageOrder = ConfigManager.Instance.Settings.Background.ImageOrder;

            if (Directory.Exists(PathConsts.BackgroundDirectory))
            {
                var files = Directory.EnumerateFiles(PathConsts.BackgroundDirectory, "*.*", SearchOption.AllDirectories)
                    .Where(file => AppConsts.ImageExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

                foreach (var filePath in files)
                {
                    string hash = FileUtils.CalculateFileHash(filePath);
                    _hashToPathMap[hash] = filePath;
                }
            }

            var imageList = new List<ImageItem>();
            foreach (var hash in imageOrder)
            {
                if (_hashToPathMap.TryGetValue(hash, out string path))
                {
                    BitmapImage bitmapImage = ImageUtils.LoadImage(path, maxDecodeSize: 100);
                    (int w, int h) = ImageUtils.GetImageSize(path);

                    imageList.Add(new ImageItem
                    {
                        ImageName = Path.GetFileName(path),
                        ImagePath = path,
                        ImageObj = bitmapImage,
                        ImageResolution = $"{w} x {h}",
                        ImageSize = $"{FileUtils.GetFileSize(path).ToReadableSize()}"
                    });
                }
            }

            ImageListBox.ItemsSource = imageList;
        }

        private void RemoveBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ImageListBox.SelectedItem is not ImageItem selectedItem) return;

            if (ImageListBox.Items.Count <= 1)
            {
                MessageBox.Show("至少要有一张背景图片！", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string hashToRemove = FileUtils.CalculateFileHash(selectedItem.ImagePath);

                var bgConfig = ConfigManager.Instance.Settings.Background;
                bgConfig.ImageOrder.Remove(hashToRemove);
                ConfigManager.Instance.Save();

                FileUtils.TryDelete(selectedItem.ImagePath);

                ImageCropperControl.Source = null;
                ImageCropperControl.ResetDrawThumb();
                ImageListBox.SelectedItem = null;

                BackgroundService.CleanCacheByPath(_currentImagePath);
                BackgroundService.ReloadConfig();
                BackgroundService.ValidateCurrentImage();

                LoadImagesToList();
            }
            catch (Exception ex)
            {
                WriteLog("Remove image exception.", LogLevel.Error, ex);
                MessageBox.Show($"图像移除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new()
            {
                Title = "选择图片",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Filter = "图片 (*.jpg; *.jpeg; *.png)|*.jpg;*.jpeg;*.png",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var files = openFileDialog.FileNames;
                bool changed = false;

                foreach (var file in files)
                {
                    try
                    {
                        string destPath = Path.Combine(PathConsts.BackgroundDirectory, Path.GetFileName(file));
                        File.Copy(file, destPath, true);

                        string hashToAdd = FileUtils.CalculateFileHash(file);
                        var imageOrder = ConfigManager.Instance.Settings.Background.ImageOrder;

                        // List Add (Prevent duplicates)
                        if (!imageOrder.Contains(hashToAdd))
                        {
                            imageOrder.Add(hashToAdd);
                            changed = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Add image exception: {file}", LogLevel.Error, ex);
                        MessageBox.Show($"添加图像失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                if (changed)
                {
                    ConfigManager.Instance.Save();
                    BackgroundService.ReloadConfig();
                    LoadImagesToList();
                }
            }
        }

        private void UpBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ImageListBox.SelectedItem is not ImageItem) return;
            int currentIndex = ImageListBox.SelectedIndex;
            if (currentIndex <= 0) return;

            try
            {
                var imageOrder = ConfigManager.Instance.Settings.Background.ImageOrder;

                // Swap in List
                if (currentIndex < imageOrder.Count)
                {
                    (imageOrder[currentIndex - 1], imageOrder[currentIndex]) = (imageOrder[currentIndex], imageOrder[currentIndex - 1]);
                    ConfigManager.Instance.Save();
                    BackgroundService.ReloadConfig();
                    LoadImagesToList();
                    ImageListBox.SelectedIndex = currentIndex - 1;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"上移图像失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                LoadImagesToList();
            }
        }

        private void DownBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ImageListBox.SelectedItem is not ImageItem) return;
            int currentIndex = ImageListBox.SelectedIndex;
            if (currentIndex >= ImageListBox.Items.Count - 1) return;

            try
            {
                var imageOrder = ConfigManager.Instance.Settings.Background.ImageOrder;

                // Swap in List
                if (currentIndex < imageOrder.Count - 1)
                {
                    (imageOrder[currentIndex + 1], imageOrder[currentIndex]) = (imageOrder[currentIndex], imageOrder[currentIndex + 1]);
                    ConfigManager.Instance.Save();
                    BackgroundService.ReloadConfig();
                    LoadImagesToList();
                    ImageListBox.SelectedIndex = currentIndex + 1;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"下移图像失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                LoadImagesToList();
            }
        }

        private void SetTimeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(TimeTb.Text))
                MessageBox.Show("请输入一个有效的值！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            else if (TimeTb.Text.Contains("."))
                MessageBox.Show("请输入一个整数！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            else if (TimeTb.Text.ToInt() < 1)
                MessageBox.Show("时间间隔不能小于一秒！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
            {
                ConfigManager.Instance.Settings.Background.ChangeInterval = TimeTb.Text.ToInt();
                ConfigManager.Instance.Save();

                MessageBox.Show("设置成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);

                SyncControlsFromConfig();
                BackgroundService.ReloadConfig();
            }
        }

        private void ToggleModeBtn_Click(object sender, RoutedEventArgs e)
        {
            var bgConfig = ConfigManager.Instance.Settings.Background;
            if (bgConfig.ChangeMode == ConfigConsts.SequentialMode)
                bgConfig.ChangeMode = ConfigConsts.RandomMode;
            else
                bgConfig.ChangeMode = ConfigConsts.SequentialMode;

            ConfigManager.Instance.Save();
            SyncControlsFromConfig();
            BackgroundService.ReloadConfig();
        }

        private async void CutBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentImagePath)) return;
            CutBtn.IsEnabled = false;

            Rectangle cropArea = new((int)ImageCropperControl.CroppedRegion.X,
                                     (int)ImageCropperControl.CroppedRegion.Y,
                                     (int)ImageCropperControl.CroppedRegion.Width,
                                     (int)ImageCropperControl.CroppedRegion.Height);

            await Task.Run(() =>
            {
                try
                {
                    string originHash = FileUtils.CalculateFileHash(_currentImagePath);

                    BitmapImage bitmapImage = ImageUtils.LoadImage(_currentImagePath);

                    using MemoryStream memoryStream = new();
                    PngBitmapEncoder encoder = new();
                    encoder.Frames.Add(BitmapFrame.Create(bitmapImage));
                    encoder.Save(memoryStream);
                    memoryStream.Position = 0;

                    using Bitmap original = new(memoryStream);
                    using Bitmap croppedImage = new(cropArea.Width, cropArea.Height);
                    using Graphics g = Graphics.FromImage(croppedImage);

                    g.DrawImage(original, new Rectangle(0, 0, cropArea.Width, cropArea.Height), cropArea, GraphicsUnit.Pixel);

                    string tempImagePath = Path.Combine(Path.GetDirectoryName(_currentImagePath), $"cropped_{Path.GetFileName(_currentImagePath)}");
                    croppedImage.Save(tempImagePath);

                    original.Dispose();
                    croppedImage.Dispose();
                    memoryStream.Dispose();

                    File.Replace(tempImagePath, _currentImagePath, null);

                    string newHash = FileUtils.CalculateFileHash(_currentImagePath);
                    var imageOrder = ConfigManager.Instance.Settings.Background.ImageOrder;

                    // Replace in List
                    int index = imageOrder.IndexOf(originHash);
                    if (index != -1) imageOrder[index] = newHash;
                    else imageOrder.Add(newHash);

                    ConfigManager.Instance.Save();

                    Dispatcher.Invoke(() =>
                    {
                        BackgroundService.ReloadByPath(_currentImagePath);
                        ImageCropperControl.Source = null;
                        ImageCropperControl.ResetDrawThumb();
                        ImageListBox.SelectedItem = null;
                        MessageBox.Show("图像裁剪成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadImagesToList();
                    });
                }
                catch (Exception ex)
                {
                    WriteLog("Crop exception.", LogLevel.Error, ex);
                    Dispatcher.Invoke(() => MessageBox.Show($"图像裁剪失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error));
                }
                finally
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            });
        }

        private void ResetBtn_Click(object sender, RoutedEventArgs e) =>
            ImageCropperControl.ResetDrawThumb();

        private async void DoneBtn_Click(object sender, RoutedEventArgs e)
        {
            DoneBtn.IsEnabled = false;
            BackgroundService.ReloadConfig();

            ImageListBox.ItemsSource = null;
            ImageCropperControl.Source = null;

            GC.Collect();
            GC.WaitForPendingFinalizers();

            await FadeOut(true);
        }

        private void ImageListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ImageListBox.SelectedItem is not ImageItem imageItem)
            {
                _currentImagePath = null;
                CutBtn.IsEnabled = false;
                return;
            }
            _currentImagePath = imageItem.ImagePath;

            try
            {
                using var fileStream = new FileStream(_currentImagePath, FileMode.Open, FileAccess.Read);
                using var memoryStream = new MemoryStream();
                fileStream.CopyTo(memoryStream);
                memoryStream.Position = 0;

                ImageCropperControl.LoadImageFromStream(memoryStream);
                CutBtn.IsEnabled = true;
            }
            catch (Exception ex)
            {
                WriteLog("Error loading image for cropping.", LogLevel.Error, ex);
            }

            ImageCropperControl.ResetDrawThumb();
        }

        private void SyncControlsFromConfig()
        {
            var bgConfig = ConfigManager.Instance.Settings.Background;
            TimeTb.Text = bgConfig.ChangeInterval.ToString();

            if (bgConfig.ChangeMode == ConfigConsts.RandomMode) ToggleModeBtn.Content = "随机模式";
            else ToggleModeBtn.Content = "顺序模式";
        }

        private void FadeIn()
        {
            var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromSeconds(0.8)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            BeginAnimation(OpacityProperty, fadeIn);
        }

        private async Task FadeOut(bool dialogResult = false)
        {
            var fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromSeconds(0.8)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            BeginAnimation(OpacityProperty, fadeOut);

            await Task.Delay(800);

            DialogResult = dialogResult;
            Close();
        }

        private void OnBackgroundChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ImageSwitcherService.CurrentImage)) return;

            Dispatcher.BeginInvoke(() =>
            {
                NextImage.Opacity = 0;
                NextImage.Source = BackgroundService.CurrentImage;

                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1));
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1));

                CurrentImage.BeginAnimation(OpacityProperty, fadeOut);
                NextImage.BeginAnimation(OpacityProperty, fadeIn);

                (NextImage, CurrentImage) = (CurrentImage, NextImage);
            });
        }
    }
}
