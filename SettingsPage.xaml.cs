using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace winui_local_movie
{
  public sealed partial class SettingsPage : Page
  {
    private const string BackupServiceUrlKey = "BackupServiceBaseUrl";
    private const string DefaultBackupServiceUrl = "http://localhost:8080";

    private readonly DatabaseService _databaseService;
    private readonly List<string> _directories;
    private readonly List<BackupItem> _backups;
    private readonly string _settingsFilePath;
    private readonly string _databaseFilePath;

    public SettingsPage()
    {
      InitializeComponent();
      LogInfo("初始化 SettingsPage。");
      _databaseService = ((App)Application.Current).DatabaseService;

      var localPath = ApplicationData.Current.LocalFolder.Path;
      _settingsFilePath = Path.Combine(localPath, "app_settings.json");
      _databaseFilePath = Path.Combine(localPath, "videos.db");
      LogInfo($"本地路径: {localPath}");
      LogInfo($"设置文件路径: {_settingsFilePath}");
      LogInfo($"数据库文件路径: {_databaseFilePath}");

      _directories = LoadDirectoriesFromSettings();
      _backups = new List<BackupItem>();
      DirectoriesListView.ItemsSource = _directories;
      BackupsListView.ItemsSource = _backups;

      BackupServiceUrlTextBox.Text = GetBackupServiceBaseUrl();
      ShowConfigFilePath();
      ShowLastScanTime();
      LogInfo("SettingsPage 初始化完成。");
    }

    private void ShowConfigFilePath()
    {
      try
      {
        ConfigPathText.Text = ApplicationData.Current.LocalFolder.Path;
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
        LastScanTimeText.Text = lastScanTime != DateTime.MinValue
          ? lastScanTime.ToString("yyyy-MM-dd HH:mm:ss")
          : "尚未扫描";
      }
      catch (Exception ex)
      {
        LastScanTimeText.Text = $"无法获取扫描时间: {ex.Message}";
      }
    }

    private async void SelectDirectory_Click(object sender, RoutedEventArgs e)
    {
      var folderPicker = new FolderPicker();
      var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(((App)Application.Current).MainWindow);
      WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

      folderPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
      folderPicker.FileTypeFilter.Add("*");

      var folder = await folderPicker.PickSingleFolderAsync();
      if (folder == null)
      {
        LogInfo("用户取消了目录选择。");
        return;
      }

      LogInfo($"用户选择目录: {folder.Path}");
      SelectedDirectoryText.Text = folder.Path;
      if (_directories.Contains(folder.Path))
      {
        LogInfo($"目录已存在，跳过添加: {folder.Path}");
        StatusText.Text = "目录已存在";
        return;
      }

      _directories.Add(folder.Path);
      SaveDirectoriesToSettings();
      DirectoriesListView.ItemsSource = null;
      DirectoriesListView.ItemsSource = _directories;
      StatusText.Text = "目录已添加";
      LogInfo($"目录添加成功，当前目录数量: {_directories.Count}");
    }

    private void RemoveDirectory_Click(object sender, RoutedEventArgs e)
    {
      if (DirectoriesListView.SelectedItem is not string selectedDirectory)
      {
        LogInfo("删除目录失败：未选择目录。");
        StatusText.Text = "请选择要删除的目录";
        return;
      }

      LogInfo($"准备删除目录: {selectedDirectory}");
      _directories.Remove(selectedDirectory);
      SaveDirectoriesToSettings();
      DirectoriesListView.ItemsSource = null;
      DirectoriesListView.ItemsSource = _directories;
      StatusText.Text = "目录已删除";
      LogInfo($"目录删除成功，当前目录数量: {_directories.Count}");
    }

    private async void ScanVideos_Click(object sender, RoutedEventArgs e)
    {
      if (_directories.Count == 0)
      {
        LogInfo("扫描取消：目录列表为空。");
        StatusText.Text = "请先添加视频目录";
        return;
      }

      var scanButton = sender as Button;
      if (scanButton != null)
      {
        scanButton.IsEnabled = false;
      }

      ScanProgressBar.Visibility = Visibility.Visible;
      ScanProgressBar.IsIndeterminate = true;
      StatusText.Text = "正在扫描视频...";
      LogInfo($"开始扫描视频，目录数量: {_directories.Count}");

      try
      {
        var result = await Task.Run(async () => await ScanVideosInBackgroundAsync());
        StatusText.Text = $"扫描完成，共添加 {result} 个视频";
        LogInfo($"扫描完成，新增视频数量: {result}");
      }
      catch (Exception ex)
      {
        LogError("扫描视频失败。", ex);
        StatusText.Text = $"扫描出错: {ex.Message}";
      }
      finally
      {
        ScanProgressBar.IsIndeterminate = false;
        ScanProgressBar.Visibility = Visibility.Collapsed;
        if (scanButton != null)
        {
          scanButton.IsEnabled = true;
        }
      }
    }

    private async Task<int> ScanVideosInBackgroundAsync()
    {
      LogInfo("后台扫描任务启动。");
      var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
      {
        ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm"
      };

      var scannedVideos = 0;
      var lastScanTime = GetLastScanTime();

      var tasks = _directories
        .Where(Directory.Exists)
        .Select(directory => Task.Run(async () =>
        {
          LogInfo($"扫描目录: {directory}");
          var files = Directory
            .EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(file => videoExtensions.Contains(Path.GetExtension(file)));

          foreach (var file in files)
          {
            try
            {
              var fileInfo = new FileInfo(file);
              if (fileInfo.LastWriteTime <= lastScanTime)
              {
                continue;
              }

              LogInfo($"发现新视频文件: {file}");
              var duration = await GetVideoDurationAsync(file);
              var fileSizeMB = Math.Round(fileInfo.Length / 1024.0 / 1024.0, 0);

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
              LogInfo($"已写入数据库: {video.Title} ({video.FilePath})");
            }
            catch (Exception ex)
            {
              LogError($"处理文件失败: {file}", ex);
            }
          }
        }));

      await Task.WhenAll(tasks);
      LogInfo($"后台扫描任务完成，总新增: {scannedVideos}");
      UpdateLastScanTime(DateTime.Now);
      return scannedVideos;
    }

    private async void UploadBackup_Click(object sender, RoutedEventArgs e)
    {
      var backupName = BackupNameTextBox.Text?.Trim();
      LogInfo($"开始上传备份，名称: {backupName}");
      if (string.IsNullOrWhiteSpace(backupName))
      {
        UpdateStatus("请输入备份名称后再上传。", isError: true);
        return;
      }

      if (!File.Exists(_databaseFilePath))
      {
        UpdateStatus($"未找到数据库文件: {_databaseFilePath}", isError: true);
        return;
      }

      EnsureSettingsFileExists();
      var baseUrl = GetBackupServiceBaseUrl();
      var tempDatabaseCopyPath = Path.Combine(Path.GetTempPath(), $"videos-backup-{Guid.NewGuid():N}.db");

      try
      {
        await CopyDatabaseForBackupAsync(_databaseFilePath, tempDatabaseCopyPath);

        using var http = CreateBackupHttpClient(baseUrl);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(backupName, Encoding.UTF8), "name");

        await using var sqliteStream = File.OpenRead(tempDatabaseCopyPath);
        var sqliteContent = new StreamContent(sqliteStream);
        sqliteContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(sqliteContent, "sqlite", Path.GetFileName(_databaseFilePath));

        await using var jsonStream = File.OpenRead(_settingsFilePath);
        var jsonContent = new StreamContent(jsonStream);
        jsonContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(jsonContent, "json", Path.GetFileName(_settingsFilePath));

        var response = await http.PostAsync("api/backups", form);
        if (!response.IsSuccessStatusCode)
        {
          var responseText = await response.Content.ReadAsStringAsync();
          LogError($"上传备份失败，状态码: {(int)response.StatusCode}，响应: {responseText}");
          UpdateStatus($"上传失败({(int)response.StatusCode}): {responseText}", isError: true);
          return;
        }

        LogInfo("备份上传成功。");
        UpdateStatus("备份上传成功。", isError: false);
        BackupNameTextBox.Text = string.Empty;
        await RefreshBackupsAsync();
      }
      catch (Exception ex)
      {
        LogError("上传备份异常。", ex);
        UpdateStatus($"上传备份失败: {ex.Message}", isError: true);
      }
      finally
      {
        TryDeleteFile(tempDatabaseCopyPath);
      }
    }

    private async void RefreshBackups_Click(object sender, RoutedEventArgs e)
    {
      await RefreshBackupsAsync();
    }

    private async Task RefreshBackupsAsync()
    {
      var baseUrl = GetBackupServiceBaseUrl();
      LogInfo($"开始刷新备份列表，服务地址: {baseUrl}");

      try
      {
        using var http = CreateBackupHttpClient(baseUrl);
        var response = await http.GetAsync("api/backups");

        if (!response.IsSuccessStatusCode)
        {
          var responseText = await response.Content.ReadAsStringAsync();
          LogError($"刷新备份失败，状态码: {(int)response.StatusCode}，响应: {responseText}");
          UpdateStatus($"刷新备份列表失败({(int)response.StatusCode}): {responseText}", isError: true);
          return;
        }

        var json = await response.Content.ReadAsStringAsync();
        var backups = ParseBackupsFromJson(json);

        _backups.Clear();
        _backups.AddRange(backups.OrderByDescending(x => x.CreatedAt));

        BackupsListView.ItemsSource = null;
        BackupsListView.ItemsSource = _backups;

        UpdateStatus($"已加载 {_backups.Count} 条备份记录。", isError: false);
        LogInfo($"备份列表刷新成功，条数: {_backups.Count}");
      }
      catch (Exception ex)
      {
        LogError("加载备份列表异常。", ex);
        UpdateStatus($"加载备份列表失败: {ex.Message}", isError: true);
      }
    }

    private async void RestoreSelectedBackup_Click(object sender, RoutedEventArgs e)
    {
      if (BackupsListView.SelectedItem is not BackupItem selected)
      {
        UpdateStatus("请先在列表中选择要恢复的备份。", isError: true);
        return;
      }

      var baseUrl = GetBackupServiceBaseUrl();
      LogInfo($"开始恢复备份，ID: {selected.Id}，服务地址: {baseUrl}");
      var tempZipPath = Path.Combine(Path.GetTempPath(), $"localmovie-backup-{selected.Id}-{Guid.NewGuid():N}.zip");
      var tempExtractDir = Path.Combine(Path.GetTempPath(), $"localmovie-restore-{Guid.NewGuid():N}");

      try
      {
        using var http = CreateBackupHttpClient(baseUrl);
        using var response = await http.GetAsync($"api/backups/{Uri.EscapeDataString(selected.Id)}");

        if (!response.IsSuccessStatusCode)
        {
          var responseText = await response.Content.ReadAsStringAsync();
          LogError($"下载备份失败，状态码: {(int)response.StatusCode}，响应: {responseText}");
          UpdateStatus($"下载备份失败({(int)response.StatusCode}): {responseText}", isError: true);
          return;
        }

        await using (var fileStream = File.Create(tempZipPath))
        {
          await response.Content.CopyToAsync(fileStream);
        }

        Directory.CreateDirectory(tempExtractDir);
        ZipFile.ExtractToDirectory(tempZipPath, tempExtractDir, overwriteFiles: true);

        var sqlitePath = Directory
          .EnumerateFiles(tempExtractDir, "*", SearchOption.AllDirectories)
          .FirstOrDefault(f =>
          {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            return ext is ".db" or ".sqlite" or ".sqlite3";
          });

        var jsonPath = Directory
          .EnumerateFiles(tempExtractDir, "*.json", SearchOption.AllDirectories)
          .FirstOrDefault(f => !string.Equals(Path.GetFileName(f), "manifest.json", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(sqlitePath) || string.IsNullOrWhiteSpace(jsonPath))
        {
          LogError("恢复失败：备份包缺少 sqlite 或 json 文件。");
          UpdateStatus("备份包中缺少 sqlite 或 json 文件，无法恢复。", isError: true);
          return;
        }

        File.Copy(sqlitePath, _databaseFilePath, overwrite: true);
        File.Copy(jsonPath, _settingsFilePath, overwrite: true);

        _directories.Clear();
        _directories.AddRange(LoadDirectoriesFromSettings());
        DirectoriesListView.ItemsSource = null;
        DirectoriesListView.ItemsSource = _directories;
        ShowLastScanTime();

        UpdateStatus("备份已恢复成功。建议重启应用以重新加载数据库连接。", isError: false);
        LogInfo("备份恢复完成。");
      }
      catch (Exception ex)
      {
        LogError("恢复备份异常。", ex);
        UpdateStatus($"恢复备份失败: {ex.Message}", isError: true);
      }
      finally
      {
        TryDeleteFile(tempZipPath);
        TryDeleteDirectory(tempExtractDir);
      }
    }

    private async void DeleteSelectedBackup_Click(object sender, RoutedEventArgs e)
    {
      if (BackupsListView.SelectedItem is not BackupItem selected)
      {
        UpdateStatus("请先在列表中选择要删除的备份。", isError: true);
        return;
      }

      var baseUrl = GetBackupServiceBaseUrl();
      LogInfo($"开始删除备份，ID: {selected.Id}，服务地址: {baseUrl}");
      try
      {
        using var http = CreateBackupHttpClient(baseUrl);
        using var response = await http.DeleteAsync($"api/backups/{Uri.EscapeDataString(selected.Id)}");

        if (!response.IsSuccessStatusCode)
        {
          var responseText = await response.Content.ReadAsStringAsync();
          LogError($"删除备份失败，状态码: {(int)response.StatusCode}，响应: {responseText}");
          UpdateStatus($"删除备份失败({(int)response.StatusCode}): {responseText}", isError: true);
          return;
        }

        UpdateStatus($"备份 {selected.DisplayName} 已删除。", isError: false);
        LogInfo($"删除备份成功: {selected.DisplayName}");
        await RefreshBackupsAsync();
      }
      catch (Exception ex)
      {
        LogError("删除备份异常。", ex);
        UpdateStatus($"删除备份失败: {ex.Message}", isError: true);
      }
    }

    private void BackupServiceUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
      LogInfo($"备份服务地址变更为: {BackupServiceUrlTextBox.Text?.Trim()}");
      SaveBackupServiceUrlToSettings(BackupServiceUrlTextBox.Text?.Trim());
    }

    private List<string> LoadDirectoriesFromSettings()
    {
      var directories = new List<string>();
      if (!File.Exists(_settingsFilePath))
      {
        return directories;
      }

      try
      {
        var settings = LoadExistingSettings();
        var directoriesString = GetSettingString(settings, "VideoDirectories");
        if (!string.IsNullOrWhiteSpace(directoriesString))
        {
          directories.AddRange(
            directoriesString
              .Split('|', StringSplitOptions.RemoveEmptyEntries)
              .Distinct(StringComparer.OrdinalIgnoreCase));
        }
      }
      catch (Exception ex)
      {
        LogError("加载目录设置失败。", ex);
        DispatcherQueue.TryEnqueue(() => { StatusText.Text = $"加载设置失败: {ex.Message}"; });
      }

      return directories;
    }

    private DateTime GetLastScanTime()
    {
      if (!File.Exists(_settingsFilePath))
      {
        return DateTime.MinValue;
      }

      try
      {
        var settings = LoadExistingSettings();
        var timeString = GetSettingString(settings, "LastScanTime");
        if (DateTime.TryParse(timeString, out var lastScanTime))
        {
          return lastScanTime;
        }
      }
      catch (Exception ex)
      {
        LogError("加载扫描时间失败。", ex);
        DispatcherQueue.TryEnqueue(() => { StatusText.Text = $"加载设置失败: {ex.Message}"; });
      }

      return DateTime.MinValue;
    }

    private void SaveDirectoriesToSettings()
    {
      try
      {
        var settings = LoadExistingSettings();
        settings["VideoDirectories"] = string.Join("|", _directories);
        SaveSettings(settings);
      }
      catch (Exception ex)
      {
        LogError("保存目录设置失败。", ex);
        DispatcherQueue.TryEnqueue(() => { StatusText.Text = $"保存设置失败: {ex.Message}"; });
      }
    }

    private void UpdateLastScanTime(DateTime lastScanTime)
    {
      try
      {
        var settings = LoadExistingSettings();
        settings["LastScanTime"] = lastScanTime.ToString("o");
        SaveSettings(settings);
        ShowLastScanTime();
      }
      catch (Exception ex)
      {
        LogError("更新上次扫描时间失败。", ex);
      }
    }

    private Dictionary<string, object> LoadExistingSettings()
    {
      if (File.Exists(_settingsFilePath))
      {
        try
        {
          var json = File.ReadAllText(_settingsFilePath);
          var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json, new JsonSerializerOptions
          {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
          });

          return settings ?? new Dictionary<string, object>();
        }
        catch (Exception ex)
        {
          LogError("加载现有设置失败。", ex);
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
        return TimeSpan.Zero;
      }
    }

    private static HttpClient CreateBackupHttpClient(string baseUrl)
    {
      var normalizedBaseUrl = baseUrl.Trim().TrimEnd('/');
      return new HttpClient
      {
        BaseAddress = new Uri($"{normalizedBaseUrl}/movieBackup/")
      };
    }

    private static List<BackupItem> ParseBackupsFromJson(string json)
    {
      using var document = JsonDocument.Parse(json);
      var root = document.RootElement;

      IEnumerable<JsonElement> entries = root.ValueKind switch
      {
        JsonValueKind.Array => root.EnumerateArray(),
        JsonValueKind.Object when root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array => items.EnumerateArray(),
        JsonValueKind.Object when root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array => data.EnumerateArray(),
        _ => Enumerable.Empty<JsonElement>()
      };

      var result = new List<BackupItem>();
      foreach (var entry in entries)
      {
        var id = ReadJsonString(entry, "id") ?? ReadJsonString(entry, "backupId") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(id))
        {
          continue;
        }

        var name = ReadJsonString(entry, "name") ?? id;
        var createdAtRaw = ReadJsonString(entry, "createdAt") ?? ReadJsonString(entry, "created_at") ?? string.Empty;
        DateTime.TryParse(createdAtRaw, out var createdAt);
        var sizeBytes = ReadJsonLong(entry, "size") ?? ReadJsonLong(entry, "sizeBytes") ?? 0;

        result.Add(new BackupItem
        {
          Id = id,
          Name = name,
          CreatedAt = createdAt,
          Size = sizeBytes
        });
      }

      return result;
    }

    private static string? ReadJsonString(JsonElement element, string propertyName)
    {
      if (!element.TryGetProperty(propertyName, out var prop))
      {
        return null;
      }

      return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
    }

    private static long? ReadJsonLong(JsonElement element, string propertyName)
    {
      if (!element.TryGetProperty(propertyName, out var prop))
      {
        return null;
      }

      if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var n))
      {
        return n;
      }

      if (long.TryParse(prop.ToString(), out var parsed))
      {
        return parsed;
      }

      return null;
    }

    private static void TryDeleteFile(string path)
    {
      try
      {
        if (File.Exists(path))
        {
          File.Delete(path);
        }
      }
      catch
      {
      }
    }

    private static void TryDeleteDirectory(string path)
    {
      try
      {
        if (Directory.Exists(path))
        {
          Directory.Delete(path, recursive: true);
        }
      }
      catch
      {
      }
    }

    private static async Task CopyDatabaseForBackupAsync(string sourcePath, string destinationPath)
    {
      const int maxAttempts = 5;
      var delay = TimeSpan.FromMilliseconds(150);

      for (var attempt = 1; attempt <= maxAttempts; attempt++)
      {
        try
        {
          await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

          await using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

          await source.CopyToAsync(destination);
          await destination.FlushAsync();
          return;
        }
        catch (IOException) when (attempt < maxAttempts)
        {
          await Task.Delay(delay);
        }
      }

      throw new IOException("数据库文件当前被占用，请稍后重试备份。");
    }

    private string GetBackupServiceBaseUrl()
    {
      var settings = LoadExistingSettings();
      var url = GetSettingString(settings, BackupServiceUrlKey);
      return string.IsNullOrWhiteSpace(url) ? DefaultBackupServiceUrl : url;
    }

    private void SaveBackupServiceUrlToSettings(string? baseUrl)
    {
      if (string.IsNullOrWhiteSpace(baseUrl))
      {
        return;
      }

      var settings = LoadExistingSettings();
      settings[BackupServiceUrlKey] = baseUrl;
      SaveSettings(settings);
    }

    private void EnsureSettingsFileExists()
    {
      if (!File.Exists(_settingsFilePath))
      {
        File.WriteAllText(_settingsFilePath, "{}");
      }
    }

    private void SaveSettings(Dictionary<string, object> settings)
    {
      var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
      {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
      });

      File.WriteAllText(_settingsFilePath, json);
      LogInfo("设置文件已写入。");
    }

    private static string? GetSettingString(Dictionary<string, object> settings, string key)
    {
      if (!settings.TryGetValue(key, out var value) || value == null)
      {
        return null;
      }

      if (value is JsonElement element)
      {
        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
      }

      return value.ToString();
    }

    private void UpdateStatus(string message, bool isError)
    {
      var prefix = isError ? "[备份错误]" : "[备份]";
      StatusText.Text = $"{prefix} {message}";
      if (isError)
      {
        LogError(message);
      }
      else
      {
        LogInfo(message);
      }
    }

    private static void LogInfo(string message)
    {
      var log = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [SettingsPage] [INFO] {message}";
      Console.WriteLine(log);
      Debug.WriteLine(log);
    }

    private static void LogError(string message, Exception? ex = null)
    {
      var detail = ex == null ? message : $"{message} {ex}";
      var log = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [SettingsPage] [ERROR] {detail}";
      Console.WriteLine(log);
      Debug.WriteLine(log);
    }

    private sealed class BackupItem
    {
      public string Id { get; set; } = string.Empty;
      public string Name { get; set; } = string.Empty;
      public DateTime CreatedAt { get; set; }
      public long Size { get; set; }

      public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;

      public string Details
      {
        get
        {
          var time = CreatedAt == default
            ? "未知时间"
            : CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

          var size = Size <= 0 ? "大小未知" : $"{Math.Round(Size / 1024d / 1024d, 2)} MB";
          return $"ID: {Id} | 创建时间: {time} | {size}";
        }
      }
    }
  }
}
