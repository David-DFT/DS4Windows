using System;

namespace DS4Windows
{
    public delegate void SixAxisHandler<TEventArgs>(DS4SixAxis sender, TEventArgs args);

    public class SixAxisEventArgs : EventArgs
    {
        public SixAxis SixAxis { get; }
        public DateTime TimeStamp { get; }

        public SixAxisEventArgs(DateTime utcTimestamp, SixAxis sa)
        {
            SixAxis = sa;
            TimeStamp = utcTimestamp;
        }
    }

    public class SixAxis
    {
        public const int ACC_RES_PER_G = 8192;
        private const float F_ACC_RES_PER_G = ACC_RES_PER_G;
        public const int GYRO_RES_IN_DEG_SEC = 16;
        private const float F_GYRO_RES_IN_DEG_SEC = GYRO_RES_IN_DEG_SEC;

        public int GyroYaw, GyroPitch, GyroRoll;
        public int AccelX, AccelY, AccelZ;
        public int OutputAccelX, OutputAccelY, OutputAccelZ;
        public double AccelXG, AccelYG, AccelZG;
        public double AngVelYaw, AngVelPitch, AngVelRoll;
        public int GyroYawFull, GyroPitchFull, GyroRollFull;
        public int AccelXFull, AccelYFull, AccelZFull;

        public double Elapsed;
        public SixAxis PreviousAxis = null;

        public SixAxis(
            int X, int Y, int Z,
            int aX, int aY, int aZ,
            double elapsedDelta, 
            SixAxis prevAxis = null)
            => Populate(X, Y, Z, aX, aY, aZ, elapsedDelta, prevAxis);

        public void Copy(SixAxis src)
        {
            GyroYaw = src.GyroYaw;
            GyroPitch = src.GyroPitch;
            GyroRoll = src.GyroRoll;

            GyroYawFull = src.GyroYawFull;
            AccelXFull = src.AccelXFull; AccelYFull = src.AccelYFull; AccelZFull = src.AccelZFull;

            AngVelYaw = src.AngVelYaw;
            AngVelPitch = src.AngVelPitch;
            AngVelRoll = src.AngVelRoll;

            AccelXG = src.AccelXG;
            AccelYG = src.AccelYG;
            AccelZG = src.AccelZG;

            // Put accel ranges between 0 - 128 abs
            AccelX = src.AccelX;
            AccelY = src.AccelY;
            AccelZ = src.AccelZ;
            OutputAccelX = AccelX;
            OutputAccelY = AccelY;
            OutputAccelZ = AccelZ;

            Elapsed = src.Elapsed;
            PreviousAxis = src.PreviousAxis;
        }

        public void Populate(
            int X, int Y, int Z,
            int aX, int aY, int aZ,
            double elapsedDelta,
            SixAxis prevAxis = null)
        {
            GyroYaw = -X / 256;
            GyroPitch = Y / 256;
            GyroRoll = -Z / 256;

            GyroYawFull = -X; GyroPitchFull = Y; GyroRollFull = -Z;
            AccelXFull = -aX; AccelYFull = -aY; AccelZFull = aZ;

            AngVelYaw = GyroYawFull / F_GYRO_RES_IN_DEG_SEC;
            AngVelPitch = GyroPitchFull / F_GYRO_RES_IN_DEG_SEC;
            AngVelRoll = GyroRollFull / F_GYRO_RES_IN_DEG_SEC;

            AccelXG = AccelXFull / F_ACC_RES_PER_G;
            AccelYG = AccelYFull / F_ACC_RES_PER_G;
            AccelZG = AccelZFull / F_ACC_RES_PER_G;

            // Put accel ranges between 0 - 128 abs
            AccelX = -aX / 64;
            AccelY = -aY / 64;
            AccelZ = aZ / 64;

            OutputAccelX = AccelX;
            OutputAccelY = AccelY;
            OutputAccelZ = AccelZ;

            Elapsed = elapsedDelta;
            PreviousAxis = prevAxis;
        }
    }

