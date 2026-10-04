using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace winui_local_movie
{
  /// <summary>Windows 原生文件夹多选对话框（支持 Ctrl 和 Shift 多选）。</summary>
  [ComVisible(true)]
  public static class MultiFolderPicker
  {
    private const uint FOS_ALLOWMULTISELECT = 0x00000200;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint FOS_PATHMUSTEXIST = 0x00000800;
    private const uint FOS_PICKFOLDERS = 0x00000020;
    private const uint SIGDN_FILESYSPATH = 0x80058000;
    private const int HResultCanceled = unchecked((int)0x800704C7);

    public static IReadOnlyList<string> PickFolders(IntPtr ownerWindow)
    {
      IFileOpenDialog? dialog = null;
      IShellItemArray? items = null;
      var stage = "开始";
      try
      {
        Log("创建 IFileOpenDialog");
        dialog = (IFileOpenDialog)new FileOpenDialogComObject();
        stage = "读取对话框选项";
        var getOptionsResult = dialog.GetOptions(out var options);
        Log($"{stage}: HRESULT=0x{getOptionsResult:X8}, Options=0x{options:X8}");
        Marshal.ThrowExceptionForHR(getOptionsResult);
        stage = "设置多选文件夹选项";
        // 保留 Shell 的默认选项（其中包括 FOS_FILEMUSTEXIST），只追加文件夹多选所需标志。
        // 清除默认标志会让部分 Windows Shell 版本在 Show 成功后无法生成结果数组，
        // 随后的 GetResults 会返回 E_INVALIDARG。
        var newOptions = options |
                         FOS_PICKFOLDERS | FOS_ALLOWMULTISELECT | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST;
        var setOptionsResult = dialog.SetOptions(newOptions);
        Log($"{stage}: HRESULT=0x{setOptionsResult:X8}, Options=0x{newOptions:X8}");
        Marshal.ThrowExceptionForHR(setOptionsResult);
        stage = "设置对话框标题";
        var setTitleResult = dialog.SetTitle("选择一个或多个图集文件夹");
        Log($"{stage}: HRESULT=0x{setTitleResult:X8}");
        Marshal.ThrowExceptionForHR(setTitleResult);
        stage = "显示选择器";
        var showResult = dialog.Show(ownerWindow);
        Log($"{stage}: HRESULT=0x{showResult:X8}");
        if (showResult == HResultCanceled) return Array.Empty<string>();
        Marshal.ThrowExceptionForHR(showResult);
        stage = "读取已选项目";
        var getResultsResult = dialog.GetResults(out items);
        Log($"{stage}: HRESULT=0x{getResultsResult:X8}");
        Marshal.ThrowExceptionForHR(getResultsResult);
        stage = "读取已选项目数量";
        var getCountResult = items.GetCount(out var count);
        Log($"{stage}: HRESULT=0x{getCountResult:X8}, Count={count}");
        Marshal.ThrowExceptionForHR(getCountResult);
        var paths = new List<string>();
        stage = "枚举已选项目";
        for (uint index = 0; index < count; index++)
        {
          IShellItem? item = null;
          // 某些 Shell 命名空间项没有文件系统路径；跳过它们，而非中止整个批量选择。
          var getItemResult = items.GetItemAt(index, out item);
          if (getItemResult != 0 || item is null)
          {
            Log($"枚举项目 {index + 1}/{count} 失败: HRESULT=0x{getItemResult:X8}");
            continue;
          }
          try
          {
            IntPtr pathPointer = IntPtr.Zero;
            try
            {
              var getPathResult = item.GetDisplayName(SIGDN_FILESYSPATH, out pathPointer);
              var path = getPathResult == 0 && pathPointer != IntPtr.Zero
                ? Marshal.PtrToStringUni(pathPointer)
                : null;
              if (!string.IsNullOrWhiteSpace(path))
                paths.Add(path);
              else
                Log($"读取项目 {index + 1}/{count} 路径失败: HRESULT=0x{getPathResult:X8}");
            }
            finally
            {
              if (pathPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pathPointer);
            }
          }
          finally { Marshal.ReleaseComObject(item); }
          if ((index + 1) % 50 == 0 || index + 1 == count) Log($"已读取 {index + 1}/{count} 个项目，有效路径 {paths.Count} 个");
        }
        Log($"选择器完成，共返回 {paths.Count} 个文件夹");
        return paths;
      }
      catch (Exception ex)
      {
        Log($"失败阶段：{stage}{Environment.NewLine}{ex}");
        throw new InvalidOperationException($"文件夹选择器在“{stage}”阶段失败：{ex.Message}", ex);
      }
      finally
      {
        try { if (items is not null) Marshal.ReleaseComObject(items); }
        catch (Exception ex) { Log($"释放项目数组失败：{ex}"); }
        try { if (dialog is not null) Marshal.ReleaseComObject(dialog); }
        catch (Exception ex) { Log($"释放对话框失败：{ex}"); }
      }
    }

    private static void Log(string message) => Debug.WriteLine($"[GalleryFolderPicker] {message}");

    // ComImport 不会把 C# 父接口的成员自动纳入派生 COM 接口的 vtable。
    // 因此必须按 IModalWindow -> IFileDialog -> IFileOpenDialog 的原生顺序声明完整方法表。
    [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOpenDialog
    {
      [PreserveSig] int Show(IntPtr parent);
      [PreserveSig] int SetFileTypes(uint count, IntPtr fileTypes);
      [PreserveSig] int SetFileTypeIndex(uint index);
      [PreserveSig] int GetFileTypeIndex(out uint index);
      [PreserveSig] int Advise(IntPtr events, out uint cookie);
      [PreserveSig] int Unadvise(uint cookie);
      [PreserveSig] int SetOptions(uint options);
      [PreserveSig] int GetOptions(out uint options);
      [PreserveSig] int SetDefaultFolder(IntPtr folder);
      [PreserveSig] int SetFolder(IntPtr folder);
      [PreserveSig] int GetFolder(out IntPtr folder);
      [PreserveSig] int GetCurrentSelection(out IntPtr item);
      [PreserveSig] int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
      [PreserveSig] int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
      [PreserveSig] int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
      [PreserveSig] int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
      [PreserveSig] int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
      [PreserveSig] int GetResult(out IntPtr item);
      [PreserveSig] int AddPlace(IntPtr item, uint placement);
      [PreserveSig] int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
      [PreserveSig] int Close(int hr);
      [PreserveSig] int SetClientGuid(ref Guid guid);
      [PreserveSig] int ClearClientData();
      [PreserveSig] int SetFilter(IntPtr filter);
      [PreserveSig] int GetResults([MarshalAs(UnmanagedType.Interface)] out IShellItemArray items);
      [PreserveSig] int GetSelectedItems([MarshalAs(UnmanagedType.Interface)] out IShellItemArray items);
    }

    [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialogComObject { }

    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemArray
    {
      [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid bhid, ref Guid riid, out IntPtr result);
      [PreserveSig] int GetPropertyStore(int flags, ref Guid riid, out IntPtr result);
      [PreserveSig] int GetPropertyDescriptionList(ref IntPtr keyType, ref Guid riid, out IntPtr result);
      [PreserveSig] int GetAttributes(uint attributes, out uint result);
      [PreserveSig] int GetCount(out uint count);
      [PreserveSig] int GetItemAt(uint index, out IShellItem item);
      [PreserveSig] int EnumItems(out IntPtr enumShellItems);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
      [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid bhid, ref Guid riid, out IntPtr result);
      [PreserveSig] int GetParent(out IShellItem parent);
      [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr name);
      [PreserveSig] int GetAttributes(uint attributes, out uint result);
      [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
    }
  }
}
