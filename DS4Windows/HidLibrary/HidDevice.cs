using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
namespace DS4Windows
{
    public class HidDevice : IDisposable
    {
        private const string BLANK_SERIAL = "00:00:00:00:00:00";

        public enum ReadStatus
        {
            Success = 0,
            WaitTimedOut = 1,
            WaitFail = 2,
            NoDataRead = 3,
            ReadError = 4,
            NotConnected = 5
        }

        private string Serial { get; set; } = null;
        public SafeFileHandle SafeReadHandle { get; private set; }
        public FileStream FileStream { get; private set; }
        public bool IsOpen { get; private set; }
        public bool IsExclusive { get; private set; }
        public bool IsConnected { get { return HidDevices.IsConnected(DevicePath); } }
        public string Description { get; }
        public HidDeviceCapabilities Capabilities { get; }
        public HidDeviceAttributes Attributes { get; }
        public string DevicePath { get; }

        internal HidDevice(string devicePath, string description = null)
        {
            DevicePath = devicePath;
            Description = description;

            try
            {
                var hidHandle = OpenHandle(DevicePath, false);

                Attributes = GetDeviceAttributes(hidHandle);
                Capabilities = GetDeviceCapabilities(hidHandle);

                hidHandle.Close();
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
                throw new Exception(string.Format("Error querying HID device '{0}'.", devicePath), exception);
            }
        }

        public override string ToString()
            => $"VendorID={Attributes.VendorHexId}, ProductID={Attributes.ProductHexId}, Version={Attributes.Version}, DevicePath={DevicePath}";

        public void OpenDevice(bool isExclusive)
        {
            if (IsOpen)
                return;

            try
            {
                if (SafeReadHandle is null || SafeReadHandle.IsInvalid)
                    SafeReadHandle = OpenHandle(DevicePath, isExclusive);
            }
            catch (Exception exception)
            {
                IsOpen = false;
                throw new Exception("Error opening HID device.", exception);
            }

            IsOpen = !SafeReadHandle.IsInvalid;
            IsExclusive = isExclusive;
        }

        public void OpenFileStream(int reportSize)
        {
            if (FileStream is null && !SafeReadHandle.IsInvalid)
                FileStream = new FileStream(SafeReadHandle, FileAccess.ReadWrite, reportSize, true);
        }

        public bool IsFileStreamOpen()
        {
            bool result = false;
            if (FileStream != null)
                result = !FileStream.SafeFileHandle.IsInvalid && !FileStream.SafeFileHandle.IsClosed;
            
            return result;
        }

        public void CloseDevice()
        {
            if (!IsOpen) return;
            CloseFileStreamIO();

            IsOpen = false;
        }

        public void Dispose()
        {
            CancelIO();
            CloseDevice();
        }

        public void CancelIO()
        {
            if (IsOpen)
                NativeMethods.CancelIoEx(SafeReadHandle.DangerousGetHandle(), IntPtr.Zero);
        }

        public bool ReadInputReport(byte[] data)
        {
            EnsureReadHandleActive();
            return NativeMethods.HidD_GetInputReport(SafeReadHandle, data, data.Length);
        }

        public bool WriteFeatureReport(byte[] data)
        {
            bool result = false;
            if (IsOpen && SafeReadHandle != null)
                result = NativeMethods.HidD_SetFeature(SafeReadHandle, data, data.Length);
            
            return result;
        }


        private static HidDeviceAttributes GetDeviceAttributes(SafeFileHandle hidHandle)
        {
            var deviceAttributes = default(NativeMethods.HIDD_ATTRIBUTES);
            deviceAttributes.Size = Marshal.SizeOf(deviceAttributes);
            NativeMethods.HidD_GetAttributes(hidHandle.DangerousGetHandle(), ref deviceAttributes);
            return new HidDeviceAttributes(deviceAttributes);
        }

        private static HidDeviceCapabilities GetDeviceCapabilities(SafeFileHandle hidHandle)
        {
            var capabilities = default(NativeMethods.HIDP_CAPS);
            var preparsedDataPointer = default(IntPtr);

            if (NativeMethods.HidD_GetPreparsedData(hidHandle.DangerousGetHandle(), ref preparsedDataPointer))
            {
                NativeMethods.HidP_GetCaps(preparsedDataPointer, ref capabilities);
                NativeMethods.HidD_FreePreparsedData(preparsedDataPointer);
            }

            return new HidDeviceCapabilities(capabilities);
        }

        private void CloseFileStreamIO()
        {
            if (FileStream != null)
            {
                try { FileStream.Close(); } catch { }
            }

            FileStream = null;

            if (SafeReadHandle != null && !SafeReadHandle.IsInvalid)
            {
                try
                {
                    if (!SafeReadHandle.IsClosed)
                        SafeReadHandle.Close();
                }
                catch (IOException) { }
            }

            SafeReadHandle = null;
        }

        public void Flush_Queue()
        {
            if (SafeReadHandle != null)
                NativeMethods.HidD_FlushQueue(SafeReadHandle);
        }