    internal sealed class CalibData
    {
        public const int GyroPitchIdx = 0, GyroYawIdx = 1, GyroRollIdx = 2, AccelXIdx = 3, AccelYIdx = 4, AccelZIdx = 5;

        public int Bias { get; set; }
        public int SensNumer { get; set; }
        public int SensDenom { get; set; }
    }

    public class DS4SixAxis
    {
        public event SixAxisHandler<SixAxisEventArgs> SixAccelMoved = null;

        private SixAxis Previous { get; } = new SixAxis(0, 0, 0, 0, 0, 0, 0.0);
        private SixAxis Current { get; } = new SixAxis(0, 0, 0, 0, 0, 0, 0.0);

        private CalibData[] CalibData { get;} = new CalibData[6] 
        {
            new CalibData(),
            new CalibData(),
            new CalibData(),
            new CalibData(),
            new CalibData(),
            new CalibData()
        };
        private bool calibDone = false;

        public void SetCalibrationData(ref byte[] calibData, bool fromUSB)
        {
            int pitchPlus, pitchMinus, yawPlus, yawMinus, rollPlus, rollMinus,
                accelXPlus, accelXMinus, accelYPlus, accelYMinus, accelZPlus, accelZMinus,
                gyroSpeedPlus, gyroSpeedMinus;

            CalibData[0].Bias = (short)((ushort)(calibData[2] << 8) | calibData[1]);
            CalibData[1].Bias = (short)((ushort)(calibData[4] << 8) | calibData[3]);
            CalibData[2].Bias = (short)((ushort)(calibData[6] << 8) | calibData[5]);

            if (!fromUSB)
            {
                pitchPlus = (short)((ushort)(calibData[8] << 8) | calibData[7]);
                yawPlus = (short)((ushort)(calibData[10] << 8) | calibData[9]);
                rollPlus = (short)((ushort)(calibData[12] << 8) | calibData[11]);
                pitchMinus = (short)((ushort)(calibData[14] << 8) | calibData[13]);
                yawMinus = (short)((ushort)(calibData[16] << 8) | calibData[15]);
                rollMinus = (short)((ushort)(calibData[18] << 8) | calibData[17]);
            }
            else
            {
                pitchPlus = (short)((ushort)(calibData[8] << 8) | calibData[7]);
                pitchMinus = (short)((ushort)(calibData[10] << 8) | calibData[9]);
                yawPlus = (short)((ushort)(calibData[12] << 8) | calibData[11]);
                yawMinus = (short)((ushort)(calibData[14] << 8) | calibData[13]);
                rollPlus = (short)((ushort)(calibData[16] << 8) | calibData[15]);
                rollMinus = (short)((ushort)(calibData[18] << 8) | calibData[17]);
            }

            gyroSpeedPlus = (short)((ushort)(calibData[20] << 8) | calibData[19]);
            gyroSpeedMinus = (short)((ushort)(calibData[22] << 8) | calibData[21]);
            accelXPlus = (short)((ushort)(calibData[24] << 8) | calibData[23]);
            accelXMinus = (short)((ushort)(calibData[26] << 8) | calibData[25]);

            accelYPlus = (short)((ushort)(calibData[28] << 8) | calibData[27]);
            accelYMinus = (short)((ushort)(calibData[30] << 8) | calibData[29]);

            accelZPlus = (short)((ushort)(calibData[32] << 8) | calibData[31]);
            accelZMinus = (short)((ushort)(calibData[34] << 8) | calibData[33]);

            int gyroSpeed2x = (gyroSpeedPlus + gyroSpeedMinus);
            CalibData[0].SensNumer = gyroSpeed2x* SixAxis.GYRO_RES_IN_DEG_SEC;
            CalibData[0].SensDenom = pitchPlus - pitchMinus;

            CalibData[1].SensNumer = gyroSpeed2x* SixAxis.GYRO_RES_IN_DEG_SEC;
            CalibData[1].SensDenom = yawPlus - yawMinus;

            CalibData[2].SensNumer = gyroSpeed2x* SixAxis.GYRO_RES_IN_DEG_SEC;
            CalibData[2].SensDenom = rollPlus - rollMinus;

            int accelRange = accelXPlus - accelXMinus;
            CalibData[3].Bias = accelXPlus - accelRange / 2;
            CalibData[3].SensNumer = 2 * SixAxis.ACC_RES_PER_G;
            CalibData[3].SensDenom = accelRange;

            accelRange = accelYPlus - accelYMinus;
            CalibData[4].Bias = accelYPlus - accelRange / 2;
            CalibData[4].SensNumer = 2 * SixAxis.ACC_RES_PER_G;
            CalibData[4].SensDenom = accelRange;

            accelRange = accelZPlus - accelZMinus;
            CalibData[5].Bias = accelZPlus - accelRange / 2;
            CalibData[5].SensNumer = 2 * SixAxis.ACC_RES_PER_G;
            CalibData[5].SensDenom = accelRange;

            // Check that denom will not be zero.
            calibDone = CalibData[0].SensDenom != 0 &&
                CalibData[1].SensDenom != 0 &&
                CalibData[2].SensDenom != 0 &&
                accelRange != 0;
        }

