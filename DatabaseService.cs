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
            var localFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dbPath = Path.Combine(localFolder, "winui_local_movie", "videos.db");
            
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
                    IsWatchLater INTEGER DEFAULT 0
                )";
            command.ExecuteNonQuery();
        }
        
        public async Task AddVideoAsync(VideoModel video)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO Videos 
                (Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater)
                VALUES (@Title, @FilePath, @ThumbnailPath, @Duration, @DateAdded, @IsFavorite, @IsWatchLater)";
                
            command.Parameters.AddWithValue("@Title", video.Title);
            command.Parameters.AddWithValue("@FilePath", video.FilePath);
            command.Parameters.AddWithValue("@ThumbnailPath", video.ThumbnailPath ?? "");
            command.Parameters.AddWithValue("@Duration", video.Duration.ToString());
            command.Parameters.AddWithValue("@DateAdded", video.DateAdded.ToString("o"));
            command.Parameters.AddWithValue("@IsFavorite", video.IsFavorite ? 1 : 0);
            command.Parameters.AddWithValue("@IsWatchLater", video.IsWatchLater ? 1 : 0);
            
            await command.ExecuteNonQueryAsync();
        }
        
        public async Task<List<VideoModel>> GetFeaturedVideosAsync(int count = 6)
        {
            var videos = new List<VideoModel>();
            
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater
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
                    IsWatchLater = reader.GetInt32("IsWatchLater") == 1
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
                SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater
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
                    IsWatchLater = reader.GetInt32("IsWatchLater") == 1
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
                SELECT Id, Title, FilePath, ThumbnailPath, Duration, DateAdded, IsFavorite, IsWatchLater
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
                    IsWatchLater = reader.GetInt32("IsWatchLater") == 1
                });
            }
            
            return videos;
        }
    }
}