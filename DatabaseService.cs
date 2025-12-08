// DatabaseService.cs
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;

namespace winui_local_movie
{
  public class DatabaseService
  {
    private readonly string _connectionString;

    public DatabaseService()
    {
      var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
      var dbPath = Path.Combine(localFolder, "videos.db");
      // 确保目录存在
      Directory.CreateDirectory(Path.GetDirectoryName(dbPath));

      _connectionString = $"Data Source={dbPath}";
      InitializeDatabase();
    }

    private void InitializeDatabase()
    {
      using var connection = new SqliteConnection(_connectionString);
      connection.Open();

      var command = connection.CreateCommand();
      command.CommandText = @"
    CREATE TABLE IF NOT EXISTS Videos (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Title TEXT NOT NULL,
        FilePath TEXT NOT NULL UNIQUE,
        ThumbnailPath TEXT,
        Duration TEXT,
        DateAdded TEXT NOT NULL,
        IsFavorite INTEGER DEFAULT 0,
        IsWatchLater INTEGER DEFAULT 0,
        FileSize INTEGER DEFAULT 0,
    CreationDate TEXT
    )";
      command.ExecuteNonQuery();
      connection.Close();
    }

    // 在 DatabaseService.cs 中更新 AddVideoAsync 方法
    public async Task AddVideoAsync(VideoModel video)
    {
      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = @"
        INSERT INTO Videos 
        (Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater, FileSize, CreationDate)
        SELECT @Title, @FilePath, @ThumbnailPath, @Duration, @DateAdded, @IsFavorite, @IsWatchLater, @FileSize, @CreationDate
        WHERE NOT EXISTS (
           SELECT 1 FROM Videos WHERE FilePath = @FilePath
        )";

      command.Parameters.AddWithValue("@FileSize", video.FileSize);
      command.Parameters.AddWithValue("@Title", video.Title);
      command.Parameters.AddWithValue("@FilePath", video.FilePath);
      command.Parameters.AddWithValue("@ThumbnailPath", video.ThumbnailPath ?? "");
      command.Parameters.AddWithValue("@Duration", video.Duration.ToString());
      command.Parameters.AddWithValue("@DateAdded", video.DateAdded.ToString("o"));
      command.Parameters.AddWithValue("@IsFavorite", video.IsFavorite ? 1 : 0);
      command.Parameters.AddWithValue("@IsWatchLater", video.IsWatchLater ? 1 : 0);
      command.Parameters.AddWithValue("@CreationDate", (object)video.CreationDate?.ToString("o") ?? DBNull.Value);

      await command.ExecuteNonQueryAsync();
    }

    public async Task<List<VideoModel>> GetFeaturedVideosAsync(int count = 6)
    {
      var videos = new List<VideoModel>();

      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = @"
                SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater, FileSize, CreationDate
                FROM Videos 
                ORDER BY DateAdded DESC 
                LIMIT @Count";
      command.Parameters.AddWithValue("@Count", count);

      using var reader = await command.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        videos.Add(new VideoModel
        {
          Id = reader.GetInt32("Id"),
          Title = reader.GetString("Title"),
          FilePath = reader.GetString("FilePath"),
          ThumbnailPath = reader.IsDBNull("ThumbnailPath") ? null : reader.GetString("ThumbnailPath"),
          Duration = TimeSpan.Parse(reader.GetString("Duration")),
          DateAdded = DateTime.Parse(reader.GetString("DateAdded")),
          IsFavorite = reader.GetInt32("IsFavorite") == 1,
          IsWatchLater = reader.GetInt32("IsWatchLater") == 1,
          FileSize = reader.IsDBNull("FileSize") ? 0 : reader.GetInt64("FileSize"),
          CreationDate = reader.IsDBNull("CreationDate") ? null : DateTime.Parse(reader.GetString("CreationDate")),

        });
      }

      return videos;
    }

