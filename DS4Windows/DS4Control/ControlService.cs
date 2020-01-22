using Nefarius.ViGEm.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static DS4Windows.Global;

namespace DS4Windows
{
    public class X360Data
    {
        public byte[] Report = new byte[28];
        public byte[] Rumble = new byte[8];
    }
    public class DS4
    {
        public DS4()
        {
            ExposedState = new DS4StateExposed(CurrentState);
        }

        public bool TouchReleased { get; set; } = true;
        public bool TouchSlid { get; set; } = false;
        public Mouse TouchPad { get; set; }

        public DS4Device Device { get; set; }
        public OutputDevice Output { get; set; }

        public X360Data ProcessingData { get; } = new X360Data();
        public byte[] UdpOutBuffer { get; } = new byte[100];

        public DS4State MappedState { get; } = new DS4State();
        public DS4State CurrentState { get; } = new DS4State();
        public DS4State PreviousState { get; } = new DS4State();
        public DS4State TempState { get; } = new DS4State();
        public DS4StateExposed ExposedState { get; }

        public void EnableTouchPad()
        {
            Device.Touchpad.TouchButtonDown += TouchPad.TouchButtonDown;
            Device.Touchpad.TouchButtonUp += TouchPad.TouchButtonUp;
            Device.Touchpad.TouchesBegan += TouchPad.TouchesBegan;
            Device.Touchpad.TouchesMoved += TouchPad.TouchesMoved;
            Device.Touchpad.TouchesEnded += TouchPad.TouchesEnded;
            Device.Touchpad.TouchUnchanged += TouchPad.TouchUnchanged;
            Device.Touchpad.PreTouchProcess += OnPreTouchProcess;
            Device.SixAxis.SixAccelMoved += TouchPad.SixAxisMoved;
        }
        public void DisableTouchPad()
        {
            Device.Touchpad.TouchButtonDown -= TouchPad.TouchButtonDown;
            Device.Touchpad.TouchButtonUp -= TouchPad.TouchButtonUp;
            Device.Touchpad.TouchesBegan -= TouchPad.TouchesBegan;
            Device.Touchpad.TouchesMoved -= TouchPad.TouchesMoved;
            Device.Touchpad.TouchesEnded -= TouchPad.TouchesEnded;
            Device.Touchpad.TouchUnchanged -= TouchPad.TouchUnchanged;
            Device.Touchpad.PreTouchProcess -= OnPreTouchProcess;
            Device.SixAxis.SixAccelMoved -= TouchPad.SixAxisMoved;
        }

        private void OnPreTouchProcess(DS4Touchpad sender, EventArgs args)
            => TouchPad.PopulatePriorButtonStates();
    }
    public class ControlService
    {
        public const int DS4_CONTROLLER_COUNT = 4;

        private ViGEmClient viGEmTestClient = null;

        public DS4[] Controllers { get; } = new DS4[DS4_CONTROLLER_COUNT];

        public DS4Device GetDevice(int i)
        {
            if (i >= 0 && i < Controllers.Length)
                return Controllers[i].Device;
            return null;
        }

        public ControllerSlotManager SlotManager { get; } = new ControllerSlotManager();
        public bool Running { get; private set; } = false;
        public bool RecordMacro { get; set; } = false;

        public event EventHandler<DebugEventArgs> Debug = null;

        Thread tempThread;
        Thread tempBusThread;

        public List<string> affectedDevs = new List<string>()
        {
            @"HID\VID_054C&PID_05C4",
            @"HID\VID_054C&PID_09CC&MI_03",
            @"HID\VID_054C&PID_0BA0&MI_03",
            @"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&05c4",
            @"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&09cc",
        };

        public bool suspending;
        private UdpServer _udpServer;
        private OutputSlotManager OutputSlotManager { get; }

        public event EventHandler ServiceStarted;
        public event EventHandler PreServiceStop;
        public event EventHandler ServiceStopped;
        public event EventHandler RunningChanged;
        //public event EventHandler HotplugFinished;
        public delegate void HotplugControllerHandler(ControlService sender, DS4Device device, int index);
        public event HotplugControllerHandler HotplugController;

        private object busThrLck = new object();
        private bool busThrRunning = false;
        private Queue<Action> busEvtQueue = new Queue<Action>();
        private object busEvtQueueLock = new object();

        public ControlService()
        {
            Crc32Algorithm.InitializeTable(DS4Device.DefaultPolynomial);

            //sp.Stream = DS4WinWPF.Properties.Resources.EE;
            // Cause thread affinity to not be tied to main GUI thread
            tempBusThread = new Thread(() =>
            {
                //_udpServer = new UdpServer(GetPadDetailForIdx);
                busThrRunning = true;

                while (busThrRunning)
                {
                    lock (busEvtQueueLock)
                    {
                        Action tempAct = null;
                        for (int actInd = 0, actLen = busEvtQueue.Count; actInd < actLen; actInd++)
                        {
                            tempAct = busEvtQueue.Dequeue();
                            tempAct.Invoke();
                        }
                    }

                    lock (busThrLck)
                        Monitor.Wait(busThrLck);
                }
            })
            {
                Priority = ThreadPriority.Normal,
                IsBackground = true
            };
            tempBusThread.Start();
            //while (_udpServer == null)
            //{
            //    Thread.SpinWait(500);
            //}

            if (IsHidGuardianInstalled)
                StartHidGuardian();

            for (int i = 0; i < Controllers.Length; ++i)
                Controllers[i] = new DS4();

            OutputSlotManager = new OutputSlotManager();
        }

        private static void StartHidGuardian()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(ExeDirectoryPath + "\\HidGuardHelper.exe")
            {
                Verb = "runas",
                Arguments = Process.GetCurrentProcess().Id.ToString(),
                WorkingDirectory = ExeDirectoryPath
            };

            try
            {
                Process tempProc = Process.Start(startInfo);
                tempProc.Dispose();
            }
            catch { }
        }

