using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using Windows.System;

namespace winui_local_movie
{
  public sealed partial class GalleryPage : Page
  {
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
    private static readonly IComparer<string> FileNameComparer = new NaturalFileNameComparer();
    private readonly DatabaseService _databaseService;
    private readonly DispatcherTimer _autoPlayTimer = new();
    private List<GalleryImage> _readerImages = new();
    private GalleryAlbum? _currentAlbum;
    private int _currentImageIndex;
    private bool _suppressThumbnailSelection;
    private bool _isAutoPlaying;

    public GalleryPage()
    {
      InitializeComponent();
      _databaseService = ((App)Application.Current).DatabaseService;
      _autoPlayTimer.Interval = TimeSpan.FromSeconds(5);
      _autoPlayTimer.Tick += (_, _) => ShowImage(_currentImageIndex + 1, true);
      Loaded += async (_, _) => await LoadAlbumsAsync();
    }

    private async Task LoadAlbumsAsync()
    {
      var albums = await _databaseService.GetGalleryAlbumsAsync();
      AlbumsGridView.ItemsSource = albums;
      LibrarySummaryText.Text = albums.Count == 0 ? "导入一个包含图片的文件夹以开始" : $"共 {albums.Count} 个图集或漫画";
    }

    private static List<string> FindImages(string folderPath) => Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
      .Where(path => ImageExtensions.Contains(Path.GetExtension(path))).OrderBy(path => Path.GetFileName(path), FileNameComparer).ToList();

    private async void ImportFolder_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        var ownerWindow = WinRT.Interop.WindowNative.GetWindowHandle(((App)Application.Current).MainWindow);
        var selectedFolders = MultiFolderPicker.PickFolders(ownerWindow);
        foreach (var folderPath in selectedFolders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
          await ImportOrRefreshFolderAsync(folderPath, Path.GetFileName(folderPath));
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[GalleryPage] 批量选择文件夹失败：{ex}");
        await ShowDialogAsync("无法打开文件夹选择器", ex.Message);
      }
    }

    private async Task ImportOrRefreshFolderAsync(string folderPath, string title)
    {
      var images = await Task.Run(() => FindImages(folderPath));
      if (images.Count == 0) { await ShowDialogAsync("未找到图片", "该文件夹不包含 jpg、png、gif、webp、bmp 等受支持的图片。"); return; }
      await _databaseService.UpsertGalleryAlbumAsync(folderPath, title, images);
      await LoadAlbumsAsync();
    }

    private async void RefreshAlbum_Click(object sender, RoutedEventArgs e)
    {
      if (AlbumsGridView.SelectedItem is not GalleryAlbum album) { await ShowDialogAsync("请选择图集", "请先选中要刷新的图集。"); return; }
      if (!Directory.Exists(album.FolderPath)) { await ShowDialogAsync("文件夹不可用", "原文件夹不存在或当前无法访问。"); return; }
      await ImportOrRefreshFolderAsync(album.FolderPath, album.Title);
    }

    private async void RemoveAlbum_Click(object sender, RoutedEventArgs e)
    {
      if (AlbumsGridView.SelectedItem is not GalleryAlbum album) { await ShowDialogAsync("请选择图集", "请先选中要移除的图集。"); return; }
      await _databaseService.DeleteGalleryAlbumAsync(album.Id);
      await LoadAlbumsAsync();
    }

