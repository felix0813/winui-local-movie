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
            // 直接显示"首页"内容（使用 HomePage 控件）
            LoadHomePage();
        }

        private void LoadHomePage()
        {
            // 使用 HomePage 用户控件替代手动创建的 UI 元素
            ContentFrame.Content = new HomePage();
        }

        private void GoHome_Click(object sender, RoutedEventArgs e)
        {
            // 返回首页，使用 HomePage 控件
            LoadHomePage();
        }

        private void ShowLater_Click(object sender, RoutedEventArgs e)
{
    // 暂时用消息提示代替页面
    var dlg = new ContentDialog
    {
        Title = "稍后播放",
        Content = "功能开发中...",
        CloseButtonText = "确定",
        XamlRoot = ContentFrame.XamlRoot  // 添加这一行
    };
    dlg.ShowAsync();
}

private void ShowLiked_Click(object sender, RoutedEventArgs e)
{
    // 暂时用消息提示代替页面
    var dlg = new ContentDialog
    {
        Title = "喜欢",
        Content = "功能开发中...",
        CloseButtonText = "确定",
        XamlRoot = ContentFrame.XamlRoot  // 添加这一行
    };
    dlg.ShowAsync();
}
    }
}