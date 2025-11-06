using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace winui_local_movie
{
  public sealed partial class MergeVideosDialog : ContentDialog
  {
    private CancellationTokenSource _cancellationTokenSource;
    private bool _isMerging = false;
    public MergeVideosDialog()
    {
      this.InitializeComponent();
      this.Closing += MergeVideosDialog_Closing;
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
      // 清除之前的错误消息
      ClearErrorMessage();
      // 如果正在合并，则阻止再次点击
      if (_isMerging)
      {
        args.Cancel = true;
        return;
      }

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
        args.Cancel = true; // 阻止默认关闭行为

        // 先在UI线程上初始化UI状态
        MergeProgressBar.Visibility = Visibility.Visible;
        MergeProgressBar.IsIndeterminate = true;
        var outputPath = OutputNameTextBox.Text;
        // 然后启动后台任务
        _ = Task.Run(async () =>
        {
          try
          {
            await MergeVideos(videoPaths, outputPath);
          }
          catch (Exception ex)
          {
            Debug.WriteLine($"Task exception: {ex.GetType().Name} - {ex.Message}");
            Debug.WriteLine($"Stack trace: {ex.StackTrace}");

            // 在UI线程上显示错误
            DispatcherQueue.TryEnqueue(() =>
        {
          ShowErrorMessage($"合并失败: {ex.Message}");
        });
          }
        });
      }
      catch (Exception ex)
      {
        args.Cancel = true;
        ShowErrorMessage($"合并失败: {ex.Message}");
      }
    }

    private void ShowErrorMessage(string message)
    {
      DispatcherQueue.TryEnqueue(() =>
      {
        ErrorMessageTextBlock.Text = message;
        ErrorMessageTextBlock.Visibility = Visibility.Visible;
      });
    }

    // 清除错误消息的方法
    private void ClearErrorMessage()
    {
      DispatcherQueue.TryEnqueue(() =>
      {
        ErrorMessageTextBlock.Visibility = Visibility.Collapsed;
        ErrorMessageTextBlock.Text = string.Empty;
      });
    }
    // 替换原来的 OnClosing 方法为如下内容：
    private void MergeVideosDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
      // 如果正在合并，则取消合并
      if (_isMerging && _cancellationTokenSource != null)
      {
        _cancellationTokenSource.Cancel();
        args.Cancel = true; // 暂时取消关闭，等待合并取消完成

        // 在后台等待取消完成后再关闭对话框
        Task.Run(async () =>
        {
          await Task.Delay(500); // 给一点时间让取消生效
          DispatcherQueue.TryEnqueue(() =>
{
  Hide(); // 手动隐藏对话框
});
        });
      }
    }
    private async Task ShowSuccessMessage(string message)
    {
      var successDialog = new ContentDialog
      {
        Title = "成功",
        Content = message,
        CloseButtonText = "确定",
        XamlRoot = this.XamlRoot
      };
      await successDialog.ShowAsync();
    }
    private async Task MergeVideos(List<string> videoPaths, string outputName)
    {
      // 设置合并状态
      _isMerging = true;

      DispatcherQueue.TryEnqueue(() =>
   {
     // 初始化进度控件
     MergeProgressBar.Visibility = Visibility.Visible;
     MergeProgressBar.Value = 0;
     CancelButton.Visibility = Visibility.Visible;

     // 禁用主按钮防止重复点击
     this.IsPrimaryButtonEnabled = false;
     this.IsSecondaryButtonEnabled = false;
     ClearErrorMessage();
   });


      // 创建取消令牌
      _cancellationTokenSource = new CancellationTokenSource();

      string firstVideoDirectory = Path.GetDirectoryName(videoPaths[0]);
      string outputFilePath = Path.Combine(firstVideoDirectory, $"{outputName}.mp4");

      try
      {
        // 使用FFmpeg合并视频（需要先安装FFmpeg）
        await MergeVideosWithFFmpeg(videoPaths, outputFilePath, _cancellationTokenSource.Token);

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
            Debug.WriteLine($"Failed to delete file {path}: {ex.Message}");
          }
        }

        // 将新视频插入数据库
        await InsertVideoToDatabase(outputFilePath, outputName);

        // 显示成功消息
        DispatcherQueue.TryEnqueue(async () =>
      {
        this.Hide();
      });
        await ShowSuccessMessage($"视频已合并并保存到: {outputFilePath}");
      }
      catch (OperationCanceledException ex)
      {
        // 用户取消操作
        DispatcherQueue.TryEnqueue(() =>
        {
          ShowErrorMessage($"视频合并过程中取消: {ex.Message}");
        });
      }
      catch (Exception ex)
      {
        DispatcherQueue.TryEnqueue(() =>
        {
          ShowErrorMessage($"视频合并过程中发生错误: {ex.Message}");
        });
      }
      finally
      {
        DispatcherQueue.TryEnqueue(() =>
        {
          // 重置合并状态
          _isMerging = false;

          // 隐藏进度控件
          MergeProgressBar.Visibility = Visibility.Collapsed;
          CancelButton.Visibility = Visibility.Collapsed;

          // 恢复按钮状态
          this.IsPrimaryButtonEnabled = true;
          this.IsSecondaryButtonEnabled = true;
        });


        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
      }
    }
    private async Task MergeVideosWithFFmpeg(List<string> videoPaths, string outputFilePath, CancellationToken cancellationToken)
    {
      string tempFileListPath = Path.GetTempFileName();
      Process process = null;

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

        process = new Process();
        process.StartInfo = processInfo;
        process.EnableRaisingEvents = true;

        // 启动进程
        if (process.Start())
        {
          // 异步读取标准错误输出以获取进度信息
          string errorOutput = await process.StandardError.ReadToEndAsync();

          // 等待进程完成或被取消
          while (!process.HasExited)
          {
            // 检查是否请求了取消
            if (cancellationToken.IsCancellationRequested)
            {
              try
              {
                process.Kill();
              }
              catch
              {
                // 忽略杀死进程时的异常
              }

              throw new OperationCanceledException(cancellationToken);
            }

            await Task.Delay(100); // 短暂延迟避免过度占用CPU
          }

          // 检查是否执行成功
          if (process.ExitCode != 0)
          {
            throw new Exception($"FFmpeg执行失败: {errorOutput}");
          }
        }
        else
        {
          throw new Exception("无法启动FFmpeg进程");
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

        process?.Dispose();
      }
    }
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
      _cancellationTokenSource?.Cancel();
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