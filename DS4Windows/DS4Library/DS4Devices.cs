using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace DS4Windows
{
    public sealed class VidPidInfo
    {
        public int Vid { get; }
        public int Pid { get; }
        public string Name { get; }

        internal VidPidInfo(int vid, int pid, string name = "Generic DS4")
        {
            Vid = vid;
            Pid = pid;
            Name = name;
        }
    }

    public class DS4Devices
    {
        private const string DeviceIgnoreDescription = "HID-compliant vendor-defined device";

        // (HID device path, DS4Device)
        private static Dictionary<string, DS4Device> Devices { get; } = new Dictionary<string, DS4Device>();
        private static HashSet<string> DeviceSerials { get; } = new HashSet<string>();
        private static HashSet<string> DevicePaths { get; } = new HashSet<string>();
        // Keep instance of opened exclusive mode devices not in use (Charging while using BT connection)
        private static List<HidDevice> DisabledDevices { get; } = new List<HidDevice>();
        private static Stopwatch Stopwatch { get; } = new Stopwatch();

        public static bool IsExclusiveMode { get; set; } = false;

        internal const int SONY_VID = 0x054C;
        internal const int RAZER_VID = 0x1532;
        internal const int NACON_VID = 0x146B;
        internal const int HORI_VID = 0x0F0D;

        // https://support.steampowered.com/kb_article.php?ref=5199-TOKV-4426&l=english web site has a list of other PS4 compatible device VID/PID values and brand names. 
        // However, not all those are guaranteed to work with DS4Windows app so support is added case by case when users of DS4Windows app tests non-official DS4 gamepads.

        private static VidPidInfo[] KnownDevices { get; } =
        {
            new VidPidInfo(SONY_VID, 0xBA0, "Sony WA"),
            new VidPidInfo(SONY_VID, 0x5C4, "DS4 v.1"),
            new VidPidInfo(SONY_VID, 0x09CC, "DS4 v.2"),
            new VidPidInfo(RAZER_VID, 0x1000, "Razer Raiju PS4"),
            new VidPidInfo(NACON_VID, 0x0D01, "Nacon Revol Pro v.1"),
            new VidPidInfo(NACON_VID, 0x0D02, "Nacon Revol Pro v.2"),
            new VidPidInfo(HORI_VID, 0x00EE, "Hori PS4 Mini"),    // Hori PS4 Mini Wired Gamepad
            new VidPidInfo(0x7545, 0x0104, "Armor 3 LU Cobra"), // Armor 3 Level Up Cobra
            new VidPidInfo(0x2E95, 0x7725, "Scuf Vantage"), // Scuf Vantage gamepad
            new VidPidInfo(0x11C0, 0x4001, "PS4 Fun"), // PS4 Fun Controller
            new VidPidInfo(RAZER_VID, 0x1007, "Razer Raiju TE"), // Razer Raiju Tournament Edition
            new VidPidInfo(RAZER_VID, 0x1004, "Razer Raiju UE USB"), // Razer Raiju Ultimate Edition (wired)
            new VidPidInfo(RAZER_VID, 0x1009, "Razer Raiju UE BT"), // Razer Raiju Ultimate Edition (BT). Doesn't work yet for some reason even when non-steam Razer driver lists the BT Razer Ultimate with this ID.
            new VidPidInfo(SONY_VID, 0x05C5, "CronusMax (PS4 Mode)"), // CronusMax (PS4 Output Mode)
            new VidPidInfo(0x0C12, 0x57AB, "Warrior Joypad JS083"), // Warrior Joypad JS083 (wired). Custom lightbar color doesn't work, but everything else works OK (except touchpad and gyro because the gamepad doesnt have those).
            new VidPidInfo(0x0C12, 0x0E16, "Steel Play MetalTech"), // Steel Play Metaltech P4 (wired)
            new VidPidInfo(NACON_VID, 0x0D08, "Nacon Revol U Pro"), // Nacon Revolution Unlimited Pro
            new VidPidInfo(NACON_VID, 0x0D10, "Nacon Revol Infinite"), // Nacon Revolution Infinite (sometimes known as Revol Unlimited Pro v2?). Touchpad, gyro, rumble, "led indicator" lightbar.
            new VidPidInfo(HORI_VID, 0x0084, "Hori Fighting Cmd"), // Hori Fighting Commander (special kind of gamepad without touchpad or sticks. There is a hardware switch to alter d-pad type between dpad and LS/RS)
            new VidPidInfo(NACON_VID, 0x0D13, "Nacon Revol Pro v.3"),
        };

        private static string DevicePathToInstanceId(string devicePath)
        {
            string deviceInstanceId = devicePath;
            deviceInstanceId = deviceInstanceId.Remove(0, deviceInstanceId.LastIndexOf('\\') + 1);
            deviceInstanceId = deviceInstanceId.Remove(deviceInstanceId.LastIndexOf('{'));
            deviceInstanceId = deviceInstanceId.Replace('#', '\\');
            if (deviceInstanceId.EndsWith("\\"))
                deviceInstanceId = deviceInstanceId.Remove(deviceInstanceId.Length - 1);
            return deviceInstanceId;
        }

        private static bool IsRealDS4(HidDevice hDevice)
        {
            string deviceInstanceId = DevicePathToInstanceId(hDevice.DevicePath);
            string temp = Global.GetDeviceProperty(deviceInstanceId, NativeMethods.DEVPKEY_Device_UINumber);
            return string.IsNullOrEmpty(temp);
        }

        // Enumerates ds4 controllers in the system
        public static void FindControllers()
        {
            lock (Devices)
            {
                // Sort Bluetooth first in case USB is also connected on the same controller.
                var hids = HidDevices.EnumerateDS4(KnownDevices).Where(dev => IsRealDS4(dev)).OrderBy(d2 => DS4Device.GetHidConnectionType(d2));

                PurgeHiddenExclusiveDevices();
                var disabledCopy = DisabledDevices.ToList();

                foreach (HidDevice hDevice in hids)
                    EvalHid(hDevice);

                foreach (HidDevice hDevice in disabledCopy)
                    EvalHid(hDevice);
            }
        }

        private static void EvalHid(HidDevice hDevice)
        {
            if (IgnoreDevice(hDevice))
                return;

            if (!hDevice.IsOpen)
                OpenDevice(hDevice);

            if (!hDevice.IsOpen)
                return;

            string serial = hDevice.ReadSerial();
            if (DS4Device.IsValidSerial(serial))
            {
                if (DeviceSerials.Contains(serial))
                    OnSerialExists(hDevice);
                else
                {
                    try
                    {
                        VidPidInfo metainfo = KnownDevices.Single(x =>
                            x.Vid == hDevice.Attributes.VendorId &&
                            x.Pid == hDevice.Attributes.ProductId);

                        if (metainfo != null)
                            OnAddSerial(hDevice, metainfo, serial);
                    }
                    catch 
                    {
                        // Single() may throw an exception
                    }
                }
            }
        }

        private static bool IgnoreDevice(HidDevice hDevice)
        {
            if (hDevice.Description == DeviceIgnoreDescription)
                return true; // ignore the Nacon Revolution Pro programming interface

            if (DevicePaths.Contains(hDevice.DevicePath))
                return true; // BT/USB endpoint already open once

            return false;
        }

        private static void OnAddSerial(HidDevice hDevice, VidPidInfo metainfo, string serial)
        {
            DS4Device ds4Device = new DS4Device(hDevice, metainfo.Name);
            //ds4Device.Removal += On_Removal;
            if (!ds4Device.ExitOutputThread)
            {
                Devices.Add(hDevice.DevicePath, ds4Device);
                DevicePaths.Add(hDevice.DevicePath);
                DeviceSerials.Add(serial);
            }
        }

        private static void OnSerialExists(HidDevice hDevice)
        {
            // happens when the BT endpoint already is open and the USB is plugged into the same host
            if (IsExclusiveMode && hDevice.IsExclusive && !DisabledDevices.Contains(hDevice))
            {
                // Grab reference to exclusively opened HidDevice so device
                // stays hidden to other processes
                DisabledDevices.Add(hDevice);
                //DevicePaths.Add(hDevice.DevicePath);
            }
        }

        private static void OpenDevice(HidDevice hDevice)
        {
            hDevice.OpenDevice(IsExclusiveMode);
            if (!hDevice.IsOpen && IsExclusiveMode)
            {
                try
                {
                    WindowsIdentity identity = WindowsIdentity.GetCurrent();
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    bool elevated = principal.IsInRole(WindowsBuiltInRole.Administrator);

                    if (!elevated)
                    {
                        // Launches an elevated child process to re-enable device
                        string exeName = Process.GetCurrentProcess().MainModule.FileName;
                        ProcessStartInfo startInfo = new ProcessStartInfo(exeName);
                        startInfo.Verb = "runas";
                        startInfo.Arguments = "re-enabledevice " + DevicePathToInstanceId(hDevice.DevicePath);
                        Process child = Process.Start(startInfo);

                        if (!child.WaitForExit(30000))
                        {
                            child.Kill();
                        }
                        else if (child.ExitCode == 0)
                        {
                            hDevice.OpenDevice(IsExclusiveMode);
                        }
                    }
                    else
                    {
                        ReEnableDevice(DevicePathToInstanceId(hDevice.DevicePath));
                        hDevice.OpenDevice(IsExclusiveMode);
                    }
                }
                catch (Exception) { }
            }

            // TODO in exclusive mode, try to hold both open when both are connected
            if (IsExclusiveMode && !hDevice.IsOpen)
                hDevice.OpenDevice(false);
        }

        // Returns DS4 controllers that were found and are running
        public static IEnumerable<DS4Device> GetDS4Controllers()
        {
            lock (Devices)
            {
                DS4Device[] controllers = new DS4Device[Devices.Count];
                Devices.Values.CopyTo(controllers, 0);
                return controllers;
            }
        }

        public static void StopControllers()
        {
            lock (Devices)
            {
                IEnumerable<DS4Device> devices = GetDS4Controllers();
                foreach (DS4Device device in devices)
                {
                    device.StopUpdate();
                    device.HidDevice.CloseDevice();
                }

                Devices.Clear();
                DevicePaths.Clear();
                DeviceSerials.Clear();
                DisabledDevices.Clear();
            }
        }

        // Called when devices is diconnected, timed out or has input reading failure
        public static void OnRemoval(object sender, EventArgs e) => RemoveDevice(sender as DS4Device);

        public static void RemoveDevice(DS4Device device)
        {
            if (device is null)
                return;

            lock (Devices)
            {
                device.HidDevice.CloseDevice();
                Devices.Remove(device.HidDevice.DevicePath);
                DevicePaths.Remove(device.HidDevice.DevicePath);
                DeviceSerials.Remove(device.MacAddress);
            }
        }

        public static void UpdateSerial(object sender, EventArgs e)
        {
            lock (Devices)
            {
                DS4Device device = (DS4Device)sender;
                if (device != null)
                {
                    string devPath = device.HidDevice.DevicePath;
                    string serial = device.MacAddress;
                    if (Devices.ContainsKey(devPath))
                    {
                        DeviceSerials.Remove(serial);
                        device.updateSerial();
                        serial = device.MacAddress;
                        if (DS4Device.IsValidSerial(serial))
                            DeviceSerials.Add(serial);
                        
                        if (device.ShouldRunCalib)
                            device.RefreshCalibration();
                    }
                }
            }
        }

        private static void PurgeHiddenExclusiveDevices()
        {
            int disabledDevCount = DisabledDevices.Count;
            if (disabledDevCount > 0)
            {
                List<HidDevice> disabledDevList = new List<HidDevice>();
                for (var devEnum = DisabledDevices.GetEnumerator(); devEnum.MoveNext();)
                //for (int i = 0, arlen = disabledDevCount; i < arlen; i++)
                {
                    //HidDevice tempDev = DisabledDevices.ElementAt(i);
                    HidDevice tempDev = devEnum.Current;
                    if (tempDev != null)
                    {
                        if (tempDev.IsOpen && tempDev.IsConnected)
                        {
                            disabledDevList.Add(tempDev);
                        }
                        else if (tempDev.IsOpen)
                        {
                            if (!tempDev.IsConnected)
                            {
                                try
                                {
                                    tempDev.CloseDevice();
                                }
                                catch { }
                            }

                            if (DevicePaths.Contains(tempDev.DevicePath))
                            {
                                DevicePaths.Remove(tempDev.DevicePath);
                            }
                        }
                    }
                }

                DisabledDevices.Clear();
                DisabledDevices.AddRange(disabledDevList);
            }
        }

        public static void ReEnableDevice(string deviceInstanceId)
        {
            Guid hidGuid = new Guid();
            NativeMethods.HidD_GetHidGuid(ref hidGuid);
            IntPtr deviceInfoSet = NativeMethods.SetupDiGetClassDevs(ref hidGuid, deviceInstanceId, 0, NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
            NativeMethods.SP_DEVINFO_DATA deviceInfoData = new NativeMethods.SP_DEVINFO_DATA();
            deviceInfoData.cbSize = Marshal.SizeOf(deviceInfoData);

            if (!NativeMethods.SetupDiEnumDeviceInfo(deviceInfoSet, 0, ref deviceInfoData))
                throw new Exception("Error getting device info data, error code = " + Marshal.GetLastWin32Error());
            
            // Check that we have a unique device
            if (NativeMethods.SetupDiEnumDeviceInfo(deviceInfoSet, 1, ref deviceInfoData))
                throw new Exception("Can't find unique device");
            
            NativeMethods.SP_PROPCHANGE_PARAMS propChangeParams = new NativeMethods.SP_PROPCHANGE_PARAMS();
            propChangeParams.classInstallHeader.cbSize = Marshal.SizeOf(propChangeParams.classInstallHeader);
            propChangeParams.classInstallHeader.installFunction = NativeMethods.DIF_PROPERTYCHANGE;
            propChangeParams.stateChange = NativeMethods.DICS_DISABLE;
            propChangeParams.scope = NativeMethods.DICS_FLAG_GLOBAL;
            propChangeParams.hwProfile = 0;

            if (!NativeMethods.SetupDiSetClassInstallParams(deviceInfoSet, ref deviceInfoData, ref propChangeParams, Marshal.SizeOf(propChangeParams)))
                throw new Exception("Error setting class install params, error code = " + Marshal.GetLastWin32Error());
            
            NativeMethods.SetupDiCallClassInstaller(NativeMethods.DIF_PROPERTYCHANGE, deviceInfoSet, ref deviceInfoData);
            // TEST: If previous SetupDiCallClassInstaller fails, just continue
            // otherwise device will likely get permanently disabled.
            /*if (!success)
            {
                throw new Exception("Error disabling device, error code = " + Marshal.GetLastWin32Error());
            }
            */

            Stopwatch.Restart();
            while (Stopwatch.ElapsedMilliseconds < 50)
            {
                // Use SpinWait to keep control of current thread. Using Sleep could potentially
                // cause other events to get run out of order
                System.Threading.Thread.SpinWait(100);
            }
            Stopwatch.Stop();

            propChangeParams.stateChange = NativeMethods.DICS_ENABLE;

            if (!NativeMethods.SetupDiSetClassInstallParams(deviceInfoSet, ref deviceInfoData, ref propChangeParams, Marshal.SizeOf(propChangeParams)))
                throw new Exception("Error setting class install params, error code = " + Marshal.GetLastWin32Error());
            
            if (!NativeMethods.SetupDiCallClassInstaller(NativeMethods.DIF_PROPERTYCHANGE, deviceInfoSet, ref deviceInfoData))
                throw new Exception("Error enabling device, error code = " + Marshal.GetLastWin32Error());
            
            Stopwatch.Restart();
            while (Stopwatch.ElapsedMilliseconds < 50)
            {
                // Use SpinWait to keep control of current thread. Using Sleep could potentially
                // cause other events to get run out of order
                System.Threading.Thread.SpinWait(100);
            }
            Stopwatch.Stop();

            NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }
}
