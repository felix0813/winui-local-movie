// HomePage.xaml.cs
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace winui_local_movie
{
    public sealed partial class HomePage : UserControl
    {
        private readonly DatabaseService _databaseService;
        public ObservableCollection<VideoModel> FeaturedVideos { get; } = new ObservableCollection<VideoModel>();
        
        public HomePage()
        {
            this.InitializeComponent();
            _databaseService = new DatabaseService();
            FeaturedVideosRepeater.ItemsSource = FeaturedVideos;
            LoadFeaturedVideosAsync();
        }
        
        private async Task LoadFeaturedVideosAsync()
        {
            var videos = await _databaseService.GetFeaturedVideosAsync();
            FeaturedVideos.Clear();
            foreach (var video in videos)
            {
                FeaturedVideos.Add(video);
            }
        }
    }
}