        private ReadStatus ReadWithFileStreamTask(byte[] inputBuffer)
        {
            try
            {
                if (FileStream.Read(inputBuffer, 0, inputBuffer.Length) > 0)
                    return ReadStatus.Success;
                else
                    return ReadStatus.NoDataRead;
            }
            catch (Exception)
            {
                return ReadStatus.ReadError;
            }
        }

        public ReadStatus ReadFile(byte[] inputBuffer)
        {
            EnsureReadHandleActive();
            try
            {
                if (NativeMethods.ReadFile(SafeReadHandle.DangerousGetHandle(), inputBuffer, (uint)inputBuffer.Length, out uint bytesRead, IntPtr.Zero))
                    return ReadStatus.Success;
                else
                    return ReadStatus.NoDataRead;
            }
            catch (Exception)
            {
                return ReadStatus.ReadError;
            }
        }

        public ReadStatus ReadWithFileStream(byte[] inputBuffer)
        {
            try
            {
                if (FileStream.Read(inputBuffer, 0, inputBuffer.Length) > 0)
                    return ReadStatus.Success;
                else
                    return ReadStatus.NoDataRead;
            }
            catch (Exception)
            {
                return ReadStatus.ReadError;
            }
        }

        public ReadStatus ReadWithFileStream(byte[] inputBuffer, int timeout)
        {
            try
            {
                EnsureReadHandleActive();

                if (FileStream == null && !SafeReadHandle.IsInvalid)
                    FileStream = new FileStream(SafeReadHandle, FileAccess.ReadWrite, inputBuffer.Length, true);

                if (!SafeReadHandle.IsInvalid && FileStream.CanRead)
                {
                    Task<ReadStatus> readFileTask = new Task<ReadStatus>(() => ReadWithFileStreamTask(inputBuffer));
                    readFileTask.Start();

                    if (!readFileTask.Wait(timeout))
                        return ReadStatus.WaitTimedOut;

                    return readFileTask.Result;
                }
            }
            catch (Exception e)
            {
                if (e is AggregateException)
                {
                    Console.WriteLine(e.Message);
                    return ReadStatus.WaitFail;
                }
                else
                {
                    return ReadStatus.ReadError;
                }
            }

            return ReadStatus.ReadError;
        }

        private void EnsureReadHandleActive()
        {
            if (SafeReadHandle is null)
                SafeReadHandle = OpenHandle(DevicePath, true);
        }

        public ReadStatus ReadAsyncWithFileStream(byte[] inputBuffer, int timeout)
        {
            try
            {
                EnsureReadHandleActive();

                if (FileStream == null && !SafeReadHandle.IsInvalid)
                    FileStream = new FileStream(SafeReadHandle, FileAccess.ReadWrite, inputBuffer.Length, true);

                if (!SafeReadHandle.IsInvalid && FileStream.CanRead)
                {
                    Task<int> readTask = FileStream.ReadAsync(inputBuffer, 0, inputBuffer.Length);
                    if (!readTask.Wait(timeout))
                        return ReadStatus.WaitTimedOut;

                    return readTask.Result > 0 ? ReadStatus.Success : ReadStatus.NoDataRead;
                }

            }
            catch (Exception e)
            {
                if (e is AggregateException)
                {
                    Console.WriteLine(e.Message);
                    return ReadStatus.WaitFail;
                }
                else
                {
                    return ReadStatus.ReadError;
                }
            }

            return ReadStatus.ReadError;
        }

        public bool WriteOutputReportViaControl(byte[] outputBuffer)
        {
            EnsureReadHandleActive();
            return NativeMethods.HidD_SetOutputReport(SafeReadHandle, outputBuffer, outputBuffer.Length);
        }

        private bool WriteOutputReportViaInterruptTask(byte[] outputBuffer)
        {
            try
            {
                FileStream.Write(outputBuffer, 0, outputBuffer.Length);
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return false;
            }
        }

