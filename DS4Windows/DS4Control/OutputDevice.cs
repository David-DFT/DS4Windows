namespace DS4Windows
{
    public abstract class OutputDevice
    {
        public abstract void ConvertAndSendReport(DS4State state, int device);
        public abstract void Connect();
        public abstract void Disconnect();
        public abstract string GetDeviceType();
    }
}
