using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DshEdgeDock
{
    internal static class Native
    {
        public const int WH_MOUSE_LL = 14;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_MBUTTONDOWN = 0x0207;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_ASYNCWINDOWPOS = 0x4000;

        public const int SW_HIDE = 0;
        public const int SW_SHOWNOACTIVATE = 4;
        public const int SW_RESTORE = 9;
        public const int SW_SHOW = 5;

        public const uint MONITOR_DEFAULTTONEAREST = 2;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        public const int VK_LBUTTON = 0x01;
        public const int VK_RBUTTON = 0x02;

        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOOWNERZORDER = 0x0200;
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOPMOST = 0x00000008;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO info);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);

        public static bool IsTopmost(IntPtr h)
        {
            return (GetWindowLong(h, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
        }

        public static void EnablePerMonitorDpi()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }
    }

    /// <summary>
    /// Windows 11 贴靠布局（Snap Layouts）把窗口放进半屏 / 三分屏 / 四分屏等布局槽时，
    /// 窗口尺寸会被强制成工作区宽度的固定比例（1/2、1/3、2/3、1/4），高度铺满或半高。
    /// 这类由系统布局摆放的窗口不做自动收起；只对用户自己拖到边缘的窗口生效。
    /// </summary>
    internal static class DockMath
    {
        private const int Tolerance = 3;

        private static bool Near(int a, int b) { return Math.Abs(a - b) <= Tolerance; }

        public static bool IsSnapLayout(Native.RECT rect, Native.RECT work, bool enabled)
        {
            if (!enabled) return false;
            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;
            int W = work.Right - work.Left;
            int H = work.Bottom - work.Top;
            if (W <= 0 || H <= 0 || w <= 0 || h <= 0) return false;

            bool fullHeight = Near(h, H) && Near(rect.Top, work.Top);
            bool halfHeight = Near(h, H / 2) &&
                (Near(rect.Top, work.Top) || Near(rect.Bottom, work.Bottom));

            if (fullHeight)
            {
                if (Near(w, W)) return true;
                if (Near(w, W / 2)) return true;
                if (Near(w, W / 3)) return true;
                if (Near(w, (W * 2) / 3)) return true;
                if (Near(w, W / 4)) return true;
            }
            if (halfHeight && (Near(w, W / 2) || Near(w, W / 3) || Near(w, (W * 2) / 3))) return true;
            return false;
        }
    }

    internal enum DockSide { None, Left, Right }

    /// <summary>
    /// 一条来自本机控制端口（默认 127.0.0.1:59731）的请求。
    /// 请求在后台线程收下、排队，由 UI 线程执行（所有窗口操作用户界面线程做才安全），
    /// 后台线程最多等 800ms 拿回应答。
    /// </summary>
    internal sealed class ControlRequest
    {
        public string Command;
        public string Response = "err timeout";
        public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
    }

    internal sealed class EdgeDockContext : ApplicationContext
    {
        private const int TickMs = 16;
        private const int EdgeTolerancePx = 10;
        // 用户常把窗口拖得比屏幕边缘多出十几像素（DWM 边框 + 手滑），这种也算"贴着"。
        private const int OvershootPx = 64;
        private const int SliverPx = 4;
        private const int RestoreGuardMs = 700;

        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer timer;
        private readonly string configPath;
        private readonly string logPath;
        private readonly string exeDir;

        private bool paused;
        private bool excludeSnapLayout = true;
        private bool collapsed;
        private bool hideMode;
        private bool animating;
        private int animTargetX;
        private int normalX;
        private int normalY;
        private DockSide side = DockSide.None;
        private IntPtr hwnd = IntPtr.Zero;
        private uint windowPid;
        private bool orphanChecked;
        private bool mouseWasDown;
        private long lastRestoreTick;
        private int collapsedTargetX;
        private long collapsedSettleTick;
        private int animFromX;
        private int animStepIndex;
        private const int animStepCount = 6;
        private int winDx;
        private int winDy;
        private string lastLogState = "";

        // ---- v2：控制端口、置顶、全屏唤出 ----
        internal const int ControlPort = 59731;
        internal const string Version = "2.0";
        private bool alwaysOnTop;
        private bool showAboveFullscreen = true;
        private bool topmostApplied;
        private long lastTopmostCheck;
        private readonly Queue<ControlRequest> controlQueue = new Queue<ControlRequest>();
        private Thread controlThread;
        private volatile bool stopping;
        private bool tempTopmost;
        private IntPtr fgAtCollapse;
        private ToolStripMenuItem pauseMenuItem;
        private ToolStripMenuItem topMenuItem;
        private ToolStripMenuItem fullMenuItem;

        public EdgeDockContext()
        {
            exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DshEdgeDock", "config.ini");
            logPath = Path.Combine(Path.GetDirectoryName(configPath), "edgedock.log");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath));
            LoadConfig();

            tray = new NotifyIcon();
            tray.Icon = LoadIcon();
            tray.Text = "DSH 边缘自动收起";
            tray.ContextMenuStrip = BuildMenu();
            tray.Visible = true;

            timer = new System.Windows.Forms.Timer();
            timer.Interval = TickMs;
            timer.Tick += OnTick;
            timer.Start();

            StartControlServer();

            Log("started pid=" + Process.GetCurrentProcess().Id + " paused=" + paused);
            UpdateTrayText();
        }

        private Icon LoadIcon()
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName("DeepSeek Harness"))
                {
                    try
                    {
                        Icon ic = Icon.ExtractAssociatedIcon(p.MainModule.FileName);
                        if (ic != null) return ic;
                    }
                    catch { }
                }
            }
            catch { }
            return SystemIcons.Application;
        }

        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem pauseItem = new ToolStripMenuItem("暂停自动收起", null, delegate(object s, EventArgs e)
            {
                paused = !paused;
                SaveConfig();
                ((ToolStripMenuItem)s).Checked = paused;
                if (paused && collapsed) RestoreWindow();
                UpdateTrayText();
                Log("paused=" + paused);
            });
            pauseItem.Checked = paused;
            pauseMenuItem = pauseItem;

            ToolStripMenuItem autoItem = new ToolStripMenuItem("开机自动启动", null, delegate(object s, EventArgs e)
            {
                bool on = !AutoStartEnabled();
                SetAutoStart(on);
                ((ToolStripMenuItem)s).Checked = on;
                Log("autostart=" + on);
            });
            autoItem.Checked = AutoStartEnabled();

            ToolStripMenuItem topItem = new ToolStripMenuItem("窗口置顶", null, delegate(object s, EventArgs e)
            {
                SetAlwaysOnTop(!alwaysOnTop);
            });
            topItem.Checked = alwaysOnTop;
            topMenuItem = topItem;

            ToolStripMenuItem fullItem = new ToolStripMenuItem("其他窗口全屏时也能唤出", null, delegate(object s, EventArgs e)
            {
                showAboveFullscreen = !showAboveFullscreen;
                ((ToolStripMenuItem)s).Checked = showAboveFullscreen;
                SaveConfig();
                Log("showAboveFullscreen=" + showAboveFullscreen);
            });
            fullItem.Checked = showAboveFullscreen;
            fullMenuItem = fullItem;

            ToolStripMenuItem showItem = new ToolStripMenuItem("显示 Harness 窗口", null, delegate(object s, EventArgs e)
            {
                IntPtr w = FindWindow();
                if (w != IntPtr.Zero)
                {
                    if (collapsed) RestoreWindow();
                    Native.ShowWindow(w, Native.SW_RESTORE);
                    Native.ShowWindow(w, Native.SW_SHOW);
                }
            });

            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出", null, delegate(object s, EventArgs e)
            {
                if (collapsed) RestoreWindow();
                ExitThread();
            });

            menu.Items.Add(pauseItem);
            menu.Items.Add(autoItem);
            menu.Items.Add(topItem);
            menu.Items.Add(fullItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(showItem);
            menu.Items.Add(exitItem);
            return menu;
        }

        private void UpdateTrayText()
        {
            tray.Text = "DSH 边缘自动收起" + (paused ? "（已暂停）" : "");
        }

        private bool AutoStartEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (k == null) return false;
                    string v = k.GetValue("DshEdgeDock") as string;
                    return v != null && v.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        private void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (k == null) return;
                    if (on) k.SetValue("DshEdgeDock", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("DshEdgeDock", false);
                }
            }
            catch (Exception ex) { Log("autostart error: " + ex.Message); }
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(configPath)) return;
                foreach (string line in File.ReadAllLines(configPath))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim();
                    string v = line.Substring(i + 1).Trim();
                    if (k == "paused") paused = v == "1";
                    else if (k == "excludeSnapLayout") excludeSnapLayout = v != "0";
                    else if (k == "alwaysOnTop") alwaysOnTop = v == "1";
                    else if (k == "showAboveFullscreen") showAboveFullscreen = v != "0";
                }
            }
            catch { }
        }

        private void SaveConfig()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("paused=" + (paused ? "1" : "0"));
                sb.AppendLine("excludeSnapLayout=" + (excludeSnapLayout ? "1" : "0"));
                sb.AppendLine("alwaysOnTop=" + (alwaysOnTop ? "1" : "0"));
                sb.AppendLine("showAboveFullscreen=" + (showAboveFullscreen ? "1" : "0"));
                File.WriteAllText(configPath, sb.ToString());
            }
            catch { }
        }

        private void Log(string message)
        {
            try
            {
                string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message;
                File.AppendAllText(logPath, line + Environment.NewLine);
                if (File.Exists(logPath) && new FileInfo(logPath).Length > 256 * 1024) File.Delete(logPath);
            }
            catch { }
        }

        private IntPtr FindWindow()
        {
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!Native.IsWindowVisible(h)) return true;
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid == 0) return true;
                string name = "";
                try { name = Process.GetProcessById((int)pid).ProcessName; } catch { }
                if (name != "DeepSeek Harness") return true;
                StringBuilder cls = new StringBuilder(128);
                Native.GetClassName(h, cls, cls.Capacity);
                if (cls.ToString() != "Chrome_WidgetWin_1") return true;
                Native.RECT r;
                if (!Native.GetWindowRect(h, out r)) return true;
                long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (area > bestArea) { bestArea = area; best = h; windowPid = pid; }
                return true;
            }, IntPtr.Zero);
            return best;
        }

        /// <summary>
        /// 返回窗口的"可见边框"（DWM 扩展边框，不含 Win32 隐形的缩放边框），
        /// 并记下 可见边框 → SetWindowPos 坐标 的偏移，移动窗口时用它换算，
        /// 避免两种坐标系混用导致窗口每恢复一次就右移 7px。
        /// </summary>
        private bool TryGetBounds(IntPtr h, out Native.RECT rect)
        {
            rect = new Native.RECT();
            Native.RECT win;
            bool haveWin = Native.GetWindowRect(h, out win);
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf(typeof(Native.RECT))) == 0)
            {
                if (haveWin) { winDx = win.Left - rect.Left; winDy = win.Top - rect.Top; }
                else { winDx = 0; winDy = 0; }
                return true;
            }
            winDx = 0;
            winDy = 0;
            if (haveWin) { rect = win; return true; }
            return false;
        }

        private Native.MONITORINFO GetMonitor(IntPtr h)
        {
            Native.MONITORINFO info = new Native.MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
            IntPtr mon = Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST);
            Native.GetMonitorInfo(mon, ref info);
            return info;
        }

        private bool HasAdjacentMonitor(Native.MONITORINFO info, DockSide s)
        {
            foreach (Screen sc in Screen.AllScreens)
            {
                Rectangle b = sc.Bounds;
                if (s == DockSide.Right && b.Left == info.rcMonitor.Right) return true;
                if (s == DockSide.Left && b.Right == info.rcMonitor.Left) return true;
            }
            return false;
        }

        private DockSide DetectDock(IntPtr h, Native.RECT rect, Native.MONITORINFO info)
        {
            if (Native.IsZoomed(h) || Native.IsIconic(h)) return DockSide.None;
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 400 || height < 300) return DockSide.None;
            // A window covering the whole monitor is maximized/fullscreen, not docked.
            if (Math.Abs(rect.Left - info.rcMonitor.Left) <= EdgeTolerancePx && Math.Abs(rect.Right - info.rcMonitor.Right) <= EdgeTolerancePx) return DockSide.None;

            DockSide side = DockSide.None;
            if (rect.Right >= info.rcMonitor.Right - EdgeTolerancePx && rect.Right <= info.rcMonitor.Right + OvershootPx) side = DockSide.Right;
            else if (rect.Left <= info.rcMonitor.Left + EdgeTolerancePx && rect.Left >= info.rcMonitor.Left - OvershootPx) side = DockSide.Left;
            if (side == DockSide.None) return DockSide.None;

            // 系统"窗口布局"（贴靠布局）摆放的窗口不动。
            if (DockMath.IsSnapLayout(rect, info.rcWork, excludeSnapLayout))
            {
                if (lastLogState != "snap-layout")
                {
                    lastLogState = "snap-layout";
                    Log("skip: window is in a Windows snap layout (" + width + "x" + height + ")");
                }
                return DockSide.None;
            }
            return side;
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                DrainControlQueue();
                TickTopmost();

                if (paused)
                {
                    if (collapsed) RestoreWindow();
                    return;
                }

                if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd))
                {
                    collapsed = false;
                    animating = false;
                    hwnd = FindWindow();
                    if (hwnd == IntPtr.Zero) return;
                    Log("window found hwnd=" + hwnd + " pid=" + windowPid);
                }

                // 最小化 / 隐藏（托盘收起）时窗口坐标是假的 (-32000)，不要当成"被移到别处"。
                if (Native.IsIconic(hwnd) || !Native.IsWindowVisible(hwnd))
                {
                    if (collapsed) { collapsed = false; animating = false; }
                    return;
                }

                Native.RECT rect;
                if (!TryGetBounds(hwnd, out rect)) { hwnd = IntPtr.Zero; return; }
                Native.MONITORINFO mon = GetMonitor(hwnd);

                if (!orphanChecked)
                {
                    orphanChecked = true;
                    if (RecoverOrphan(rect, mon)) return;
                }

                if (animating)
                {
                    StepAnimation();
                    return;
                }

                if (collapsed)
                {
                    TickCollapsed(rect, mon);
                    return;
                }

                side = DetectDock(hwnd, rect, mon);
                if (side == DockSide.None)
                {
                    if (lastLogState != "none") { lastLogState = "none"; }
                    return;
                }
                if (lastLogState != side.ToString())
                {
                    lastLogState = side.ToString();
                    Log("docked " + side + " rect=" + rect.Left + "," + rect.Top + "," + rect.Right + "," + rect.Bottom + " mon=" + mon.rcMonitor.Left + "," + mon.rcMonitor.Right);
                }

                TickClickOutside(rect);
            }
            catch (Exception ex)
            {
                Log("tick error: " + ex.Message);
            }
        }

        private void TickClickOutside(Native.RECT rect)
        {
            bool down = (Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) != 0;
            if (!down) down = (Native.GetAsyncKeyState(Native.VK_RBUTTON) & 0x8000) != 0;
            bool fresh = down && !mouseWasDown;
            mouseWasDown = down;
            if (!fresh) return;

            Native.POINT pt;
            if (!Native.GetCursorPos(out pt)) return;

            bool inside = pt.X >= rect.Left && pt.X < rect.Right && pt.Y >= rect.Top && pt.Y < rect.Bottom;
            if (inside) return;

            IntPtr under = Native.WindowFromPoint(pt);
            if (under != IntPtr.Zero)
            {
                IntPtr root = Native.GetAncestor(under, 2);
                uint upid;
                Native.GetWindowThreadProcessId(root, out upid);
                if (upid == windowPid) return; // an app-owned popup/menu, not an outside click
            }

            if (Environment.TickCount - lastRestoreTick < RestoreGuardMs) return;
            CollapseWindow(rect);
        }

        private void TickCollapsed(Native.RECT rect, Native.MONITORINFO mon)
        {
            // SetWindowPos 是异步投递的，窗口位置会晚几帧才真正到位；
            // 在稳定窗口期内不做"外部移动"判断，否则会把自己的滑动动画误判成用户拖动。
            if (!hideMode && Environment.TickCount >= collapsedSettleTick &&
                Math.Abs(rect.Left - collapsedTargetX) > 3)
            {
                Log("external move while collapsed; reset (x=" + rect.Left + " target=" + collapsedTargetX + ")");
                collapsed = false;
                animating = false;
                lastRestoreTick = Environment.TickCount;
                return;
            }

            Native.POINT pt;
            Native.GetCursorPos(out pt);
            bool atEdge = side == DockSide.Right ? pt.X >= mon.rcMonitor.Right - 1 : pt.X <= mon.rcMonitor.Left;
            if (atEdge && pt.Y >= rect.Top - 4 && pt.Y <= rect.Bottom + 4)
            {
                RestoreWindow();
                return;
            }

            if (!hideMode && Environment.TickCount - lastRestoreTick > RestoreGuardMs)
            {
                // 只有"收起之后才抢到前台"才算用户点了窗口；置顶窗口收起时前台本来就是它，
                // 拿这个当信号会收起后立刻又弹回来。
                IntPtr fg = Native.GetForegroundWindow();
                if (fg == hwnd && fg != fgAtCollapse) RestoreWindow();
            }
        }

        /// <summary>
        /// 上一个实例异常退出时窗口可能被留在屏幕外（只剩几像素）。
        /// 启动时发现这种"孤儿"位置就把窗口拉回屏幕内，免得用户再也找不到窗口。
        /// </summary>
        private bool RecoverOrphan(Native.RECT rect, Native.MONITORINFO mon)
        {
            if (Native.IsIconic(hwnd)) return false;
            int w = rect.Right - rect.Left;
            // 最小化窗口的坐标是 -32000 之类的假值；只认真实贴边的那条窄带，且纵向要落在屏幕范围内。
            if (rect.Top < mon.rcMonitor.Top - 50 || rect.Bottom > mon.rcMonitor.Bottom + 50) return false;
            int x = int.MinValue;
            if (rect.Left >= mon.rcMonitor.Right - 6 && rect.Left <= mon.rcMonitor.Right + 40) x = mon.rcMonitor.Right - w;
            else if (rect.Right <= mon.rcMonitor.Left + 6 && rect.Right >= mon.rcMonitor.Left - 40) x = mon.rcMonitor.Left;
            if (x == int.MinValue) return false;
            Native.SetWindowPos(hwnd, IntPtr.Zero, x + winDx, rect.Top + winDy, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            Log("orphan window found off-screen at " + rect.Left + ", pulled back to " + x);
            return true;
        }

        private int TargetX(Native.RECT rect, Native.MONITORINFO mon)
        {
            int width = rect.Right - rect.Left;
            if (side == DockSide.Right) return mon.rcMonitor.Right - SliverPx;
            return mon.rcMonitor.Left - width + SliverPx;
        }

        private void CollapseWindow(Native.RECT rect)
        {
            if (collapsed) return;
            Native.MONITORINFO mon = GetMonitor(hwnd);
            hideMode = HasAdjacentMonitor(mon, side);
            normalX = rect.Left;
            normalY = rect.Top;
            collapsed = true;
            fgAtCollapse = Native.GetForegroundWindow();
            // 收起后不再占着"最前"，否则那条 4px 缝会一直浮在别的窗口上。
            if (!alwaysOnTop && topmostApplied)
            {
                ApplyTopmost(false);
                topmostApplied = false;
                tempTopmost = false;
            }
            if (hideMode)
            {
                Native.ShowWindow(hwnd, Native.SW_HIDE);
                Log("collapsed(hide) side=" + side);
                return;
            }
            animTargetX = TargetX(rect, mon);
            collapsedTargetX = animTargetX;
            collapsedSettleTick = Environment.TickCount + 400;
            animFromX = rect.Left;
            animStepIndex = 0;
            animating = true;
            Log("collapsed(slide) side=" + side + " from=" + rect.Left + " to=" + animTargetX);
        }

        private void StepAnimation()
        {
            // 位置按"起点 + 进度"直接算，不读窗口当前坐标：
            // SetWindowPos(ASYNCWINDOWPOS) 落地有延迟，读回来的坐标是旧值，会让动画原地打转。
            animStepIndex++;
            int x;
            if (animStepIndex >= animStepCount)
            {
                x = animTargetX;
                animating = false;
            }
            else
            {
                double t = (double)animStepIndex / animStepCount;
                x = animFromX + (int)Math.Round((animTargetX - animFromX) * t);
            }
            Native.SetWindowPos(hwnd, IntPtr.Zero, x + winDx, normalY + winDy, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
        }

        private void RestoreWindow()
        {
            if (!collapsed) return;
            CollapsedOff();
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return;
            if (hideMode)
            {
                Native.SetWindowPos(hwnd, IntPtr.Zero, normalX + winDx, normalY + winDy, 0, 0,
                    Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
                Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE);
            }
            else
            {
                Native.SetWindowPos(hwnd, IntPtr.Zero, normalX + winDx, normalY + winDy, 0, 0,
                    Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
            }
            lastRestoreTick = Environment.TickCount;
            Log("restored x=" + normalX);
            RaiseForReveal();
        }

        private void CollapsedOff()
        {
            collapsed = false;
            animating = false;
            hideMode = false;
        }

        // ================= v2：控制端口 / 置顶 / 全屏唤出 =================

        /// <summary>本机控制端口：插件（Node 侧）通过它读状态、改设置、立刻收起/唤出。</summary>
        private void StartControlServer()
        {
            controlThread = new Thread(ControlLoop);
            controlThread.IsBackground = true;
            controlThread.Name = "dsh-edge-dock-control";
            controlThread.Start();
        }

        private void ControlLoop()
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, ControlPort);
                listener.Start();
                Log("control port listening on 127.0.0.1:" + ControlPort);
            }
            catch (Exception ex)
            {
                Log("control port error: " + ex.Message);
                return;
            }
            while (!stopping)
            {
                TcpClient client = null;
                try
                {
                    client = listener.AcceptTcpClient();
                    NetworkStream stream = client.GetStream();
                    StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                    StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false));
                    writer.AutoFlush = true;
                    string line = reader.ReadLine();
                    if (line != null && line.Trim().Length > 0)
                    {
                        ControlRequest req = new ControlRequest();
                        req.Command = line.Trim();
                        lock (controlQueue) controlQueue.Enqueue(req);
                        req.Done.Wait(800);
                        writer.WriteLine(req.Response);
                    }
                }
                catch { }
                finally
                {
                    try { if (client != null) client.Close(); } catch { }
                }
            }
            try { listener.Stop(); } catch { }
        }

        /// <summary>把排队中的请求交给 UI 线程执行（窗口操作只能在 UI 线程做）。</summary>
        private void DrainControlQueue()
        {
            for (int guard = 0; guard < 8; guard++)
            {
                ControlRequest req;
                lock (controlQueue)
                {
                    if (controlQueue.Count == 0) return;
                    req = controlQueue.Dequeue();
                }
                string response;
                try { response = HandleCommand(req.Command); }
                catch (Exception ex) { response = "err " + ex.Message; }
                req.Response = response;
                req.Done.Set();
            }
        }

        private string HandleCommand(string command)
        {
            string[] parts = command.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "err empty";
            string verb = parts[0].ToLowerInvariant();

            if (verb == "ping") return "ok";
            if (verb == "state") return StateJson();

            if (verb == "set" && parts.Length >= 3)
            {
                bool on = parts[2] == "1" || parts[2].ToLowerInvariant() == "true";
                return ApplySetting(parts[1], on);
            }

            if (verb == "reveal")
            {
                hwnd = FindWindow();
                if (hwnd == IntPtr.Zero) return "err no-window";
                if (!Native.IsWindowVisible(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
                Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE);
                if (collapsed) RestoreWindow();
                RaiseForReveal();
                return StateJson();
            }

            if (verb == "collapse")
            {
                hwnd = FindWindow();
                if (hwnd == IntPtr.Zero) return "err no-window";
                Native.RECT rect;
                if (!TryGetBounds(hwnd, out rect)) return "err bounds";
                Native.MONITORINFO mon = GetMonitor(hwnd);
                DockSide s = DetectDock(hwnd, rect, mon);
                if (s == DockSide.None) return "err not-docked";
                side = s;
                CollapseWindow(rect);
                return StateJson();
            }

            if (verb == "quit")
            {
                if (collapsed) RestoreWindow();
                stopping = true;
                Log("quit via control port");
                ThreadPool.QueueUserWorkItem(delegate(object o)
                {
                    Thread.Sleep(200);
                    try { Environment.Exit(0); } catch { }
                });
                return "ok";
            }

            return "err unknown-command";
        }

        private string ApplySetting(string key, bool on)
        {
            if (key == "paused") { paused = on; if (paused && collapsed) RestoreWindow(); UpdateTrayText(); }
            else if (key == "alwaysOnTop") SetAlwaysOnTop(on);
            else if (key == "showAboveFullscreen") showAboveFullscreen = on;
            else if (key == "excludeSnapLayout") excludeSnapLayout = on;
            else if (key == "autoStart") SetAutoStart(on);
            else return "err unknown-key";
            SaveConfig();
            SyncMenuChecks();
            Log("set " + key + "=" + on);
            return StateJson();
        }

        private void SyncMenuChecks()
        {
            try
            {
                if (pauseMenuItem != null) pauseMenuItem.Checked = paused;
                if (topMenuItem != null) topMenuItem.Checked = alwaysOnTop;
                if (fullMenuItem != null) fullMenuItem.Checked = showAboveFullscreen;
            }
            catch { }
        }

        private string StateJson()
        {
            bool hasWindow = hwnd != IntPtr.Zero && Native.IsWindow(hwnd);
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"ok\":true,\"version\":\"").Append(Version).Append("\",\"running\":true");
            sb.Append(",\"hasWindow\":").Append(hasWindow ? "true" : "false");
            sb.Append(",\"docked\":\"").Append(side.ToString()).Append("\"");
            sb.Append(",\"collapsed\":").Append(collapsed ? "true" : "false");
            sb.Append(",\"paused\":").Append(paused ? "true" : "false");
            sb.Append(",\"alwaysOnTop\":").Append(alwaysOnTop ? "true" : "false");
            sb.Append(",\"showAboveFullscreen\":").Append(showAboveFullscreen ? "true" : "false");
            sb.Append(",\"excludeSnapLayout\":").Append(excludeSnapLayout ? "true" : "false");
            sb.Append(",\"autoStart\":").Append(AutoStartEnabled() ? "true" : "false");
            sb.Append(",\"topmost\":").Append(hasWindow && Native.IsTopmost(hwnd) ? "true" : "false");
            sb.Append(",\"fullscreenForeground\":").Append(ForegroundIsFullscreen() ? "true" : "false");
            sb.Append(",\"port\":").Append(ControlPort);
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>置顶开关的守护：用户手动"取消置顶"或窗口重建后要重新贴上。</summary>
        private void TickTopmost()
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd) || Native.IsIconic(hwnd)) return;
            if (Environment.TickCount - lastTopmostCheck < 800) return;
            lastTopmostCheck = Environment.TickCount;

            if (alwaysOnTop)
            {
                if (!Native.IsTopmost(hwnd)) ApplyTopmost(true);
                topmostApplied = true;
                return;
            }
            if (tempTopmost)
            {
                if (!collapsed) { if (!Native.IsTopmost(hwnd)) ApplyTopmost(true); return; }
                tempTopmost = false;
            }
            if (topmostApplied)
            {
                ApplyTopmost(false);
                topmostApplied = false;
            }
        }

        /// <summary>统一的"窗口置顶"开关：立刻贴上/撤掉，不等 800ms 守护，并落盘。</summary>
        private void SetAlwaysOnTop(bool on)
        {
            alwaysOnTop = on;
            if (!on) tempTopmost = false;
            ApplyTopmost(on);
            topmostApplied = false;
            if (topMenuItem != null) topMenuItem.Checked = on;
            SaveConfig();
            Log("alwaysOnTop=" + on);
        }

        private void ApplyTopmost(bool on)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return;
            Native.SetWindowPos(hwnd, on ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        /// <summary>前台窗口是否铺满它所在的显示器（全屏视频 / 全屏游戏 / 全屏浏览器）。</summary>
        private bool ForegroundIsFullscreen()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == hwnd) return false;
            Native.RECT r;
            if (!Native.GetWindowRect(fg, out r)) return false;
            Native.MONITORINFO mon = GetMonitor(fg);
            if (mon.rcMonitor.Right <= mon.rcMonitor.Left) return false;
            return r.Left <= mon.rcMonitor.Left + 2 && r.Top <= mon.rcMonitor.Top + 2 &&
                   r.Right >= mon.rcMonitor.Right - 2 && r.Bottom >= mon.rcMonitor.Bottom - 2;
        }

        /// <summary>
        /// 唤出时如果别的窗口是全屏（或用户开了"窗口置顶"），把 Harness 临时提到最前，
        /// 否则全屏窗口会把刚滑出来的 Harness 盖住，看着像没反应。
        /// </summary>
        private void RaiseForReveal()
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return;
            if (!alwaysOnTop && !(showAboveFullscreen && ForegroundIsFullscreen())) return;
            ApplyTopmost(true);
            Native.BringWindowToTop(hwnd);
            topmostApplied = true;
            if (!alwaysOnTop) tempTopmost = true;
            Log("raised above fullscreen (mode=" + (alwaysOnTop ? "always" : "temp") + ")");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                stopping = true;
                // 退出时别把窗口留在屏幕外。
                if (collapsed) RestoreWindow();
                if (alwaysOnTop == false && topmostApplied) ApplyTopmost(false);
                if (timer != null) timer.Dispose();
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
            }
            base.Dispose(disposing);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--selftest")
            {
                SelfTest();
                return;
            }
            Native.EnablePerMonitorDpi();
            bool created;
            using (Mutex mutex = new Mutex(true, "DshEdgeDock.SingleInstance", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new EdgeDockContext());
            }
        }

        /// <summary>--selftest：用合成坐标验证"贴靠布局"判定，不碰真实窗口。</summary>
        private static void SelfTest()
        {
            Native.RECT work = new Native.RECT();
            work.Left = 0; work.Top = 0; work.Right = 1920; work.Bottom = 1032;
            string[,] cases = new string[,] {
                // left, top, right, bottom, expected
                { "960",  "0",   "1920", "1032", "snap"    }, // 右半屏
                { "0",    "0",   "640",  "1032", "snap"    }, // 左三分之一
                { "640",  "0",   "1280", "1032", "snap"    }, // 中间三分之二
                { "0",    "0",   "480",  "1032", "snap"    }, // 左四分之一
                { "0",    "0",   "1920", "1032", "snap"    }, // 全屏铺满
                { "960",  "516", "1920", "1032", "snap"    }, // 右下四分之一
                { "642",  "38",  "1924", "962",  "free"    }, // 用户手动拖到右缘
                { "0",    "0",   "1100", "1032", "free"    }, // 手动拉宽
            };
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < cases.GetLength(0); i++)
            {
                Native.RECT r = new Native.RECT();
                r.Left = int.Parse(cases[i, 0]); r.Top = int.Parse(cases[i, 1]);
                r.Right = int.Parse(cases[i, 2]); r.Bottom = int.Parse(cases[i, 3]);
                bool got = DockMath.IsSnapLayout(r, work, true);
                string expect = cases[i, 4];
                bool ok = got == (expect == "snap");
                sb.AppendLine(string.Format("{0} rect={1},{2},{3},{4} got={5} expect={6}",
                    ok ? "PASS" : "FAIL", r.Left, r.Top, r.Right, r.Bottom,
                    got ? "snap" : "free", expect));
            }
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DshEdgeDock", "selftest.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
        }
    }
}
