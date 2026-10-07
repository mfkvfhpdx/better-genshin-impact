using System;
using System.IO;
using System.Security.AccessControl;
using System.Threading;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// 解析日志分析 / HtmlMask 等功能共用的 WebView2 用户数据目录。
/// 同一进程固定使用一个槽位；若公用目录已被其他进程占用，则复用有限个备用目录（不按 PID 新建）。
/// </summary>
public static class WebView2SharedUserDataFolder
{
    /// <summary>公用目录 + 备用目录数量上限，避免无限增生。</summary>
    private const int MaxSlots = 8;

    private static readonly object Gate = new();
    private static string? _resolvedPath;
    private static Mutex? _slotMutex;

    /// <summary>
    /// 获取当前进程应使用的共享 WebView2 用户数据目录（进程内缓存，多次调用返回同一路径）。
    /// </summary>
    public static string Resolve()
    {
        lock (Gate)
        {
            if (_resolvedPath != null)
            {
                return _resolvedPath;
            }

            var baseDir = AppContext.BaseDirectory;
            for (var slot = 0; slot < MaxSlots; slot++)
            {
                var path = GetSlotPath(baseDir, slot);
                if (!TryClaimSlot(slot, path, out var mutex))
                {
                    continue;
                }

                EnsureFolder(path);
                _slotMutex = mutex;
                _resolvedPath = path;
                return path;
            }

            // 槽位均被占用时仍回退到公用目录，由 WebView2 自身报错，避免静默写到未知位置
            var fallback = GetSlotPath(baseDir, 0);
            EnsureFolder(fallback);
            _resolvedPath = fallback;
            return fallback;
        }
    }

    private static string GetSlotPath(string baseDir, int slot)
    {
        // slot0: WebView2Data；其后: WebView2Data_2 ... WebView2Data_8
        return slot == 0
            ? Path.Combine(baseDir, "WebView2Data")
            : Path.Combine(baseDir, $"WebView2Data_{slot + 1}");
    }

    private static string MutexName(int slot) => $@"Local\BetterGI.WebView2Shared.Slot.{slot}";

    private static bool TryClaimSlot(int slot, string path, out Mutex? mutex)
    {
        mutex = null;
        Mutex? created = null;
        try
        {
            created = new Mutex(initiallyOwned: false, MutexName(slot));
            bool acquired;
            try
            {
                acquired = created.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                // 上一进程异常退出，视为可接管该槽位
                acquired = true;
            }

            if (!acquired)
            {
                created.Dispose();
                return false;
            }

            // Mutex 空闲但 WebView2 lockfile 仍被其他进程占用（例如异常残留的浏览器进程）
            if (IsWebView2LockFileHeld(path))
            {
                try
                {
                    created.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }

                created.Dispose();
                return false;
            }

            mutex = created;
            return true;
        }
        catch
        {
            if (created != null)
            {
                try
                {
                    created.Dispose();
                }
                catch
                {
                    // ignore
                }
            }

            return false;
        }
    }

    /// <summary>
    /// 检测 Chromium/WebView2 用户数据目录是否已被其他进程锁定。
    /// </summary>
    private static bool IsWebView2LockFileHeld(string userDataFolder)
    {
        var lockFile = Path.Combine(userDataFolder, "EBWebView", "lockfile");
        if (!File.Exists(lockFile))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void EnsureFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var info = new DirectoryInfo(folder);
            var access = info.GetAccessControl();
            access.AddAccessRule(new FileSystemAccessRule(
                "Everyone",
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            info.SetAccessControl(access);
        }
        catch
        {
            // 权限设置失败不阻断功能
        }
    }
}
