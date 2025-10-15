// VideoModel.cs
using System;

namespace winui_local_movie
{
    public class VideoModel
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string FilePath { get; set; }
        public string ThumbnailPath { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime DateAdded { get; set; }
        public bool IsFavorite { get; set; }
        public bool IsWatchLater { get; set; }
    }
}