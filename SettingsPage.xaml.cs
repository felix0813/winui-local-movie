using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace winui_local_movie
{
  public sealed partial class SettingsPage : Page
  {
    private readonly DatabaseService _databaseService;
    private readonly List<string> _directories;
    private readonly ApplicationDataContainer _localSettings;

    public SettingsPage()
    {
      this.InitializeComponent();
      _databaseService = ((App)Application.Current).DatabaseService;
      _localSettings = ApplicationData.Current.LocalSettings;

      _directories = LoadDirectoriesFromSettings();
      DirectoriesListView.ItemsSource = _directories;
    }

    private List<string> LoadDirectoriesFromSettings()
    {
      var directories = new List<string>();
      var savedDirectories = _localSettings.Values["VideoDirectories"] as string;

      if (!string.IsNullOrEmpty(savedDirectories))
      {
        directories.AddRange(savedDirectories.Split('|'));
      }

      return directories;
    }

    private void SaveDirectoriesToSettings()
    {
      _localSettings.Values["VideoDirectories"] = string.Join("|", _directories);
    }

    private async void SelectDirectory_Click(object sender, RoutedEventArgs e)
    {
      var folderPicker = new FolderPicker();

      // 获取当前窗口的句柄并初始化文件选择器
      var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(((App)Application.Current).MainWindow);
      WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

      folderPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
      folderPicker.FileTypeFilter.Add("*");

      var folder = await folderPicker.PickSingleFolderAsync();
      if (folder != null)
      {
        SelectedDirectoryText.Text = folder.Path;

        // 自动添加选中的目录
        if (!_directories.Contains(folder.Path))
        {
          _directories.Add(folder.Path);
          SaveDirectoriesToSettings();
          DirectoriesListView.ItemsSource = null;
          DirectoriesListView.ItemsSource = _directories;
          StatusText.Text = "目录已添加";
        }
        else
        {
          StatusText.Text = "目录已存在";
        }
      }
    }

    private void RemoveDirectory_Click(object sender, RoutedEventArgs e)
    {
      var selectedDirectory = DirectoriesListView.SelectedItem as string;
      if (selectedDirectory != null)
      {
        _directories.Remove(selectedDirectory);
        SaveDirectoriesToSettings();
        DirectoriesListView.ItemsSource = null;
        DirectoriesListView.ItemsSource = _directories;
        StatusText.Text = "目录已删除";
      }
      else
      {
        StatusText.Text = "请选择要删除的目录";
      }
    }

    private async void ScanVideos_Click(object sender, RoutedEventArgs e)
    {
      if (_directories.Count == 0)
      {
        StatusText.Text = "请先添加视频目录";
        return;
      }

      // 禁用扫描按钮防止重复点击
      var scanButton = sender as Button;
      scanButton.IsEnabled = false;

      ScanProgressBar.Visibility = Visibility.Visible;
      ScanProgressBar.IsIndeterminate = true;
      StatusText.Text = "正在扫描视频...";

      try
      {
        // 在后台线程执行扫描操作
        var result = await Task.Run(async () => await ScanVideosInBackgroundAsync());
        StatusText.Text = $"扫描完成，共添加 {result} 个视频";
      }
      catch (Exception ex)
      {
        StatusText.Text = $"扫描出错: {ex.Message}";
      }
      finally
      {
        ScanProgressBar.IsIndeterminate = false;
        ScanProgressBar.Visibility = Visibility.Collapsed;
        scanButton.IsEnabled = true;
      }
    }

    private async Task<int> ScanVideosInBackgroundAsync()
    {
      var videoExtensions = new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm" };
      var scannedVideos = 0;

      foreach (var directory in _directories)
      {
        if (!Directory.Exists(directory))
        {
          continue;
        }

        var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(file => videoExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()));

        foreach (var file in files)
        {
          // 获取视频时长
          var duration = await GetVideoDurationAsync(file);
          var fileInfo = new FileInfo(file);
          double fileSizeMB = Math.Round(fileInfo.Length / 1024.0 / 1024.0, 0);


          var video = new VideoModel
          {
            Title = Path.GetFileNameWithoutExtension(file),
            FilePath = file,
            DateAdded = DateTime.Now,
            Duration = duration,  // 使用实际获取的时长
            FileSize = (long)fileSizeMB,
            CreationDate = fileInfo.CreationTime
          };

          await _databaseService.AddVideoAsync(video);
          scannedVideos++;
        }
      }

      return scannedVideos;
    }

    private async Task<TimeSpan> GetVideoDurationAsync(string filePath)
    {
      try
      {
        var file = await StorageFile.GetFileFromPathAsync(filePath);
        var basicProperties = await file.Properties.GetVideoPropertiesAsync();
        return basicProperties.Duration;
      }
      catch (Exception)
      {
        return TimeSpan.Zero; // 如果无法获取时长，返回默认值  
      }
    }
  }
}