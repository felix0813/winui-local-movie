using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace winui_local_movie
{
  public sealed partial class ArchiveFolderMigrationDialog : ContentDialog
  {
    private readonly ObservableCollection<string> _sourceFolders = new();

    public ArchiveFolderMigrationDialog()
    {
      InitializeComponent();
      SourceFoldersListView.ItemsSource = _sourceFolders;
    }

    private async void AddSourceFolder_Click(object sender, RoutedEventArgs e)
    {
      var selectedPath = await PickFolderPathAsync();
      if (selectedPath != null && !_sourceFolders.Any(path => PathsEqual(path, selectedPath)))
      {
        _sourceFolders.Add(selectedPath);
      }
    }

    private void RemoveSelectedFolders_Click(object sender, RoutedEventArgs e)
    {
      var selectedFolders = SourceFoldersListView.SelectedItems.Cast<string>().ToList();
      foreach (var folder in selectedFolders)
      {
        _sourceFolders.Remove(folder);
      }
    }

    private async void SelectDestinationFolder_Click(object sender, RoutedEventArgs e)
    {
      var selectedPath = await PickFolderPathAsync();
      if (selectedPath != null)
      {
        DestinationFolderTextBox.Text = selectedPath;
      }
    }

    private async Task<string?> PickFolderPathAsync()
    {
      var picker = new FolderPicker();
      var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(((App)Application.Current).MainWindow);
      WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
      picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
      picker.FileTypeFilter.Add("*");
      var folder = await picker.PickSingleFolderAsync();
      return folder?.Path;
    }

    private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
      var deferral = args.GetDeferral();
      args.Cancel = true;
      try
      {
        await MigrateFoldersAsync();
      }
      finally
      {
        deferral.Complete();
      }
    }

    private async Task MigrateFoldersAsync()
    {
      var destinationParentPath = Path.TrimEndingDirectorySeparator(DestinationFolderTextBox.Text.Trim());
      if (_sourceFolders.Count == 0)
      {
        ShowStatus("请至少添加一个归档文件夹。", true);
        return;
      }
      if (!Directory.Exists(destinationParentPath))
      {
        ShowStatus("请选择存在的目标父文件夹。", true);
        return;
      }

      if (_sourceFolders.Any(folder => !Directory.Exists(folder)))
      {
        ShowStatus("归档文件夹列表中包含不存在的目录，请移除后重试。", true);
        return;
      }

      IsPrimaryButtonEnabled = false;
      MigrationProgressBar.Visibility = Visibility.Visible;
      ShowStatus("正在迁移文件夹并更新数据库记录…", false);
      var migratedCount = 0;
      var updatedRows = 0;
      var errors = new List<string>();
      var filesMoved = false;
      try
      {
        foreach (var sourceFolder in _sourceFolders.ToList())
        {
          var sourcePath = Path.TrimEndingDirectorySeparator(sourceFolder);
          var folderName = Path.GetFileName(sourcePath);
          var destinationPath = Path.Combine(destinationParentPath, folderName);
          filesMoved = false;

          if (string.IsNullOrWhiteSpace(folderName))
          {
            errors.Add($"{sourcePath}：不能迁移磁盘根目录。");
            continue;
          }
          if (PathsEqual(sourcePath, destinationPath))
          {
            errors.Add($"{folderName}：文件夹已在目标位置。");
            continue;
          }
          if (IsPathInside(sourcePath, destinationParentPath))
          {
            errors.Add($"{folderName}：目标位置不能是该文件夹或其子文件夹。");
            continue;
          }
          if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
          {
            errors.Add($"{folderName}：目标文件夹已存在。");
            continue;
          }

          try
          {
            await Task.Run(() => MoveDirectory(sourcePath, destinationPath));
            filesMoved = true;
            updatedRows += await ((App)Application.Current).DatabaseService
              .UpdatePathsForMovedFolderAsync(sourcePath, destinationPath);
            migratedCount++;
          }
          catch (Exception ex)
          {
            var rollbackSucceeded = false;
            if (filesMoved && Directory.Exists(destinationPath) && !Directory.Exists(sourcePath))
            {
              try
              {
                await Task.Run(() => MoveDirectory(destinationPath, sourcePath));
                rollbackSucceeded = true;
              }
              catch { }
            }
            errors.Add($"{folderName}：{ex.Message}" + (filesMoved
              ? (rollbackSucceeded ? "（已还原）" : $"（文件仍位于 {destinationPath}）")
              : string.Empty));
          }
        }

        var errorMessage = errors.Count == 0 ? string.Empty : $" 失败 {errors.Count} 个：{string.Join("；", errors)}";
        ShowStatus($"迁移完成，成功 {migratedCount} 个，已更新 {updatedRows} 条数据库记录。{errorMessage}", errors.Count > 0);
      }
      finally
      {
        MigrationProgressBar.Visibility = Visibility.Collapsed;
        IsPrimaryButtonEnabled = true;
      }
    }

    private static void MoveDirectory(string sourcePath, string destinationPath)
    {
      try
      {
        Directory.Move(sourcePath, destinationPath);
      }
      catch (IOException) when (IsCrossVolumeMove(sourcePath, destinationPath) && !Directory.Exists(destinationPath))
      {
        CopyDirectory(sourcePath, destinationPath);
        Directory.Delete(sourcePath, true);
      }
    }

    private static bool IsCrossVolumeMove(string sourcePath, string destinationPath) =>
      !string.Equals(Path.GetPathRoot(sourcePath), Path.GetPathRoot(destinationPath), StringComparison.OrdinalIgnoreCase);

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
      Directory.CreateDirectory(destinationPath);
      foreach (var file in Directory.EnumerateFiles(sourcePath))
      {
        File.Copy(file, Path.Combine(destinationPath, Path.GetFileName(file)), false);
      }
      foreach (var directory in Directory.EnumerateDirectories(sourcePath))
      {
        CopyDirectory(directory, Path.Combine(destinationPath, Path.GetFileName(directory)));
      }
    }

    private static bool PathsEqual(string first, string second) =>
      string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

    private static bool IsPathInside(string parentPath, string childPath)
    {
      var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentPath)) + Path.DirectorySeparatorChar;
      var child = Path.TrimEndingDirectorySeparator(Path.GetFullPath(childPath)) + Path.DirectorySeparatorChar;
      return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    private void ShowStatus(string message, bool isError)
    {
      StatusTextBlock.Text = message;
      StatusTextBlock.Foreground = isError
        ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red)
        : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green);
      StatusTextBlock.Visibility = Visibility.Visible;
    }
  }
}