        private void GetPadDetailForIdx(int padIdx, ref DualShockPadMeta meta)
        {
            //meta = new DualShockPadMeta();
            meta.PadId = (byte)padIdx;
            meta.Model = DsModel.DS4;

            var d = GetDevice(padIdx);
            if (d is null)
            {
                meta.PadMacAddress = null;
                meta.PadState = DsState.Disconnected;
                meta.ConnectionType = DsConnection.None;
                meta.Model = DsModel.None;
                meta.BatteryStatus = 0;
                meta.IsActive = false;
                return;
                //return meta;
            }

            bool isValidSerial = false;
            string stringMac = d.MacAddress;
            if (!string.IsNullOrEmpty(stringMac))
            {
                stringMac = string.Join("", stringMac.Split(':'));
                //stringMac = stringMac.Replace(":", "").Trim();
                meta.PadMacAddress = System.Net.NetworkInformation.PhysicalAddress.Parse(stringMac);
                isValidSerial = d.IsValidSerial();
            }

            if (!isValidSerial)
            {
                //meta.PadMacAddress = null;
                meta.PadState = DsState.Disconnected;
            }
            else
            {
                if (d.IsSynced || d.IsAlive)
                    meta.PadState = DsState.Connected;
                else
                    meta.PadState = DsState.Reserved;
            }

            meta.ConnectionType = (d.ConnectionType == ConnectionType.USB) ? DsConnection.Usb : DsConnection.Bluetooth;
            meta.IsActive = !d.IsDS4Idle();

            if (d.IsCharging && d.Battery >= 100)
                meta.BatteryStatus = DsBattery.Charged;
            else
            {
                if (d.Battery >= 95)
                    meta.BatteryStatus = DsBattery.Full;
                else if (d.Battery >= 70)
                    meta.BatteryStatus = DsBattery.High;
                else if (d.Battery >= 50)
                    meta.BatteryStatus = DsBattery.Medium;
                else if (d.Battery >= 20)
                    meta.BatteryStatus = DsBattery.Low;
                else if (d.Battery >= 5)
                    meta.BatteryStatus = DsBattery.Dying;
                else
                    meta.BatteryStatus = DsBattery.None;
            }

            //return meta;
        }

        private void TestQueueBus(Action temp)
        {
            lock (busEvtQueueLock)
            {
                busEvtQueue.Enqueue(temp);
            }

            lock (busThrLck)
                Monitor.Pulse(busThrLck);
        }

        public void ChangeUDPStatus(bool state, bool openPort=true)
        {
            if (state && _udpServer == null)
            {
                udpChangeStatus = true;
                TestQueueBus(() =>
                {
                    _udpServer = new UdpServer(GetPadDetailForIdx);
                    if (openPort)
                    {
                        // Change thread affinity of object to have normal priority
                        Task.Run(() =>
                        {
                            var UDP_SERVER_PORT = getUDPServerPortNum();
                            var UDP_SERVER_LISTEN_ADDRESS = getUDPServerListenAddress();

                            try
                            {
                                _udpServer.Start(UDP_SERVER_PORT, UDP_SERVER_LISTEN_ADDRESS);
                                LogDebug($"UDP server listening on address {UDP_SERVER_LISTEN_ADDRESS} port {UDP_SERVER_PORT}");
                            }
                            catch (System.Net.Sockets.SocketException ex)
                            {
                                var errMsg = string.Format("Couldn't start UDP server on address {0}:{1}, outside applications won't be able to access pad data ({2})", UDP_SERVER_LISTEN_ADDRESS, UDP_SERVER_PORT, ex.SocketErrorCode);

                                LogDebug(errMsg, true);
                                AppLogger.LogToTray(errMsg, true, true);
                            }
                        }).Wait();
                    }

                    udpChangeStatus = false;
                });
            }
            else if (!state && _udpServer != null)
            {
                TestQueueBus(() =>
                {
                    udpChangeStatus = true;
                    _udpServer.Stop();
                    _udpServer = null;
                    AppLogger.LogToGui("Closed UDP server", false);
                    udpChangeStatus = false;
                });
            }
        }

        public void ChangeMotionEventStatus(bool state)
        {
            IEnumerable<DS4Device> devices = DS4Devices.GetDS4Controllers();
            if (state)
            {
                foreach (DS4Device dev in devices)
                {
                    dev.QueueEvent(() =>
                    {
                        dev.Report += dev.MotionEvent;
                    });
                }
            }
            else
            {
                foreach (DS4Device dev in devices)
                {
                    dev.QueueEvent(() =>
                    {
                        dev.Report -= dev.MotionEvent;
                    });
                }
            }
        }

        private bool udpChangeStatus = false;
        public bool changingUDPPort = false;
        public async void UseUDPPort()
        {
            changingUDPPort = true;
            IEnumerable<DS4Device> devices = DS4Devices.GetDS4Controllers();
            foreach (DS4Device dev in devices)
            {
                dev.QueueEvent(() =>
                {
                    dev.Report -= dev.MotionEvent;
                });
            }

            await Task.Delay(100);

            var UDP_SERVER_PORT = getUDPServerPortNum();
            var UDP_SERVER_LISTEN_ADDRESS = getUDPServerListenAddress();

            try
            {
                _udpServer.Start(UDP_SERVER_PORT, UDP_SERVER_LISTEN_ADDRESS);
                foreach (DS4Device dev in devices)
                {
                    dev.QueueEvent(() =>
                    {
                        dev.Report += dev.MotionEvent;
                    });
                }
                LogDebug($"UDP server listening on address {UDP_SERVER_LISTEN_ADDRESS} port {UDP_SERVER_PORT}");
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                var errMsg = string.Format("Couldn't start UDP server on address {0}:{1}, outside applications won't be able to access pad data ({2})", UDP_SERVER_LISTEN_ADDRESS, UDP_SERVER_PORT, ex.SocketErrorCode);

                LogDebug(errMsg, true);
                AppLogger.LogToTray(errMsg, true, true);
            }

            changingUDPPort = false;
        }

        private void WarnExclusiveModeFailure(DS4Device device)
        {
            if (!DS4Devices.IsExclusiveMode || device.IsExclusive)
                return;
            
            string message = $"{DS4WinWPF.Properties.Resources.CouldNotOpenDS4.Replace("*Mac address*", device.MacAddress)} {DS4WinWPF.Properties.Resources.QuitOtherPrograms}";
            LogDebug(message, true);
            AppLogger.LogToTray(message, true);
        }

        private void StartViGEm()
        {
            tempThread = new Thread(() => { try { viGEmTestClient = new ViGEmClient(); } catch { } });
            tempThread.Priority = ThreadPriority.AboveNormal;
            tempThread.IsBackground = true;
            tempThread.Start();
            while (tempThread.IsAlive)
            {
                Thread.SpinWait(500);
            }

            tempThread = null;
        }

        private void stopViGEm()
        {
            if (viGEmTestClient != null)
            {
                viGEmTestClient.Dispose();
                viGEmTestClient = null;
            }
        }

        public void PluginOutDev(int index, DS4Device device)
        {
            OutControllerType contType = Global.OutContType[index];
            if (UseDInputOnly[index])
            {
                switch (contType)
                {
                    case OutControllerType.X360:
                        PlugInX360(index, device);
                        break;
                    case OutControllerType.DS4:
                        PlugInDS4(index, device);
                        break;
                }
            }

            UseDInputOnly[index] = false;
        }

