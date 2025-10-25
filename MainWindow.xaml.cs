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