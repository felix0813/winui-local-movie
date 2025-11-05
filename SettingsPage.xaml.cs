using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;

namespace winui_local_movie
{
  public sealed partial class SettingsPage : Page
  {
    private readonly DatabaseService _databaseService;
    private readonly List<string> _directories;
    private readonly string _settingsFilePath;


    public SettingsPage()
    {
      this.InitializeComponent();
      _databaseService = ((App)Application.Current).DatabaseService;

      // 使用exe目录下的配置文件
      _settingsFilePath = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "app_settings.json");

      _directories = LoadDirectoriesFromSettings();
      DirectoriesListView.ItemsSource = _directories;
      ShowConfigFilePath();
      ShowLastScanTime();
    }
    private void ShowConfigFilePath()
    {
      try
      {
        // 显示 ApplicationData 目录路径
        var appDataPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        ConfigPathText.Text = appDataPath;
      }
      catch (Exception ex)
      {
        ConfigPathText.Text = $"无法获取路径: {ex.Message}";
      }
    }

    private void ShowLastScanTime()
    {
      try
      {
        var lastScanTime = GetLastScanTime();
        if (lastScanTime != DateTime.MinValue)
        {
          LastScanTimeText.Text = lastScanTime.ToString("yyyy-MM-dd HH:mm:ss");
        }
        else
        {
          LastScanTimeText.Text = "尚未扫描";
        }
      }
      catch (Exception ex)
      {
        LastScanTimeText.Text = $"无法获取扫描时间: {ex.Message}";
      }
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
      var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm"
            };
      var scannedVideos = 0;

      // 获取上次扫描时间
      var lastScanTime = GetLastScanTime();

      var tasks = _directories
          .Where(Directory.Exists)
          .Select(directory => Task.Run(async () =>
          {
            var files = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                      .Where(file => videoExtensions.Contains(Path.GetExtension(file)));

            foreach (var file in files)
            {
              try
              {
                var fileInfo = new FileInfo(file);

                // 仅处理新增或修改过的文件
                if (fileInfo.LastWriteTime <= lastScanTime)
                  continue;

                var duration = await GetVideoDurationAsync(file);
                double fileSizeMB = Math.Round(fileInfo.Length / 1024.0 / 1024.0, 0);

                var video = new VideoModel
                {
                  Title = Path.GetFileNameWithoutExtension(file),
                  FilePath = file,
                  DateAdded = DateTime.Now,
                  Duration = duration,
                  FileSize = (long)fileSizeMB,
                  CreationDate = fileInfo.CreationTime
                };

                await _databaseService.AddVideoAsync(video);
                Interlocked.Increment(ref scannedVideos);
              }
              catch (Exception ex)
              {
                System.Diagnostics.Debug.WriteLine($"处理文件失败: {file}, 错误: {ex.Message}");
              }
            }
          }));

      await Task.WhenAll(tasks);

      // 更新上次扫描时间
      UpdateLastScanTime(DateTime.Now);

      return scannedVideos;
    }
    private List<string> LoadDirectoriesFromSettings()
    {
      var directories = new List<string>();

      if (File.Exists(_settingsFilePath))
      {
        try
        {
          var json = File.ReadAllText(_settingsFilePath);
          var options = new JsonSerializerOptions
          {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver() // 与保存时保持一致
          };

          var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json, options);

          if (settings != null && settings.ContainsKey("VideoDirectories") && settings["VideoDirectories"] != null)
          {
            var dirsJson = settings["VideoDirectories"].ToString();
            if (!string.IsNullOrEmpty(dirsJson))
            {
              directories.AddRange(dirsJson.Split('|'));
            }
          }
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"加载目录设置失败: {ex.Message}");
          DispatcherQueue.TryEnqueue(() =>
          {
            StatusText.Text = $"加载设置失败: {ex.Message}";
          });
        }
      }

      return directories;
    }

    private DateTime GetLastScanTime()
    {

      if (File.Exists(_settingsFilePath))
      {
        try
        {
          var json = File.ReadAllText(_settingsFilePath);
          var options = new JsonSerializerOptions
          {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver() // 与保存时保持一致
          };

          var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json, options);

          if (settings != null && settings.ContainsKey("LastScanTime") && settings["LastScanTime"] != null)
          {
            var dirsJson = settings["LastScanTime"].ToString();
            if (!string.IsNullOrEmpty(dirsJson))
            {
              return DateTime.TryParse(dirsJson, out var lastScanTime) ? lastScanTime : DateTime.MinValue;
            }
          }
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"加载扫描时间失败: {ex.Message}");
          DispatcherQueue.TryEnqueue(() =>
          {
            StatusText.Text = $"加载设置失败: {ex.Message}";
          });
        }
      }

      return DateTime.MinValue;
    }

    private void SaveDirectoriesToSettings()
    {
      try
      {
        var settings = LoadExistingSettings();
        settings["VideoDirectories"] = string.Join("|", _directories);

        var options = new JsonSerializerOptions
        {
          WriteIndented = true,
          TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

        var json = JsonSerializer.Serialize(settings, options);
        File.WriteAllText(_settingsFilePath, json);
      }
      catch (Exception ex)
      {
        DispatcherQueue.TryEnqueue(() =>
        {
          StatusText.Text = $"保存设置失败: {ex.Message}";
        });
      }
    }

    private void UpdateLastScanTime(DateTime lastScanTime)
    {
      try
      {
        var settings = LoadExistingSettings();
        settings["LastScanTime"] = lastScanTime.ToString("o");

        var options = new JsonSerializerOptions
        {
          WriteIndented = true,
          TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

        var json = JsonSerializer.Serialize(settings, options);
        File.WriteAllText(_settingsFilePath, json);
        ShowLastScanTime();
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"更新上次扫描时间失败: {ex.Message}");
      }
    }

    private Dictionary<string, object> LoadExistingSettings()
    {
      if (File.Exists(_settingsFilePath))
      {
        try
        {
          var json = File.ReadAllText(_settingsFilePath);
          var options = new JsonSerializerOptions
          {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
          };

          var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json, options);
          return settings ?? new Dictionary<string, object>();
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"加载现有设置失败: {ex.Message}");
        }
      }

      return new Dictionary<string, object>();
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