        private void PlugInDS4(int index, DS4Device device)
        {
            LogDebug("Plugging in DS4 Controller for input #" + (index + 1));
            ActiveOutDevType[index] = OutControllerType.DS4;
            //DS4OutDevice tempDS4 = new DS4OutDevice(vigemTestClient);
            DS4OutDevice tempDS4 = OutputSlotManager.AllocateController(OutControllerType.DS4, viGEmTestClient) as DS4OutDevice;
            //outputDevices[index] = tempDS4;
            int devIndex = index;

            //TODO: fix memory leak
            tempDS4.Controller.FeedbackReceived += (sender, args)
                => SetDevRumble(device, args.LargeMotor, args.SmallMotor, devIndex);

            OutputSlotManager.DeferredPlugin(tempDS4, Controllers[index]);
            //tempDS4.Connect();
            //LogDebug("DS4 Controller #" + (index + 1) + " connected");
        }

        private void PlugInX360(int index, DS4Device device)
        {
            LogDebug($"Plugging in X360 Controller for input #{index + 1}");

            ActiveOutDevType[index] = OutControllerType.X360;

            Xbox360OutDevice tempXbox = OutputSlotManager.AllocateController(OutControllerType.X360, viGEmTestClient) as Xbox360OutDevice;
            int devIndex = index;

            //TODO: fix memory leak
            tempXbox.Controller.FeedbackReceived += (sender, args)
                => SetDevRumble(device, args.LargeMotor, args.SmallMotor, devIndex);

            OutputSlotManager.DeferredPlugin(tempXbox, Controllers[index]);
        }

        public void UnplugOutDev(int index, DS4Device device, bool immediate = false)
        {
            if (!UseDInputOnly[index])
            {
                OutputDevice dev = Controllers[index].Output;

                LogDebug($"Unplugging {dev.GetDeviceType()} Controller for input #{index + 1}", false);

                Controllers[index].Output = null;
                ActiveOutDevType[index] = OutControllerType.None;
                OutputSlotManager.DeferredRemoval(dev, Controllers[index], immediate);
                UseDInputOnly[index] = true;
            }
        }

