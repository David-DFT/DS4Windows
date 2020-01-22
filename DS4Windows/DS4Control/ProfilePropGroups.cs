namespace DS4Windows
{
    public class SquareStickInfo
    {
        public bool LsMode;
        public bool RsMode;
        public double LsRoundness = 5.0;
        public double RsRoundness = 5.0;
    }

    public class StickDeadZoneInfo
    {
        public int DeadZone;
        public int AntiDeadZone;
        public int MaxZone = 100;
        public double MaxOutput = 100.0;
    }

    public class TriggerDeadZoneZInfo
    {
        public byte DeadZone;// Trigger deadzone is expressed in axis units
        public int AntiDeadZone;
        public int MaxZone = 100;
        public double MaxOutput = 100.0;
    }

    public class GyroMouseInfo
    {

    }

    public class GyroMouseStickInfo
    {
        public int DeadZone;
        public int MaxZone;
        public double AntiDeadX;
        public double AntiDeadY;
        public int VertScale;
        // Flags representing invert axis choices
        public uint Inverted;
        public bool UseSmoothing;
        public double SmoothWeight;
    }
}