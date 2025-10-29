using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using SQLitePCL;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace winui_local_movie
{
  /// <summary>
  /// Provides application-specific behavior to supplement the default Application class.
  /// </summary>
  public partial class App : Application
  {
    public DatabaseService DatabaseService { get; private set; }

    public App()
    {
      this.InitializeComponent();

      try
      {
        Batteries.Init();
        // 添加更多初始化确保在Release模式下正常工作
        SQLitePCL.raw.sqlite3_config(SQLitePCL.raw.SQLITE_CONFIG_MULTITHREAD);
      }
      catch (Exception ex)
      {
        // 处理初始化失败的情况
        System.Diagnostics.Debug.WriteLine($"Database initialization failed: {ex.Message}");
      }
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
      DatabaseService ??= new DatabaseService();
      MainWindow = new MainWindow();
      MainWindow.Activate();
    }

    public Window MainWindow { get; private set; } // 添加这行
  }
}