        public bool Start(bool showLog = true)
        {
            StartViGEm();
            if (viGEmTestClient != null)
            //if (x360Bus.Open() && x360Bus.Start())
            {
                if (showLog)
                    LogDebug(DS4WinWPF.Properties.Resources.Starting);

                LogDebug($"Connection to ViGEmBus {ViGEmBusVersion} established");

                DS4Devices.IsExclusiveMode = GetUseExclusiveMode();
                //uiContext = tempui as SynchronizationContext;
                if (showLog)
                {
                    LogDebug(DS4WinWPF.Properties.Resources.SearchingController);
                    LogDebug(DS4Devices.IsExclusiveMode ? DS4WinWPF.Properties.Resources.UsingExclusive : DS4WinWPF.Properties.Resources.UsingShared);
                }

                if (isUsingUDPServer() && _udpServer == null)
                {
                    ChangeUDPStatus(true, false);
                    while (udpChangeStatus == true)
                    {
                        Thread.SpinWait(500);
                    }
                }

                try
                {
                    DS4Devices.FindControllers();
                    IEnumerable<DS4Device> devices = DS4Devices.GetDS4Controllers();

                    //int ind = 0;
                    DS4LightBar.DefaultLight = false;
                    //foreach (DS4Device device in devices)

                    //for (int i = 0, devCount = devices.Count(); i < devCount; i++)
                    int i = 0;
                    for (var devEnum = devices.GetEnumerator(); devEnum.MoveNext(); i++)
                    {
                        DS4Device device = devEnum.Current;
                        if (showLog)
                            LogDebug($"{DS4WinWPF.Properties.Resources.FoundController} {device.MacAddress} ({device.ConnectionType}) ({device.DisplayName})");

                        Task task = new Task(() => { Thread.Sleep(5); WarnExclusiveModeFailure(device); });
                        task.Start();

                        Controllers[i].Device = device;
                        Controllers[i].TouchPad = new Mouse(i, device);

                        SlotManager.AddController(device, i);
                        LinkDevice(device);

                        if (!UseTempProfile[i])
                        {
                            if (device.IsValidSerial() && ContainsLinkedProfile(device.MacAddress))
                            {
                                ProfilePath[i] = GetLinkedProfile(device.MacAddress);
                                LinkedProfileCheck[i] = true;
                            }
                            else
                            {
                                ProfilePath[i] = OlderProfilePath[i];
                                LinkedProfileCheck[i] = false;
                            }

                            LoadProfile(i, false, this, false, false);
                        }

                        device.LightBarColor = GetMainColor(i);

                        if (!GetDInputOnly(i) && device.IsSynced)
                        {
                            //useDInputOnly[i] = false;
                            PluginOutDev(i, device);
                            
                        }
                        else
                        {
                            UseDInputOnly[i] = true;
                            ActiveOutDevType[i] = OutControllerType.None;
                        }

                        int tempIdx = i;

                        //TODO: fix memory leak
                        void OnReportLocal(DS4Device sender, EventArgs e) => OnReport(sender, e, tempIdx);
                        device.Report += OnReportLocal;

                        void tempEvnt(DS4Device sender, EventArgs args)
                        {
                            DualShockPadMeta padDetail = new DualShockPadMeta();
                            GetPadDetailForIdx(tempIdx, ref padDetail);
                            _udpServer.NewReportIncoming(ref padDetail, Controllers[tempIdx].CurrentState, Controllers[tempIdx].UdpOutBuffer);
                        }
                        device.MotionEvent = tempEvnt;

                        if (_udpServer != null)
                        {
                            device.Report += tempEvnt;
                        }

                        Controllers[i].EnableTouchPad();

                        CheckProfileOptions(i, device, true);
                        device.StartUpdate();
                        //string filename = ProfilePath[ind];
                        //ind++;

                        if (showLog)
                            LogUsingProfile(i);

                        if (i >= 4) // out of Xinput devices!
                            break;
                    }
                }
                catch (Exception e)
                {
                    LogDebug(e.Message);
                    AppLogger.LogToTray(e.Message);
                }

                Running = true;

                if (_udpServer != null)
                {
                    //var UDP_SERVER_PORT = 26760;
                    var UDP_SERVER_PORT = getUDPServerPortNum();
                    var UDP_SERVER_LISTEN_ADDRESS = getUDPServerListenAddress();

                    try
                    {
                        _udpServer.Start(UDP_SERVER_PORT, UDP_SERVER_LISTEN_ADDRESS);
                        LogDebug($"UDP server listening on address {UDP_SERVER_LISTEN_ADDRESS} port {UDP_SERVER_PORT}");
                    }
                    catch (System.Net.Sockets.SocketException ex)
                    {
                        var errMsg = string.Format("Couldn't start UDP server on address {0}:{1}, outside applications won't be able to access pad data ({2})", UDP_SERVER_LISTEN_ADDRESS, UDP_SERVER_PORT, ex.SocketErrorCode);

                        LogDebug(errMsg, true);
                        AppLogger.LogToTray(errMsg, true, true);
                    }
                }
            }
            else
            {
                string logMessage = string.Empty;
                if (!IsViGEmInstalled)
                {
                    logMessage = "ViGEmBus is not installed";
                }
                else
                {
                    logMessage = "Could not connect to ViGEmBus. Please check the status of the System device in Device Manager and if Visual C++ 2017 Redistributable is installed.";
                }

                LogDebug(logMessage);
                AppLogger.LogToTray(logMessage);
            }

            RunHotPlug = true;
            ServiceStarted?.Invoke(this, EventArgs.Empty);
            RunningChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private void CheckQuickCharge(object sender, EventArgs e)
        {
            DS4Device device = sender as DS4Device;
            if (device.IsBT && QuickCharge && device.IsCharging)
                device.DisconnectBT();
        }

        public bool Stop(bool showlog = true)
        {
            if (Running)
            {
                Running = false;
                RunHotPlug = false;
                PreServiceStop?.Invoke(this, EventArgs.Empty);

                if (showlog)
                    LogDebug(DS4WinWPF.Properties.Resources.StoppingX360);

                LogDebug("Closing connection to ViGEmBus");

                for (int i = 0, arlength = Controllers.Length; i < arlength; i++)
                {
                    DS4Device tempDevice = Controllers[i].Device;
                    if (tempDevice != null)
                    {
                        if ((DCBTatStop && !tempDevice.IsCharging) || suspending)
                        {
                            tempDevice.StopUpdate();
                            switch (tempDevice.ConnectionType)
                            {
                                case ConnectionType.BT:
                                    tempDevice.DisconnectBT(true);
                                    break;
                                case ConnectionType.SONYWA:
                                    tempDevice.DisconnectDongle(true);
                                    break;
                            }
                        }
                        else
                        {
                            DS4LightBar.ForceLight[i] = false;
                            DS4LightBar.ForcedFlash[i] = 0;
                            DS4LightBar.DefaultLight = true;
                            DS4LightBar.UpdateLightBar(tempDevice, i);
                            tempDevice.IsRemoved = true;
                            tempDevice.StopUpdate();
                            DS4Devices.RemoveDevice(tempDevice);
                            Thread.Sleep(50);
                        }

                        Controllers[i].CurrentState.Battery = Controllers[i].PreviousState.Battery = 0; // Reset for the next connection's initial status change.
                        OutputDevice tempout = Controllers[i].Output;
                        if (tempout != null)
                        {
                            UnplugOutDev(i, tempDevice, true);
                        }

                        //outputDevices[i] = null;
                        //useDInputOnly[i] = true;
                        //Global.activeOutDevType[i] = OutContType.None;
                        Controllers[i].Device = null;
                        Controllers[i].TouchPad = null;
                        lag[i] = false;
                        inWarnMonitor[i] = false;
                    }
                }

                if (showlog)
                    LogDebug(DS4WinWPF.Properties.Resources.StoppingDS4);

                DS4Devices.StopControllers();
                SlotManager.ClearControllerList();

                if (_udpServer != null)
                    ChangeUDPStatus(false);
                    //_udpServer.Stop();

                if (showlog)
                    LogDebug(DS4WinWPF.Properties.Resources.StoppedDS4Windows);

                while (OutputSlotManager.RunningQueue)
                {
                    Thread.SpinWait(500);
                }

                stopViGEm();
            }

            RunHotPlug = false;
            ServiceStopped?.Invoke(this, EventArgs.Empty);
            RunningChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private bool PhysicalDeviceExists(DS4Device device)
        {
            for (int i = 0, arlength = Controllers.Length; i < arlength; i++)
                if (Controllers[i].Device != null && Controllers[i].Device.MacAddress == device.MacAddress)
                    return true;
            
            return false;
        }

        public bool HotPlug()
        {
            if (Running)
            {
                DS4Devices.FindControllers();
                IEnumerable<DS4Device> devices = DS4Devices.GetDS4Controllers();

                foreach (DS4Device device in devices)
                {
                    if (device.IsDisconnecting || PhysicalDeviceExists(device))
                        continue;
                    
                    //Search for open controller slot
                    for (int i = 0, arlength = Controllers.Length; i < arlength; i++)
                    {
                        DS4 ds4 = Controllers[i];
                        if (ds4.Device != null)
                            continue;

                        LogDebug($"{DS4WinWPF.Properties.Resources.FoundController} {device.MacAddress} ({device.ConnectionType}) ({device.DisplayName})");

                        Task task = new Task(() => 
                        {
                            Thread.Sleep(5);
                            WarnExclusiveModeFailure(device); 
                        });
                        task.Start();

                        ds4.Device = device;
                        ds4.TouchPad = new Mouse(i, device);

                        SlotManager.AddController(device, i);
                        LinkDevice(device);

                        if (!UseTempProfile[i])
                        {
                            if (device.IsValidSerial() && ContainsLinkedProfile(device.MacAddress))
                            {
                                ProfilePath[i] = GetLinkedProfile(device.MacAddress);
                                LinkedProfileCheck[i] = true;
                            }
                            else
                            {
                                ProfilePath[i] = OlderProfilePath[i];
                                LinkedProfileCheck[i] = false;
                            }

                            LoadProfile(i, false, this, false, false);
                        }

                        device.LightBarColor = GetMainColor(i);

                        int tempIdx = i;

                        //TODO: fix memory leak
                        device.Report += (sender, e) => OnReport(sender, e, tempIdx);

                        void tempEvnt(DS4Device sender, EventArgs args)
                        {
                            DualShockPadMeta padDetail = new DualShockPadMeta();
                            GetPadDetailForIdx(tempIdx, ref padDetail);
                            _udpServer.NewReportIncoming(ref padDetail, Controllers[tempIdx].CurrentState, Controllers[tempIdx].UdpOutBuffer);
                        }
                        device.MotionEvent = tempEvnt;

                        if (_udpServer != null)
                        {
                            device.Report += tempEvnt;
                        }

                        if (!GetDInputOnly(i) && device.IsSynced)
                        {
                            //useDInputOnly[Index] = false;
                            PluginOutDev(i, device);
                        }
                        else
                        {
                            UseDInputOnly[i] = true;
                            ActiveOutDevType[i] = OutControllerType.None;
                        }

                        ds4.EnableTouchPad();

                        CheckProfileOptions(i, device);
                        device.StartUpdate();

                        LogUsingProfile(i);

                        HotplugController?.Invoke(this, device, i);

                        break;
                    }
                }
            }

            return true;
        }

        private void LogUsingProfile(int profileIndex)
        {
            string profileLog;

            if (File.Exists(AppDataPath + "\\Profiles\\" + ProfilePath[profileIndex] + ".xml"))
                profileLog = DS4WinWPF.Properties.Resources.UsingProfile.Replace("*number*", (profileIndex + 1).ToString()).Replace("*Profile name*", ProfilePath[profileIndex]);
            else
                profileLog = DS4WinWPF.Properties.Resources.NotUsingProfile.Replace("*number*", (profileIndex + 1).ToString());
            
            LogDebug(profileLog);
            AppLogger.LogToTray(profileLog);
        }

        private void LinkDevice(DS4Device device)
        {
            if (device is null)
                return;

            device.Removal += OnDS4Removal;
            device.Removal += DS4Devices.OnRemoval;
            device.SyncChange += OnSyncChange;
            device.SyncChange += DS4Devices.UpdateSerial;
            device.SerialChange += OnSerialChange;
            device.ChargingChanged += CheckQuickCharge;
        }
        private void UnlinkDevice(DS4Device device)
        {
            if (device is null)
                return;

            device.Removal -= OnDS4Removal;
            device.Removal -= DS4Devices.OnRemoval;
            device.SyncChange -= OnSyncChange;
            device.SyncChange -= DS4Devices.UpdateSerial;
            device.SerialChange -= OnSerialChange;
            device.ChargingChanged -= CheckQuickCharge;
        }

        private void CheckProfileOptions(int ind, DS4Device device, bool startUp = false)
        {
            device.IdleTimeout = GetIdleDisconnectTimeout(ind);
            device.BTPollRate = GetBTPollRate(ind);

            Controllers[ind].TouchPad.ResetTrackAccel(GetTrackballFriction(ind));

            if (!startUp)
                CheckLauchProfileOption(ind, device);
        }

        private void CheckLauchProfileOption(int ind, DS4Device device)
        {
            string programPath = LaunchProgram[ind];
            if (programPath != string.Empty)
            {
                Process[] localAll = Process.GetProcesses();
                bool procFound = false;
                for (int procInd = 0, procsLen = localAll.Length; !procFound && procInd < procsLen; procInd++)
                {
                    try
                    {
                        string temp = localAll[procInd].MainModule.FileName;
                        if (temp == programPath)
                        {
                            procFound = true;
                        }
                    }
                    // Ignore any process for which this information
                    // is not exposed
                    catch { }
                }

                if (!procFound)
                {
                    Task processTask = new Task(() =>
                    {
                        Thread.Sleep(5000);
                        Process tempProcess = new Process();
                        tempProcess.StartInfo.FileName = programPath;
                        tempProcess.StartInfo.WorkingDirectory = new FileInfo(programPath).Directory.ToString();
                        //tempProcess.StartInfo.UseShellExecute = false;
                        try { tempProcess.Start(); }
                        catch { }
                    });

                    processTask.Start();
                }
            }
        }

        public string GetDS4ControllerInfo(int index)
        {
            DS4Device d = Controllers[index].Device;
            if (d != null)
            {
                if (!d.IsAlive)
                {
                    return DS4WinWPF.Properties.Resources.Connecting;
                }

                string battery;
                if (d.IsCharging)
                {
                    if (d.Battery >= 100)
                        battery = DS4WinWPF.Properties.Resources.Charged;
                    else
                        battery = DS4WinWPF.Properties.Resources.Charging.Replace("*number*", d.Battery.ToString());
                }
                else
                {
                    battery = DS4WinWPF.Properties.Resources.Battery.Replace("*number*", d.Battery.ToString());
                }

                return $"{d.MacAddress} ({d.ConnectionType}), {battery}";
            }
            else
                return string.Empty;
        }

        public string getDS4MacAddress(int index)
        {
            DS4Device d = Controllers[index].Device;
            if (d != null)
            {
                if (!d.IsAlive)
                {
                    return DS4WinWPF.Properties.Resources.Connecting;
                }

                return d.MacAddress;
            }
            else
                return string.Empty;
        }

        public string getShortDS4ControllerInfo(int index)
        {
            DS4Device d = Controllers[index].Device;
            if (d != null)
            {
                string battery;
                if (!d.IsAlive)
                    battery = "...";
                else if (d.IsCharging)
                {
                    if (d.Battery >= 100)
                        battery = DS4WinWPF.Properties.Resources.Full;
                    else
                        battery = d.Battery + "%+";
                }
                else
                {
                    battery = d.Battery + "%";
                }

                return (d.ConnectionType + " " + battery);
            }
            else
                return DS4WinWPF.Properties.Resources.NoneText;
        }

        public string getDS4Battery(int index)
        {
            DS4Device d = Controllers[index].Device;
            if (d != null)
            {
                string battery;
                if (!d.IsAlive)
                    battery = "...";
                else if (d.IsCharging)
                {
                    if (d.Battery >= 100)
                        battery = DS4WinWPF.Properties.Resources.Full;
                    else
                        battery = d.Battery + "%+";
                }
                else
                {
                    battery = d.Battery + "%";
                }

                return battery;
            }
            else
                return DS4WinWPF.Properties.Resources.NA;
        }

        public string getDS4Status(int index)
        {
            DS4Device d = Controllers[index].Device;
            if (d != null)
            {
                return d.ConnectionType + "";
            }
            else
                return DS4WinWPF.Properties.Resources.NoneText;
        }

        protected void OnSerialChange(object sender, EventArgs e)
        {
            DS4Device device = (DS4Device)sender;
            int ind = -1;
            for (int i = 0, arlength = DS4_CONTROLLER_COUNT; ind == -1 && i < arlength; i++)
            {
                DS4Device tempDev = Controllers[i].Device;
                if (tempDev != null && device == tempDev)
                    ind = i;
            }

            if (ind >= 0)
            {
                OnDeviceSerialChange(this, ind, device.MacAddress);
            }
        }

        protected void OnSyncChange(object sender, EventArgs e)
        {
            DS4Device device = (DS4Device)sender;
            int ind = -1;
            for (int i = 0, arlength = DS4_CONTROLLER_COUNT; ind == -1 && i < arlength; i++)
            {
                DS4Device tempDev = Controllers[i].Device;
                if (tempDev != null && device == tempDev)
                    ind = i;
            }

            if (ind >= 0)
            {
                bool synced = device.IsSynced;

                if (!synced)
                {
                    if (!UseDInputOnly[ind])
                    {
                        //string tempType = outputDevices[ind].GetDeviceType();
                        //outputDevices[ind].Disconnect();
                        //outputDevices[ind] = null;
                        //useDInputOnly[ind] = true;
                        //LogDebug(tempType + " Controller #" + (ind + 1) + " unplugged");
                        ActiveOutDevType[ind] = OutControllerType.None;
                        UnplugOutDev(ind, device);
                    }
                }
                else
                {
                    if (!GetDInputOnly(ind))
                    {
                        PluginOutDev(ind, device);
                        /*OutContType conType = Global.OutContType[ind];
                        if (conType == OutContType.X360)
                        {
                            LogDebug("Plugging in X360 Controller #" + (ind + 1));
                            Global.activeOutDevType[ind] = OutContType.X360;
                            Xbox360OutDevice tempXbox = new Xbox360OutDevice(vigemTestClient);
                            outputDevices[ind] = tempXbox;
                            tempXbox.cont.FeedbackReceived += (eventsender, args) =>
                            {
                                SetDevRumble(device, args.LargeMotor, args.SmallMotor, ind);
                            };

                            tempXbox.Connect();
                            LogDebug("X360 Controller #" + (ind + 1) + " connected");
                        }
                        else if (conType == OutContType.DS4)
                        {
                            LogDebug("Plugging in DS4 Controller #" + (ind + 1));
                            Global.activeOutDevType[ind] = OutContType.DS4;
                            DS4OutDevice tempDS4 = new DS4OutDevice(vigemTestClient);
                            outputDevices[ind] = tempDS4;
                            int devIndex = ind;
                            tempDS4.cont.FeedbackReceived += (eventsender, args) =>
                            {
                                SetDevRumble(device, args.LargeMotor, args.SmallMotor, devIndex);
                            };

                            tempDS4.Connect();
                            LogDebug("DS4 Controller #" + (ind + 1) + " connected");
                        }
                        */

                        //useDInputOnly[ind] = false;
                    }
                }
            }
        }

        //Called when DS4 is disconnected or timed out
        protected virtual void OnDS4Removal(object sender, EventArgs e)
        {
            DS4Device device = (DS4Device)sender;
            int ind = -1;
            for (int i = 0, arlength = Controllers.Length; ind == -1 && i < arlength; i++)
            {
                if (Controllers[i].Device != null && device.MacAddress == Controllers[i].Device.MacAddress)
                    ind = i;
            }

            if (ind != -1)
            {
                bool removingStatus = false;
                lock (device.RemoveLocker)
                {
                    if (!device.IsRemoving)
                    {
                        removingStatus = true;
                        device.IsRemoving = true;
                    }
                }

                if (removingStatus)
                {
                    Controllers[ind].CurrentState.Battery = Controllers[ind].PreviousState.Battery = 0; // Reset for the next connection's initial status change.
                    if (!UseDInputOnly[ind])
                    {
                        UnplugOutDev(ind, device);
                    }

                    // Use Task to reset device synth state and commit it
                    Task.Run(() =>
                    {
                        Mapping.Commit(ind);
                    }).Wait();

                    string removed = DS4WinWPF.Properties.Resources.ControllerWasRemoved.Replace("*Mac address*", (ind + 1).ToString());
                    if (device.Battery <= 20 &&
                        device.IsBT && !device.IsCharging)
                    {
                        removed += ". " + DS4WinWPF.Properties.Resources.ChargeController;
                    }

                    LogDebug(removed);
                    AppLogger.LogToTray(removed);
                    /*Stopwatch sw = new Stopwatch();
                    sw.Start();
                    while (sw.ElapsedMilliseconds < XINPUT_UNPLUG_SETTLE_TIME)
                    {
                        // Use SpinWait to keep control of current thread. Using Sleep could potentially
                        // cause other events to get run out of order
                        System.Threading.Thread.SpinWait(500);
                    }
                    sw.Stop();
                    */

                    device.IsRemoved = true;
                    device.IsSynced = false;
                    Controllers[ind].Device = null;
                    SlotManager.RemoveController(device, ind);
                    Controllers[ind].TouchPad = null;
                    lag[ind] = false;
                    inWarnMonitor[ind] = false;
                    UseDInputOnly[ind] = true;
                    ActiveOutDevType[ind] = OutControllerType.None;
                    /*uiContext?.Post(new SendOrPostCallback((state) =>
                    {
                        OnControllerRemoved(this, ind);
                    }), null);
                    */
                    //Thread.Sleep(XINPUT_UNPLUG_SETTLE_TIME);
                }
            }
        }

        public bool[] lag = new bool[4] { false, false, false, false };
        public bool[] inWarnMonitor = new bool[4] { false, false, false, false };
        private byte[] currentBattery = new byte[4] { 0, 0, 0, 0 };
        private bool[] charging = new bool[4] { false, false, false, false };
        private string[] tempStrings = new string[4] { string.Empty, string.Empty, string.Empty, string.Empty };

        // Called every time a new input report has arrived
        //protected virtual void On_Report(object sender, EventArgs e, int ind)
        protected virtual void OnReport(DS4Device device, EventArgs e, int ind)
        {
            //DS4Device device = (DS4Device)sender;

            if (ind != -1)
            {
                if (getFlushHIDQueue(ind))
                    device.FlushHID();

                string devError = tempStrings[ind] = device.error;
                if (!string.IsNullOrEmpty(devError))
                {
                    LogDebug(devError);
                    /*uiContext?.Post(new SendOrPostCallback(delegate (object state)
                    {
                        LogDebug(devError);
                    }), null);
                    */
                }

                if (inWarnMonitor[ind])
                {
                    int flashWhenLateAt = getFlashWhenLateAt();
                    if (!lag[ind] && device.Latency >= flashWhenLateAt)
                    {
                        lag[ind] = true;
                        LagFlashWarning(ind, true);
                        /*uiContext?.Post(new SendOrPostCallback(delegate (object state)
                        {
                            LagFlashWarning(ind, true);
                        }), null);
                        */
                    }
                    else if (lag[ind] && device.Latency < flashWhenLateAt)
                    {
                        lag[ind] = false;
                        LagFlashWarning(ind, false);
                        /*uiContext?.Post(new SendOrPostCallback(delegate (object state)
                        {
                            LagFlashWarning(ind, false);
                        }), null);
                        */
                    }
                }
                else
                {
                    if (DateTime.UtcNow - device.firstActive > TimeSpan.FromSeconds(5))
                    {
                        inWarnMonitor[ind] = true;
                    }
                }

                device.getCurrentState(Controllers[ind].CurrentState);
                DS4State cState = Controllers[ind].CurrentState;
                DS4State pState = device.GetPreviousStateRef();
                //device.getPreviousState(PreviousState[ind]);
                //DS4State pState = PreviousState[ind];

                if (device.firstReport && device.IsAlive)
                {
                    device.firstReport = false;
                    /*uiContext?.Post(new SendOrPostCallback(delegate (object state)
                    {
                        OnDeviceStatusChanged(this, ind);
                    }), null);
                    */
                }
                //else if (pState.Battery != cState.Battery || device.oldCharging != device.Charging)
                //{
                //    byte tempBattery = currentBattery[ind] = cState.Battery;
                //    bool tempCharging = charging[ind] = device.Charging;
                //    /*uiContext?.Post(new SendOrPostCallback(delegate (object state)
                //    {
                //        OnBatteryStatusChange(this, ind, tempBattery, tempCharging);
                //    }), null);
                //    */
                //}

                if (getEnableTouchToggle(ind))
                    CheckForTouchToggle(ind, cState, pState);

                cState = Mapping.SetCurveAndDeadzone(ind, cState, Controllers[ind].TempState);

                if (!RecordMacro && (UseTempProfile[ind] ||
                    containsCustomAction(ind) || containsCustomExtras(ind) ||
                    GetProfileActionCount(ind) > 0 ||
                    GetSASteeringWheelEmulationAxis(ind) >= SASteeringWheelEmulationAxisType.VJoy1X))
                {
                    Mapping.MapCustom(ind, cState, Controllers[ind].MappedState, Controllers[ind].ExposedState, Controllers[ind].TouchPad, this);
                    cState = Controllers[ind].MappedState;
                }

                if (!UseDInputOnly[ind])
                {
                    Controllers[ind].Output?.ConvertAndSendReport(cState, ind);
                    //testNewReport(ref x360reports[ind], cState, ind);
                    //x360controls[ind]?.SendReport(x360reports[ind]);

                    //x360Bus.Parse(cState, processingData[ind].Report, ind);
                    // We push the translated Xinput state, and simultaneously we
                    // pull back any possible rumble data coming from Xinput consumers.
                    /*if (x360Bus.Report(processingData[ind].Report, processingData[ind].Rumble))
                    {
                        byte Big = processingData[ind].Rumble[3];
                        byte Small = processingData[ind].Rumble[4];

                        if (processingData[ind].Rumble[1] == 0x08)
                        {
                            SetDevRumble(device, Big, Small, ind);
                        }
                    }
                    */
                }
                else
                {
                    // UseDInputOnly profile may re-map sixaxis gyro sensor values as a VJoy joystick axis (steering wheel emulation mode using VJoy output device). Handle this option because VJoy output works even in USeDInputOnly mode.
                    // If steering wheel emulation uses LS/RS/R2/L2 output axies then the profile should NOT use UseDInputOnly option at all because those require a virtual output device.
                    SASteeringWheelEmulationAxisType steeringWheelMappedAxis = GetSASteeringWheelEmulationAxis(ind);
                    switch (steeringWheelMappedAxis)
                    {
                        case SASteeringWheelEmulationAxisType.None: break;

                        case SASteeringWheelEmulationAxisType.VJoy1X:
                        case SASteeringWheelEmulationAxisType.VJoy2X:
                            VJoyFeeder.vJoyFeeder.FeedAxisValue(cState.SASteeringWheelEmulationUnit, ((((uint)steeringWheelMappedAxis) - ((uint)SASteeringWheelEmulationAxisType.VJoy1X)) / 3) + 1, VJoyFeeder.HID_USAGES.HID_USAGE_X);
                            break;

                        case SASteeringWheelEmulationAxisType.VJoy1Y:
                        case SASteeringWheelEmulationAxisType.VJoy2Y:
                            VJoyFeeder.vJoyFeeder.FeedAxisValue(cState.SASteeringWheelEmulationUnit, ((((uint)steeringWheelMappedAxis) - ((uint)SASteeringWheelEmulationAxisType.VJoy1X)) / 3) + 1, VJoyFeeder.HID_USAGES.HID_USAGE_Y);
                            break;

                        case SASteeringWheelEmulationAxisType.VJoy1Z:
                        case SASteeringWheelEmulationAxisType.VJoy2Z:
                            VJoyFeeder.vJoyFeeder.FeedAxisValue(cState.SASteeringWheelEmulationUnit, ((((uint)steeringWheelMappedAxis) - ((uint)SASteeringWheelEmulationAxisType.VJoy1X)) / 3) + 1, VJoyFeeder.HID_USAGES.HID_USAGE_Z);
                            break;
                    }
                }

                // Output any synthetic events.
                Mapping.Commit(ind);

                // Update the GUI/whatever.
                DS4LightBar.UpdateLightBar(device, ind);
            }
        }

        public void LagFlashWarning(int ind, bool on)
        {
            if (on)
            {
                lag[ind] = true;
                LogDebug(DS4WinWPF.Properties.Resources.LatencyOverTen.Replace("*number*", (ind + 1).ToString()), true);
                if (getFlashWhenLate())
                {
                    DS4Color color = new DS4Color { Red = 50, Green = 0, Blue = 0 };
                    DS4LightBar.ForcedColor[ind] = color;
                    DS4LightBar.ForcedFlash[ind] = 2;
                    DS4LightBar.ForceLight[ind] = true;
                }
            }
            else
            {
                lag[ind] = false;
                LogDebug(DS4WinWPF.Properties.Resources.LatencyNotOverTen.Replace("*number*", (ind + 1).ToString()));
                DS4LightBar.ForceLight[ind] = false;
                DS4LightBar.ForcedFlash[ind] = 0;
            }
        }

        public DS4Controls GetActiveInputControl(int ind)
        {
            DS4State cState = Controllers[ind].CurrentState;
            DS4StateExposed eState = Controllers[ind].ExposedState;
            Mouse tp = Controllers[ind].TouchPad;
            DS4Controls result = DS4Controls.None;

            if (Controllers[ind].Device != null)
            {
                if (Mapping.getBoolButtonMapping(cState.Cross))
                    result = DS4Controls.Cross;
                else if (Mapping.getBoolButtonMapping(cState.Circle))
                    result = DS4Controls.Circle;
                else if (Mapping.getBoolButtonMapping(cState.Triangle))
                    result = DS4Controls.Triangle;
                else if (Mapping.getBoolButtonMapping(cState.Square))
                    result = DS4Controls.Square;
                else if (Mapping.getBoolButtonMapping(cState.L1))
                    result = DS4Controls.L1;
                else if (Mapping.GetBoolTriggerMapping(cState.L2))
                    result = DS4Controls.L2;
                else if (Mapping.getBoolButtonMapping(cState.L3))
                    result = DS4Controls.L3;
                else if (Mapping.getBoolButtonMapping(cState.R1))
                    result = DS4Controls.R1;
                else if (Mapping.GetBoolTriggerMapping(cState.R2))
                    result = DS4Controls.R2;
                else if (Mapping.getBoolButtonMapping(cState.R3))
                    result = DS4Controls.R3;
                else if (Mapping.getBoolButtonMapping(cState.DpadUp))
                    result = DS4Controls.DpadUp;
                else if (Mapping.getBoolButtonMapping(cState.DpadDown))
                    result = DS4Controls.DpadDown;
                else if (Mapping.getBoolButtonMapping(cState.DpadLeft))
                    result = DS4Controls.DpadLeft;
                else if (Mapping.getBoolButtonMapping(cState.DpadRight))
                    result = DS4Controls.DpadRight;
                else if (Mapping.getBoolButtonMapping(cState.Share))
                    result = DS4Controls.Share;
                else if (Mapping.getBoolButtonMapping(cState.Options))
                    result = DS4Controls.Options;
                else if (Mapping.getBoolButtonMapping(cState.PS))
                    result = DS4Controls.PS;
                else if (Mapping.getBoolAxisDirMapping(cState.LX, true))
                    result = DS4Controls.LXPos;
                else if (Mapping.getBoolAxisDirMapping(cState.LX, false))
                    result = DS4Controls.LXNeg;
                else if (Mapping.getBoolAxisDirMapping(cState.LY, true))
                    result = DS4Controls.LYPos;
                else if (Mapping.getBoolAxisDirMapping(cState.LY, false))
                    result = DS4Controls.LYNeg;
                else if (Mapping.getBoolAxisDirMapping(cState.RX, true))
                    result = DS4Controls.RXPos;
                else if (Mapping.getBoolAxisDirMapping(cState.RX, false))
                    result = DS4Controls.RXNeg;
                else if (Mapping.getBoolAxisDirMapping(cState.RY, true))
                    result = DS4Controls.RYPos;
                else if (Mapping.getBoolAxisDirMapping(cState.RY, false))
                    result = DS4Controls.RYNeg;
                else if (Mapping.GetBoolTouchMapping(tp.leftDown))
                    result = DS4Controls.TouchLeft;
                else if (Mapping.GetBoolTouchMapping(tp.rightDown))
                    result = DS4Controls.TouchRight;
                else if (Mapping.GetBoolTouchMapping(tp.multiDown))
                    result = DS4Controls.TouchMulti;
                else if (Mapping.GetBoolTouchMapping(tp.upperDown))
                    result = DS4Controls.TouchUpper;
            }

            return result;
        }

        protected virtual void CheckForTouchToggle(int deviceID, DS4State cState, DS4State pState)
        {
            if (!GetUseTouchPadForControls(deviceID) && cState.Touch1 && pState.PS)
            {
                if (GetTouchActive(deviceID) && Controllers[deviceID].TouchReleased)
                {
                    TouchActive[deviceID] = false;
                    LogDebug(DS4WinWPF.Properties.Resources.TouchpadMovementOff);
                    AppLogger.LogToTray(DS4WinWPF.Properties.Resources.TouchpadMovementOff);
                    Controllers[deviceID].TouchReleased = false;
                }
                else if (Controllers[deviceID].TouchReleased)
                {
                    TouchActive[deviceID] = true;
                    LogDebug(DS4WinWPF.Properties.Resources.TouchpadMovementOn);
                    AppLogger.LogToTray(DS4WinWPF.Properties.Resources.TouchpadMovementOn);
                    Controllers[deviceID].TouchReleased = false;
                }
            }
            else
                Controllers[deviceID].TouchReleased = true;
        }

        public virtual void StartTouchPadOff(int deviceID)
        {
            if (deviceID < 4)
            {
                TouchActive[deviceID] = false;
            }
        }

        public virtual string TouchpadSlide(int ind)
        {
            var c = Controllers[ind];
            DS4State cState = c.CurrentState;
            string slidedir = "none";
            if (c.Device != null && cState.Touch2 && !(c.TouchPad.dragging || c.TouchPad.dragging2))
            {
                if (c.TouchPad.slideright && !c.TouchSlid)
                {
                    slidedir = "right";
                    c.TouchSlid = true;
                }
                else if (c.TouchPad.slideleft && !c.TouchSlid)
                {
                    slidedir = "left";
                    c.TouchSlid = true;
                }
                else if (!c.TouchPad.slideleft && !c.TouchPad.slideright)
                {
                    slidedir = "";
                    c.TouchSlid = false;
                }
            }

            return slidedir;
        }

        public virtual void LogDebug(string message, bool warning = false)
            => OnDebug(this, new DebugEventArgs(message, warning));

        public virtual void OnDebug(object sender, DebugEventArgs args)
            => Debug?.Invoke(this, args);

        // sets the rumble adjusted with rumble boost. General use method
        public virtual void SetRumble(byte heavyMotor, byte lightMotor, int deviceNum)
        {
            if (deviceNum >= Controllers.Length || deviceNum < 0)
                return;
            
            DS4Device device = Controllers[deviceNum].Device;
            if (device != null)
                SetDevRumble(device, heavyMotor, lightMotor, deviceNum);
        }

        // sets the rumble adjusted with rumble boost. Method more used for
        // report handling. Avoid constant checking for a device.
        public void SetDevRumble(DS4Device device,
            byte heavyMotor, byte lightMotor, int deviceNum)
        {
            byte boost = GetRumbleBoost(deviceNum);
            uint lightBoosted = ((uint)lightMotor * (uint)boost) / 100;
            if (lightBoosted > 255)
                lightBoosted = 255;
            uint heavyBoosted = ((uint)heavyMotor * (uint)boost) / 100;
            if (heavyBoosted > 255)
                heavyBoosted = 255;

            device.SetRumble((byte)lightBoosted, (byte)heavyBoosted);
        }

        public DS4State GetDS4State(int ind) => Controllers[ind].CurrentState;
        public DS4State GetDS4StateMapped(int ind) => Controllers[ind].MappedState;
        public DS4State GetDS4StateTemp(int ind) => Controllers[ind].TempState;
    }
}
