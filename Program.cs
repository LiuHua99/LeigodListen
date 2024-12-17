using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LeiShen
{
    internal class Program
    {
        static string ConfigFile = "配置.ini";
        static LeiShengService authService;
        private static IntPtr selfHandle;
        static NotifyIcon notifyIcon;

        [STAThread]
        static void Main(string[] args)
        {
            ConfigFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFile);
            NotifyIconInit();
            // 设置关闭事件的回调处理函数
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            var isAutoRun = ConfigHelper.INIRead("配置", "开机启动", ConfigFile);
            SetAutoRun(isAutoRun == "1");
            FirstRun();
            CreateProcessStopListen();
            Application.Run();
        }

  

        private static void NotifyIconInit()
        {
            selfHandle = GetConsoleWindow();
            ShowWindow(selfHandle, SW_HIDE);

            //初始化 NotifyIcon
            notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application, // 这里可以替换为你自己的图标
                Visible = true,
                Text = "雷神加速器监听器"
            };

            // 创建右键菜单
            var contextMenu = new ContextMenu();
            contextMenu.MenuItems.Add(new MenuItem("退出程序", (s, e) => Exit()));
            notifyIcon.ContextMenu = contextMenu;
            // 订阅双击事件
            notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
        }

        private static void Exit()
        {
            throw new NotImplementedException();
        }

        private static void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            ShowWindow(selfHandle, SW_SHOW);
            notifyIcon.Visible = false;
        }

        private static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            var isAutoRun = ConfigHelper.INIRead("配置", "开机启动", ConfigFile);
            SetAutoRun(isAutoRun == "1");
        }

        private static void SetAutoRun(bool isAutoRun = true)
        {
            // 获取注册表键值
            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            string appName = "LeigodListen";
            if (isAutoRun)
            {
                string appPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.FriendlyName);
                // 添加开机自启动项
                key.SetValue(appName, appPath);
                ConsoleWriteLine("已设置开机自启动!");
            }
            else
            {
                var names = key.GetValueNames();
                if (names.Contains(appName))
                {
                    key.DeleteValue(appName);
                    ConsoleWriteLine("已关闭开机自启动!");
                }
            }
        }

        private static void CreateProcessStopListen()
        {
            ManagementEventWatcher StartedWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            StartedWatcher.EventArrived += new EventArrivedEventHandler(HandleProcessStart);
            StartedWatcher.Start();

            ManagementEventWatcher StopWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
            StopWatcher.EventArrived += new EventArrivedEventHandler(HandleProcessStop);
            StopWatcher.Start();
        }

        private static void HandleProcessStart(object sender, EventArrivedEventArgs e)
        {
            var pname = e.NewEvent["ProcessName"].ToString();
            if (pname == "leigod.exe")
            {
                var nlgodintpr = GetProcesses("leigod.exe");
                var nlgodpid = GetProcessId(nlgodintpr);

                if (nlgodpid == LeigodPID || nlgodintpr == LeigodIntPtr)
                    return;

                LeigodIntPtr = nlgodintpr;
                LeigodPID = nlgodpid;

                if (LeigodPID == 0)
                    return;

                ConsoleWriteLine($"监听到加速器启动! pid: {LeigodPID} Name： {pname}");
            }
        }


        public static void ConsoleWriteLine(string msg)
        {
            Console.WriteLine($"[{DateTime.Now}]: {msg}");
        }


        private static async void HandleProcessStop(object sender, EventArrivedEventArgs e)
        {
            if (LeigodIntPtr == IntPtr.Zero)
                return;
            var pname = e.NewEvent["ProcessName"].ToString();
            if (pname != "leigod.exe")
                return;

            var pid = (uint)(e.NewEvent["ProcessID"]);
            //WriteLine($"0 ProcessID:{pid} ProcessName:{pname}");
            if (LeigodPID == pid)
            {
                ConsoleWriteLine($"监听到加速器退出! pid: {pid}  Name：{pname}");

                while (!await StopLeigodTime())
                {
                    if (errorCount >= 3)
                    {
                        MessageBox.Show("出错次数超过3次!请检查网络或者配置!", "警告", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    errorCount++;
                    ConsoleWriteLine($"失败!正在进行第[{errorCount}]次尝试.");
                    Thread.Sleep(TimeSpan.FromSeconds(3));
                }

            }
        }


        private static async void FirstRun()
        {
            var user = ConfigHelper.INIRead("配置", "账号", ConfigFile);
            var pwd = ConfigHelper.INIRead("配置", "密码", ConfigFile);
            var token = ConfigHelper.INIRead("配置", "Token", ConfigFile);
            authService = new LeiShengService(token);
            var pausestatus = await authService.GetIsPause();
            if (pausestatus != 0 && pausestatus != 1 && pausestatus != LeiShengService.EXPERIENCE_END_TIME)
            {
                var loginmsg = await authService.LoginAsync(user, pwd);
                if (!loginmsg.Key)
                {
                    ConsoleWriteLine(loginmsg.Value);
                    ConsoleWriteLine("登陆失败");
                    Console.ReadKey();
                    Process.GetCurrentProcess().Kill();
                }
                else
                {
                    ConsoleWriteLine("账号密码登陆成功");
                    ConfigHelper.INIWrite("配置", "Token", loginmsg.Value, ConfigFile);
                }
            }
            else
            {
                ConsoleWriteLine("Token登陆成功");
            }
            ConsoleWriteLine("开始监听........");
            LeigodIntPtr = GetProcesses("leigod.exe");

            if (LeigodIntPtr == IntPtr.Zero && pausestatus == 0)
            {
                ConsoleWriteLine("首次启动监听加速器时间没有暂停,将执行暂停!");
                await StopLeigodTime();
            }

            if (LeigodIntPtr != IntPtr.Zero)
            {
                LeigodPID = GetProcessId(LeigodIntPtr);
                ConsoleWriteLine("首次启动监听到加速器正在运行!");
            }
            else
            {
                ConsoleWriteLine("首次启动没有监听到加速器启动,将在后台持续监听!");
            }
        }

        static int errorCount = 0;
        private static async Task<bool> StopLeigodTime()
        {
            //为了支持运行过程中直接改配置生效
            var user = ConfigHelper.INIRead("配置", "账号", ConfigFile);
            var pwd = ConfigHelper.INIRead("配置", "密码", ConfigFile);

            //如果雷神程序已经退出
            var resultIsPause = await authService.GetIsPause();
            switch (resultIsPause)
            {
                case -1://登录过期
                    {
                        ConsoleWriteLine("登录过期!尝试重新登录");
                        var loginResult = await authService.LoginAsync(user, pwd);
                        if (!loginResult.Key)
                        {
                            ConsoleWriteLine(loginResult.Value);
                        }
                        ConsoleWriteLine("重新登录成功");
                    }
                    break;
                case 0://未暂停
                    {
                        ConsoleWriteLine("执行停止功能");
                        var pauseResult = await authService.PauseAsync();
                        return pauseResult != LeiShengService.HTTP_TOKEN_EXPIRE;
                    }
                    break;
                case 1://已经暂停
                    {

                    }
                    return true;
                case 404:
                    {
                        //没网
                        ConsoleWriteLine("无网络连接!");
                    }
                    break;
                case LeiShengService.EXPERIENCE_END_TIME://无法暂停的体验时间
                    {
                        var experienceEndTime = authService.GetExperienceEndTime();
                        var timespan = experienceEndTime - DateTime.Now;
                        ConsoleWriteLine("当前为无法暂停的体验时间时间,直到:" + experienceEndTime);
                        Thread.Sleep(timespan); //暂停检测直到体验时间结束

                    }
                    return true;
                default: //累计未知错误次数
                    break;
            }

            return false;
        }

        public static bool IsProcessRunning(IntPtr processHandle)
        {
            // 获取与句柄相关联的进程 ID
            int processId = GetProcessId(processHandle);

            try
            {
                Process process = Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                return false;
            }
            // 如果 PID 大于 0，则该进程正在运行
            return true;
        }

        public static IntPtr LeigodIntPtr = IntPtr.Zero;
        public static int LeigodPID = 0;

        public static IntPtr GetProcesses(string exePath)
        {
            List<uint> listprocesses = new List<uint>();
            // 遍历进程
            uint[] processes = new uint[2048];
            uint bytesNeeded;

            // 获取所有进程 ID
            EnumProcesses(processes, (uint)(processes.Length * sizeof(uint)), out bytesNeeded);
            if (bytesNeeded > 2048)
            {
                processes = new uint[bytesNeeded];
                EnumProcesses(processes, (uint)(processes.Length * sizeof(uint)), out bytesNeeded);
            }
            listprocesses.AddRange(processes);
            listprocesses.RemoveRange((int)bytesNeeded, (int)(listprocesses.Count - bytesNeeded));

            for (int i = 0; i < listprocesses.Count; i++)
            {
                IntPtr hProcess = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, (int)listprocesses[i]);
                if (hProcess != IntPtr.Zero)
                {
                    System.Text.StringBuilder filePath = new System.Text.StringBuilder(1024);
                    if (GetModuleFileNameEx(hProcess, IntPtr.Zero, filePath, filePath.Capacity))
                    {
                        string processPath = filePath.ToString();
                        var tempname = processPath.Substring(processPath.Length - exePath.Length);
                        // 比较路径（使用 StringComparison.OrdinalIgnoreCase 忽略大小写）
                        if (tempname == exePath)
                        {
                            //ConsoleWriteLine($"找到进程: {Path.GetFileName(processPath)}  PID: {processes[i]}");
                            return hProcess;
                        }
                    }

                }
            }

            return IntPtr.Zero;
        }


        // 定义 Windows API 函数
        // 定义委托，用于处理控制台关闭事件
        private delegate bool ConsoleEventHandler(int eventType);
        private static ConsoleEventHandler _handler; // 保持委托的引用，避免被垃圾回收

        // 引入 Windows API
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(ConsoleEventHandler callback, bool add);

        // 关闭事件类型
        private const int CTRL_CLOSE_EVENT = 2;
        // 导入 User32.dll
        [DllImport("user32.dll")]
        private static extern int ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        const int SW_HIDE = 0;
        const int SW_SHOW = 5;


        [DllImport("kernel32.dll")]
        private static extern int GetProcessId(IntPtr hProcess);

        [DllImport("psapi.dll")]
        private static extern bool EnumProcesses(uint[] lpidProcess, uint cb, out uint lpcbNeeded);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("psapi.dll")]
        private static extern bool GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, System.Text.StringBuilder lpBaseName, int nSize);

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_READ = 0x0010;
    }
}
