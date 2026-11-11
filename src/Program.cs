using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace EndfieldDirect
{
    static class Loc
    {
        public static bool Chinese = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";

        public static string T(string zh, string en)
        {
            return Chinese ? zh : en;
        }
    }

    sealed class Options
    {
        public string Lang;
        public bool CreateShortcut;
        public string ShortcutPath;
        public int GraceSeconds = 3;
        public int WindowTimeoutSeconds = 60;
        public bool Hold;
        public bool DryRun;
        public bool Verbose;

        public readonly List<KeyValuePair<string, string>> LauncherArgs = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("-launcher_lang", "zh-cn"),
            new KeyValuePair<string, string>("-launcher_sub_channel", "1"),
        };
        public readonly List<KeyValuePair<string, string>> RegistryValues = new List<KeyValuePair<string, string>>();
        public readonly List<string> PassThrough = new List<string>();

        public readonly List<string> ShortcutArgs = new List<string>();

        static readonly Regex Named = new Regex(@"^--?([A-Za-z][A-Za-z0-9_\-]*)(?:=(.*))?$", RegexOptions.Singleline);

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                Match m = Named.Match(arg);
                if (!m.Success) { o.PassThrough.Add(arg); o.ShortcutArgs.Add(arg); continue; }

                string key = m.Groups[1].Value;
                string val = m.Groups[2].Success ? m.Groups[2].Value : null;
                string lower = key.ToLowerInvariant();

                if (lower == "sc")
                {
                    o.CreateShortcut = true;
                    if (val != null) o.ShortcutPath = val;
                    else if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) o.ShortcutPath = args[++i];
                    continue;
                }
                if (lower == "dry-run" && val == null) { o.DryRun = true; continue; }

                o.ShortcutArgs.Add(arg);

                if (lower == "wait" && val != null) o.GraceSeconds = ToSeconds(val, arg);
                else if (lower == "timeout" && val != null) o.WindowTimeoutSeconds = ToSeconds(val, arg);
                else if (lower == "hold" && val == null) o.Hold = true;
                else if ((lower == "v" || lower == "verbose") && val == null) o.Verbose = true;
                else if (lower == "lang" && val != null) o.Lang = val.ToLowerInvariant();
                else if (lower.StartsWith("language_") && val != null)
                    o.RegistryValues.Add(new KeyValuePair<string, string>(key, val));
                else if (lower.StartsWith("launcher_") && val != null)
                    o.SetLauncherArg("-" + key, val);
                else o.PassThrough.Add(arg);
            }
            return o;
        }

        static int ToSeconds(string val, string arg)
        {
            int n;
            if (!int.TryParse(val, out n) || n < 0) throw new ArgumentException(Loc.T("参数无效: ", "Invalid argument: ") + arg);
            return n;
        }

        void SetLauncherArg(string key, string val)
        {
            for (int i = 0; i < LauncherArgs.Count; i++)
            {
                if (string.Equals(LauncherArgs[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    LauncherArgs[i] = new KeyValuePair<string, string>(LauncherArgs[i].Key, val);
                    return;
                }
            }
            LauncherArgs.Add(new KeyValuePair<string, string>(key, val));
        }
    }

    static class Native
    {
        const int PROCESS_CREATE_PROCESS = 0x0080;
        const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        static readonly IntPtr PROC_THREAD_ATTRIBUTE_PARENT_PROCESS = (IntPtr)0x00020000;
        const int SW_HIDE = 0;

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STARTUPINFO
        {
            public uint cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STARTUPINFOEX
        {
            public STARTUPINFO StartupInfo;
            public IntPtr lpAttributeList;
        }

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(int access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attr, IntPtr value, IntPtr size, IntPtr prev, IntPtr retSize);

        [DllImport("kernel32.dll")]
        static extern void DeleteProcThreadAttributeList(IntPtr list);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcess(string app, string cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags,
            IntPtr env, string dir, ref STARTUPINFOEX si, out PROCESS_INFORMATION pi);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int cmd);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        const int PROCESS_VM_ACCESS = 0x0008 | 0x0010 | 0x0020;
        const uint TB_GETBUTTON = 0x0417;
        const uint TB_BUTTONCOUNT = 0x0418;
        const uint NIM_DELETE = 2;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string className, string title);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint type, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint type);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr read);

        public static int RemoveTrayIcons(ICollection<int> pids)
        {
            var bars = new List<IntPtr>();
            foreach (string root in new[] { "Shell_TrayWnd", "NotifyIconOverflowWindow" })
            {
                IntPtr top = FindWindow(root, null);
                if (top == IntPtr.Zero) continue;
                EnumChildWindows(top, delegate (IntPtr child, IntPtr _)
                {
                    var cls = new StringBuilder(64);
                    GetClassName(child, cls, cls.Capacity);
                    if (cls.ToString() == "ToolbarWindow32") bars.Add(child);
                    return true;
                }, IntPtr.Zero);
            }

            int removed = 0;
            foreach (IntPtr bar in bars) removed += RemoveFromToolbar(bar, pids);
            return removed;
        }

        [DllImport("user32.dll")]
        static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc proc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);

        static int RemoveFromToolbar(IntPtr bar, ICollection<int> pids)
        {
            uint shellPid;
            GetWindowThreadProcessId(bar, out shellPid);
            IntPtr shell = OpenProcess(PROCESS_VM_ACCESS, false, (int)shellPid);
            if (shell == IntPtr.Zero) return 0;

            var owned = new List<KeyValuePair<IntPtr, uint>>();
            IntPtr remote = VirtualAllocEx(shell, IntPtr.Zero, (UIntPtr)64, 0x3000, 0x04);
            try
            {
                if (remote == IntPtr.Zero) return 0;
                int count = (int)SendMessage(bar, TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
                var button = new byte[32];
                var data = new byte[16];
                UIntPtr read;

                for (int i = 0; i < count; i++)
                {
                    if (SendMessage(bar, TB_GETBUTTON, (IntPtr)i, remote) == IntPtr.Zero) continue;
                    if (!ReadProcessMemory(shell, remote, button, (UIntPtr)button.Length, out read)) continue;

                    long dwData = BitConverter.ToInt64(button, 16);
                    if (dwData == 0) continue;
                    if (!ReadProcessMemory(shell, (IntPtr)dwData, data, (UIntPtr)data.Length, out read)) continue;

                    IntPtr owner = (IntPtr)BitConverter.ToInt64(data, 0);
                    uint ownerPid;
                    GetWindowThreadProcessId(owner, out ownerPid);
                    if (pids.Contains((int)ownerPid))
                        owned.Add(new KeyValuePair<IntPtr, uint>(owner, BitConverter.ToUInt32(data, 8)));
                }
            }
            finally
            {
                if (remote != IntPtr.Zero) VirtualFreeEx(shell, remote, UIntPtr.Zero, 0x8000);
                CloseHandle(shell);
            }

            foreach (var icon in owned)
            {
                var nid = new NOTIFYICONDATA();
                nid.cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONDATA));
                nid.hWnd = icon.Key;
                nid.uID = icon.Value;
                Shell_NotifyIcon(NIM_DELETE, ref nid);
            }
            return owned.Count;
        }

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public static void CloseWindows(int pid)
        {
            EnumWindows(delegate (IntPtr hWnd, IntPtr _)
            {
                uint owner;
                GetWindowThreadProcessId(hWnd, out owner);
                if (owner == (uint)pid) PostMessage(hWnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        public static void ShowError(string text)
        {
            Console.Error.WriteLine(text);
            ShowMessage(text);
        }

        public static void ShowMessage(string text)
        {
            MessageBox(IntPtr.Zero, text, "EndfieldDirect", 0x10);
        }

        public static void ShowInfo(string text)
        {
            MessageBox(IntPtr.Zero, text, "EndfieldDirect", 0x40);
        }

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int processId);

        public static bool AttachParentConsole()
        {
            return AttachConsole(-1);
        }

        public static string GetImagePath(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        public static bool HasVisibleWindow(int pid)
        {
            bool visible = false;
            EnumWindows(delegate (IntPtr hWnd, IntPtr _)
            {
                uint owner;
                GetWindowThreadProcessId(hWnd, out owner);
                if (owner == (uint)pid && IsWindowVisible(hWnd)) { visible = true; return false; }
                return true;
            }, IntPtr.Zero);
            return visible;
        }

        public static void HideWindow(IntPtr hWnd)
        {
            ShowWindow(hWnd, SW_HIDE);
        }

        public static int HideWindows(int pid)
        {
            int hidden = 0;
            EnumWindows(delegate (IntPtr hWnd, IntPtr _)
            {
                uint owner;
                GetWindowThreadProcessId(hWnd, out owner);
                if (owner == (uint)pid)
                {
                    if (IsWindowVisible(hWnd)) hidden++;
                    ShowWindow(hWnd, SW_HIDE);
                }
                return true;
            }, IntPtr.Zero);
            return hidden;
        }

        public static int CreateWithParent(string exe, string cmdLine, string workDir, int parentPid)
        {
            IntPtr parentHandle = OpenProcess(PROCESS_CREATE_PROCESS, false, parentPid);
            if (parentHandle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(),
                    Loc.T("无法打开父进程", "Cannot open the parent process"));

            IntPtr attrList = IntPtr.Zero, parentHandleBuf = IntPtr.Zero;
            bool attrInit = false;
            try
            {
                IntPtr size = IntPtr.Zero;
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                attrList = Marshal.AllocHGlobal(size);
                if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref size))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                attrInit = true;

                parentHandleBuf = Marshal.AllocHGlobal(IntPtr.Size);
                Marshal.WriteIntPtr(parentHandleBuf, parentHandle);
                if (!UpdateProcThreadAttribute(attrList, 0, PROC_THREAD_ATTRIBUTE_PARENT_PROCESS,
                        parentHandleBuf, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                var si = new STARTUPINFOEX();
                si.StartupInfo.cb = (uint)Marshal.SizeOf(typeof(STARTUPINFOEX));
                si.lpAttributeList = attrList;

                PROCESS_INFORMATION pi;
                if (!CreateProcess(exe, cmdLine, IntPtr.Zero, IntPtr.Zero, false, EXTENDED_STARTUPINFO_PRESENT,
                        IntPtr.Zero, workDir, ref si, out pi))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                foreach (IntPtr h in new[] { pi.hThread, pi.hProcess }) CloseHandle(h);
                return (int)pi.dwProcessId;
            }
            finally
            {
                if (attrInit) DeleteProcThreadAttributeList(attrList);
                if (attrList != IntPtr.Zero) Marshal.FreeHGlobal(attrList);
                if (parentHandleBuf != IntPtr.Zero) Marshal.FreeHGlobal(parentHandleBuf);
                CloseHandle(parentHandle);
            }
        }
    }

    sealed class WindowHider
    {
        const uint EVENT_OBJECT_SHOW = 0x8002;
        const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        const uint GA_ROOT = 2;
        const uint WM_QUIT = 0x0012;

        delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [StructLayout(LayoutKind.Sequential)]
        struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int x, y;
        }

        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint tid, uint flags);

        [DllImport("user32.dll")]
        static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("user32.dll")]
        static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);

        [DllImport("user32.dll")]
        static extern bool TranslateMessage(ref MSG msg);

        [DllImport("user32.dll")]
        static extern IntPtr DispatchMessage(ref MSG msg);

        [DllImport("user32.dll")]
        static extern bool PostThreadMessage(uint tid, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        readonly string root;
        readonly Action<string> log;
        readonly Dictionary<uint, bool> watched = new Dictionary<uint, bool>();
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        WinEventProc callback;
        uint threadId;

        public WindowHider(string root, Action<string> log)
        {
            this.root = root;
            this.log = log;
        }

        public void Start()
        {
            var t = new Thread(Loop);
            t.IsBackground = true;
            t.Start();
            ready.WaitOne(2000);
        }

        public void Stop()
        {
            if (threadId != 0) PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        void Loop()
        {
            threadId = GetCurrentThreadId();
            callback = OnShow;
            IntPtr hook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, callback, 0, 0,
                                          WINEVENT_OUTOFCONTEXT);
            ready.Set();

            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            if (hook != IntPtr.Zero) UnhookWinEvent(hook);
        }

        void OnShow(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0 || hwnd == IntPtr.Zero) return;
            if (GetAncestor(hwnd, GA_ROOT) != hwnd) return;

            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (!IsLauncherProcess(pid)) return;

            Native.HideWindow(hwnd);
            log(Loc.T("已在窗口显示的瞬间隐藏它 (PID ", "Hid a window the instant it was shown (PID ") + pid + ")");
        }

        bool IsLauncherProcess(uint pid)
        {
            bool known;
            if (watched.TryGetValue(pid, out known)) return known;

            string path = Native.GetImagePath((int)pid);
            if (path == null) return false;

            string file = Path.GetFileName(path);
            known = path.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                    (file.Equals("Games.exe", StringComparison.OrdinalIgnoreCase) ||
                     file.Equals("Launcher.exe", StringComparison.OrdinalIgnoreCase));
            watched[pid] = known;
            return known;
        }
    }

    static class Program
    {
        static readonly string[] RegistryPaths =
        {
            @"Software\HYPERGRYPH\Endfield",
            @"Software\Hypergryph\Endfield",
        };

        static bool verbose;
        static volatile bool hiding;

        static readonly Stopwatch clock = Stopwatch.StartNew();
        static bool hasConsole;

        static void Report(string message)
        {
            if (hasConsole) Console.Error.WriteLine(message);
            else Native.ShowMessage(message);
        }

        static void Notify(string message)
        {
            if (hasConsole) Console.WriteLine(message);
            else Native.ShowInfo(message);
        }

        static void Log(string message)
        {
            if (verbose) Console.WriteLine("[+" + clock.Elapsed.TotalSeconds.ToString("0.00") + "s] " + message);
        }

        static int Main(string[] args)
        {
            hasConsole = Native.AttachParentConsole();

            foreach (string a in args)
            {
                string lowered = a.ToLowerInvariant();
                if (lowered == "-lang=zh" || lowered == "--lang=zh") Loc.Chinese = true;
                else if (lowered == "-lang=en" || lowered == "--lang=en") Loc.Chinese = false;
            }

            Options opts;
            try { opts = Options.Parse(args); }
            catch (ArgumentException ex) { Report(ex.Message); return 2; }

            string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string launcherExe = Path.Combine(root, "Launcher.exe");
            string gameDir = Path.Combine(root, @"games\Endfield Game");
            string gameExe = Path.Combine(gameDir, "Endfield.exe");

            verbose = opts.Verbose;

            if (!File.Exists(launcherExe))
            {
                Native.ShowError(Loc.T(
                    "未找到启动器 Launcher.exe。\n\n请把本程序放到游戏启动器所在的目录（与 Launcher.exe 同一文件夹）后再运行。\n\n当前位置: ",
                    "Launcher.exe was not found.\n\nPlease put this program in the game launcher's folder " +
                    "(the same folder as Launcher.exe) and run it again.\n\nCurrent location: ") + root);
                return 1;
            }
            if (!File.Exists(gameExe))
            {
                Native.ShowError(Loc.T("未找到游戏文件:\n", "Game file not found:\n") + gameExe +
                    Loc.T("\n\n请确认游戏已安装，并且本程序放在游戏启动器所在的目录。",
                          "\n\nMake sure the game is installed and this program is in the launcher's folder."));
                return 1;
            }

            string cmdLine = BuildCommandLine(gameExe, opts);

            Log(Loc.T("安装目录: ", "Install dir: ") + root);
            Log(Loc.T("启动器: ", "Launcher: ") + launcherExe);
            Log(Loc.T("游戏: ", "Game: ") + gameExe);
            Log(Loc.T("选项: ", "Options: ") + "wait=" + opts.GraceSeconds + "s timeout=" + opts.WindowTimeoutSeconds +
                "s hold=" + opts.Hold + " dry-run=" + opts.DryRun + " lang=" + (Loc.Chinese ? "zh" : "en"));
            foreach (var kv in opts.LauncherArgs) Log(Loc.T("启动器参数: ", "Launcher arg: ") + kv.Key + "=" + kv.Value);
            foreach (string a in opts.PassThrough) Log(Loc.T("透传参数: ", "Pass-through arg: ") + a);
            foreach (var kv in opts.RegistryValues)
                Log(Loc.T("注册表写入请求: ", "Registry write request: ") + kv.Key + "* = " + kv.Value);
            Log(Loc.T("最终命令行: ", "Final command line: ") + cmdLine);

            if (opts.CreateShortcut) return CreateShortcut(opts, gameExe);

            if (opts.DryRun)
            {
                Console.WriteLine(Loc.T("命令行: ", "Command line: ") + cmdLine);
                foreach (var kv in opts.RegistryValues)
                    Console.WriteLine(Loc.T("将写入注册表: ", "Will write registry: ") + kv.Key + " = " + kv.Value);
                Console.WriteLine(Loc.T(
                    "等待 " + opts.GraceSeconds + " 秒后" + (opts.Hold ? "，游戏退出时才收尾" : "收尾"),
                    "Cleanup after " + opts.GraceSeconds + " s" + (opts.Hold ? ", only once the game exits" : "")));
                return 0;
            }

            bool admin = IsAdministrator();
            Log(Loc.T("管理员权限: ", "Administrator: ") +
                (admin ? Loc.T("是", "yes") : Loc.T("否，请求提权并以管理员身份重新运行", "no, requesting elevation and relaunching as administrator")));
            if (!admin) return Elevate(args);

            if (Process.GetProcessesByName("Endfield").Length > 0)
            {
                Report(Loc.T("Endfield.exe 已在运行，请先关闭后再启动。",
                             "Endfield.exe is already running. Please close it first."));
                return 3;
            }
            Log(Loc.T("未检测到已运行的 Endfield.exe", "No running Endfield.exe detected"));

            ApplyRegistry(opts.RegistryValues);

            try { return Run(opts, root, launcherExe, gameExe, gameDir, cmdLine); }
            catch (Win32Exception ex)
            {
                Report(Loc.T("启动失败: ", "Launch failed: ") + ex.Message +
                       Loc.T(" (错误码 ", " (error code ") + ex.NativeErrorCode + ")");
                return ex.NativeErrorCode != 0 ? ex.NativeErrorCode : 1;
            }
        }

        static int Run(Options opts, string root, string launcherExe, string gameExe, string gameDir, string cmdLine)
        {
            var windowHider = new WindowHider(root, Log);
            windowHider.Start();
            hiding = true;
            var hider = new Thread(() =>
            {
                while (hiding)
                {
                    HideLauncherUi(root);
                    Thread.Sleep(100);
                }
            });
            hider.IsBackground = true;
            hider.Start();
            Log(Loc.T("隐藏已就绪：窗口事件钩子 + 每 100ms 轮询（隐藏窗口、注销托盘图标）",
                      "Hiding is ready: window event hook + polling every 100 ms (hide windows, unregister tray icons)"));

            Process parent = FindInInstall("Games", root);
            Log(parent != null
                ? Loc.T("已有 Games.exe 在运行 (PID " + parent.Id + ")，直接作为父进程",
                        "Games.exe is already running (PID " + parent.Id + "), using it as the parent")
                : Loc.T("未发现 Games.exe，启动 Launcher.exe 来唤起它",
                        "Games.exe not found, starting Launcher.exe to bring it up"));
            if (parent == null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = launcherExe,
                    Arguments = "--reason=0 --game=endfield",
                    WorkingDirectory = root,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = true,
                });
                parent = WaitFor(() => FindInInstall("Games", root), 10);
                Log(parent != null
                    ? Loc.T("Games.exe 已出现 (PID " + parent.Id + ")", "Games.exe appeared (PID " + parent.Id + ")")
                    : Loc.T("等待 10 秒后仍未出现 Games.exe", "Games.exe did not appear within 10 s"));
            }
            if (parent == null)
            {
                Report(Loc.T("未能通过 Launcher.exe 唤起 Games.exe。",
                             "Failed to bring up Games.exe through Launcher.exe."));
                return 1;
            }

            Log(Loc.T("以 Games.exe (PID " + parent.Id + ") 为父进程创建 Endfield.exe ...",
                      "Creating Endfield.exe with Games.exe (PID " + parent.Id + ") as the parent ..."));
            int gamePid = Native.CreateWithParent(gameExe, cmdLine, gameDir, parent.Id);
            Log(Loc.T("已启动 Endfield.exe (PID ", "Started Endfield.exe (PID ") + gamePid + ")");

            Process game = Process.GetProcessById(gamePid);
            double waitStart = clock.Elapsed.TotalSeconds;
            bool ready = WaitFor(() => game.HasExited || Native.HasVisibleWindow(gamePid) ? game : null,
                                 opts.WindowTimeoutSeconds) != null;
            if (game.HasExited)
            {
                Report(Loc.T("游戏进程已提前退出，退出码 ", "The game process exited early, exit code ") + game.ExitCode);
                Cleanup(root, parent);
                return 1;
            }
            Log((ready ? Loc.T("游戏窗口已就绪", "Game window ready")
                       : Loc.T("等待游戏窗口超时，继续收尾", "Timed out waiting for the game window, continuing")) +
                Loc.T("，窗口等待用时 ", ", window wait took ") +
                (clock.Elapsed.TotalSeconds - waitStart).ToString("0.00") + "s");

            Log(Loc.T("缓冲等待 " + opts.GraceSeconds + " 秒", "Grace period: " + opts.GraceSeconds + " s"));
            if (opts.GraceSeconds > 0) Thread.Sleep(opts.GraceSeconds * 1000);
            windowHider.Stop();
            hiding = false;

            if (opts.Hold)
            {
                Log(Loc.T("等待游戏退出后再关闭启动器...", "Waiting for the game to exit before closing the launcher..."));
                game.WaitForExit();
                Log(Loc.T("Endfield.exe 已退出，退出码 ", "Endfield.exe exited, exit code ") + game.ExitCode);
            }

            Cleanup(root, parent);
            Log(Loc.T("全部完成，总用时 ", "All done, total time ") + clock.Elapsed.TotalSeconds.ToString("0.00") + "s");
            return 0;
        }

        static string BuildCommandLine(string gameExe, Options opts)
        {
            var parts = new List<string> { Quote(gameExe) };
            foreach (var kv in opts.LauncherArgs) parts.Add(kv.Key + "=" + kv.Value);
            foreach (string a in opts.PassThrough) parts.Add(Quote(a));
            return string.Join(" ", parts.ToArray());
        }

        static string Quote(string s)
        {
            if (s.Length > 0 && s.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return s;
            string escaped = Regex.Replace(s, @"(\\*)""", @"$1$1\""");
            escaped = Regex.Replace(escaped, @"(\\+)$", @"$1$1");
            return "\"" + escaped + "\"";
        }

        static void ApplyRegistry(List<KeyValuePair<string, string>> values)
        {
            foreach (var kv in values)
            {
                int num;
                bool numeric = int.TryParse(kv.Value, out num);
                foreach (string path in RegistryPaths)
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path, true))
                    {
                        if (key == null)
                        {
                            Log(Loc.T("注册表项不存在，跳过: HKCU\\", "Registry key missing, skipped: HKCU\\") + path);
                            continue;
                        }
                        foreach (string name in key.GetValueNames())
                        {
                            if (!name.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)) continue;
                            if (numeric) key.SetValue(name, num, RegistryValueKind.DWord);
                            else key.SetValue(name, kv.Value, RegistryValueKind.String);
                            Log(Loc.T("已写入注册表: HKCU\\", "Wrote registry: HKCU\\") + path + "\\" + name + " = " + kv.Value +
                                (numeric ? " (DWORD)" : " (String)"));
                        }
                    }
                }
            }
        }

        static Process FindInInstall(string name, string root)
        {
            foreach (Process p in Process.GetProcessesByName(name))
            {
                string path = Native.GetImagePath(p.Id);
                if (path != null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }

        static void HideLauncherUi(string root)
        {
            var pids = new HashSet<int>();
            foreach (string name in new[] { "Games", "Launcher" })
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    string path = Native.GetImagePath(p.Id);
                    if (path == null || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    int hidden = Native.HideWindows(p.Id);
                    if (hidden > 0)
                        Log(Loc.T("已隐藏 " + name + ".exe (PID " + p.Id + ") 的窗口 " + hidden + " 个",
                                  "Hid " + hidden + " window(s) of " + name + ".exe (PID " + p.Id + ")"));
                    pids.Add(p.Id);
                }
            }
            if (pids.Count == 0) return;
            try
            {
                int removed = Native.RemoveTrayIcons(pids);
                if (removed > 0)
                    Log(Loc.T("已注销托盘图标 " + removed + " 个", "Unregistered " + removed + " tray icon(s)"));
            }
            catch (Exception ex) { Log(Loc.T("注销托盘图标出错: ", "Error unregistering tray icons: ") + ex.Message); }
        }

        static void Cleanup(string root, Process parent)
        {
            var targets = new List<Process>();
            var names = new Dictionary<int, string>();
            foreach (string name in new[] { "Games", "Launcher" })
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    string path = Native.GetImagePath(p.Id);
                    bool ours = p.Id == parent.Id || (path != null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase));
                    if (!ours) continue;
                    targets.Add(p);
                    names[p.Id] = name + ".exe";
                }
            }

            Log(Loc.T("开始收尾，需要关闭的进程 " + targets.Count + " 个",
                      "Cleaning up, " + targets.Count + " process(es) to close"));
            foreach (Process p in targets) Log("  - " + names[p.Id] + " (PID " + p.Id + ")");

            var pids = new HashSet<int>();
            foreach (Process p in targets) pids.Add(p.Id);
            try
            {
                int removed = Native.RemoveTrayIcons(pids);
                Log(Loc.T("收尾前注销托盘图标 " + removed + " 个", "Unregistered " + removed + " tray icon(s) before cleanup"));
            }
            catch (Exception ex) { Log(Loc.T("注销托盘图标出错: ", "Error unregistering tray icons: ") + ex.Message); }

            foreach (Process p in targets) Native.CloseWindows(p.Id);
            Log(Loc.T("已向这些进程的窗口发送关闭请求，最多等待 3 秒",
                      "Sent close requests to their windows, waiting up to 3 s"));

            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            var survivors = new List<Process>();
            foreach (Process p in targets)
            {
                try
                {
                    int left = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
                    if (p.WaitForExit(left))
                        Log(names[p.Id] + " (PID " + p.Id + ")" + Loc.T(" 已正常退出", " exited normally"));
                    else
                    {
                        survivors.Add(p);
                        Log(names[p.Id] + " (PID " + p.Id + ")" + Loc.T(" 超时未退出", " did not exit in time"));
                    }
                }
                catch (Exception) { }
            }

            foreach (Process p in survivors)
            {
                try
                {
                    p.Kill();
                    Log(Loc.T("已强制结束 ", "Force-killed ") + names[p.Id] + " (PID " + p.Id + ")");
                }
                catch (Exception ex)
                {
                    Log(Loc.T("强制结束 " + names[p.Id] + " 失败: ", "Failed to kill " + names[p.Id] + ": ") + ex.Message);
                }
            }
        }

        static int CreateShortcut(Options opts, string gameExe)
        {
            const string FileName = "Endfield Direct.lnk";
            string self = Process.GetCurrentProcess().MainModule.FileName;

            string lnk;
            if (string.IsNullOrEmpty(opts.ShortcutPath))
            {
                lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), FileName);
            }
            else
            {
                string full = Path.GetFullPath(opts.ShortcutPath);
                lnk = full.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? full : Path.Combine(full, FileName);
            }

            var quoted = new List<string>();
            foreach (string a in opts.ShortcutArgs) quoted.Add(Quote(a));
            string arguments = string.Join(" ", quoted.ToArray());

            Log(Loc.T("快捷方式位置: ", "Shortcut location: ") + lnk);
            Log(Loc.T("快捷方式目标: ", "Shortcut target: ") + self);
            Log(Loc.T("快捷方式参数: ", "Shortcut arguments: ") + (arguments.Length > 0 ? arguments : Loc.T("(无)", "(none)")));
            Log(Loc.T("快捷方式图标: ", "Shortcut icon: ") + gameExe + ",0");

            if (opts.DryRun)
            {
                Notify(Loc.T("将创建快捷方式: ", "Would create shortcut: ") + lnk +
                       (arguments.Length > 0 ? Loc.T("，参数: ", ", arguments: ") + arguments : ""));
                return 0;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lnk));

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) throw new InvalidOperationException("WScript.Shell is not available");
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic link = shell.CreateShortcut(lnk);
                link.TargetPath = self;
                link.Arguments = arguments;
                link.WorkingDirectory = Path.GetDirectoryName(self);
                link.IconLocation = gameExe + ",0";
                link.Description = Loc.T("直接启动《明日方舟：终末地》", "Launch Arknights: Endfield directly");
                link.Save();
            }
            catch (Exception ex)
            {
                Report(Loc.T("创建快捷方式失败: ", "Failed to create the shortcut: ") + ex.Message);
                return 1;
            }

            Notify(Loc.T("已创建快捷方式:\n", "Shortcut created:\n") + lnk);
            return 0;
        }

        static T WaitFor<T>(Func<T> probe, int timeoutSeconds) where T : class
        {
            DateTime end = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            do
            {
                T v = probe();
                if (v != null) return v;
                Thread.Sleep(100);
            } while (DateTime.UtcNow < end);
            return null;
        }

        static bool IsAdministrator()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        static int Elevate(string[] args)
        {
            var quoted = new List<string>();
            foreach (string a in args) quoted.Add(Quote(a));
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Process.GetCurrentProcess().MainModule.FileName,
                    Arguments = string.Join(" ", quoted.ToArray()),
                    Verb = "runas",
                    UseShellExecute = true,
                });
                return 0;
            }
            catch (Win32Exception)
            {
                Report(Loc.T("需要管理员权限，已取消。", "Administrator rights are required; cancelled."));
                return 1;
            }
        }
    }
}
