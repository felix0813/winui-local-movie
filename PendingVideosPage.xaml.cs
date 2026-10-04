using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace winui_local_movie
{
  public sealed partial class PendingVideosPage : Page
  {
    private readonly DatabaseService _databaseService;

    public PendingVideosPage()
    {
      InitializeComponent();
      _databaseService = ((App)Application.Current).DatabaseService;
      Loaded += PendingVideosPage_Loaded;
    }

    private async void PendingVideosPage_Loaded(object sender, RoutedEventArgs e)
    {
      await LoadVideosAsync();
    }

    private async Task LoadVideosAsync()
    {
      var videos = await _databaseService.GetUnwatchedVideosAsync();
      VideosGridView.ItemsSource = videos;
      DescriptionText.Text = videos.Count == 0 ? "没有待观看视频" : $"共有 {videos.Count} 个尚未观看的视频";
    }

    private async void VideosGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
      if (e.ClickedItem is not VideoModel video) return;
      try
      {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(video.FilePath);
        if (!await Windows.System.Launcher.LaunchFileAsync(file)) return;
        await _databaseService.RecordVideoPlayedAsync(video.Id, DateTime.Now);
        await LoadVideosAsync();
      }
      catch (Exception ex)
      {
        var dialog = new ContentDialog
        {
          Title = "无法打开视频",
          Content = ex.Message,
          CloseButtonText = "确定",
          XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
      }
    }
  }
}
