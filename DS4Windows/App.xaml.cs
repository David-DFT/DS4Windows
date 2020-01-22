using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using WPFLocalizeExtension.Engine;
using NLog;
using DS4Windows;
using DS4WinWPF.DS4Forms;

namespace DS4WinWPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    [System.Security.SuppressUnmanagedCodeSecurity]
    public partial class App : Application
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", EntryPoint = "FindWindow")]
        private static extern IntPtr FindWindow(string sClass, string sWindow);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = false)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        private Thread controlThread;
        public static ControlService RootHub;
        public static HttpClient RequestClient;
        private bool skipSave;
        private bool runShutdown;
        private bool exitApp;
        private Thread testThread;
        private bool ExitComThread = false;
        private const string SingleAppComEventName = "{a52b5b20-d9ee-4f32-8518-307fa14aa0c6}";
        private EventWaitHandle ThreadComEvent = null;
        private Timer CollectTimer;
        private static LoggerHolder logHolder;

        private MemoryMappedFile ipcClassNameMMF = null; // MemoryMappedFile for inter-process communication used to hold className of DS4Form window
        private MemoryMappedViewAccessor ipcClassNameMMA = null;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            runShutdown = true;
            skipSave = true;

            ArgumentParser parser = new ArgumentParser();
            parser.Parse(e.Args);
            CheckOptions(parser);

            if (exitApp)
            {
                return;
            }

            try
            {
                Process.GetCurrentProcess().PriorityClass =
                    ProcessPriorityClass.High;
            }
            catch { } // Ignore problems raising the priority.

            // Force Normal IO Priority
            IntPtr ioPrio = new IntPtr(2);
            Util.NtSetInformationProcess(Process.GetCurrentProcess().Handle,
                Util.PROCESS_INFORMATION_CLASS.ProcessIoPriority, ref ioPrio, 4);

            // Force Normal Page Priority
            IntPtr pagePrio = new IntPtr(5);
            Util.NtSetInformationProcess(Process.GetCurrentProcess().Handle,
                Util.PROCESS_INFORMATION_CLASS.ProcessPagePriority, ref pagePrio, 4);

            try
            {
                // another instance is already running if OpenExisting succeeds.
                ThreadComEvent = EventWaitHandle.OpenExisting(SingleAppComEventName,
                    System.Security.AccessControl.EventWaitHandleRights.Synchronize |
                    System.Security.AccessControl.EventWaitHandleRights.Modify);
                ThreadComEvent.Set();  // signal the other instance.
                ThreadComEvent.Close();
                Current.Shutdown();    // Quit temp instance
                return;
            }
            catch { /* don't care about errors */ }

            // Create the Event handle
            ThreadComEvent = new EventWaitHandle(false, EventResetMode.ManualReset, SingleAppComEventName);
            CreateTempWorkerThread();

            CreateControlService();

            Global.FindConfigLocation();
            bool firstRun = Global.FirstRun;
            if (firstRun)
            {
                SaveWhere savewh = new SaveWhere(false);
                savewh.ShowDialog();
            }

            Global.Load();
            if (!CreateConfDirSkeleton())
            {
                MessageBox.Show($"Cannot create config folder structure in {Global.AppDataPath}. Exiting",
                    "DS4Windows", MessageBoxButton.OK, MessageBoxImage.Error);
                Current.Shutdown(1);
            }

            logHolder = new LoggerHolder(RootHub);
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            Logger logger = logHolder.Logger;
            string version = Global.ExeVersion;
            logger.Info($"DS4Windows version {version}");
            //logger.Info("DS4Windows version 2.0");
            logger.Info("Logger created");

            //DS4Windows.Global.ProfilePath[0] = "mixed";
            //DS4Windows.Global.LoadProfile(0, false, rootHub, false, false);
            if (firstRun)
            {
                logger.Info("No config found. Creating default config");
                //Directory.CreateDirectory(DS4Windows.Global.appdatapath);
                AttemptSave();

                //Directory.CreateDirectory(DS4Windows.Global.appdatapath + @"\Profiles\");
                //Directory.CreateDirectory(DS4Windows.Global.appdatapath + @"\Macros\");
                Global.SaveProfile(0, "Default");
                Global.ProfilePath[0] = Global.OlderProfilePath[0] = "Default";
                /*DS4Windows.Global.ProfilePath[1] = DS4Windows.Global.OlderProfilePath[1] = "Default";
                DS4Windows.Global.ProfilePath[2] = DS4Windows.Global.OlderProfilePath[2] = "Default";
                DS4Windows.Global.ProfilePath[3] = DS4Windows.Global.OlderProfilePath[3] = "Default";
                */
                logger.Info("Default config created");
            }

            skipSave = false;

            if (!Global.LoadActions())
            {
                Global.CreateStdActions();
            }

            SetUICulture(Global.UseLang);
            Global.LoadLinkedProfiles();
            MainWindow window = new MainWindow(parser);
            MainWindow = window;
            window.Show();
            window.CheckMinStatus();
            HwndSource source = PresentationSource.FromVisual(window) as HwndSource;
            CreateIPCClassNameMMF(source.Handle);
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            //Console.WriteLine("App Crashed");
            //Console.WriteLine(e.Exception.StackTrace);
            Logger logger = logHolder.Logger;
            logger.Error($"App Crashed with message {e.Exception.Message}");
            logger.Error(e.Exception.ToString());
            LogManager.Flush();
            LogManager.Shutdown();
        }

        private bool CreateConfDirSkeleton()
        {
            bool result = true;
            try
            {
                Directory.CreateDirectory(Global.AppDataPath);
                Directory.CreateDirectory(Global.AppDataPath + @"\Profiles\");
                Directory.CreateDirectory(Global.AppDataPath + @"\Logs\");
                //Directory.CreateDirectory(DS4Windows.Global.appdatapath + @"\Macros\");
            }
            catch (UnauthorizedAccessException)
            {
                result = false;
            }


            return result;
        }

        private void AttemptSave()
        {
            if (!Global.Save()) //if can't write to file
            {
                if (MessageBox.Show("Cannot write at current location\nCopy Settings to appdata?", "DS4Windows",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    try
                    {
                        Directory.CreateDirectory(Global.AppDataPath);
                        File.Copy(Global.ExeDirectoryPath + "\\Profiles.xml", Global.AppDataPath + "\\Profiles.xml");
                        File.Copy(Global.ExeDirectoryPath + "\\Auto Profiles.xml", Global.AppDataPath + "\\Auto Profiles.xml");
                        Directory.CreateDirectory(Global.AppDataPath + "\\Profiles");
                        foreach (string s in Directory.GetFiles(Global.ExeDirectoryPath + "\\Profiles"))
                        {
                            File.Copy(s, Global.AppDataPath + "\\Profiles\\" + Path.GetFileName(s));
                        }
                    }
                    catch { }
                    MessageBox.Show("Copy complete, please relaunch DS4Windows and remove settings from Program Directory",
                        "DS4Windows");
                }
                else
                {
                    MessageBox.Show("DS4Windows cannot edit settings here, This will now close",
                        "DS4Windows");
                }

                Global.AppDataPath = null;
                skipSave = true;
                Current.Shutdown();
                return;
            }
        }

        private void CheckOptions(ArgumentParser parser)
        {
            if (parser.HasErrors)
            {
                runShutdown = false;
                exitApp = true;
                Current.Shutdown(1);
            }
            else if (parser.Driverinstall)
            {
                CreateBaseThread();
                WelcomeDialog dialog = new WelcomeDialog(true);
                dialog.ShowDialog();
                runShutdown = false;
                exitApp = true;
                Current.Shutdown();
            }
            else if (parser.ReenableDevice)
            {
                DS4Devices.ReEnableDevice(parser.DeviceInstanceId);
                runShutdown = false;
                exitApp = true;
                Current.Shutdown();
            }
            else if (parser.Runtask)
            {
                StartupMethods.LaunchOldTask();
                runShutdown = false;
                exitApp = true;
                Current.Shutdown();
            }
            else if (parser.Command)
            {
                IntPtr hWndDS4WindowsForm = FindWindow(ReadIPCClassNameMMF(), "DS4Windows");
                if (hWndDS4WindowsForm != IntPtr.Zero)
                {
                    COPYDATASTRUCT cds;
                    cds.lpData = IntPtr.Zero;

                    try
                    {
                        cds.dwData = IntPtr.Zero;
                        cds.cbData = parser.CommandArgs.Length;
                        cds.lpData = Marshal.StringToHGlobalAnsi(parser.CommandArgs);
                        SendMessage(hWndDS4WindowsForm, DS4Forms.MainWindow.WM_COPYDATA, IntPtr.Zero, ref cds);
                    }
                    finally
                    {
                        if (cds.lpData != IntPtr.Zero)
                            Marshal.FreeHGlobal(cds.lpData);
                    }
                }

                runShutdown = false;
                exitApp = true;
                Current.Shutdown();
            }
        }

        private void CreateControlService()
        {
            controlThread = new Thread(() =>
            {
                RootHub = new ControlService();
                Program.RootHub = RootHub;
                RequestClient = new HttpClient();
                CollectTimer = new Timer(GarbageTask, null, 30000, 30000);

            })
            {
                Priority = ThreadPriority.Normal,
                IsBackground = true
            };

            controlThread.Start();
            while (controlThread.IsAlive)
                Thread.SpinWait(500);
        }

        private void CreateBaseThread()
        {
            controlThread = new Thread(() =>
            {
                Program.RootHub = RootHub;
                RequestClient = new HttpClient();
                CollectTimer = new Timer(GarbageTask, null, 30000, 30000);
            })
            {
                Priority = ThreadPriority.Normal,
                IsBackground = true
            };

            controlThread.Start();
            while (controlThread.IsAlive)
                Thread.SpinWait(500);
        }

        private void GarbageTask(object state)
        {
            GC.Collect(0, GCCollectionMode.Forced, false);
        }

        private void CreateTempWorkerThread()
        {
            testThread = new Thread(SingleAppComThread_DoWork);
            testThread.Priority = ThreadPriority.Lowest;
            testThread.IsBackground = true;
            testThread.Start();
        }

        private void SingleAppComThread_DoWork()
        {
            while (!ExitComThread)
            {
                // check for a signal.
                if (ThreadComEvent.WaitOne())
                {
                    ThreadComEvent.Reset();
                    // The user tried to start another instance. We can't allow that,
                    // so bring the other instance back into view and enable that one.
                    // That form is created in another thread, so we need some thread sync magic.
                    if (!ExitComThread)
                    {
                        Dispatcher.BeginInvoke((Action)(() =>
                        {
                            MainWindow.Show();
                            MainWindow.WindowState = WindowState.Normal;
                        }));
                    }
                }
            }
        }

        public void CreateIPCClassNameMMF(IntPtr hWnd)
        {
            if (ipcClassNameMMA != null) 
                return; // Already holding a handle to MMF file. No need to re-write the data

            try
            {
                StringBuilder wndClassNameStr = new StringBuilder(128);
                if (GetClassName(hWnd, wndClassNameStr, wndClassNameStr.Capacity) != 0 && wndClassNameStr.Length > 0)
                {
                    byte[] buffer = Encoding.ASCII.GetBytes(wndClassNameStr.ToString());

                    ipcClassNameMMF = MemoryMappedFile.CreateNew("DS4Windows_IPCClassName.dat", 128);
                    ipcClassNameMMA = ipcClassNameMMF.CreateViewAccessor(0, buffer.Length);
                    ipcClassNameMMA.WriteArray(0, buffer, 0, buffer.Length);
                    // The MMF file is alive as long this process holds the file handle open
                }
            }
            catch (Exception)
            {
                /* Eat all exceptions because errors here are not fatal for DS4Win */
            }
        }

        private string ReadIPCClassNameMMF()
        {
            MemoryMappedFile mmf = null;
            MemoryMappedViewAccessor mma = null;

            try
            {
                byte[] buffer = new byte[128];
                mmf = MemoryMappedFile.OpenExisting("DS4Windows_IPCClassName.dat");
                mma = mmf.CreateViewAccessor(0, 128);
                mma.ReadArray(0, buffer, 0, buffer.Length);
                return Encoding.ASCII.GetString(buffer);
            }
            catch (Exception)
            {
                // Eat all exceptions
            }
            finally
            {
                mma?.Dispose();
                mmf?.Dispose();
            }

            return null;
        }

        private void SetUICulture(string culture)
        {
            try
            {
                //CultureInfo ci = new CultureInfo("ja");
                CultureInfo ci = CultureInfo.GetCultureInfo(culture);
                LocalizeDictionary.Instance.SetCurrentThreadCulture = true;
                LocalizeDictionary.Instance.Culture = ci;
                // fixes the culture in threads
                CultureInfo.DefaultThreadCurrentCulture = ci;
                CultureInfo.DefaultThreadCurrentUICulture = ci;
                //DS4WinWPF.Properties.Resources.Culture = ci;
                Thread.CurrentThread.CurrentCulture = ci;
                Thread.CurrentThread.CurrentUICulture = ci;
            }
            catch (CultureNotFoundException) { /* Skip setting culture that we cannot set */ }
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            Logger logger = logHolder.Logger;
            logger.Info("Request App Shutdown");
            CleanShutdown();
        }

        private void Application_SessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            Logger logger = logHolder.Logger;
            logger.Info("User Session Ending");
            CleanShutdown();
        }

        private void CleanShutdown()
        {
            if (runShutdown)
            {
                if (RootHub != null)
                    Task.Run(() => RootHub.Stop()).Wait();
                
                if (!skipSave)
                    Global.Save();
                
                ExitComThread = true;
                if (ThreadComEvent != null)
                {
                    ThreadComEvent.Set();  // signal the other instance.
                    while (testThread.IsAlive)
                        Thread.SpinWait(500);
                    ThreadComEvent.Close();
                }

                if (ipcClassNameMMA != null) ipcClassNameMMA.Dispose();
                if (ipcClassNameMMF != null) ipcClassNameMMF.Dispose();

                LogManager.Flush();
                LogManager.Shutdown();
            }
        }
    }
}
