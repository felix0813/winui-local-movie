using System;
using System.IO;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace winui_local_movie
{
  public class GalleryAlbum
  {
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string? CoverPath { get; set; }
    public DateTime DateAdded { get; set; }
    public bool IsFavorite { get; set; }
    public DateTime? LastViewedAt { get; set; }
    public int LastViewedIndex { get; set; }
    public int ImageCount { get; set; }
    public ImageSource? GetCover() => string.IsNullOrWhiteSpace(CoverPath) ? null : new BitmapImage(new Uri(CoverPath));
    public string ProgressText() => ImageCount == 0 ? "没有图片" : LastViewedAt is null ? $"{ImageCount} 张 · 未开始" : $"{ImageCount} 张 · 读到第 {Math.Min(LastViewedIndex + 1, ImageCount)} 张";
  }

  public class GalleryImage
  {
    public int Id { get; set; }
    public int AlbumId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public ImageSource? GetSource() => new BitmapImage(new Uri(FilePath));
    public string GetFileName() => Path.GetFileName(FilePath);
  }
}
