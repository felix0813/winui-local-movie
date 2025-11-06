using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace winui_local_movie
{
  public sealed partial class MergeVideosDialog : ContentDialog
  {
    public MergeVideosDialog()
    {
      this.InitializeComponent();
    }

    private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
      // 验证输入
      if (string.IsNullOrWhiteSpace(OutputNameTextBox.Text))
      {
        args.Cancel = true;
        ShowErrorMessage("请输入合并后的视频名称");
        return;
      }

      var videoPaths = new List<string>
            {
                VideoPath1TextBox.Text,
                VideoPath2TextBox.Text,
                VideoPath3TextBox.Text,
                VideoPath4TextBox.Text
            }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();

      if (videoPaths.Count < 2)
      {
        args.Cancel = true;
        ShowErrorMessage("至少需要两个视频路径");
        return;
      }

      // 检查文件是否存在
      foreach (var path in videoPaths)
      {
        if (!File.Exists(path))
        {
          args.Cancel = true;
          ShowErrorMessage($"文件不存在: {path}");
          return;
        }
      }

      try
      {
        // 合并视频逻辑
        await MergeVideos(videoPaths, OutputNameTextBox.Text);
      }
      catch (Exception ex)
      {
        args.Cancel = true;
        ShowErrorMessage($"合并失败: {ex.Message}");
      }
    }

    private async void ShowErrorMessage(string message)
    {
      var dialog = new ContentDialog
      {
        Title = "错误",
        Content = message,
        CloseButtonText = "确定",
        XamlRoot = this.XamlRoot
      };
      await dialog.ShowAsync();
    }

    private async System.Threading.Tasks.Task MergeVideos(List<string> videoPaths, string outputName)
    {
      // 获取第一个视频的目录作为输出目录
      string firstVideoDirectory = Path.GetDirectoryName(videoPaths[0]);
      string outputFilePath = Path.Combine(firstVideoDirectory, $"{outputName}.mp4");

      try
      {
        // 使用FFmpeg合并视频（需要先安装FFmpeg）
        await MergeVideosWithFFmpeg(videoPaths, outputFilePath);

        // 删除原始文件
        foreach (var path in videoPaths)
        {
          try
          {
            File.Delete(path);
          }
          catch (Exception ex)
          {
            // 记录删除失败的日志，但不中断流程
            System.Diagnostics.Debug.WriteLine($"Failed to delete file {path}: {ex.Message}");
          }
        }

        // 将新视频插入数据库
        await InsertVideoToDatabase(outputFilePath, outputName);

        // 显示成功消息
        var successDialog = new ContentDialog
        {
          Title = "成功",
          Content = $"视频已合并并保存到: {outputFilePath}",
          CloseButtonText = "确定",
          XamlRoot = this.XamlRoot
        };
        await successDialog.ShowAsync();
      }
      catch (Exception ex)
      {
        // 处理合并过程中的错误
        var errorDialog = new ContentDialog
        {
          Title = "合并失败",
          Content = $"视频合并过程中发生错误: {ex.Message}",
          CloseButtonText = "确定",
          XamlRoot = this.XamlRoot
        };
        await errorDialog.ShowAsync();
      }
    }

    private async System.Threading.Tasks.Task MergeVideosWithFFmpeg(List<string> videoPaths, string outputFilePath)
    {
      // 创建临时文件列表用于FFmpeg concat协议
      string tempFileListPath = Path.GetTempFileName();

      try
      {
        // 写入文件列表
        using (var writer = new StreamWriter(tempFileListPath))
        {
          foreach (var path in videoPaths)
          {
            await writer.WriteLineAsync($"file '{path.Replace("'", "\\'")}'");
          }
        }

        // 构建FFmpeg命令
        string arguments = $"-f concat -safe 0 -i \"{tempFileListPath}\" -c copy \"{outputFilePath}\"";

        // 启动FFmpeg进程
        var processInfo = new ProcessStartInfo
        {
          FileName = "ffmpeg", // 确保系统PATH中包含ffmpeg，或者使用完整路径
          Arguments = arguments,
          UseShellExecute = false,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          CreateNoWindow = true
        };

        using (var process = Process.Start(processInfo))
        {
          if (process != null)
          {
            // 等待进程完成
            await process.WaitForExitAsync();

            // 检查是否执行成功
            if (process.ExitCode != 0)
            {
              string errorOutput = await process.StandardError.ReadToEndAsync();
              throw new Exception($"FFmpeg执行失败: {errorOutput}");
            }
          }
          else
          {
            throw new Exception("无法启动FFmpeg进程");
          }
        }
      }
      finally
      {
        // 清理临时文件
        if (File.Exists(tempFileListPath))
        {
          try
          {
            File.Delete(tempFileListPath);
          }
          catch
          {
            // 忽略清理失败
          }
        }
      }
    }

    private async System.Threading.Tasks.Task InsertVideoToDatabase(string filePath, string title)
    {
      try
      {
        // 获取文件信息
        var fileInfo = new FileInfo(filePath);

        // 创建VideoModel对象
        var video = new VideoModel
        {
          Title = title,
          FilePath = filePath,
          DateAdded = DateTime.Now,
          FileSize = fileInfo.Length,
          IsFavorite = false,
          IsWatchLater = false,
          Duration = TimeSpan.Zero // 如果有获取视频时长的方法，可以在这里调用
        };

        // 插入数据库
        var databaseService = new DatabaseService();
        await databaseService.AddVideoAsync(video);
      }
      catch (Exception ex)
      {
        // 记录数据库插入错误，但不中断主流程
        System.Diagnostics.Debug.WriteLine($"Failed to insert video to database: {ex.Message}");
      }
    }
  }
}