    private async void AlbumsGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
      if (e.ClickedItem is not GalleryAlbum album) return;
      _currentAlbum = album;
      _readerImages = (await _databaseService.GetGalleryImagesAsync(album.Id))
        .OrderBy(image => Path.GetFileName(image.FilePath), FileNameComparer)
        .ToList();
      if (_readerImages.Count == 0) { await ShowDialogAsync("图集为空", "请刷新图集以读取文件夹中的图片。"); return; }
      AlbumImagesGridView.ItemsSource = _readerImages;
      UpdateAlbumDetail();
      LibraryPanel.Visibility = Visibility.Collapsed;
      AlbumDetailPanel.Visibility = Visibility.Visible;
    }

    private void UpdateAlbumDetail()
    {
      if (_currentAlbum is null) return;
      AlbumDetailTitleText.Text = _currentAlbum.Title;
      AlbumDetailSummaryText.Text = _currentAlbum.ProgressText();
      AlbumFolderPathText.Text = _currentAlbum.FolderPath;
      AlbumImageCountText.Text = $"{_readerImages.Count} 张";
      AlbumDateAddedText.Text = _currentAlbum.DateAdded.ToString("yyyy-MM-dd HH:mm");
      AlbumReadingStatusText.Text = _currentAlbum.LastViewedAt is null
        ? "未开始"
        : $"上次读到第 {Math.Min(_currentAlbum.LastViewedIndex + 1, _readerImages.Count)} 张";
    }

    private void AlbumImagesGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
      if (e.ClickedItem is not GalleryImage image) return;
      var index = _readerImages.IndexOf(image);
      if (index >= 0) OpenReader(index);
    }

    private void ContinueReading_Click(object sender, RoutedEventArgs e)
    {
      if (_currentAlbum is null || _readerImages.Count == 0) return;
      OpenReader(Math.Clamp(_currentAlbum.LastViewedIndex, 0, _readerImages.Count - 1));
    }

    private void OpenReader(int index)
    {
      if (_currentAlbum is null || _readerImages.Count == 0) return;
      SetAutoPlay(false);
      AlbumDetailPanel.Visibility = Visibility.Collapsed;
      ReaderPanel.Visibility = Visibility.Visible;
      ReaderTitleText.Text = _currentAlbum.Title;
      FavoriteButton.Content = _currentAlbum.IsFavorite ? "♥ 已收藏" : "♡ 收藏";
      ThumbnailsListView.ItemsSource = _readerImages;
      ShowImage(index, false);
      Focus(FocusState.Programmatic);
    }

    private async void ShowImage(int index, bool wrap)
    {
      if (_currentAlbum is null || _readerImages.Count == 0) return;
      if (wrap) index = (index + _readerImages.Count) % _readerImages.Count;
      else index = Math.Clamp(index, 0, _readerImages.Count - 1);
      _currentImageIndex = index; var image = _readerImages[index];
      ReaderImage.Source = new BitmapImage(new Uri(image.FilePath));
      ResetImageView();
      ReaderPositionText.Text = $"第 {index + 1} / {_readerImages.Count} 张"; PreviousButton.IsEnabled = index > 0; NextButton.IsEnabled = index < _readerImages.Count - 1;
      _suppressThumbnailSelection = true; ThumbnailsListView.SelectedIndex = index; ThumbnailsListView.ScrollIntoView(image); _suppressThumbnailSelection = false;
      await _databaseService.UpdateGalleryProgressAsync(_currentAlbum.Id, index);
      _currentAlbum.LastViewedAt = DateTime.Now;
      _currentAlbum.LastViewedIndex = index;
    }

    private void ResetImageView()
    {
      UpdateReaderImageViewport();
      ImageScrollViewer.ChangeView(0, 0, 1.0f, true);
      DispatcherQueue.TryEnqueue(() =>
      {
        UpdateReaderImageViewport();
        ImageScrollViewer.ChangeView(0, 0, 1.0f, true);
      });
    }

    private void ImageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateReaderImageViewport();

    private void UpdateReaderImageViewport()
    {
      var width = ImageScrollViewer.ViewportWidth > 0 ? ImageScrollViewer.ViewportWidth : ImageScrollViewer.ActualWidth;
      var height = ImageScrollViewer.ViewportHeight > 0 ? ImageScrollViewer.ViewportHeight : ImageScrollViewer.ActualHeight;
      if (width > 0) ReaderImageViewport.Width = width;
      if (height > 0) ReaderImageViewport.Height = height;
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e) => ShowImage(_currentImageIndex - 1, false);
    private void NextButton_Click(object sender, RoutedEventArgs e) => ShowImage(_currentImageIndex + 1, false);
    private void ThumbnailsListView_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_suppressThumbnailSelection && ThumbnailsListView.SelectedIndex >= 0) ShowImage(ThumbnailsListView.SelectedIndex, false); }
    private void Reader_PointerWheelChanged(object sender, PointerRoutedEventArgs e) { var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta; if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control)) ImageScrollViewer.ChangeView(null, null, Math.Clamp(ImageScrollViewer.ZoomFactor + (delta > 0 ? .2f : -.2f), .2f, 5f)); else ShowImage(_currentImageIndex + (delta < 0 ? 1 : -1), false); e.Handled = true; }
    private void Page_KeyDown(object sender, KeyRoutedEventArgs e) { if (ReaderPanel.Visibility != Visibility.Visible) return; if (e.Key is VirtualKey.Right or VirtualKey.Down or VirtualKey.Space) ShowImage(_currentImageIndex + 1, false); else if (e.Key is VirtualKey.Left or VirtualKey.Up) ShowImage(_currentImageIndex - 1, false); else if (e.Key == VirtualKey.Escape) CloseReader(); }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ImageScrollViewer.ChangeView(null, null, Math.Min(5, ImageScrollViewer.ZoomFactor + .25f));
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ImageScrollViewer.ChangeView(null, null, Math.Max(.2f, ImageScrollViewer.ZoomFactor - .25f));
    private void ResetZoom_Click(object sender, RoutedEventArgs e) => ResetImageView();
    private void AutoPlayButton_Click(object sender, RoutedEventArgs e) => SetAutoPlay(!_isAutoPlaying);
    private void SetAutoPlay(bool enabled)
    {
      _isAutoPlaying = enabled && ReaderPanel.Visibility == Visibility.Visible && _readerImages.Count > 1;
      if (_isAutoPlaying) _autoPlayTimer.Start(); else _autoPlayTimer.Stop();
      AutoPlayButton.Content = _isAutoPlaying ? "暂停自动播放" : "开始自动播放";
    }
    private void AutoPlayIntervalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (AutoPlayIntervalComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var seconds)) _autoPlayTimer.Interval = TimeSpan.FromSeconds(seconds); }
    private async void FavoriteButton_Click(object sender, RoutedEventArgs e) { if (_currentAlbum is null) return; _currentAlbum.IsFavorite = !_currentAlbum.IsFavorite; await _databaseService.UpdateGalleryFavoriteAsync(_currentAlbum.Id, _currentAlbum.IsFavorite); FavoriteButton.Content = _currentAlbum.IsFavorite ? "♥ 已收藏" : "♡ 收藏"; UpdateAlbumDetail(); }
    private void CloseReader_Click(object sender, RoutedEventArgs e) => CloseReader();
    private void CloseReader() { SetAutoPlay(false); ReaderPanel.Visibility = Visibility.Collapsed; AlbumDetailPanel.Visibility = Visibility.Visible; UpdateAlbumDetail(); }
    private async void BackToLibrary_Click(object sender, RoutedEventArgs e)
    {
      SetAutoPlay(false);
      AlbumDetailPanel.Visibility = Visibility.Collapsed;
      ReaderPanel.Visibility = Visibility.Collapsed;
      LibraryPanel.Visibility = Visibility.Visible;
      await LoadAlbumsAsync();
    }
    private async Task ShowDialogAsync(string title, string content) { var dialog = new ContentDialog { Title = title, Content = content, CloseButtonText = "确定", XamlRoot = Content.XamlRoot }; await dialog.ShowAsync(); }

    private sealed class NaturalFileNameComparer : IComparer<string>
    {
      public int Compare(string? left, string? right)
      {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        var leftIndex = 0;
        var rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
          if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex]))
          {
            var leftRunStart = leftIndex;
            var rightRunStart = rightIndex;
            while (leftIndex < left.Length && left[leftIndex] == '0') leftIndex++;
            while (rightIndex < right.Length && right[rightIndex] == '0') rightIndex++;
            var leftNumberStart = leftIndex;
            var rightNumberStart = rightIndex;
            while (leftIndex < left.Length && char.IsDigit(left[leftIndex])) leftIndex++;
            while (rightIndex < right.Length && char.IsDigit(right[rightIndex])) rightIndex++;

            var leftNumberLength = leftIndex - leftNumberStart;
            var rightNumberLength = rightIndex - rightNumberStart;
            if (leftNumberLength != rightNumberLength) return leftNumberLength.CompareTo(rightNumberLength);
            for (var offset = 0; offset < leftNumberLength; offset++)
            {
              var digitComparison = left[leftNumberStart + offset].CompareTo(right[rightNumberStart + offset]);
              if (digitComparison != 0) return digitComparison;
            }

            var leftRunLength = leftIndex - leftRunStart;
            var rightRunLength = rightIndex - rightRunStart;
            if (leftRunLength != rightRunLength) return leftRunLength.CompareTo(rightRunLength);
            continue;
          }

          var characterComparison = char.ToUpperInvariant(left[leftIndex]).CompareTo(char.ToUpperInvariant(right[rightIndex]));
          if (characterComparison != 0) return characterComparison;
          leftIndex++;
          rightIndex++;
        }

        var lengthComparison = left.Length.CompareTo(right.Length);
        return lengthComparison != 0 ? lengthComparison : StringComparer.Ordinal.Compare(left, right);
      }
    }
  }
}
