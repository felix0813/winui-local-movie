using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace winui_local_movie
{
    public sealed partial class ToolboxPage : Page
    {
        public ToolboxPage()
        {
            this.InitializeComponent();
        }

        private void MergeVideos_Click(object sender, RoutedEventArgs e)
        {
            // 打开合并视频对话框
            var dialog = new MergeVideosDialog();
            dialog.XamlRoot = this.XamlRoot;
            _ = dialog.ShowAsync();
        }
    }
}