    public async Task<List<VideoModel>> GetWatchLaterVideosAsync()
    {
      var videos = new List<VideoModel>();

      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = @"
                SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater, FileSize, CreationDate
                FROM Videos 
                WHERE IsWatchLater = 1
                ORDER BY DateAdded DESC";

      using var reader = await command.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        videos.Add(new VideoModel
        {
          Id = reader.GetInt32("Id"),
          Title = reader.GetString("Title"),
          FilePath = reader.GetString("FilePath"),
          ThumbnailPath = reader.IsDBNull("ThumbnailPath") ? null : reader.GetString("ThumbnailPath"),
          Duration = TimeSpan.Parse(reader.GetString("Duration")),
          DateAdded = DateTime.Parse(reader.GetString("DateAdded")),
          IsFavorite = reader.GetInt32("IsFavorite") == 1,
          IsWatchLater = reader.GetInt32("IsWatchLater") == 1,
          FileSize = reader.IsDBNull("FileSize") ? 0 : reader.GetInt64("FileSize"),
          CreationDate = reader.IsDBNull("CreationDate") ? null : DateTime.Parse(reader.GetString("CreationDate")),

        });
      }

      return videos;
    }

    public async Task<List<VideoModel>> GetFavoriteVideosAsync()
    {
      var videos = new List<VideoModel>();

      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = @"
                SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater, FileSize, CreationDate
                FROM Videos 
                WHERE IsFavorite = 1
                ORDER BY DateAdded DESC";

      using var reader = await command.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        videos.Add(new VideoModel
        {
          Id = reader.GetInt32("Id"),
          Title = reader.GetString("Title"),
          FilePath = reader.GetString("FilePath"),
          ThumbnailPath = reader.IsDBNull("ThumbnailPath") ? null : reader.GetString("ThumbnailPath"),
          Duration = TimeSpan.Parse(reader.GetString("Duration")),
          DateAdded = DateTime.Parse(reader.GetString("DateAdded")),
          IsFavorite = reader.GetInt32("IsFavorite") == 1,
          IsWatchLater = reader.GetInt32("IsWatchLater") == 1,
          FileSize = reader.IsDBNull("FileSize") ? 0 : reader.GetInt64("FileSize"),
          CreationDate = reader.IsDBNull("CreationDate") ? null : DateTime.Parse(reader.GetString("CreationDate")),

        });
      }

      return videos;
    }
    // DatabaseService.cs

    public async Task<List<VideoModel>> GetFavoriteVideosSortedAsync(string sortProperty, bool ascending, int offset, int limit)
    {
      var videos = new List<VideoModel>();
      var order = ascending ? "ASC" : "DESC";
      var limitClause = "LIMIT @Limit OFFSET @Offset";

      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = $@"
        SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater, FileSize, CreationDate
        FROM Videos 
        WHERE IsFavorite = 1
        ORDER BY {sortProperty} {order}, DateAdded DESC 
        {limitClause}";

      command.Parameters.AddWithValue("@Limit", limit);
      command.Parameters.AddWithValue("@Offset", offset);

      using var reader = await command.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        videos.Add(new VideoModel
        {
          Id = reader.GetInt32("Id"),
          Title = reader.GetString("Title"),
          FilePath = reader.GetString("FilePath"),
          ThumbnailPath = reader.IsDBNull("ThumbnailPath") ? null : reader.GetString("ThumbnailPath"),
          Duration = TimeSpan.Parse(reader.GetString("Duration")),
          DateAdded = DateTime.Parse(reader.GetString("DateAdded")),
          IsFavorite = reader.GetInt32("IsFavorite") == 1,
          IsWatchLater = reader.GetInt32("IsWatchLater") == 1,
          FileSize = reader.IsDBNull("FileSize") ? 0 : reader.GetInt64("FileSize"),
          CreationDate = reader.IsDBNull("CreationDate") ? null : DateTime.Parse(reader.GetString("CreationDate")),
        });
      }

      return videos;
    }

    public async Task<List<VideoModel>> GetWatchLaterVideosSortedAsync(string sortProperty, bool ascending, int offset, int limit)
    {
      var videos = new List<VideoModel>();
      var order = ascending ? "ASC" : "DESC";
      var limitClause = "LIMIT @Limit OFFSET @Offset";

      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = $@"
        SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater, FileSize, CreationDate
        FROM Videos 
        WHERE IsWatchLater = 1
        ORDER BY {sortProperty} {order}, DateAdded DESC 
        {limitClause}";

      command.Parameters.AddWithValue("@Limit", limit);
      command.Parameters.AddWithValue("@Offset", offset);

      using var reader = await command.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        videos.Add(new VideoModel
        {
          Id = reader.GetInt32("Id"),
          Title = reader.GetString("Title"),
          FilePath = reader.GetString("FilePath"),
          ThumbnailPath = reader.IsDBNull("ThumbnailPath") ? null : reader.GetString("ThumbnailPath"),
          Duration = TimeSpan.Parse(reader.GetString("Duration")),
          DateAdded = DateTime.Parse(reader.GetString("DateAdded")),
          IsFavorite = reader.GetInt32("IsFavorite") == 1,
          IsWatchLater = reader.GetInt32("IsWatchLater") == 1,
          FileSize = reader.IsDBNull("FileSize") ? 0 : reader.GetInt64("FileSize"),
          CreationDate = reader.IsDBNull("CreationDate") ? null : DateTime.Parse(reader.GetString("CreationDate")),
        });
      }

      return videos;
    }
    public async Task<int> GetTotalVideosCountAsync()
    {
      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = "SELECT COUNT(*) FROM Videos";

      var result = await command.ExecuteScalarAsync();
      return Convert.ToInt32(result);
    }

    public async Task<List<VideoModel>> GetVideosAsync(int offset, int limit)
    {
      var videos = new List<VideoModel>();

      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = @"
        SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater, FileSize, CreationDate
        FROM Videos 
        ORDER BY DateAdded DESC 
        LIMIT @Limit OFFSET @Offset";
      command.Parameters.AddWithValue("@Limit", limit);
      command.Parameters.AddWithValue("@Offset", offset);

      using var reader = await command.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        videos.Add(new VideoModel
        {
          Id = reader.GetInt32("Id"),
          Title = reader.GetString("Title"),
          FilePath = reader.GetString("FilePath"),
          ThumbnailPath = reader.IsDBNull("ThumbnailPath") ? null : reader.GetString("ThumbnailPath"),
          Duration = TimeSpan.Parse(reader.GetString("Duration")),
          DateAdded = DateTime.Parse(reader.GetString("DateAdded")),
          IsFavorite = reader.GetInt32("IsFavorite") == 1,
          IsWatchLater = reader.GetInt32("IsWatchLater") == 1,
          FileSize = reader.IsDBNull("FileSize") ? 0 : reader.GetInt64("FileSize"),
          CreationDate = reader.IsDBNull("CreationDate") ? null : DateTime.Parse(reader.GetString("CreationDate")),

        });
      }

      return videos;
    }
    public async Task UpdateVideoFavoriteStatusAsync(int videoId, bool isFavorite)
    {
      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = "UPDATE Videos SET IsFavorite = @IsFavorite WHERE Id = @Id";
      command.Parameters.AddWithValue("@IsFavorite", isFavorite ? 1 : 0);
      command.Parameters.AddWithValue("@Id", videoId);

      await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateVideoWatchLaterStatusAsync(int videoId, bool isWatchLater)
    {
      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = "UPDATE Videos SET IsWatchLater = @IsWatchLater WHERE Id = @Id";
      command.Parameters.AddWithValue("@IsWatchLater", isWatchLater ? 1 : 0);
      command.Parameters.AddWithValue("@Id", videoId);

      await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteVideoAsync(int videoId)
    {
      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = "DELETE FROM Videos WHERE Id = @Id";
      command.Parameters.AddWithValue("@Id", videoId);

      await command.ExecuteNonQueryAsync();
    }
    public async Task RefreshVideoMetadataAsync(string filePath, Func<string, Task<TimeSpan>>? getDurationAsync = null)
    {
      // 获取文件大小（MB）
      double fileSizeMB = 0;
      if (File.Exists(filePath))
      {
        var fileInfo = new FileInfo(filePath);
        fileSizeMB = Math.Round(fileInfo.Length / 1024.0 / 1024.0, 2);
      }

      // 获取时长（可选，需UI层传入委托）
      TimeSpan duration = TimeSpan.Zero;
      if (getDurationAsync != null)
      {
        duration = await getDurationAsync(filePath);
      }

      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      if (getDurationAsync != null)
      {
        command.CommandText = @"
            UPDATE Videos 
            SET FileSize = @FileSize, Duration = @Duration
            WHERE FilePath = @FilePath";
        command.Parameters.AddWithValue("@Duration", duration.ToString());
      }
      else
      {
        command.CommandText = @"
            UPDATE Videos 
            SET FileSize = @FileSize
            WHERE FilePath = @FilePath";
      }
      command.Parameters.AddWithValue("@FileSize", fileSizeMB);
      command.Parameters.AddWithValue("@FilePath", filePath);

      await command.ExecuteNonQueryAsync();
    }
    public async Task<List<VideoModel>> GetVideosSortedByFileSizeAsync(bool ascending = true, int offset = 0, int limit = 0)
    {
      var videos = new List<VideoModel>();
      var order = ascending ? "ASC" : "DESC";
      var limitClause = limit > 0 ? "LIMIT @Limit OFFSET @Offset" : "";

      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = $@"
        SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater, FileSize, CreationDate
        FROM Videos 
        ORDER BY FileSize {order}, DateAdded DESC 
        {limitClause}";

      if (limit > 0)
      {
        command.Parameters.AddWithValue("@Limit", limit);
        command.Parameters.AddWithValue("@Offset", offset);
      }

      using var reader = await command.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        videos.Add(new VideoModel
        {
          Id = reader.GetInt32("Id"),
          Title = reader.GetString("Title"),
          FilePath = reader.GetString("FilePath"),
          ThumbnailPath = reader.IsDBNull("ThumbnailPath") ? null : reader.GetString("ThumbnailPath"),
          Duration = TimeSpan.Parse(reader.GetString("Duration")),
          DateAdded = DateTime.Parse(reader.GetString("DateAdded")),
          IsFavorite = reader.GetInt32("IsFavorite") == 1,
          IsWatchLater = reader.GetInt32("IsWatchLater") == 1,
          FileSize = reader.IsDBNull("FileSize") ? 0 : reader.GetInt64("FileSize"),
          CreationDate = reader.IsDBNull("CreationDate") ? null : DateTime.Parse(reader.GetString("CreationDate")),
        });
      }

      return videos;
    }

    public async Task<List<VideoModel>> GetVideosSortedByDateAddedAsync(bool ascending = true, int offset = 0, int limit = 0)
    {
      var videos = new List<VideoModel>();
      var order = ascending ? "ASC" : "DESC";
      var limitClause = limit > 0 ? "LIMIT @Limit OFFSET @Offset" : "";

      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = $@"
        SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater, FileSize, CreationDate
        FROM Videos 
        ORDER BY DateAdded {order} 
        {limitClause}";

      if (limit > 0)
      {
        command.Parameters.AddWithValue("@Limit", limit);
        command.Parameters.AddWithValue("@Offset", offset);
      }

      using var reader = await command.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        videos.Add(new VideoModel
        {
          Id = reader.GetInt32("Id"),
          Title = reader.GetString("Title"),
          FilePath = reader.GetString("FilePath"),
          ThumbnailPath = reader.IsDBNull("ThumbnailPath") ? null : reader.GetString("ThumbnailPath"),
          Duration = TimeSpan.Parse(reader.GetString("Duration")),
          DateAdded = DateTime.Parse(reader.GetString("DateAdded")),
          IsFavorite = reader.GetInt32("IsFavorite") == 1,
          IsWatchLater = reader.GetInt32("IsWatchLater") == 1,
          FileSize = reader.IsDBNull("FileSize") ? 0 : reader.GetInt64("FileSize"),
          CreationDate = reader.IsDBNull("CreationDate") ? null : DateTime.Parse(reader.GetString("CreationDate")),
        });
      }

      return videos;
    }

    public async Task<List<VideoModel>> GetVideosSortedByCreationDateAsync(bool ascending = true, int offset = 0, int limit = 0)
    {
      var videos = new List<VideoModel>();
      var order = ascending ? "ASC" : "DESC";
      var limitClause = limit > 0 ? "LIMIT @Limit OFFSET @Offset" : "";

      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = $@"
        SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater, FileSize, CreationDate
        FROM Videos 
        ORDER BY CreationDate {order}, DateAdded DESC 
        {limitClause}";

      if (limit > 0)
      {
        command.Parameters.AddWithValue("@Limit", limit);
        command.Parameters.AddWithValue("@Offset", offset);
      }

      using var reader = await command.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        videos.Add(new VideoModel
        {
          Id = reader.GetInt32("Id"),
          Title = reader.GetString("Title"),
          FilePath = reader.GetString("FilePath"),
          ThumbnailPath = reader.IsDBNull("ThumbnailPath") ? null : reader.GetString("ThumbnailPath"),
          Duration = TimeSpan.Parse(reader.GetString("Duration")),
          DateAdded = DateTime.Parse(reader.GetString("DateAdded")),
          IsFavorite = reader.GetInt32("IsFavorite") == 1,
          IsWatchLater = reader.GetInt32("IsWatchLater") == 1,
          FileSize = reader.IsDBNull("FileSize") ? 0 : reader.GetInt64("FileSize"),
          CreationDate = reader.IsDBNull("CreationDate") ? null : DateTime.Parse(reader.GetString("CreationDate")),
        });
      }

      return videos;
    }

    public async Task<List<VideoModel>> GetVideosSortedByDurationAsync(bool ascending = true, int offset = 0, int limit = 0)
    {
      var videos = new List<VideoModel>();
      var order = ascending ? "ASC" : "DESC";
      var limitClause = limit > 0 ? "LIMIT @Limit OFFSET @Offset" : "";

      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = $@"
        SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater, FileSize, CreationDate
        FROM Videos 
        ORDER BY Duration {order}, DateAdded DESC 
        {limitClause}";

      if (limit > 0)
      {
        command.Parameters.AddWithValue("@Limit", limit);
        command.Parameters.AddWithValue("@Offset", offset);
      }

      using var reader = await command.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        videos.Add(new VideoModel
        {
          Id = reader.GetInt32("Id"),
          Title = reader.GetString("Title"),
          FilePath = reader.GetString("FilePath"),
          ThumbnailPath = reader.IsDBNull("ThumbnailPath") ? null : reader.GetString("ThumbnailPath"),
          Duration = TimeSpan.Parse(reader.GetString("Duration")),
          DateAdded = DateTime.Parse(reader.GetString("DateAdded")),
          IsFavorite = reader.GetInt32("IsFavorite") == 1,
          IsWatchLater = reader.GetInt32("IsWatchLater") == 1,
          FileSize = reader.IsDBNull("FileSize") ? 0 : reader.GetInt64("FileSize"),
          CreationDate = reader.IsDBNull("CreationDate") ? null : DateTime.Parse(reader.GetString("CreationDate")),
        });
      }

      return videos;
    }
  }
}