        public bool WriteOutputReportViaInterrupt(byte[] outputBuffer, int timeout)
        {
            try
            {
                EnsureReadHandleActive();

                if (FileStream is null && !SafeReadHandle.IsInvalid)
                    FileStream = new FileStream(SafeReadHandle, FileAccess.ReadWrite, outputBuffer.Length, true);
                
                if (FileStream != null && FileStream.CanWrite && !SafeReadHandle.IsInvalid)
                {
                    FileStream.Write(outputBuffer, 0, outputBuffer.Length);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }

        }

        public bool WriteAsyncOutputReportViaInterrupt(byte[] outputBuffer)
        {
            try
            {
                EnsureReadHandleActive();

                if (FileStream is null && !SafeReadHandle.IsInvalid)
                    FileStream = new FileStream(SafeReadHandle, FileAccess.ReadWrite, outputBuffer.Length, true);
                
                if (FileStream != null && FileStream.CanWrite && !SafeReadHandle.IsInvalid)
                {
                    FileStream.WriteAsync(outputBuffer, 0, outputBuffer.Length);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private SafeFileHandle OpenHandle(string devicePathName, bool isExclusive)
            => NativeMethods.CreateFile(
                devicePathName, 
                NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE, 
                isExclusive ? 0 : NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethods.OpenExisting,
                0x20000000 | 0x80000000 | 0x100 | NativeMethods.FILE_FLAG_OVERLAPPED, 
                0);

        public bool ReadFeatureData(byte[] inputBuffer)
            => NativeMethods.HidD_GetFeature(SafeReadHandle.DangerousGetHandle(), inputBuffer, inputBuffer.Length);

        public void ResetSerial() => Serial = null;
        public string ReadSerial()
        {
            if (Serial != null)
                return Serial;

            // Some devices don't have MAC address (especially gamepads with USB only suports in PC). If the serial number reading fails 
            // then use dummy zero MAC address, because there is a good chance the gamepad stll works in DS4Windows app (the code would throw
            // an index out of bounds exception anyway without IF-THEN-ELSE checks after trying to read a serial number).

            if (Capabilities.InputReportByteLength == 64)
            {
                byte[] buffer = new byte[16];
                buffer[0] = 18;
                if (ReadFeatureData(buffer))
                {
                    Serial = $"{buffer[6]:X02}:{buffer[5]:X02}:{buffer[4]:X02}:{buffer[3]:X02}:{buffer[2]:X02}:{buffer[1]:X02}";
                    return Serial;
                }
            }
            else
            {
                byte[] buffer = new byte[126];
#if WIN64
                ulong bufferLen = 126;
#else
                uint bufferLen = 126;
#endif
                if (NativeMethods.HidD_GetSerialNumberString(SafeReadHandle.DangerousGetHandle(), buffer, bufferLen))
                {
                    string mac = System.Text.Encoding.Unicode.GetString(buffer).Replace("\0", string.Empty).ToUpper();
                    Serial = $"{mac[0]}{mac[1]}:{mac[2]}{mac[3]}:{mac[4]}{mac[5]}:{mac[6]}{mac[7]}:{mac[8]}{mac[9]}:{mac[10]}{mac[11]}";
                    return Serial;
                }
            }

            // If serial# reading failed then generate a dummy MAC address based on HID device path (WinOS generated runtime unique value based on connected usb port and hub or BT channel).
            // The device path remains the same as long the gamepad is always connected to the same usb/BT port, but may be different in other usb ports. Therefore this value is unique
            // as long the same device is always connected to the same usb port.

            AppLogger.LogToGui($"WARNING: Failed to read serial# from a gamepad ({Attributes.VendorHexId}/{Attributes.ProductHexId}). Generating MAC address from a device path. From now on you should connect this gamepad always into the same USB port or BT pairing host to keep the same device path.", true);
            
            GenerateSerial();

            return Serial;
        }

        private void GenerateSerial()
        {
            try
            {
                string mac = string.Empty;

                // Substring: \\?\hid#vid_054c&pid_09cc&mi_03#7&1f882A25&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030} -> \\?\hid#vid_054c&pid_09cc&mi_03#7&1f882A25&0&0001#
                int endPos = DevicePath.LastIndexOf('{');
                if (endPos < 0)
                    endPos = DevicePath.Length;

                // String array: \\?\hid#vid_054c&pid_09cc&mi_03#7&1f882A25&0&0001# -> [0]=\\?\hidvid_054c, [1]=pid_09cc, [2]=mi_037, [3]=1f882A25, [4]=0, [5]=0001
                string[] devPathItems = DevicePath.Substring(0, endPos).Replace("#", "").Replace("-", "").Replace("{", "").Replace("}", "").Split('&');

                if (devPathItems.Length >= 3)
                    mac = devPathItems[devPathItems.Length - 3].ToUpper()                 // 1f882A25
                        + devPathItems[devPathItems.Length - 2].ToUpper()                 // 0
                        + devPathItems[devPathItems.Length - 1].TrimStart('0').ToUpper(); // 0001 -> 1

                else if (devPathItems.Length >= 1)
                    // Device and usb hub and port identifiers missing in devicePath string. Fallback to use vendor and product ID values and 
                    // take a number from the last part of the devicePath. Hopefully the last part is a usb port number as it usually should be.
                    mac = Attributes.VendorId.ToString("X4")
                        + Attributes.ProductId.ToString("X4")
                        + devPathItems[devPathItems.Length - 1].TrimStart('0').ToUpper();

                if (!string.IsNullOrEmpty(mac))
                {
                    mac = mac.PadRight(12, '0');
                    Serial = $"{mac[0]}{mac[1]}:{mac[2]}{mac[3]}:{mac[4]}{mac[5]}:{mac[6]}{mac[7]}:{mac[8]}{mac[9]}:{mac[10]}{mac[11]}";
                }
                else
                    // Hmm... Shold never come here. Strange format in devicePath because all identifier items of devicePath string are missing.
                    Serial = BLANK_SERIAL;
            }
            catch (Exception e)
            {
                AppLogger.LogToGui($"ERROR: Failed to generate runtime MAC address from device path {DevicePath}. {e.Message}", true);
                Serial = BLANK_SERIAL;
            }
        }
    }
}
