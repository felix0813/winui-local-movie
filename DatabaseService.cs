// DatabaseService.cs
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
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

      // 迁移：为现有数据库添加 LastWatched 列
      var checkCmd = connection.CreateCommand();
      checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Videos') WHERE name='LastWatched'";
      var columnExists = Convert.ToInt32(checkCmd.ExecuteScalar()) > 0;
      if (!columnExists)
      {
        var alterCmd = connection.CreateCommand();
        alterCmd.CommandText = "ALTER TABLE Videos ADD COLUMN LastWatched TEXT";
        try { alterCmd.ExecuteNonQuery(); } catch { }
      }

      var playCountColumnCommand = connection.CreateCommand();
      playCountColumnCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Videos') WHERE name='PlayCount'";
      var playCountColumnExists = Convert.ToInt32(playCountColumnCommand.ExecuteScalar()) > 0;
      if (!playCountColumnExists)
      {
        var alterPlayCountCommand = connection.CreateCommand();
        alterPlayCountCommand.CommandText = "ALTER TABLE Videos ADD COLUMN PlayCount INTEGER NOT NULL DEFAULT 0";
        try { alterPlayCountCommand.ExecuteNonQuery(); } catch { }
      }

      var tagTablesCommand = connection.CreateCommand();
      tagTablesCommand.CommandText = @"
        CREATE TABLE IF NOT EXISTS Tags (
          Id INTEGER PRIMARY KEY AUTOINCREMENT,
          Name TEXT NOT NULL COLLATE NOCASE UNIQUE
        );
        CREATE TABLE IF NOT EXISTS VideoTags (
          VideoId INTEGER NOT NULL,
          TagId INTEGER NOT NULL,
          PRIMARY KEY (VideoId, TagId),
          FOREIGN KEY (VideoId) REFERENCES Videos(Id) ON DELETE CASCADE,
          FOREIGN KEY (TagId) REFERENCES Tags(Id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_VideoTags_TagId ON VideoTags(TagId);";
      tagTablesCommand.ExecuteNonQuery();

      var galleryTablesCommand = connection.CreateCommand();
      galleryTablesCommand.CommandText = @"
        CREATE TABLE IF NOT EXISTS GalleryAlbums (
          Id INTEGER PRIMARY KEY AUTOINCREMENT,
          Title TEXT NOT NULL,
          FolderPath TEXT NOT NULL UNIQUE,
          CoverPath TEXT,
          DateAdded TEXT NOT NULL,
          IsFavorite INTEGER NOT NULL DEFAULT 0,
          LastViewedAt TEXT,
          LastViewedIndex INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE IF NOT EXISTS GalleryImages (
          Id INTEGER PRIMARY KEY AUTOINCREMENT,
          AlbumId INTEGER NOT NULL,
          FilePath TEXT NOT NULL,
          SortOrder INTEGER NOT NULL,
          UNIQUE (AlbumId, FilePath),
          FOREIGN KEY (AlbumId) REFERENCES GalleryAlbums(Id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_GalleryImages_AlbumId_SortOrder ON GalleryImages(AlbumId, SortOrder);";
      galleryTablesCommand.ExecuteNonQuery();

      connection.Close();
    }

    /// <summary>
    /// 安全地从 SQLite reader 中读取可空 DateTime 列。
    /// 使用静态缓存避免每行都尝试 GetOrdinal + 异常捕获，只在首次访问时检测列是否存在。
    /// </summary>
    private static readonly Dictionary<(object, string), bool> _columnExistsCache = new();

    private static DateTime? TryGetDateTime(Microsoft.Data.Sqlite.SqliteDataReader reader, string column)
    {
      var key = ((object)reader, column);
      if (!_columnExistsCache.TryGetValue(key, out bool exists))
      {
        try
        {
          reader.GetOrdinal(column);
          exists = true;
        }
        catch
        {
          exists = false;
        }
        _columnExistsCache[key] = exists;
      }
      if (!exists) return null;

      int ordinal = reader.GetOrdinal(column);
      return reader.IsDBNull(ordinal) ? null : DateTime.Parse(reader.GetString(ordinal));
    }

    public async Task<List<string>> GetVideoTagsAsync(int videoId)
    {
      var tags = new List<string>();
      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();
      var command = connection.CreateCommand();
      command.CommandText = @"
        SELECT Tags.Name FROM Tags
        INNER JOIN VideoTags ON VideoTags.TagId = Tags.Id
        WHERE VideoTags.VideoId = @VideoId
        ORDER BY Tags.Name COLLATE NOCASE";
      command.Parameters.AddWithValue("@VideoId", videoId);
      using var reader = await command.ExecuteReaderAsync();
      while (await reader.ReadAsync()) tags.Add(reader.GetString(0));
      return tags;
    }

    public async Task<int> UpsertGalleryAlbumAsync(string folderPath, string title, IReadOnlyList<string> imagePaths)
    {
      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();
      using var transaction = connection.BeginTransaction();
      var coverPath = imagePaths.FirstOrDefault() ?? string.Empty;
      using var albumCommand = connection.CreateCommand();
      albumCommand.Transaction = transaction;
      albumCommand.CommandText = @"
        INSERT INTO GalleryAlbums (Title, FolderPath, CoverPath, DateAdded)
        VALUES (@Title, @FolderPath, @CoverPath, @DateAdded)
        ON CONFLICT(FolderPath) DO UPDATE SET Title = excluded.Title, CoverPath = excluded.CoverPath
        RETURNING Id";
      albumCommand.Parameters.AddWithValue("@Title", title);
      albumCommand.Parameters.AddWithValue("@FolderPath", folderPath);
      albumCommand.Parameters.AddWithValue("@CoverPath", coverPath);
      albumCommand.Parameters.AddWithValue("@DateAdded", DateTime.Now.ToString("o"));
      var albumId = Convert.ToInt32(await albumCommand.ExecuteScalarAsync());

      using var deleteImages = connection.CreateCommand();
      deleteImages.Transaction = transaction;
      deleteImages.CommandText = "DELETE FROM GalleryImages WHERE AlbumId = @AlbumId";
      deleteImages.Parameters.AddWithValue("@AlbumId", albumId);
      await deleteImages.ExecuteNonQueryAsync();

      // 为整个批量导入复用同一个原生 prepared statement，避免每张图片都残留一个
      // 未释放的 sqlite3_stmt，图片较多时可能耗尽或破坏原生 SQLite 状态。
      using var imageCommand = connection.CreateCommand();
      imageCommand.Transaction = transaction;
      imageCommand.CommandText = "INSERT INTO GalleryImages (AlbumId, FilePath, SortOrder) VALUES (@AlbumId, @FilePath, @SortOrder)";
      var imageAlbumIdParameter = imageCommand.Parameters.Add("@AlbumId", SqliteType.Integer);
      var imageFilePathParameter = imageCommand.Parameters.Add("@FilePath", SqliteType.Text);
      var imageSortOrderParameter = imageCommand.Parameters.Add("@SortOrder", SqliteType.Integer);
      imageAlbumIdParameter.Value = albumId;
      imageCommand.Prepare();

      for (var index = 0; index < imagePaths.Count; index++)
      {
        imageFilePathParameter.Value = imagePaths[index];
        imageSortOrderParameter.Value = index;
        await imageCommand.ExecuteNonQueryAsync();
      }
      await transaction.CommitAsync();
      return albumId;
    }

    public async Task<List<GalleryAlbum>> GetGalleryAlbumsAsync()
    {
      var albums = new List<GalleryAlbum>();
      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();
      var command = connection.CreateCommand();
      command.CommandText = @"
        SELECT a.Id, a.Title, a.FolderPath, a.CoverPath, a.DateAdded, a.IsFavorite, a.LastViewedAt, a.LastViewedIndex,
               COUNT(i.Id) AS ImageCount
        FROM GalleryAlbums a LEFT JOIN GalleryImages i ON i.AlbumId = a.Id
        GROUP BY a.Id ORDER BY a.IsFavorite DESC, a.DateAdded DESC";
      using var reader = await command.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        albums.Add(new GalleryAlbum
        {
          Id = reader.GetInt32("Id"), Title = reader.GetString("Title"), FolderPath = reader.GetString("FolderPath"),
          CoverPath = reader.IsDBNull("CoverPath") ? null : reader.GetString("CoverPath"),
          DateAdded = DateTime.Parse(reader.GetString("DateAdded")), IsFavorite = reader.GetInt32("IsFavorite") == 1,
          LastViewedAt = TryGetDateTime(reader, "LastViewedAt"), LastViewedIndex = reader.GetInt32("LastViewedIndex"),
          ImageCount = reader.GetInt32("ImageCount")
        });
      }
      return albums;
    }

    public async Task<List<GalleryImage>> GetGalleryImagesAsync(int albumId)
    {
      var images = new List<GalleryImage>();
      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();
      var command = connection.CreateCommand();
      command.CommandText = "SELECT Id, AlbumId, FilePath, SortOrder FROM GalleryImages WHERE AlbumId = @AlbumId ORDER BY SortOrder";
      command.Parameters.AddWithValue("@AlbumId", albumId);
      using var reader = await command.ExecuteReaderAsync();
      while (await reader.ReadAsync()) images.Add(new GalleryImage { Id = reader.GetInt32("Id"), AlbumId = reader.GetInt32("AlbumId"), FilePath = reader.GetString("FilePath"), SortOrder = reader.GetInt32("SortOrder") });
      return images;
    }

    public async Task UpdateGalleryProgressAsync(int albumId, int imageIndex)
    {
      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();
      var command = connection.CreateCommand();
      command.CommandText = "UPDATE GalleryAlbums SET LastViewedAt = @LastViewedAt, LastViewedIndex = @LastViewedIndex WHERE Id = @Id";
      command.Parameters.AddWithValue("@LastViewedAt", DateTime.Now.ToString("o")); command.Parameters.AddWithValue("@LastViewedIndex", imageIndex); command.Parameters.AddWithValue("@Id", albumId);
      await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateGalleryFavoriteAsync(int albumId, bool isFavorite)
    {
      using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync();
      var command = connection.CreateCommand(); command.CommandText = "UPDATE GalleryAlbums SET IsFavorite = @IsFavorite WHERE Id = @Id";
      command.Parameters.AddWithValue("@IsFavorite", isFavorite ? 1 : 0); command.Parameters.AddWithValue("@Id", albumId); await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteGalleryAlbumAsync(int albumId)
    {
      using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync();
      using var transaction = connection.BeginTransaction();
      var deleteImages = connection.CreateCommand(); deleteImages.Transaction = transaction; deleteImages.CommandText = "DELETE FROM GalleryImages WHERE AlbumId = @Id"; deleteImages.Parameters.AddWithValue("@Id", albumId); await deleteImages.ExecuteNonQueryAsync();
      var deleteAlbum = connection.CreateCommand(); deleteAlbum.Transaction = transaction; deleteAlbum.CommandText = "DELETE FROM GalleryAlbums WHERE Id = @Id"; deleteAlbum.Parameters.AddWithValue("@Id", albumId); await deleteAlbum.ExecuteNonQueryAsync();
      transaction.Commit();
    }

    public async Task<int> GetVideoPlayCountAsync(int videoId)
    {
      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();
      var command = connection.CreateCommand();
      command.CommandText = "SELECT PlayCount FROM Videos WHERE Id = @VideoId";
      command.Parameters.AddWithValue("@VideoId", videoId);
      var result = await command.ExecuteScalarAsync();
      return result is null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
    }

    public async Task<List<VideoModel>> GetRecommendedVideosAsync(int limit = 100)
    {
      var videos = new List<VideoModel>();
      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();
      var command = connection.CreateCommand();
      command.CommandText = @"
        SELECT v.Id, v.Title, v.FilePath, v.ThumbnailPath, v.Duration, v.DateAdded,
               v.IsFavorite, v.IsWatchLater, v.FileSize, v.CreationDate, v.LastWatched, v.PlayCount,
               (v.IsFavorite * 1000) + (v.PlayCount * 20) +
               CASE WHEN v.LastWatched IS NULL THEN 0
                    ELSE MAX(0, 180 - (julianday('now', 'localtime') - julianday(v.LastWatched))) END +
               (SELECT COUNT(*) * 50
                  FROM VideoTags vt
                  WHERE vt.VideoId = v.Id
                    AND EXISTS (
                      SELECT 1 FROM VideoTags related
                      INNER JOIN Videos relatedVideo ON relatedVideo.Id = related.VideoId
                      WHERE related.TagId = vt.TagId AND related.VideoId <> v.Id
                        AND (relatedVideo.IsFavorite = 1
                             OR julianday(relatedVideo.LastWatched) >= julianday('now', '-90 days', 'localtime'))
                    )) AS RecommendationScore
        FROM Videos v
        ORDER BY RecommendationScore DESC, v.DateAdded DESC
        LIMIT @Limit";
      command.Parameters.AddWithValue("@Limit", limit);
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
          LastWatched = TryGetDateTime(reader, "LastWatched"),
          PlayCount = reader.IsDBNull("PlayCount") ? 0 : reader.GetInt32("PlayCount")
        });
      }
      return videos;
    }

    public async Task<int> GetPendingVideosCountAsync()
    {
      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();
      var command = connection.CreateCommand();
      command.CommandText = "SELECT COUNT(*) FROM Videos WHERE LastWatched IS NULL";
      return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task<List<VideoModel>> GetPendingVideosSortedAsync(string sortProperty, bool ascending, int offset, int limit)
    {
      var videos = new List<VideoModel>();
      var order = ascending ? "ASC" : "DESC";
      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();
      var command = connection.CreateCommand();
      command.CommandText = $@"
        SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater,
               FileSize, CreationDate, LastWatched, PlayCount
        FROM Videos
        WHERE LastWatched IS NULL
        ORDER BY {sortProperty} {order}, DateAdded DESC
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
          LastWatched = TryGetDateTime(reader, "LastWatched"),
          PlayCount = reader.IsDBNull("PlayCount") ? 0 : reader.GetInt32("PlayCount")
        });
      }
      return videos;
    }

    public async Task UpdateVideoTagsAsync(int videoId, IEnumerable<string> tagNames)
    {
      var normalizedTags = tagNames
        .Select(tag => tag.Trim())
        .Where(tag => !string.IsNullOrWhiteSpace(tag))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();
      using var transaction = connection.BeginTransaction();

      var deleteCommand = connection.CreateCommand();
      deleteCommand.Transaction = transaction;
      deleteCommand.CommandText = "DELETE FROM VideoTags WHERE VideoId = @VideoId";
      deleteCommand.Parameters.AddWithValue("@VideoId", videoId);
      await deleteCommand.ExecuteNonQueryAsync();

      foreach (var tagName in normalizedTags)
      {
        var insertTagCommand = connection.CreateCommand();
        insertTagCommand.Transaction = transaction;
        insertTagCommand.CommandText = "INSERT INTO Tags (Name) VALUES (@Name) ON CONFLICT(Name) DO NOTHING";
        insertTagCommand.Parameters.AddWithValue("@Name", tagName);
        await insertTagCommand.ExecuteNonQueryAsync();

        var linkCommand = connection.CreateCommand();
        linkCommand.Transaction = transaction;
        linkCommand.CommandText = @"
          INSERT OR IGNORE INTO VideoTags (VideoId, TagId)
          SELECT @VideoId, Id FROM Tags WHERE Name = @Name COLLATE NOCASE";
        linkCommand.Parameters.AddWithValue("@VideoId", videoId);
        linkCommand.Parameters.AddWithValue("@Name", tagName);
        await linkCommand.ExecuteNonQueryAsync();
      }

      var cleanupCommand = connection.CreateCommand();
      cleanupCommand.Transaction = transaction;
      cleanupCommand.CommandText = "DELETE FROM Tags WHERE NOT EXISTS (SELECT 1 FROM VideoTags WHERE VideoTags.TagId = Tags.Id)";
      await cleanupCommand.ExecuteNonQueryAsync();
      transaction.Commit();
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
                SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater, FileSize, CreationDate, LastWatched
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
          LastWatched = TryGetDateTime(reader, "LastWatched"),

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
                SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater, FileSize, CreationDate, LastWatched
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
          LastWatched = TryGetDateTime(reader, "LastWatched"),

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
                SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater, FileSize, CreationDate, LastWatched
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
          LastWatched = TryGetDateTime(reader, "LastWatched"),

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
          LastWatched = TryGetDateTime(reader, "LastWatched"),
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
          LastWatched = TryGetDateTime(reader, "LastWatched"),
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
          LastWatched = TryGetDateTime(reader, "LastWatched"),

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

    public async Task UpdateVideoFileInfoAsync(int videoId, string title, string filePath, string? thumbnailPath)
    {
      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = @"
        UPDATE Videos
        SET Title = @Title,
            FilePath = @FilePath,
            ThumbnailPath = @ThumbnailPath
        WHERE Id = @Id";

      command.Parameters.AddWithValue("@Title", title);
      command.Parameters.AddWithValue("@FilePath", filePath);
      command.Parameters.AddWithValue("@ThumbnailPath", thumbnailPath ?? "");
      command.Parameters.AddWithValue("@Id", videoId);

      await command.ExecuteNonQueryAsync();
    }


    public async Task<int> UpdateVideoPathByFilePathAsync(string oldFilePath, string newFilePath, string? newThumbnailPath)
    {
      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = @"
        UPDATE Videos
        SET FilePath = @NewFilePath,
            ThumbnailPath = CASE
                WHEN @NewThumbnailPath IS NULL THEN ThumbnailPath
                ELSE @NewThumbnailPath
            END
        WHERE FilePath = @OldFilePath";

      command.Parameters.AddWithValue("@NewFilePath", newFilePath);
      command.Parameters.AddWithValue("@NewThumbnailPath", string.IsNullOrWhiteSpace(newThumbnailPath) ? DBNull.Value : newThumbnailPath);
      command.Parameters.AddWithValue("@OldFilePath", oldFilePath);

      return await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Updates all video and thumbnail paths beneath a folder after it has been moved.
    /// </summary>
    public async Task<int> UpdatePathsForMovedFolderAsync(string oldFolderPath, string newFolderPath)
    {
      var oldRoot = Path.TrimEndingDirectorySeparator(oldFolderPath);
      var newRoot = Path.TrimEndingDirectorySeparator(newFolderPath);
      var oldPrefix = oldRoot + Path.DirectorySeparatorChar;

      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();
      using var transaction = connection.BeginTransaction();

      var command = connection.CreateCommand();
      command.Transaction = transaction;
      command.CommandText = @"
        UPDATE Videos
        SET FilePath = CASE
              WHEN FilePath = @OldRoot THEN @NewRoot
              WHEN substr(FilePath, 1, length(@OldPrefix)) = @OldPrefix THEN @NewRoot || substr(FilePath, length(@OldRoot) + 1)
              ELSE FilePath
            END,
            ThumbnailPath = CASE
              WHEN ThumbnailPath = @OldRoot THEN @NewRoot
              WHEN substr(ThumbnailPath, 1, length(@OldPrefix)) = @OldPrefix THEN @NewRoot || substr(ThumbnailPath, length(@OldRoot) + 1)
              ELSE ThumbnailPath
            END
        WHERE FilePath = @OldRoot OR substr(FilePath, 1, length(@OldPrefix)) = @OldPrefix
           OR ThumbnailPath = @OldRoot OR substr(ThumbnailPath, 1, length(@OldPrefix)) = @OldPrefix";
      command.Parameters.AddWithValue("@OldRoot", oldRoot);
      command.Parameters.AddWithValue("@NewRoot", newRoot);
      command.Parameters.AddWithValue("@OldPrefix", oldPrefix);

      var affectedRows = await command.ExecuteNonQueryAsync();
      await transaction.CommitAsync();
      return affectedRows;
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
          LastWatched = TryGetDateTime(reader, "LastWatched"),
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
          LastWatched = TryGetDateTime(reader, "LastWatched"),
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
          LastWatched = TryGetDateTime(reader, "LastWatched"),
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
          LastWatched = TryGetDateTime(reader, "LastWatched"),
        });
      }

      return videos;
    }
    public async Task<List<VideoModel>> SearchVideosAsync(string searchTerm, string sortProperty, bool ascending, int offset = 0, int limit = 0)
    {
      var videos = new List<VideoModel>();
      var limitClause = limit > 0 ? "LIMIT @Limit OFFSET @Offset" : "";

      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = $@"
        SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater, FileSize, CreationDate, LastWatched
        FROM Videos 
        WHERE Title LIKE @SearchTerm OR FilePath LIKE @SearchTerm
          OR EXISTS (
            SELECT 1 FROM VideoTags
            INNER JOIN Tags ON Tags.Id = VideoTags.TagId
            WHERE VideoTags.VideoId = Videos.Id AND Tags.Name LIKE @SearchTerm)
        ORDER BY {sortProperty} {(ascending ? "ASC" : "DESC")}, DateAdded DESC
        {limitClause}";

      command.Parameters.AddWithValue("@SearchTerm", $"%{searchTerm}%");
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
          LastWatched = TryGetDateTime(reader, "LastWatched"),
        });
      }

      return videos;
    }
    public async Task<int> GetSearchVideosCountAsync(string searchTerm)
{
    using var connection = new SqliteConnection(_connectionString);
    await connection.OpenAsync();

    var command = connection.CreateCommand();
    command.CommandText = @"
        SELECT COUNT(*) 
        FROM Videos 
        WHERE Title LIKE @SearchTerm OR FilePath LIKE @SearchTerm
          OR EXISTS (
            SELECT 1 FROM VideoTags
            INNER JOIN Tags ON Tags.Id = VideoTags.TagId
            WHERE VideoTags.VideoId = Videos.Id AND Tags.Name LIKE @SearchTerm)";
    
    command.Parameters.AddWithValue("@SearchTerm", $"%{searchTerm}%");

    var result = await command.ExecuteScalarAsync();
    return Convert.ToInt32(result);
}

    public async Task RecordVideoPlayedAsync(int videoId, DateTime lastWatched)
    {
      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = "UPDATE Videos SET LastWatched = @LastWatched, PlayCount = PlayCount + 1 WHERE Id = @Id";
      command.Parameters.AddWithValue("@LastWatched", lastWatched.ToString("o"));
      command.Parameters.AddWithValue("@Id", videoId);

      await command.ExecuteNonQueryAsync();
    }

    public async Task<int> GetNotWatchedVideosCountAsync(int days)
    {
      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = @"
        SELECT COUNT(*) FROM Videos 
        WHERE COALESCE(LastWatched, DateAdded) < datetime('now', '-' || @Days || ' days', 'localtime')";
      command.Parameters.AddWithValue("@Days", days);

      var result = await command.ExecuteScalarAsync();
      return Convert.ToInt32(result);
    }

    public async Task<List<VideoModel>> GetNotWatchedVideosSortedAsync(int days, string sortProperty, bool ascending, int offset, int limit)
    {
      var videos = new List<VideoModel>();
      var order = ascending ? "ASC" : "DESC";

      using var connection = new SqliteConnection(_connectionString);
      await connection.OpenAsync();

      var command = connection.CreateCommand();
      command.CommandText = $@"
        SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater, FileSize, CreationDate, LastWatched
        FROM Videos 
        WHERE COALESCE(LastWatched, DateAdded) < datetime('now', '-' || @Days || ' days', 'localtime')
        ORDER BY {sortProperty} {order}, COALESCE(LastWatched, DateAdded) ASC 
        LIMIT @Limit OFFSET @Offset";

      command.Parameters.AddWithValue("@Days", days);
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
          LastWatched = TryGetDateTime(reader, "LastWatched"),
        });
      }

      return videos;
    }
  }
}
