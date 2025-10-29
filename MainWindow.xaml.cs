// MainWindow.xaml.cs
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace winui_local_movie
{
  public sealed partial class MainWindow : Window
  {

    public MainWindow()
    {
      this.InitializeComponent();
        }
    private void ContentFrame_Loaded(object sender, RoutedEventArgs e)
    {
      ContentFrame.Navigate(typeof(AllVideosPage));
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
      ContentFrame.Navigate(typeof(SettingsPage));
    }
    private void ShowAllVideos_Click(object sender, RoutedEventArgs e)
    {
      ContentFrame.Navigate(typeof(AllVideosPage));
    }

  }
}