// VideoModel.cs
using System;
using System.IO;
using Microsoft.UI.Xaml.Media.Imaging;

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

    public long FileSize { get; set; }
    public DateTime? CreationDate { get; set; }

    public Microsoft.UI.Xaml.Media.ImageSource? GetThumbnailPath(string videoFilePath)
    {
      if (string.IsNullOrEmpty(videoFilePath))
        return null;

      // 获取不带扩展名的文件名
      var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(videoFilePath);
      // 构造缩略图路径（视频名-poster.jpg）
      var thumbnailPath = Path.Combine(
          Path.GetDirectoryName(videoFilePath),
          fileNameWithoutExtension + "-poster.jpg");

      // 检查缩略图文件是否存在
      if (File.Exists(thumbnailPath))
      {
        return new BitmapImage(new Uri(thumbnailPath));
      }

      return null; // 或返回默认图片
    }

    public string FormatDuration(TimeSpan duration)
    {
      // 只显示到秒，去掉小数部分
      return $"{duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
    }
    public string GetFavoriteIcon(bool isFavorite)
    {
      return isFavorite ? "❤" : "♡";
    }

    public Microsoft.UI.Xaml.Media.SolidColorBrush GetFavoriteColor(bool isFavorite)
    {
      return isFavorite ?
          new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red) :
          new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public string GetWatchLaterIcon(bool isWatchLater)
    {
      return isWatchLater ? "⏱" : "⏳";
    }

    public Microsoft.UI.Xaml.Media.SolidColorBrush GetWatchLaterColor(bool isWatchLater)
    {
      return isWatchLater ?
          new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange) :
          new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
  }
}