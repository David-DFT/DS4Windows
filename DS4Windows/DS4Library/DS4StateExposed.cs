
namespace DS4Windows
{
    public class DS4StateExposed
    {
        public DS4StateExposed() => _state = new DS4State();
        public DS4StateExposed(DS4State state) => _state = state;

        private readonly DS4State _state;

        public bool Square => _state.Square;
        public bool Triangle => _state.Triangle;
        public bool Circle => _state.Circle;
        public bool Cross => _state.Cross;

        public bool DpadUp => _state.DpadUp;
        public bool DpadDown => _state.DpadDown;
        public bool DpadLeft => _state.DpadLeft;
        public bool DpadRight => _state.DpadRight;

        public bool L1 => _state.L1;
        public bool L3 => _state.L3;

        public bool R1 => _state.R1;
        public bool R3 => _state.R3;

        public bool Share => _state.Share;
        public bool Options => _state.Options;
        public bool PS => _state.PS;

        public bool Touch1 => _state.Touch1;
        public bool Touch2 => _state.Touch2;
        public bool TouchButton => _state.TouchButton;
        public bool Touch1Finger => _state.Touch1Finger;
        public bool Touch2Fingers => _state.Touch2Fingers;

        public byte LX => _state.LX;
        public byte RX => _state.RX;

        public byte LY => _state.LY;
        public byte RY => _state.RY;

        public byte L2 => _state.L2;
        public byte R2 => _state.R2;

        public int Battery => _state.Battery;

        public int GyroYaw => _state.Motion.gyroYaw;
        public int GyroPitch => _state.Motion.gyroPitch;
        public int GyroRoll => _state.Motion.gyroRoll;

        public int AccelX => _state.Motion.accelX;
        public int AccelY => _state.Motion.accelY;
        public int AccelZ => _state.Motion.accelZ;

        public int OutputAccelX => _state.Motion.outputAccelX;
        public int OutputAccelY => _state.Motion.outputAccelY;
        public int OutputAccelZ => _state.Motion.outputAccelZ;
    }
}