        private void ApplyCalibs(
            ref int yaw, ref int pitch, ref int roll,
            ref int accelX, ref int accelY, ref int accelZ)
        {
            Calib(ref pitch, CalibData[0]);
            Calib(ref yaw, CalibData[1]);
            Calib(ref roll, CalibData[2]);
            Calib(ref accelX, CalibData[3]);
            Calib(ref accelY, CalibData[4]);
            Calib(ref accelZ, CalibData[5]);
        }

        private void Calib(ref int value, CalibData current)
            => value = (int)((value - current.Bias) * (current.SensNumer / (float)current.SensDenom));

        public void HandleSixAxis(byte[] gyro, byte[] accel, DS4State state, double elapsedDelta)
        {
            int currentYaw = (short)((ushort)(gyro[3] << 8) | gyro[2]);
            int currentPitch = (short)((ushort)(gyro[1] << 8) | gyro[0]);
            int currentRoll = (short)((ushort)(gyro[5] << 8) | gyro[4]);
            int AccelX = (short)((ushort)(accel[1] << 8) | accel[0]);
            int AccelY = (short)((ushort)(accel[3] << 8) | accel[2]);
            int AccelZ = (short)((ushort)(accel[5] << 8) | accel[4]);

            if (calibDone)
                ApplyCalibs(ref currentYaw, ref currentPitch, ref currentRoll, ref AccelX, ref AccelY, ref AccelZ);

            if (AccelX != 0 || AccelY != 0 || AccelZ != 0)
            {
                if (SixAccelMoved != null)
                {
                    Previous.Copy(Current);
                    Current.Populate(currentYaw, currentPitch, currentRoll,
                        AccelX, AccelY, AccelZ, elapsedDelta, Previous);

                    SixAxisEventArgs args = new SixAxisEventArgs(state.ReportTimeStamp, Current);
                    state.Motion = Current;
                    SixAccelMoved(this, args);
                }
            }
        }

        public bool FixupInvertedGyroAxis()
        {
            bool result = false;
            // Some, not all, DS4 rev1 gamepads have an inverted YAW gyro axis calibration value (sensNumber>0 but at the same time sensDenom value is <0 while other two axies have both attributes >0).
            // If this gamepad has YAW calibration with weird mixed values then fix it automatically to workaround inverted YAW axis problem.
            if (CalibData[1].SensNumer > 0 && CalibData[1].SensDenom < 0 &&
                CalibData[0].SensDenom > 0 && CalibData[2].SensDenom > 0)
            {
                CalibData[1].SensDenom *= -1;
                result = true; // Fixed inverted axis
            }
            return result;
        }

    }
}
