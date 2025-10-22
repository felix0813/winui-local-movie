// MainWindow.xaml.cs
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace winui_local_movie
{
  public sealed partial class MainWindow : Window
  {
    private readonly DatabaseService _databaseService;

    public MainWindow()
    {
      this.InitializeComponent();
      _databaseService = new DatabaseService(); // 初始化数据库服务
      LoadHomePage();
    }

    private void LoadHomePage()
    {
      ContentFrame.Content = new HomePage();
    }

    private void GoHome_Click(object sender, RoutedEventArgs e)
    {
      LoadHomePage();
    }

    private void ShowLater_Click(object sender, RoutedEventArgs e)
    {
      var dlg = new ContentDialog
      {
        Title = "稍后播放",
        Content = "功能开发中...",
        CloseButtonText = "确定",
        XamlRoot = ContentFrame.XamlRoot
      };
      dlg.ShowAsync();
    }

    private void ShowLiked_Click(object sender, RoutedEventArgs e)
    {
      var dlg = new ContentDialog
      {
        Title = "喜欢",
        Content = "功能开发中...",
        CloseButtonText = "确定",
        XamlRoot = ContentFrame.XamlRoot
      };
      dlg.ShowAsync();
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
      ContentFrame.Content = new SettingsPage();
    }
    private void ShowAllVideos_Click(object sender, RoutedEventArgs e)
    {
      ContentFrame.Navigate(typeof(AllVideosPage));
    }

  }
}