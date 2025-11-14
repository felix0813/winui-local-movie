using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;

namespace winui_local_movie
{
  public sealed partial class AllVideosPage : Page
  {
    private enum ViewMode
    {
      All,
      Favorites,
      WatchLater
    }
    private readonly DatabaseService _databaseService;
    private int _currentPage = 1;
    private const int PageSize = 100;
    private int _totalVideos;
    private string _currentSortProperty = "DateAdded";
    private bool _isAscending = false;

    private ViewMode currentViewMode = ViewMode.All;
    private bool _isMultiSelectMode = false;
    private List<VideoModel> _selectedVideos = new List<VideoModel>();
    public AllVideosPage()
    {
      this.InitializeComponent();
      _databaseService = ((App)Application.Current).DatabaseService;
      LoadVideosAsync();

      VideosGridView.ContainerContentChanging += VideosGridView_ContainerContentChanging;

    }
    // 容器内容更改事件处理
    private void VideosGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
      if (args.ItemContainer is GridViewItem itemContainer && args.Item is VideoModel)
      {
        // 更新容器的选中状态样式
        UpdateContainerSelectionStyle(itemContainer);

        // 监听选择状态变化
        itemContainer.RegisterPropertyChangedCallback(GridViewItem.IsSelectedProperty, (s, e) =>
        {
          if (s is GridViewItem gridViewItem)
          {
            UpdateContainerSelectionStyle(gridViewItem);
          }
        });
      }
    }
    private void UpdateContainerSelectionStyle(GridViewItem item)
    {
      if (item.IsSelected)
      {
        // 选中状态 - 添加蓝色边框和背景色
        item.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 224, 240, 255));
        item.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Blue);
        item.BorderThickness = new Thickness(2);
      }
      else
      {
        // 未选中状态 - 恢复默认样式
        item.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        item.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        item.BorderThickness = new Thickness(0);
      }
    }
    private async void FilterButton_Click(object sender, RoutedEventArgs e)
    {
      var button = sender as Button;
      var tag = button.Tag.ToString();

      // 更新当前视图模式
      switch (tag)
      {
        case "All":
          currentViewMode = ViewMode.All;
          break;
        case "Favorites":
          currentViewMode = ViewMode.Favorites;
          break;
        case "WatchLater":
          currentViewMode = ViewMode.WatchLater;
          break;
      }

      // 更新按钮样式
      UpdateFilterButtonStyles(tag);

      // 重置到第一页
      _currentPage = 1;

      // 刷新数据
      await LoadVideosAsync();
    }

    private void UpdateFilterButtonStyles(string activeTag)
    {
      // 重置所有按钮样式
      AllVideosButton.Style = Application.Current.Resources["DefaultButtonStyle"] as Style;
      FavoritesButton.Style = Application.Current.Resources["DefaultButtonStyle"] as Style;
      WatchLaterButton.Style = Application.Current.Resources["DefaultButtonStyle"] as Style;

      // 设置活动按钮样式
      switch (activeTag)
      {
        case "All":
          AllVideosButton.Style = Application.Current.Resources["AccentButtonStyle"] as Style;
          break;
        case "Favorites":
          FavoritesButton.Style = Application.Current.Resources["AccentButtonStyle"] as Style;
          break;
        case "WatchLater":
          WatchLaterButton.Style = Application.Current.Resources["AccentButtonStyle"] as Style;
          break;
      }
    }


    // 修改 LoadVideosAsync 方法以支持排序
    private async Task LoadVideosAsync()
    {
      if (_isMultiSelectMode)
        return;

      List<VideoModel> videos = new List<VideoModel>();
      int totalCount = 0;

      switch (currentViewMode)
      {
        case ViewMode.Favorites:
          videos = await _databaseService.GetFavoriteVideosAsync();
          totalCount = videos.Count;
          // 应用分页
          var favoritePageVideos = videos.Skip((_currentPage - 1) * PageSize).Take(PageSize).ToList();
          VideosGridView.ItemsSource = favoritePageVideos;
          break;

        case ViewMode.WatchLater:
          videos = await _databaseService.GetWatchLaterVideosAsync();
          totalCount = videos.Count;
          // 应用分页
          var watchLaterPageVideos = videos.Skip((_currentPage - 1) * PageSize).Take(PageSize).ToList();
          VideosGridView.ItemsSource = watchLaterPageVideos;
          break;

        default: // ViewMode.All
                 // 根据当前排序选项获取数据
          if (_currentSortProperty == "FileSize")
          {
            videos = await _databaseService.GetVideosSortedByFileSizeAsync(_isAscending,
                (_currentPage - 1) * PageSize, PageSize);
          }
          else if (_currentSortProperty == "CreationDate")
          {
            videos = await _databaseService.GetVideosSortedByCreationDateAsync(_isAscending,
                (_currentPage - 1) * PageSize, PageSize);
          }
          else if (_currentSortProperty == "Duration")
          {
            videos = await _databaseService.GetVideosSortedByDurationAsync(_isAscending,
                (_currentPage - 1) * PageSize, PageSize);
          }
          else
          {
            videos = await _databaseService.GetVideosSortedByDateAddedAsync(_isAscending,
                (_currentPage - 1) * PageSize, PageSize);
          }

          VideosGridView.ItemsSource = videos;
          totalCount = await _databaseService.GetTotalVideosCountAsync();
          break;
      }
      _totalVideos = totalCount;
      // 更新分页信息
      int totalPages = (int)Math.Ceiling((double)totalCount / PageSize);
      PageInfoText.Text = $"第 {_currentPage} 页，共 {totalPages} 页";

      // 更新按钮状态
      PreviousPageButton.IsEnabled = _currentPage > 1;
      NextPageButton.IsEnabled = _currentPage < totalPages;
    }

    private void PreviousPageButton_Click(object sender, RoutedEventArgs e)
    {
      if (_currentPage > 1)
      {
        _currentPage--;
        LoadVideosAsync();
      }
    }

    private void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
      int totalPages = (int)Math.Ceiling((double)_totalVideos / PageSize);
      if (_currentPage < totalPages)
      {
        _currentPage++;
        LoadVideosAsync();
      }
    }

    // 缩略图双击播放事件
    private async void Thumbnail_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
      if (_isMultiSelectMode)
        return;
      var frameworkElement = sender as FrameworkElement;
      if (frameworkElement?.DataContext is VideoModel video)
      {
        await PlayVideoAsync(video);
      }
    }

    // 喜欢按钮点击事件
    private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
      if (_isMultiSelectMode)
        return;
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
      if (_isMultiSelectMode)
        return;
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
      if (_isMultiSelectMode)
        return;
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
            LoadVideosAsync();

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
      if (_isMultiSelectMode)
        return;
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
        // 异步执行缩略图生成，不等待完成
        Task.Run(async () => await GenerateThumbnailAsync(video.FilePath));

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
    private async Task<string> GenerateThumbnailAsync(string videoPath)
    {
      try
      {
        if (!File.Exists(videoPath))
          return null;

        // 生成缩略图路径：与视频同目录，文件名为 [原文件名]-poster.jpg
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(videoPath);
        string directory = Path.GetDirectoryName(videoPath);
        string thumbnailPath = Path.Combine(directory, $"{fileNameWithoutExtension}-poster.jpg");

        // 如果缩略图已存在，直接返回路径
        if (File.Exists(thumbnailPath))
          return thumbnailPath;

        // 使用 Windows.Storage API 生成缩略图
        var storageFile = await StorageFile.GetFileFromPathAsync(videoPath);
        var thumbnail = await storageFile.GetThumbnailAsync(
            Windows.Storage.FileProperties.ThumbnailMode.SingleItem,
            320, // 缩略图大小
            Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale);

        if (thumbnail != null && thumbnail.Size > 0)
        {
          // 使用更可靠的方式保存缩略图
          var folder = await StorageFolder.GetFolderFromPathAsync(directory);
          var thumbnailFile = await folder.CreateFileAsync($"{fileNameWithoutExtension}-poster.jpg",
              CreationCollisionOption.ReplaceExisting);

          // 将缩略图数据写入文件
          using (var inputStream = thumbnail.GetInputStreamAt(0))
          using (var outputStream = await thumbnailFile.OpenAsync(FileAccessMode.ReadWrite))
          {
            await RandomAccessStream.CopyAsync(inputStream, outputStream);
          }

          return thumbnailPath;
        }
      }
      catch (Exception ex)
      {
        // 记录错误信息到调试输出
        System.Diagnostics.Debug.WriteLine($"生成缩略图失败: {ex.Message}");
        System.Diagnostics.Debug.WriteLine($"堆栈跟踪: {ex.StackTrace}");
      }

      return null;
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


    // 在 AllVideosPage.xaml.cs 类中添加以下方法

    private async void DetailsButton_Click(object sender, RoutedEventArgs e)
    {
      var button = sender as Button;
      if (button?.Tag is VideoModel video)
      {
        await ShowVideoDetailsDialog(video);
      }
    }

    private async Task ShowVideoDetailsDialog(VideoModel video)
    {
      // 创建弹窗内容
      var stackPanel = new StackPanel();
      // 添加视频信息
      AddDetailRow(stackPanel, "标题:", video.Title);
      AddDetailRow(stackPanel, "文件路径:", video.FilePath);
      AddDetailRow(stackPanel, "时长:", video.FormatDuration(video.Duration));
      AddDetailRow(stackPanel, "文件大小:", $"{video.FileSize} MB");
      AddDetailRow(stackPanel, "创建日期:", video.CreationDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A");
      AddDetailRow(stackPanel, "加入时间:", video.DateAdded.ToString("yyyy-MM-dd HH:mm:ss"));
      AddDetailRow(stackPanel, "收藏状态:", video.IsFavorite ? "是" : "否");
      AddDetailRow(stackPanel, "稍后观看:", video.IsWatchLater ? "是" : "否");

      // 创建并配置弹窗
      var dialog = new ContentDialog
      {
        Title = "详细信息",
        Content = new ScrollViewer
        {
          Content = stackPanel,
          Padding = new Thickness(10),
          MaxHeight = 500
        },
        CloseButtonText = "关闭",
        XamlRoot = this.Content.XamlRoot
      };

      await dialog.ShowAsync();
    }

    private void AddDetailRow(StackPanel parent, string label, string value)
{
    var row = new Grid();
    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

    var labelBlock = new TextBlock
    {
        Text = label,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 0, 10, 0),
        IsTextSelectionEnabled = true  // 允许选择标签文本
    };

    var valueBlock = new TextBlock
    {
        Text = value ?? "N/A",
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true  // 允许选择值文本
    };

    Grid.SetColumn(labelBlock, 0);
    Grid.SetColumn(valueBlock, 1);

    row.Children.Add(labelBlock);
    row.Children.Add(valueBlock);

    parent.Children.Add(row);
    parent.Children.Add(new Border
    {
        BorderBrush = new SolidColorBrush(Colors.LightGray),
        BorderThickness = new Thickness(0, 0, 0, 1),
        Margin = new Thickness(0, 5, 0, 5)
    });
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
      if (_isMultiSelectMode)
        return;
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
      await LoadVideosAsync();
    }
    // 多选按钮点击事件
    private void MultiSelectButton_Click(object sender, RoutedEventArgs e)
    {
      EnterMultiSelectMode();
    }

    // 退出多选按钮点击事件
    private void ExitMultiSelectButton_Click(object sender, RoutedEventArgs e)
    {
      ExitMultiSelectMode();
    }

    // 进入多选模式
    private void EnterMultiSelectMode()
    {
      _isMultiSelectMode = true;

      // 更新UI状态
      MultiSelectButton.Visibility = Visibility.Collapsed;
      ExitMultiSelectButton.Visibility = Visibility.Visible;

      // 显示批量操作按钮
      BulkDeleteButton.Visibility = Visibility.Visible;
      BulkRefreshMetadataButton.Visibility = Visibility.Visible;

      // 禁用分页控件
      PreviousPageButton.IsEnabled = false;
      NextPageButton.IsEnabled = false;

      // 更改GridView选择模式
      VideosGridView.SelectionMode = ListViewSelectionMode.Multiple;

      // 清空选择列表
      _selectedVideos.Clear();

      // 强制刷新所有容器样式
      for (int i = 0; i < VideosGridView.Items.Count; i++)
      {
        if (VideosGridView.ContainerFromIndex(i) is GridViewItem container)
        {
          UpdateContainerSelectionStyle(container);
        }
      }
    }

    // 退出多选模式
    private void ExitMultiSelectMode()
    {
      _isMultiSelectMode = false;

      // 更新UI状态
      MultiSelectButton.Visibility = Visibility.Visible;
      ExitMultiSelectButton.Visibility = Visibility.Collapsed;

      // 隐藏批量操作按钮
      BulkDeleteButton.Visibility = Visibility.Collapsed;
      BulkRefreshMetadataButton.Visibility = Visibility.Collapsed;

      // 恢复分页控件状态
      int totalPages = (int)Math.Ceiling((double)_totalVideos / PageSize);
      PreviousPageButton.IsEnabled = _currentPage > 1;
      NextPageButton.IsEnabled = _currentPage < totalPages;

      // 恢复GridView选择模式
      VideosGridView.SelectionMode = ListViewSelectionMode.Single;

      // 清空选择
      _selectedVideos.Clear();

      // 恢复所有容器为未选中样式
      for (int i = 0; i < VideosGridView.Items.Count; i++)
      {
        if (VideosGridView.ContainerFromIndex(i) is GridViewItem container)
        {
          UpdateContainerSelectionStyle(container);
        }
      }
    }

    // GridView选择变化事件
    private void VideosGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (_isMultiSelectMode)
      {
        // 添加新选择项
        foreach (var item in e.AddedItems)
        {
          if (item is VideoModel video && !_selectedVideos.Contains(video))
          {
            _selectedVideos.Add(video);
          }
        }

        // 移除取消选择项
        foreach (var item in e.RemovedItems)
        {
          if (item is VideoModel video)
          {
            _selectedVideos.Remove(video);
          }
        }

        // 强制更新所有容器的样式
        for (int i = 0; i < VideosGridView.Items.Count; i++)
        {
          if (VideosGridView.ContainerFromIndex(i) is GridViewItem container)
          {
            UpdateContainerSelectionStyle(container);
          }
        }
      }
    }
    // 批量删除按钮点击事件
    private async void BulkDeleteButton_Click(object sender, RoutedEventArgs e)
    {
      if (_selectedVideos.Count == 0)
      {
        await ShowErrorDialog("请选择至少一个视频进行删除");
        return;
      }

      try
      {
        // 确认对话框
        var dialog = new ContentDialog
        {
          Title = "确认删除",
          Content = $"确定要删除 {_selectedVideos.Count} 个视频文件和记录吗？",
          PrimaryButtonText = "删除",
          CloseButtonText = "取消",
          XamlRoot = this.Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
          int deletedCount = 0;
          List<string> failedFiles = new List<string>();

          // 删除每个选中的视频
          foreach (var video in _selectedVideos)
          {
            try
            {
              // 删除文件
              if (File.Exists(video.FilePath))
              {
                File.Delete(video.FilePath);
              }

              // 删除数据库记录
              await _databaseService.DeleteVideoAsync(video.Id);
              deletedCount++;
            }
            catch (Exception ex)
            {
              failedFiles.Add($"{video.Title}: {ex.Message}");
            }
          }

          // 重新加载当前页
          await LoadVideosAsync();

          // 显示结果
          string message = $"成功删除 {deletedCount} 个视频";
          if (failedFiles.Count > 0)
          {
            message += $"\n\n以下文件删除失败:\n{string.Join("\n", failedFiles.Take(5))}";
            if (failedFiles.Count > 5)
              message += $"\n...还有 {failedFiles.Count - 5} 个文件";
          }

          var successDialog = new ContentDialog
          {
            Title = "删除结果",
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = this.Content.XamlRoot
          };
          await successDialog.ShowAsync();

          // 退出多选模式
          ExitMultiSelectMode();
        }
      }
      catch (Exception ex)
      {
        await ShowErrorDialog($"批量删除失败: {ex.Message}");
      }
    }

    // 批量刷新元数据按钮点击事件
    private async void BulkRefreshMetadataButton_Click(object sender, RoutedEventArgs e)
    {
      if (_selectedVideos.Count == 0)
      {
        await ShowErrorDialog("请选择至少一个视频进行元数据刷新");
        return;
      }

      try
      {
        int refreshedCount = 0;
        List<string> failedFiles = new List<string>();

        // 刷新每个选中的视频
        foreach (var video in _selectedVideos)
        {
          try
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

            // 异步执行缩略图生成，不等待完成
            Task.Run(async () => await GenerateThumbnailAsync(video.FilePath));

            // 更新数据库
            await _databaseService.RefreshVideoMetadataAsync(
                video.FilePath,
                async (path) => await GetVideoDurationAsync(path)
            );

            // 更新界面数据
            video.Duration = duration;
            video.FileSize = (long)fileSizeMB;
            refreshedCount++;
          }
          catch (Exception ex)
          {
            failedFiles.Add($"{video.Title}: {ex.Message}");
          }
        }

        // 显示结果
        string message = $"成功刷新 {refreshedCount} 个视频的元数据";
        if (failedFiles.Count > 0)
        {
          message += $"\n\n以下文件刷新失败:\n{string.Join("\n", failedFiles.Take(5))}";
          if (failedFiles.Count > 5)
            message += $"\n...还有 {failedFiles.Count - 5} 个文件";
        }

        var dialog = new ContentDialog
        {
          Title = "刷新结果",
          Content = message,
          CloseButtonText = "确定",
          XamlRoot = this.Content.XamlRoot
        };
        await dialog.ShowAsync();

        // 如果有刷新列表方法，建议调用
        await LoadVideosAsync();
      }
      catch (Exception ex)
      {
        await ShowErrorDialog($"批量刷新元数据失败: {ex.Message}");
      }
    }
  }
}