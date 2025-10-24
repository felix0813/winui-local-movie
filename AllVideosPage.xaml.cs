using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace winui_local_movie
{
  public sealed partial class AllVideosPage : Page
  {
    private readonly DatabaseService _databaseService;
    private int _currentPage = 1;
    private const int PageSize = 100;
    private int _totalVideos;
    private string _currentSortProperty = "DateAdded";
    private bool _isAscending = false;

    public AllVideosPage()
    {
      this.InitializeComponent();
      _databaseService = ((App)Application.Current).DatabaseService;
      LoadVideosAsync(_currentPage);
    }

    // 修改 LoadVideosAsync 方法以支持排序
    private async Task LoadVideosAsync(int page)
    {
      int pageSize = 100; // 每页显示的视频数量
      int offset = (page - 1) * pageSize;

      List<VideoModel> videos = new List<VideoModel>();

      // 根据当前排序选项获取数据
      switch (_currentSortProperty)
      {
        case "FileSize":
          videos = await _databaseService.GetVideosSortedByFileSizeAsync(!_isAscending, offset, pageSize);
          break;
        case "CreationDate":
          videos = await _databaseService.GetVideosSortedByCreationDateAsync(!_isAscending, offset, pageSize);
          break;
        case "Duration":
          videos = await _databaseService.GetVideosSortedByDurationAsync(!_isAscending, offset, pageSize);
          break;
        case "DateAdded":
        default:
          videos = await _databaseService.GetVideosSortedByDateAddedAsync(!_isAscending, offset, pageSize);
          break;
      }

      VideosGridView.ItemsSource = videos;

      // 更新分页信息
      _totalVideos = await _databaseService.GetTotalVideosCountAsync();
      int totalPages = (int)Math.Ceiling((double)_totalVideos / pageSize);
      PageInfoText.Text = $"第 {page} 页，共 {totalPages} 页";

      // 更新按钮状态
      PreviousPageButton.IsEnabled = page > 1;
      NextPageButton.IsEnabled = page < totalPages;
    }

    private void PreviousPageButton_Click(object sender, RoutedEventArgs e)
    {
      if (_currentPage > 1)
      {
        _currentPage--;
        LoadVideosAsync(_currentPage);
      }
    }

    private void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
      int totalPages = (int)Math.Ceiling((double)_totalVideos / PageSize);
      if (_currentPage < totalPages)
      {
        _currentPage++;
        LoadVideosAsync(_currentPage);
      }
    }

    // 缩略图双击播放事件
    private async void Thumbnail_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
      var frameworkElement = sender as FrameworkElement;
      if (frameworkElement?.DataContext is VideoModel video)
      {
        await PlayVideoAsync(video);
      }
    }

    // 喜欢按钮点击事件
    private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
      var button = sender as Button;
      if (button?.Tag is VideoModel video)
      {
        try
        {
          // 切换喜欢状态
          video.IsFavorite = !video.IsFavorite;
          await _databaseService.UpdateVideoFavoriteStatusAsync(video.Id, video.IsFavorite);

          // 直接更新按钮UI，无需弹窗
          UpdateFavoriteButtonUI(button, video.IsFavorite);
        }
        catch (Exception ex)
        {
          await ShowErrorDialog($"操作失败: {ex.Message}");
        }
      }
    }

    // 稍后再看按钮点击事件
    private async void WatchLaterButton_Click(object sender, RoutedEventArgs e)
    {
      var button = sender as Button;
      if (button?.Tag is VideoModel video)
      {
        try
        {
          // 切换稍后再看状态
          video.IsWatchLater = !video.IsWatchLater;
          await _databaseService.UpdateVideoWatchLaterStatusAsync(video.Id, video.IsWatchLater);

          // 直接更新按钮UI，无需弹窗
          UpdateWatchLaterButtonUI(button, video.IsWatchLater);
        }
        catch (Exception ex)
        {
          await ShowErrorDialog($"操作失败: {ex.Message}");
        }
      }
    }

    // 更新喜欢按钮UI的方法
    private void UpdateFavoriteButtonUI(Button button, bool isFavorite)
    {
      button.Content = isFavorite ? "❤" : "♡";
      button.Foreground = isFavorite ?
          new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red) :
          new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    // 更新稍后再看按钮UI的方法
    private void UpdateWatchLaterButtonUI(Button button, bool isWatchLater)
    {
      button.Content = isWatchLater ? "⏱" : "⏳";
      button.Foreground = isWatchLater ?
          new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange) :
          new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    // 删除文件按钮点击事件
    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
      var button = sender as Button;
      if (button?.Tag is VideoModel video)
      {
        try
        {
          // 确认对话框
          var dialog = new ContentDialog
          {
            Title = "确认删除",
            Content = $"确定要删除视频文件和记录吗？\n{video.Title}",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            XamlRoot = this.Content.XamlRoot
          };

          var result = await dialog.ShowAsync();
          if (result == ContentDialogResult.Primary)
          {
            // 删除文件
            if (File.Exists(video.FilePath))
            {
              File.Delete(video.FilePath);
            }

            // 删除数据库记录
            await _databaseService.DeleteVideoAsync(video.Id);

            // 重新加载当前页
            LoadVideosAsync(_currentPage);

            // 提示删除成功
            var successDialog = new ContentDialog
            {
              Title = "提示",
              Content = "视频已删除",
              CloseButtonText = "确定",
              XamlRoot = this.Content.XamlRoot
            };
            await successDialog.ShowAsync();
          }
        }
        catch (Exception ex)
        {
          await ShowErrorDialog($"删除失败: {ex.Message}");
        }
      }
    }

    private async void RefreshMetadataButton_Click(object sender, RoutedEventArgs e)
    {
      if (sender is Button btn && btn.Tag is VideoModel video)
      {
        // 获取视频时长
        TimeSpan duration = await GetVideoDurationAsync(video.FilePath);

        // 获取文件大小（MB）
        double fileSizeMB = 0;
        if (File.Exists(video.FilePath))
        {
          var fileInfo = new FileInfo(video.FilePath);
          fileSizeMB = Math.Round(fileInfo.Length / 1024.0 / 1024.0, 0);
        }

        // 更新数据库
        await ((App)Application.Current).DatabaseService.RefreshVideoMetadataAsync(
            video.FilePath,
            async (path) => await GetVideoDurationAsync(path)
        );

        // 可选：刷新界面数据
        video.Duration = duration;
        video.FileSize = (long)fileSizeMB;
        // 如果有刷新列表方法，建议调用
        // await LoadVideosAsync();

        var dialog = new ContentDialog
        {
          Title = "提示",
          Content = "元数据已刷新",
          CloseButtonText = "确定",
          XamlRoot = this.Content.XamlRoot
        };
        await dialog.ShowAsync();
      }
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


    // 详情页按钮点击事件 (TODO)
    private void DetailsButton_Click(object sender, RoutedEventArgs e)
    {
      var button = sender as Button;
      if (button?.Tag is VideoModel video)
      {
        // TODO: 导航到详情页
        NavigateToDetailsPage(video);
      }
    }

    // TODO: 导航到详情页的方法
    private void NavigateToDetailsPage(VideoModel video)
    {
      // TODO: 实现导航到详情页的逻辑
      // 示例: Frame.Navigate(typeof(VideoDetailsPage), video.Id);
    }

    // 播放视频方法
    private async Task PlayVideoAsync(VideoModel video)
    {
      try
      {
        // 使用系统默认程序打开视频
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(video.FilePath);
        await Windows.System.Launcher.LaunchFileAsync(file);
      }
      catch (Exception ex)
      {
        await ShowErrorDialog($"无法打开视频: {ex.Message}");
      }
    }

    // 添加这个方法到 AllVideosPage 类中
    private void VideosGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
      // 这里可以处理项目点击事件
      // 例如：选择项目、显示详细信息等
      // 注意：双击播放已经在 Thumbnail_Tapped 中处理
    }

    // 显示错误对话框的辅助方法
    private async Task ShowErrorDialog(string message)
    {
      var dialog = new ContentDialog
      {
        Title = "错误",
        Content = message,
        CloseButtonText = "确定",
        XamlRoot = this.Content.XamlRoot
      };
      await dialog.ShowAsync();
    }

    // 排序属性选择变化事件
    private void SortPropertyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (SortPropertyComboBox.SelectedItem is ComboBoxItem selectedItem)
      {
        _currentSortProperty = selectedItem.Tag.ToString();
      }
    }

    // 排序方向选择变化事件
    private void SortOrderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (SortOrderComboBox.SelectedItem is ComboBoxItem selectedItem)
      {
        _isAscending = selectedItem.Tag.ToString() == "ASC";
      }
    }

    // 应用排序按钮点击事件
    private async void ApplySortButton_Click(object sender, RoutedEventArgs e)
    {
      await LoadVideosAsync(_currentPage);
    }
  }
}