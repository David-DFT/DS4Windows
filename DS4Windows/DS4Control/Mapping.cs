﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using static DS4Windows.Global;
using System.Drawing; // Point struct

namespace DS4Windows
{
    public class Mapping
    {
        /// <summary>
        /// Represent the synthetic keyboard and mouse events. Maintain counts for each so we don't duplicate events.
        /// </summary>
        public class SyntheticState
        {
            public struct MouseClick
            {
                public int LeftCount, MiddleCount, RightCount, FourthCount, FifthCount, WheelUpCount, WheelDownCount, ToggleCount;
                public bool Toggle;
            }
            public MouseClick previousClicks, CurrentClicks;
            public struct KeyPress
            {
                public int VkCount, ScanCodeCount, RepeatCount, ToggleCount; // repeat takes priority over non-, and scancode takes priority over non-
                public bool Toggle;
            }
            public class KeyPresses
            {
                public KeyPress Previous, Current;
            }
            public Dictionary<ushort, KeyPresses> KeyPressStates = new Dictionary<ushort, KeyPresses>();

            public void SaveToPrevious(bool performClear)
            {
                previousClicks = CurrentClicks;
                if (performClear)
                    CurrentClicks.LeftCount = CurrentClicks.MiddleCount = CurrentClicks.RightCount = CurrentClicks.FourthCount = CurrentClicks.FifthCount = CurrentClicks.WheelUpCount = CurrentClicks.WheelDownCount = CurrentClicks.ToggleCount = 0;

                //foreach (KeyPresses kp in keyPresses.Values)
                Dictionary<ushort, KeyPresses>.ValueCollection keyValues = KeyPressStates.Values;
                for (var keyEnum = keyValues.GetEnumerator(); keyEnum.MoveNext();)
                //for (int i = 0, kpCount = keyValues.Count; i < kpCount; i++)
                {
                    //KeyPresses kp = keyValues.ElementAt(i);
                    KeyPresses kp = keyEnum.Current;
                    kp.Previous = kp.Current;
                    if (performClear)
                    {
                        kp.Current.RepeatCount = kp.Current.ScanCodeCount = kp.Current.VkCount = kp.Current.ToggleCount = 0;
                        //kp.current.toggle = false;
                    }
                }
            }
        }

        public class ActionState
        {
            public bool[] dev = new bool[4];
        }

        private struct ControlToXInput
        {
            public DS4Controls DS4Input;
            public DS4Controls XOutput;

            public ControlToXInput(DS4Controls input, DS4Controls output)
            {
                DS4Input = input; XOutput = output;
            }
        }

        private static Queue<ControlToXInput>[] CustomMapQueue = new Queue<ControlToXInput>[4]
        {
            new Queue<ControlToXInput>(), new Queue<ControlToXInput>(),
            new Queue<ControlToXInput>(), new Queue<ControlToXInput>()
        };

        private struct DS4Vector2
        {
            public double X;
            public double Y;

            public DS4Vector2(double x, double y)
            {
                X = x;
                Y = y;
            }
        }

        private class DS4SquareStick
        {
            public DS4Vector2 current;
            public DS4Vector2 squared;

            public DS4SquareStick()
            {
                current = new DS4Vector2(0.0, 0.0);
                squared = new DS4Vector2(0.0, 0.0);
            }

            public void CircleToSquare(double roundness)
            {
                const double PiOverFour = Math.PI / 4.0;

                // Determine the theta angle
                double angle = Math.Atan2(current.Y, -current.X) + Math.PI;
                double cosAng = Math.Cos(angle);

                // Scale according to which wall we're clamping to

                // X+ wall
                if (angle <= PiOverFour || angle > 7.0 * PiOverFour)
                {
                    double tempVal = 1.0 / cosAng;
                    squared.X = current.X * tempVal;
                    squared.Y = current.Y * tempVal;
                }
                // Y+ wall
                else if (angle > PiOverFour && angle <= 3.0 * PiOverFour)
                {
                    double tempVal = 1.0 / Math.Sin(angle);
                    squared.X = current.X * tempVal;
                    squared.Y = current.Y * tempVal;
                }
                // X- wall
                else if (angle > 3.0 * PiOverFour && angle <= 5.0 * PiOverFour)
                {
                    double tempVal = -1.0 / cosAng;
                    squared.X = current.X * tempVal;
                    squared.Y = current.Y * tempVal;
                }
                // Y- wall
                else if (angle > 5.0 * PiOverFour && angle <= 7.0 * PiOverFour)
                {
                    double tempVal = -1.0 / Math.Sin(angle);
                    squared.X = current.X * tempVal;
                    squared.Y = current.Y * tempVal;
                }
                else 
                    return;

                double length = current.X / cosAng;
                double factor = Math.Pow(length, roundness);

                current.X += (squared.X - current.X) * factor;
                current.Y += (squared.Y - current.Y) * factor;
            }
        }

        private static DS4SquareStick[] OutSqrStk { get; } = new DS4SquareStick[4]
        {
            new DS4SquareStick(),
            new DS4SquareStick(),
            new DS4SquareStick(),
            new DS4SquareStick()
        };

        public static byte[] GyroStickX { get; } = new byte[4] { 128, 128, 128, 128 };
        public static byte[] GyroStickY { get; } = new byte[4] { 128, 128, 128, 128 };

        private static ReaderWriterLockSlim SyncStateLock = new ReaderWriterLockSlim();

        public static SyntheticState GlobalState { get; } = new SyntheticState();
        public static SyntheticState[] DeviceState { get; } = new SyntheticState[4]
        {
            new SyntheticState(), 
            new SyntheticState(), 
            new SyntheticState(),
            new SyntheticState() 
        };
        public static DS4StateFieldMapping[] FieldMappings { get; } = new DS4StateFieldMapping[4] 
        {
            new DS4StateFieldMapping(),
            new DS4StateFieldMapping(),
            new DS4StateFieldMapping(),
            new DS4StateFieldMapping()
        };
        public static DS4StateFieldMapping[] OutputFieldMappings { get; } = new DS4StateFieldMapping[4]
        {
            new DS4StateFieldMapping(),
            new DS4StateFieldMapping(),
            new DS4StateFieldMapping(),
            new DS4StateFieldMapping()
        };
        public static DS4StateFieldMapping[] PreviousFieldMappings { get; } = new DS4StateFieldMapping[4]
        {
            new DS4StateFieldMapping(),
            new DS4StateFieldMapping(),
            new DS4StateFieldMapping(),
            new DS4StateFieldMapping()
        };

        // TODO When we disconnect, process a null/dead state to release any keys or buttons.
        public static DateTime oldnow = DateTime.UtcNow;
        private static bool pressagain = false;
        private static int wheel = 0, keyshelddown = 0;

        //mapcustom
        public static bool[] PressedOnce = new bool[261], MacroDone = new bool[38];
        private static bool[] macroControl = new bool[25];
        private static uint macroCount = 0;
        private static Dictionary<string, Task>[] macroTaskQueue = new Dictionary<string, Task>[4] { new Dictionary<string, Task>(), new Dictionary<string, Task>(), new Dictionary<string, Task>(), new Dictionary<string, Task>() };

        //actions
        public static int[] fadetimer = new int[4] { 0, 0, 0, 0 };
        public static int[] prevFadetimer = new int[4] { 0, 0, 0, 0 };
        public static DS4Color[] lastColor = new DS4Color[4];
        public static List<ActionState> actionDone = new List<ActionState>();
        public static SpecialAction[] untriggeraction = new SpecialAction[4];
        public static DateTime[] nowAction = { DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue };
        public static DateTime[] oldnowAction = { DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue };
        public static int[] untriggerindex = new int[4] { -1, -1, -1, -1 };
        public static DateTime[] oldnowKeyAct = new DateTime[4] { DateTime.MinValue,
            DateTime.MinValue, DateTime.MinValue, DateTime.MinValue };

        private static DS4Controls[] shiftTriggerMapping = new DS4Controls[26] { DS4Controls.None, DS4Controls.Cross, DS4Controls.Circle, DS4Controls.Square,
            DS4Controls.Triangle, DS4Controls.Options, DS4Controls.Share, DS4Controls.DpadUp, DS4Controls.DpadDown,
            DS4Controls.DpadLeft, DS4Controls.DpadRight, DS4Controls.PS, DS4Controls.L1, DS4Controls.R1, DS4Controls.L2,
            DS4Controls.R2, DS4Controls.L3, DS4Controls.R3, DS4Controls.TouchLeft, DS4Controls.TouchUpper, DS4Controls.TouchMulti,
            DS4Controls.TouchRight, DS4Controls.GyroZNeg, DS4Controls.GyroZPos, DS4Controls.GyroXPos, DS4Controls.GyroXNeg,
        };

        private static int[] ds4ControlMapping = new int[38] 
        {
            0, // DS4Control.None
            16, // DS4Controls.LXNeg
            20, // DS4Controls.LXPos
            17, // DS4Controls.LYNeg
            21, // DS4Controls.LYPos
            18, // DS4Controls.RXNeg
            22, // DS4Controls.RXPos
            19, // DS4Controls.RYNeg
            23, // DS4Controls.RYPos
            3,  // DS4Controls.L1
            24, // DS4Controls.L2
            5,  // DS4Controls.L3
            4,  // DS4Controls.R1
            25, // DS4Controls.R2
            6,  // DS4Controls.R3
            13, // DS4Controls.Square
            14, // DS4Controls.Triangle
            15, // DS4Controls.Circle
            12, // DS4Controls.Cross
            7,  // DS4Controls.DpadUp
            10, // DS4Controls.DpadRight
            8,  // DS4Controls.DpadDown
            9,  // DS4Controls.DpadLeft
            11, // DS4Controls.PS
            27, // DS4Controls.TouchLeft
            29, // DS4Controls.TouchUpper
            26, // DS4Controls.TouchMulti
            28, // DS4Controls.TouchRight
            1,  // DS4Controls.Share
            2,  // DS4Controls.Options
            31, // DS4Controls.GyroXPos
            30, // DS4Controls.GyroXNeg
            33, // DS4Controls.GyroZPos
            32, // DS4Controls.GyroZNeg
            34, // DS4Controls.SwipeLeft
            35, // DS4Controls.SwipeRight
            36, // DS4Controls.SwipeUp
            37  // DS4Controls.SwipeDown
        };

        // Define here to save some time processing.
        // It is enough to feel a difference during gameplay.
        // 201907: Commented out these temp variables because those were not actually used anymore (value was assigned but it was never used anywhere)
        //private static int[] rsOutCurveModeArray = new int[4] { 0, 0, 0, 0 };
        //private static int[] lsOutCurveModeArray = new int[4] { 0, 0, 0, 0 };
        //static bool tempBool = false;
        //private static double[] tempDoubleArray = new double[4] { 0.0, 0.0, 0.0, 0.0 };
        //private static int[] tempIntArray = new int[4] { 0, 0, 0, 0 };

        // Special macros
        private static bool altTabDone = true;
        private static DateTime 
            altTabNow = DateTime.UtcNow,
            oldAltTabNow = DateTime.UtcNow - TimeSpan.FromSeconds(1);

        // Mouse
        public static int mcounter = 34;
        public static int mouseaccel = 0;
        public static int prevmouseaccel = 0;
        private static double horizontalRemainder = 0.0, verticalRemainder = 0.0;
        private const int MOUSESPEEDFACTOR = 48;
        private const double MOUSESTICKOFFSET = 0.0495;

        private static bool[] Held = new bool[4];
        private static int[] OldMouse = new int[4] { -1, -1, -1, -1 };

        public static void Commit(int device)
        {
            SyntheticState state = DeviceState[device];
            SyncStateLock.EnterWriteLock();

            GlobalState.CurrentClicks.LeftCount += state.CurrentClicks.LeftCount - state.previousClicks.LeftCount;
            GlobalState.CurrentClicks.MiddleCount += state.CurrentClicks.MiddleCount - state.previousClicks.MiddleCount;
            GlobalState.CurrentClicks.RightCount += state.CurrentClicks.RightCount - state.previousClicks.RightCount;
            GlobalState.CurrentClicks.FourthCount += state.CurrentClicks.FourthCount - state.previousClicks.FourthCount;
            GlobalState.CurrentClicks.FifthCount += state.CurrentClicks.FifthCount - state.previousClicks.FifthCount;
            GlobalState.CurrentClicks.WheelUpCount += state.CurrentClicks.WheelUpCount - state.previousClicks.WheelUpCount;
            GlobalState.CurrentClicks.WheelDownCount += state.CurrentClicks.WheelDownCount - state.previousClicks.WheelDownCount;
            GlobalState.CurrentClicks.ToggleCount += state.CurrentClicks.ToggleCount - state.previousClicks.ToggleCount;
            GlobalState.CurrentClicks.Toggle = state.CurrentClicks.Toggle;

            if (GlobalState.CurrentClicks.ToggleCount != 0 && GlobalState.previousClicks.ToggleCount == 0 && GlobalState.CurrentClicks.Toggle)
            {
                if (GlobalState.CurrentClicks.LeftCount != 0 && GlobalState.previousClicks.LeftCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_LEFTDOWN);
                if (GlobalState.CurrentClicks.RightCount != 0 && GlobalState.previousClicks.RightCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_RIGHTDOWN);
                if (GlobalState.CurrentClicks.MiddleCount != 0 && GlobalState.previousClicks.MiddleCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_MIDDLEDOWN);
                if (GlobalState.CurrentClicks.FourthCount != 0 && GlobalState.previousClicks.FourthCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONDOWN, 1);
                if (GlobalState.CurrentClicks.FifthCount != 0 && GlobalState.previousClicks.FifthCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONDOWN, 2);
            }
            else if (GlobalState.CurrentClicks.ToggleCount != 0 && GlobalState.previousClicks.ToggleCount == 0 && !GlobalState.CurrentClicks.Toggle)
            {
                if (GlobalState.CurrentClicks.LeftCount != 0 && GlobalState.previousClicks.LeftCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_LEFTUP);
                if (GlobalState.CurrentClicks.RightCount != 0 && GlobalState.previousClicks.RightCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_RIGHTUP);
                if (GlobalState.CurrentClicks.MiddleCount != 0 && GlobalState.previousClicks.MiddleCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_MIDDLEUP);
                if (GlobalState.CurrentClicks.FourthCount != 0 && GlobalState.previousClicks.FourthCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONUP, 1);
                if (GlobalState.CurrentClicks.FifthCount != 0 && GlobalState.previousClicks.FifthCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONUP, 2);
            }

            if (GlobalState.CurrentClicks.ToggleCount == 0 && GlobalState.previousClicks.ToggleCount == 0)
            {
                if (GlobalState.CurrentClicks.LeftCount != 0 && GlobalState.previousClicks.LeftCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_LEFTDOWN);
                else if (GlobalState.CurrentClicks.LeftCount == 0 && GlobalState.previousClicks.LeftCount != 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_LEFTUP);

                if (GlobalState.CurrentClicks.MiddleCount != 0 && GlobalState.previousClicks.MiddleCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_MIDDLEDOWN);
                else if (GlobalState.CurrentClicks.MiddleCount == 0 && GlobalState.previousClicks.MiddleCount != 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_MIDDLEUP);

                if (GlobalState.CurrentClicks.RightCount != 0 && GlobalState.previousClicks.RightCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_RIGHTDOWN);
                else if (GlobalState.CurrentClicks.RightCount == 0 && GlobalState.previousClicks.RightCount != 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_RIGHTUP);

                if (GlobalState.CurrentClicks.FourthCount != 0 && GlobalState.previousClicks.FourthCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONDOWN, 1);
                else if (GlobalState.CurrentClicks.FourthCount == 0 && GlobalState.previousClicks.FourthCount != 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONUP, 1);

                if (GlobalState.CurrentClicks.FifthCount != 0 && GlobalState.previousClicks.FifthCount == 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONDOWN, 2);
                else if (GlobalState.CurrentClicks.FifthCount == 0 && GlobalState.previousClicks.FifthCount != 0)
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONUP, 2);

                if (GlobalState.CurrentClicks.WheelUpCount != 0 && GlobalState.previousClicks.WheelUpCount == 0)
                {
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_WHEEL, 120);
                    oldnow = DateTime.UtcNow;
                    wheel = 120;
                }
                else if (GlobalState.CurrentClicks.WheelUpCount == 0 && GlobalState.previousClicks.WheelUpCount != 0)
                    wheel = 0;

                if (GlobalState.CurrentClicks.WheelDownCount != 0 && GlobalState.previousClicks.WheelDownCount == 0)
                {
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_WHEEL, -120);
                    oldnow = DateTime.UtcNow;
                    wheel = -120;
                }
                if (GlobalState.CurrentClicks.WheelDownCount == 0 && GlobalState.previousClicks.WheelDownCount != 0)
                    wheel = 0;
            }
            

            if (wheel != 0) //Continue mouse wheel movement
            {
                DateTime now = DateTime.UtcNow;
                if (now >= oldnow + TimeSpan.FromMilliseconds(100) && !pressagain)
                {
                    oldnow = now;
                    InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_WHEEL, wheel);
                }
            }

            // Merge and synthesize all key presses/releases that are present in this device's mapping.
            // TODO what about the rest?  e.g. repeat keys really ought to be on some set schedule
            Dictionary<ushort, SyntheticState.KeyPresses>.KeyCollection kvpKeys = state.KeyPressStates.Keys;
            //foreach (KeyValuePair<UInt16, SyntheticState.KeyPresses> kvp in state.keyPresses)
            //for (int i = 0, keyCount = kvpKeys.Count; i < keyCount; i++)
            for (var keyEnum = kvpKeys.GetEnumerator(); keyEnum.MoveNext();)
            {
                //UInt16 kvpKey = kvpKeys.ElementAt(i);
                ushort kvpKey = keyEnum.Current;
                SyntheticState.KeyPresses kvpValue = state.KeyPressStates[kvpKey];

                if (GlobalState.KeyPressStates.TryGetValue(kvpKey, out SyntheticState.KeyPresses gkp))
                {
                    gkp.Current.VkCount += kvpValue.Current.VkCount - kvpValue.Previous.VkCount;
                    gkp.Current.ScanCodeCount += kvpValue.Current.ScanCodeCount - kvpValue.Previous.ScanCodeCount;
                    gkp.Current.RepeatCount += kvpValue.Current.RepeatCount - kvpValue.Previous.RepeatCount;
                    gkp.Current.Toggle = kvpValue.Current.Toggle;
                    gkp.Current.ToggleCount += kvpValue.Current.ToggleCount - kvpValue.Previous.ToggleCount;
                }
                else
                {
                    gkp = new SyntheticState.KeyPresses
                    {
                        Current = kvpValue.Current
                    };
                    GlobalState.KeyPressStates[kvpKey] = gkp;
                }
                if (gkp.Current.ToggleCount != 0 && gkp.Previous.ToggleCount == 0 && gkp.Current.Toggle)
                {
                    if (gkp.Current.ScanCodeCount != 0)
                        InputMethods.performSCKeyPress(kvpKey);
                    else
                        InputMethods.performKeyPress(kvpKey);
                }
                else if (gkp.Current.ToggleCount != 0 && gkp.Previous.ToggleCount == 0 && !gkp.Current.Toggle)
                {
                    if (gkp.Previous.ScanCodeCount != 0) // use the last type of VK/SC
                        InputMethods.performSCKeyRelease(kvpKey);
                    else
                        InputMethods.performKeyRelease(kvpKey);
                }
                else if (gkp.Current.VkCount + gkp.Current.ScanCodeCount != 0 && gkp.Previous.VkCount + gkp.Previous.ScanCodeCount == 0)
                {
                    if (gkp.Current.ScanCodeCount != 0)
                    {
                        oldnow = DateTime.UtcNow;
                        InputMethods.performSCKeyPress(kvpKey);
                        pressagain = false;
                        keyshelddown = kvpKey;
                    }
                    else
                    {
                        oldnow = DateTime.UtcNow;
                        InputMethods.performKeyPress(kvpKey);
                        pressagain = false;
                        keyshelddown = kvpKey;
                    }
                }
                else if (gkp.Current.ToggleCount != 0 || gkp.Previous.ToggleCount != 0 || gkp.Current.RepeatCount != 0 || // repeat or SC/VK transition
                    (gkp.Previous.ScanCodeCount == 0 != (gkp.Current.ScanCodeCount == 0))) //repeat keystroke after 500ms
                {
                    if (keyshelddown == kvpKey)
                    {
                        DateTime now = DateTime.UtcNow;
                        if (now >= oldnow + TimeSpan.FromMilliseconds(500) && !pressagain)
                        {
                            oldnow = now;
                            pressagain = true;
                        }
                        if (pressagain && gkp.Current.ScanCodeCount != 0)
                        {
                            now = DateTime.UtcNow;
                            if (now >= oldnow + TimeSpan.FromMilliseconds(25) && pressagain)
                            {
                                oldnow = now;
                                InputMethods.performSCKeyPress(kvpKey);
                            }
                        }
                        else if (pressagain)
                        {
                            now = DateTime.UtcNow;
                            if (now >= oldnow + TimeSpan.FromMilliseconds(25) && pressagain)
                            {
                                oldnow = now;
                                InputMethods.performKeyPress(kvpKey);
                            }
                        }
                    }
                }
                if (gkp.Current.ToggleCount == 0 && gkp.Previous.ToggleCount == 0 && gkp.Current.VkCount + gkp.Current.ScanCodeCount == 0 && gkp.Previous.VkCount + gkp.Previous.ScanCodeCount != 0)
                {
                    if (gkp.Previous.ScanCodeCount != 0) // use the last type of VK/SC
                    {
                        InputMethods.performSCKeyRelease(kvpKey);
                        pressagain = false;
                    }
                    else
                    {
                        InputMethods.performKeyRelease(kvpKey);
                        pressagain = false;
                    }
                }
            }
            GlobalState.SaveToPrevious(false);

            SyncStateLock.ExitWriteLock();
            state.SaveToPrevious(true);
        }

        public enum EClick
        {
            None,
            Left,
            Middle,
            Right,
            Fourth,
            Fifth,
            WUP,
            WDOWN
        };

        public static void MapClick(int device, EClick mouseClick)
        {
            switch (mouseClick)
            {
                case EClick.Left:
                    DeviceState[device].CurrentClicks.LeftCount++;
                    break;

                case EClick.Middle:
                    DeviceState[device].CurrentClicks.MiddleCount++;
                    break;

                case EClick.Right:
                    DeviceState[device].CurrentClicks.RightCount++;
                    break;

                case EClick.Fourth:
                    DeviceState[device].CurrentClicks.FourthCount++;
                    break;

                case EClick.Fifth:
                    DeviceState[device].CurrentClicks.FifthCount++;
                    break;

                case EClick.WUP:
                    DeviceState[device].CurrentClicks.WheelUpCount++;
                    break;

                case EClick.WDOWN:
                    DeviceState[device].CurrentClicks.WheelDownCount++;
                    break;

                default: 
                    break;
            }
        }

        public static int DS4ControlToInt(DS4Controls ctrl)
        {
            int result = 0;
            if (ctrl >= DS4Controls.None && ctrl <= DS4Controls.SwipeDown)
            {
                result = ds4ControlMapping[(int)ctrl];
            }

            return result;
        }

        private static double Lerp(double value1, double value2, double percent) => (value1 * percent) + (value2 * (1.0 - percent));
        private static int Clamp(int min, int value, int max) => (value < min) ? min : (value > max) ? max : value;

        public static DS4State SetCurveAndDeadzone(int device, DS4State cState, DS4State dState)
        {
            double rotation = GetLSRotation(device);
            if (rotation > 0.0 || rotation < 0.0)
                cState.RotateLSCoordinates(rotation);

            double rotationRS = GetRSRotation(device);
            if (rotationRS > 0.0 || rotationRS < 0.0)
                cState.RotateRSCoordinates(rotationRS);

            cState.CopyTo(dState);

            HandleCurve(GetLSCurve(device), cState.LX, cState.LY, ref dState.LX, ref dState.LY);
            HandleCurve(GetRSCurve(device), cState.RX, cState.RY, ref dState.RX, ref dState.RY);
            
            HandleStickDeadZone(cState.LX, cState.LY, ref dState.LX, ref dState.LY, GetLSDeadZoneInfo(device));
            HandleStickDeadZone(cState.RX, cState.RY, ref dState.RX, ref dState.RY, GetRSDeadZoneInfo(device));

            TriggerDeadZoneZInfo l2ModInfo = GetL2ModInfo(device);
            byte l2Deadzone = l2ModInfo.DeadZone;
            int l2AntiDeadzone = l2ModInfo.AntiDeadZone;
            int l2Maxzone = l2ModInfo.MaxZone;
            double l2MaxOutput = l2ModInfo.MaxOutput;

            if (l2Deadzone > 0 || l2AntiDeadzone > 0 || l2Maxzone != 100 || l2MaxOutput != 100.0)
                HandleL2DeadZone(cState, dState, l2Deadzone, l2AntiDeadzone, l2Maxzone, l2MaxOutput);

            TriggerDeadZoneZInfo r2ModInfo = GetR2ModInfo(device);
            byte r2Deadzone = r2ModInfo.DeadZone;
            int r2AntiDeadzone = r2ModInfo.AntiDeadZone;
            int r2Maxzone = r2ModInfo.MaxZone;
            double r2MaxOutput = r2ModInfo.MaxOutput;
            if (r2Deadzone > 0 || r2AntiDeadzone > 0 || r2Maxzone != 100 || r2MaxOutput != 100.0)
            {
                HandleR2DeadZone(cState, dState, r2Deadzone, r2AntiDeadzone, r2Maxzone, r2MaxOutput);
            }

            double lsSens = GetLSSens(device);
            if (lsSens != 1.0)
            {
                dState.LX = (byte)Global.Clamp(0, lsSens * (dState.LX - 128.0) + 128.0, 255);
                dState.LY = (byte)Global.Clamp(0, lsSens * (dState.LY - 128.0) + 128.0, 255);
            }

            double rsSens = GetRSSens(device);
            if (rsSens != 1.0)
            {
                dState.RX = (byte)Global.Clamp(0, rsSens * (dState.RX - 128.0) + 128.0, 255);
                dState.RY = (byte)Global.Clamp(0, rsSens * (dState.RY - 128.0) + 128.0, 255);
            }

            double l2Sens = GetL2Sens(device);
            if (l2Sens != 1.0)
                dState.L2 = (byte)Global.Clamp(0, l2Sens * dState.L2, 255);

            double r2Sens = GetR2Sens(device);
            if (r2Sens != 1.0)
                dState.R2 = (byte)Global.Clamp(0, r2Sens * dState.R2, 255);

            SquareStickInfo squStk = GetSquareStickInfo(device);
            if (squStk.LsMode && (dState.LX != 128 || dState.LY != 128))
            {
                double capX = dState.LX >= 128 ? 127.0 : 128.0;
                double capY = dState.LY >= 128 ? 127.0 : 128.0;
                double tempX = (dState.LX - 128.0) / capX;
                double tempY = (dState.LY - 128.0) / capY;
                DS4SquareStick sqstick = OutSqrStk[device];
                sqstick.current.X = tempX; sqstick.current.Y = tempY;
                sqstick.CircleToSquare(squStk.LsRoundness);
                //Console.WriteLine("Input ({0}) | Output ({1})", tempY, sqstick.current.y);
                tempX = sqstick.current.X < -1.0 ? -1.0 : sqstick.current.X > 1.0
                    ? 1.0 : sqstick.current.X;
                tempY = sqstick.current.Y < -1.0 ? -1.0 : sqstick.current.Y > 1.0
                    ? 1.0 : sqstick.current.Y;
                dState.LX = (byte)(tempX * capX + 128.0);
                dState.LY = (byte)(tempY * capY + 128.0);
            }

            int lsOutCurveMode = GetLsOutCurveMode(device);
            if (lsOutCurveMode > 0 && (dState.LX != 128 || dState.LY != 128))
            {
                double capX = dState.LX >= 128 ? 127.0 : 128.0;
                double capY = dState.LY >= 128 ? 127.0 : 128.0;
                double tempX = (dState.LX - 128.0) / capX;
                double tempY = (dState.LY - 128.0) / capY;
                double signX = tempX >= 0.0 ? 1.0 : -1.0;
                double signY = tempY >= 0.0 ? 1.0 : -1.0;

                if (lsOutCurveMode == 1)
                {
                    double absX = Math.Abs(tempX);
                    double absY = Math.Abs(tempY);
                    double outputX = 0.0;
                    double outputY = 0.0;

                    if (absX <= 0.4)
                    {
                        outputX = 0.55 * absX;
                    }
                    else if (absX <= 0.75)
                    {
                        outputX = absX - 0.18;
                    }
                    else if (absX > 0.75)
                    {
                        outputX = (absX * 1.72) - 0.72;
                    }

                    if (absY <= 0.4)
                    {
                        outputY = 0.55 * absY;
                    }
                    else if (absY <= 0.75)
                    {
                        outputY = absY - 0.18;
                    }
                    else if (absY > 0.75)
                    {
                        outputY = (absY * 1.72) - 0.72;
                    }

                    dState.LX = (byte)(outputX * signX * capX + 128.0);
                    dState.LY = (byte)(outputY * signY * capY + 128.0);
                }
                else if (lsOutCurveMode == 2)
                {
                    double outputX = tempX * tempX;
                    double outputY = tempY * tempY;
                    dState.LX = (byte)(outputX * signX * capX + 128.0);
                    dState.LY = (byte)(outputY * signY * capY + 128.0);
                }
                else if (lsOutCurveMode == 3)
                {
                    double outputX = tempX * tempX * tempX;
                    double outputY = tempY * tempY * tempY;
                    dState.LX = (byte)(outputX * capX + 128.0);
                    dState.LY = (byte)(outputY * capY + 128.0);
                }
                else if (lsOutCurveMode == 4)
                {
                    double absX = Math.Abs(tempX);
                    double absY = Math.Abs(tempY);
                    double outputX = absX * (absX - 2.0);
                    double outputY = absY * (absY - 2.0);
                    dState.LX = (byte)(-1.0 * outputX * signX * capX + 128.0);
                    dState.LY = (byte)(-1.0 * outputY * signY * capY + 128.0);
                }
                else if (lsOutCurveMode == 5)
                {
                    double innerX = Math.Abs(tempX) - 1.0;
                    double innerY = Math.Abs(tempY) - 1.0;
                    double outputX = innerX * innerX * innerX + 1.0;
                    double outputY = innerY * innerY * innerY + 1.0;
                    dState.LX = (byte)(1.0 * outputX * signX * capX + 128.0);
                    dState.LY = (byte)(1.0 * outputY * signY * capY + 128.0);
                }
                else if (lsOutCurveMode == 6)
                {
                    dState.LX = lsOutBezierCurveObj[device].arrayBezierLUT[dState.LX];
                    dState.LY = lsOutBezierCurveObj[device].arrayBezierLUT[dState.LY];
                }
            }

            if (squStk.RsMode && (dState.RX != 128 || dState.RY != 128))
            {
                double capX = dState.RX >= 128 ? 127.0 : 128.0;
                double capY = dState.RY >= 128 ? 127.0 : 128.0;
                double tempX = (dState.RX - 128.0) / capX;
                double tempY = (dState.RY - 128.0) / capY;
                DS4SquareStick sqstick = OutSqrStk[device];
                sqstick.current.X = tempX; sqstick.current.Y = tempY;
                sqstick.CircleToSquare(squStk.RsRoundness);
                tempX = sqstick.current.X < -1.0 ? -1.0 : sqstick.current.X > 1.0
                    ? 1.0 : sqstick.current.X;
                tempY = sqstick.current.Y < -1.0 ? -1.0 : sqstick.current.Y > 1.0
                    ? 1.0 : sqstick.current.Y;
                //Console.WriteLine("Input ({0}) | Output ({1})", tempY, sqstick.current.y);
                dState.RX = (byte)(tempX * capX + 128.0);
                dState.RY = (byte)(tempY * capY + 128.0);
            }

            int rsOutCurveMode = GetRsOutCurveMode(device);
            if (rsOutCurveMode > 0 && (dState.RX != 128 || dState.RY != 128))
            {
                double capX = dState.RX >= 128 ? 127.0 : 128.0;
                double capY = dState.RY >= 128 ? 127.0 : 128.0;
                double tempX = (dState.RX - 128.0) / capX;
                double tempY = (dState.RY - 128.0) / capY;
                double signX = tempX >= 0.0 ? 1.0 : -1.0;
                double signY = tempY >= 0.0 ? 1.0 : -1.0;

                if (rsOutCurveMode == 1)
                {
                    double absX = Math.Abs(tempX);
                    double absY = Math.Abs(tempY);
                    double outputX = 0.0;
                    double outputY = 0.0;

                    if (absX <= 0.4)
                    {
                        outputX = 0.55 * absX;
                    }
                    else if (absX <= 0.75)
                    {
                        outputX = absX - 0.18;
                    }
                    else if (absX > 0.75)
                    {
                        outputX = (absX * 1.72) - 0.72;
                    }

                    if (absY <= 0.4)
                    {
                        outputY = 0.55 * absY;
                    }
                    else if (absY <= 0.75)
                    {
                        outputY = absY - 0.18;
                    }
                    else if (absY > 0.75)
                    {
                        outputY = (absY * 1.72) - 0.72;
                    }

                    dState.RX = (byte)(outputX * signX * capX + 128.0);
                    dState.RY = (byte)(outputY * signY * capY + 128.0);
                }
                else if (rsOutCurveMode == 2)
                {
                    double outputX = tempX * tempX;
                    double outputY = tempY * tempY;
                    dState.RX = (byte)(outputX * signX * capX + 128.0);
                    dState.RY = (byte)(outputY * signY * capY + 128.0);
                }
                else if (rsOutCurveMode == 3)
                {
                    double outputX = tempX * tempX * tempX;
                    double outputY = tempY * tempY * tempY;
                    dState.RX = (byte)(outputX * capX + 128.0);
                    dState.RY = (byte)(outputY * capY + 128.0);
                }
                else if (rsOutCurveMode == 4)
                {
                    double absX = Math.Abs(tempX);
                    double absY = Math.Abs(tempY);
                    double outputX = absX * (absX - 2.0);
                    double outputY = absY * (absY - 2.0);
                    dState.RX = (byte)(-1.0 * outputX * signX * capX + 128.0);
                    dState.RY = (byte)(-1.0 * outputY * signY * capY + 128.0);
                }
                else if (rsOutCurveMode == 5)
                {
                    double innerX = Math.Abs(tempX) - 1.0;
                    double innerY = Math.Abs(tempY) - 1.0;
                    double outputX = innerX * innerX * innerX + 1.0;
                    double outputY = innerY * innerY * innerY + 1.0;
                    dState.RX = (byte)(1.0 * outputX * signX * capX + 128.0);
                    dState.RY = (byte)(1.0 * outputY * signY * capY + 128.0);
                }
                else if (rsOutCurveMode == 6)
                {
                    dState.RX = rsOutBezierCurveObj[device].arrayBezierLUT[dState.RX];
                    dState.RY = rsOutBezierCurveObj[device].arrayBezierLUT[dState.RY];
                }
            }

            int l2OutCurveMode = GetL2OutCurveMode(device);
            if (l2OutCurveMode > 0 && dState.L2 != 0)
            {
                double temp = dState.L2 / 255.0;
                if (l2OutCurveMode == 1)
                {
                    double output;

                    if (temp <= 0.4)
                        output = 0.55 * temp;
                    else if (temp <= 0.75)
                        output = temp - 0.18;
                    else // if (temp > 0.75)
                        output = (temp * 1.72) - 0.72;
                    dState.L2 = (byte)(output * 255.0);
                }
                else if (l2OutCurveMode == 2)
                {
                    double output = temp * temp;
                    dState.L2 = (byte)(output * 255.0);
                }
                else if (l2OutCurveMode == 3)
                {
                    double output = temp * temp * temp;
                    dState.L2 = (byte)(output * 255.0);
                }
                else if (l2OutCurveMode == 4)
                {
                    double output = temp * (temp - 2.0);
                    dState.L2 = (byte)(-1.0 * output * 255.0);
                }
                else if (l2OutCurveMode == 5)
                {
                    double inner = Math.Abs(temp) - 1.0;
                    double output = inner * inner * inner + 1.0;
                    dState.L2 = (byte)(-1.0 * output * 255.0);
                }
                else if (l2OutCurveMode == 6)
                {
                    dState.L2 = l2OutBezierCurveObj[device].arrayBezierLUT[dState.L2];
                }
            }

            int r2OutCurveMode = getR2OutCurveMode(device);
            if (r2OutCurveMode > 0 && dState.R2 != 0)
            {
                double temp = dState.R2 / 255.0;
                if (r2OutCurveMode == 1)
                {
                    double output;

                    if (temp <= 0.4)
                        output = 0.55 * temp;
                    else if (temp <= 0.75)
                        output = temp - 0.18;
                    else // if (temp > 0.75)
                        output = (temp * 1.72) - 0.72;
                    dState.R2 = (byte)(output * 255.0);
                }
                else if (r2OutCurveMode == 2)
                {
                    double output = temp * temp;
                    dState.R2 = (byte)(output * 255.0);
                }
                else if (r2OutCurveMode == 3)
                {
                    double output = temp * temp * temp;
                    dState.R2 = (byte)(output * 255.0);
                }
                else if (r2OutCurveMode == 4)
                {
                    double output = temp * (temp - 2.0);
                    dState.R2 = (byte)(-1.0 * output * 255.0);
                }
                else if (r2OutCurveMode == 5)
                {
                    double inner = Math.Abs(temp) - 1.0;
                    double output = inner * inner * inner + 1.0;
                    dState.R2 = (byte)(-1.0 * output * 255.0);
                }
                else if (r2OutCurveMode == 6)
                {
                    dState.R2 = r2OutBezierCurveObj[device].arrayBezierLUT[dState.R2];
                }
            }


            bool sOff = IsUsingSAforMouse(device);
            if (sOff == false)
            {
                int SXD = (int)(128d * GetSXDeadzone(device));
                int SZD = (int)(128d * GetSZDeadzone(device));
                double SXMax = GetSXMaxzone(device);
                double SZMax = GetSZMaxzone(device);
                double sxAntiDead = GetSXAntiDeadzone(device);
                double szAntiDead = GetSZAntiDeadzone(device);
                double sxsens = getSXSens(device);
                double szsens = getSZSens(device);
                int result = 0;

                int gyroX = cState.Motion.AccelX, gyroZ = cState.Motion.AccelZ;
                int absx = Math.Abs(gyroX), absz = Math.Abs(gyroZ);

                if (SXD > 0 || SXMax < 1.0 || sxAntiDead > 0)
                {
                    int maxValue = (int)(SXMax * 128d);
                    if (absx > SXD)
                    {
                        double ratioX = absx < maxValue ? (absx - SXD) / (double)(maxValue - SXD) : 1.0;
                        dState.Motion.OutputAccelX = Math.Sign(gyroX) * (int)Math.Min(128d, sxsens * 128d * ((1.0 - sxAntiDead) * ratioX + sxAntiDead));
                    }
                    else
                    {
                        dState.Motion.OutputAccelX = 0;
                    }
                }
                else
                {
                    dState.Motion.OutputAccelX = Math.Sign(gyroX) * (int)Math.Min(128d, sxsens * 128d * (absx / 128d));
                }

                if (SZD > 0 || SZMax < 1.0 || szAntiDead > 0)
                {
                    int maxValue = (int)(SZMax * 128d);
                    if (absz > SZD)
                    {
                        double ratioZ = absz < maxValue ? (absz - SZD) / (double)(maxValue - SZD) : 1.0;
                        dState.Motion.OutputAccelZ = Math.Sign(gyroZ) * (int)Math.Min(128d, szsens * 128d * ((1.0 - szAntiDead) * ratioZ + szAntiDead));
                    }
                    else
                    {
                        dState.Motion.OutputAccelZ = 0;
                    }
                }
                else
                {
                    dState.Motion.OutputAccelZ = Math.Sign(gyroZ) * (int)Math.Min(128d, szsens * 128d * (absz / 128d));
                }

                int sxOutCurveMode = GetSXOutCurveMode(device);
                if (sxOutCurveMode > 0)
                {
                    double temp = dState.Motion.OutputAccelX / 128.0;
                    double sign = Math.Sign(temp);
                    if (sxOutCurveMode == 1)
                    {
                        double output;
                        double abs = Math.Abs(temp);

                        if (abs <= 0.4)
                            output = 0.55 * abs;
                        else if (abs <= 0.75)
                            output = abs - 0.18;
                        else // if (abs > 0.75)
                            output = (abs * 1.72) - 0.72;
                        dState.Motion.OutputAccelX = (byte)(output * sign * 128.0);
                    }
                    else if (sxOutCurveMode == 2)
                    {
                        double output = temp * temp;
                        result = (int)(output * sign * 128.0);
                        dState.Motion.OutputAccelX = result;
                    }
                    else if (sxOutCurveMode == 3)
                    {
                        double output = temp * temp * temp;
                        result = (int)(output * 128.0);
                        dState.Motion.OutputAccelX = result;
                    }
                    else if (sxOutCurveMode == 4)
                    {
                        double abs = Math.Abs(temp);
                        double output = abs * (abs - 2.0);
                        dState.Motion.OutputAccelX = (byte)(-1.0 * output *
                            sign * 128.0);
                    }
                    else if (sxOutCurveMode == 5)
                    {
                        double inner = Math.Abs(temp) - 1.0;
                        double output = inner * inner * inner + 1.0;
                        dState.Motion.OutputAccelX = (byte)(-1.0 * output * 255.0);
                    }
                    else if (sxOutCurveMode == 6)
                    {
                        int signSA = Math.Sign(dState.Motion.OutputAccelX);
                        dState.Motion.OutputAccelX = sxOutBezierCurveObj[device].arrayBezierLUT[Math.Min(Math.Abs(dState.Motion.OutputAccelX), 128)] * signSA;
                    }
                }

                int szOutCurveMode = GetSZOutCurveMode(device);
                if (szOutCurveMode > 0 && dState.Motion.OutputAccelZ != 0)
                {
                    double temp = dState.Motion.OutputAccelZ / 128.0;
                    double sign = Math.Sign(temp);
                    if (szOutCurveMode == 1)
                    {
                        double output;
                        double abs = Math.Abs(temp);

                        if (abs <= 0.4)
                            output = 0.55 * abs;
                        else if (abs <= 0.75)
                            output = abs - 0.18;
                        else // if (abs > 0.75)
                            output = (abs * 1.72) - 0.72;
                        dState.Motion.OutputAccelZ = (byte)(output * sign * 128.0);
                    }
                    else if (szOutCurveMode == 2)
                    {
                        double output = temp * temp;
                        result = (int)(output * sign * 128.0);
                        dState.Motion.OutputAccelZ = result;
                    }
                    else if (szOutCurveMode == 3)
                    {
                        double output = temp * temp * temp;
                        result = (int)(output * 128.0);
                        dState.Motion.OutputAccelZ = result;
                    }
                    else if (szOutCurveMode == 4)
                    {
                        double abs = Math.Abs(temp);
                        double output = abs * (abs - 2.0);
                        dState.Motion.OutputAccelZ = (byte)(-1.0 * output *
                            sign * 128.0);
                    }
                    else if (szOutCurveMode == 5)
                    {
                        double inner = Math.Abs(temp) - 1.0;
                        double output = inner * inner * inner + 1.0;
                        dState.Motion.OutputAccelZ = (byte)(-1.0 * output * 255.0);
                    }
                    else if (szOutCurveMode == 6)
                    {
                        int signSA = Math.Sign(dState.Motion.OutputAccelZ);
                        dState.Motion.OutputAccelZ = szOutBezierCurveObj[device].arrayBezierLUT[Math.Min(Math.Abs(dState.Motion.OutputAccelZ), 128)] * signSA;
                    }
                }
            }

            return dState;
        }

        private static void HandleR2DeadZone(DS4State cState, DS4State dState, byte r2Deadzone, int r2AntiDeadzone, int r2Maxzone, double r2MaxOutput)
        {
            double tempR2Output = cState.R2 / 255.0;
            double tempR2AntiDead = 0.0;
            double ratio = r2Maxzone / 100.0;
            double maxValue = 255 * ratio;

            if (r2Deadzone > 0)
            {
                if (cState.R2 > r2Deadzone)
                {
                    double current = Global.Clamp(0, dState.R2, maxValue);
                    tempR2Output = (current - r2Deadzone) / (maxValue - r2Deadzone);
                }
                else
                {
                    tempR2Output = 0.0;
                }
            }

            if (r2MaxOutput != 100.0)
            {
                double maxOutRatio = r2MaxOutput / 100.0;
                tempR2Output = Math.Min(Math.Max(tempR2Output, 0.0), maxOutRatio);
            }

            if (r2AntiDeadzone > 0)
            {
                tempR2AntiDead = r2AntiDeadzone * 0.01;
            }

            if (tempR2Output > 0.0)
            {
                dState.R2 = (byte)(((1.0 - tempR2AntiDead) * tempR2Output + tempR2AntiDead) * 255.0);
            }
            else
            {
                dState.R2 = 0;
            }
        }

        private static void HandleL2DeadZone(DS4State cState, DS4State dState, byte deadZone, int antiDeadZone, int maxZone, double maxOutput)
        {
            double tempL2Output = cState.L2 / 255.0;
            double tempL2AntiDead = 0.0;
            double ratio = maxZone / 100.0;
            double maxValue = 255.0 * ratio;

            if (deadZone > 0)
            {
                if (cState.L2 > deadZone)
                {
                    double current = Global.Clamp(0, dState.L2, maxValue);
                    tempL2Output = (current - deadZone) / (maxValue - deadZone);
                }
                else
                {
                    tempL2Output = 0.0;
                }
            }

            if (maxOutput != 100.0)
            {
                double maxOutRatio = maxOutput / 100.0;
                tempL2Output = Math.Min(Math.Max(tempL2Output, 0.0), maxOutRatio);
            }

            if (antiDeadZone > 0)
            {
                tempL2AntiDead = antiDeadZone * 0.01;
            }

            if (tempL2Output > 0.0)
            {
                dState.L2 = (byte)(((1.0 - tempL2AntiDead) * tempL2Output + tempL2AntiDead) * 255.0);
            }
            else
            {
                dState.L2 = 0;
            }
        }

        private static void HandleStickDeadZone(byte cx, byte cy, ref byte dx, ref byte dy, StickDeadZoneInfo mod)
        {
            int deadZone = mod.DeadZone;
            int antiDead = mod.AntiDeadZone;
            int maxZone = mod.MaxZone;
            double maxOutput = mod.MaxOutput;

            if (deadZone <= 0 && antiDead <= 0 && maxZone == 100 && maxOutput == 100.0)
                return;

            double squared = Math.Pow(cx - 128f, 2) + Math.Pow(cy - 128f, 2);
            double deadzoneSquared = Math.Pow(deadZone, 2);
            if (deadZone > 0 && squared <= deadzoneSquared)
            {
                dx = 128;
                dy = 128;
                return;
            }

            if ((deadZone <= 0 || squared <= deadzoneSquared) && antiDead <= 0 && maxZone == 100 && maxOutput == 100.0)
                return;
            
            double r = Math.Atan2(-(dy - 128.0), dx - 128.0);
            double maxXValue = dx >= 128.0 ? 127.0 : -128;
            double maxYValue = dy >= 128.0 ? 127.0 : -128;
            double ratio = maxZone / 100.0;
            double maxOutRatio = maxOutput / 100.0;

            double maxZoneXNegValue = (ratio * -128) + 128;
            double maxZoneXPosValue = (ratio * 127) + 128;
            double maxZoneYNegValue = maxZoneXNegValue;
            double maxZoneYPosValue = maxZoneXPosValue;
            double maxZoneX = dx >= 128.0 ? (maxZoneXPosValue - 128.0) : (maxZoneXNegValue - 128.0);
            double maxZoneY = dy >= 128.0 ? (maxZoneYPosValue - 128.0) : (maxZoneYNegValue - 128.0);

            double tempOutputX = 0.0, tempOutputY = 0.0;
            if (deadZone > 0)
            {
                double tempXDead = Math.Abs(Math.Cos(r)) * (deadZone / 127.0) * maxXValue;
                double tempYDead = Math.Abs(Math.Sin(r)) * (deadZone / 127.0) * maxYValue;

                if (squared > deadzoneSquared)
                {
                    double currentX = Global.Clamp(maxZoneXNegValue, dx, maxZoneXPosValue);
                    double currentY = Global.Clamp(maxZoneYNegValue, dy, maxZoneYPosValue);
                    tempOutputX = (currentX - 128.0 - tempXDead) / (maxZoneX - tempXDead);
                    tempOutputY = (currentY - 128.0 - tempYDead) / (maxZoneY - tempYDead);
                }
            }
            else
            {
                double currentX = Global.Clamp(maxZoneXNegValue, dx, maxZoneXPosValue);
                double currentY = Global.Clamp(maxZoneYNegValue, dy, maxZoneYPosValue);
                tempOutputX = (currentX - 128.0) / maxZoneX;
                tempOutputY = (currentY - 128.0) / maxZoneY;
            }

            if (maxOutput != 100.0)
            {
                double maxOutXRatio = Math.Abs(Math.Cos(r)) * maxOutRatio;
                double maxOutYRatio = Math.Abs(Math.Sin(r)) * maxOutRatio;
                tempOutputX = Math.Min(Math.Max(tempOutputX, 0.0), maxOutXRatio);
                tempOutputY = Math.Min(Math.Max(tempOutputY, 0.0), maxOutYRatio);
            }

            double tempXAntiDeadPercent = 0.0, tempYAntiDeadPercent = 0.0;
            if (antiDead > 0)
            {
                tempXAntiDeadPercent = antiDead * 0.01 * Math.Abs(Math.Cos(r));
                tempYAntiDeadPercent = antiDead * 0.01 * Math.Abs(Math.Sin(r));
            }

            if (tempOutputX > 0.0)
                dx = (byte)(((1.0 - tempXAntiDeadPercent) * tempOutputX + tempXAntiDeadPercent) * maxXValue + 128.0);
            else
                dx = 128;

            if (tempOutputY > 0.0)
                dy = (byte)(((1.0 - tempYAntiDeadPercent) * tempOutputY + tempYAntiDeadPercent) * maxYValue + 128.0);
            else
                dy = 128;
        }

        private static void HandleCurve(int curve, int x, int y, ref byte curveX, ref byte curveY)
        {
            // TODO: Look into curve options and make sure maximum axes values are being respected

            double curveX1;
            double curveY1;

            float max = x + y;
            double multiMax = Lerp(382.5, max, curve * 0.01);
            double multiMin = Lerp(127.5, max, curve * 0.01);

            if ((x > 127.5f && y > 127.5f) || (x < 127.5f && y < 127.5f))
            {
                curveX1 = x > 127.5f ? Math.Min(x, x / max * multiMax) : Math.Max(x, x / max * multiMin);
                curveY1 = y > 127.5f ? Math.Min(y, y / max * multiMax) : Math.Max(y, y / max * multiMin);
            }
            else if (x < 127.5f)
            {
                curveX1 = Math.Min(x, x / max * multiMax);
                curveY1 = Math.Min(y, -(y / max) * multiMax + 510);
            }
            else
            {
                curveX1 = Math.Min(x, -(x / max) * multiMax + 510);
                curveY1 = Math.Min(y, y / max * multiMax);
            }

            curveX = (byte)Math.Round(curveX1, 0);
            curveY = (byte)Math.Round(curveY1, 0);
        }

        /* TODO: Possibly remove usage of this version of the method */
        private static bool ShiftTrigger(int trigger, int device, DS4State cState, DS4StateExposed eState, Mouse tp)
        {
            bool result = false;
            if (trigger == 0)
            {
                result = false;
            }
            else
            {
                DS4Controls ds = shiftTriggerMapping[trigger];
                result = GetBoolMapping(device, ds, cState, eState, tp);
            }

            return result;
        }

        private static bool ShiftTrigger2(int trigger, int device, DS4State cState, DS4StateExposed eState, Mouse tp, DS4StateFieldMapping fieldMapping)
        {
            bool result = false;
            if (trigger == 0)
            {
                result = false;
            }
            else if (trigger < 26)
            {
                DS4Controls ds = shiftTriggerMapping[trigger];
                result = GetBoolMapping2(device, ds, cState, eState, tp, fieldMapping);
            }
            else if (trigger == 26)
            {
                result = cState.Touch1Finger;
            }

            return result;
        }

        private static X360Controls GetX360ControlsByName(string key)
        {
            if (Enum.TryParse(key, true, out X360Controls x3c))
                return x3c;

            switch (key)
            {
                case "Back": return X360Controls.Back;
                case "Left Stick": return X360Controls.LS;
                case "Right Stick": return X360Controls.RS;
                case "Start": return X360Controls.Start;
                case "Up Button": return X360Controls.DpadUp;
                case "Right Button": return X360Controls.DpadRight;
                case "Down Button": return X360Controls.DpadDown;
                case "Left Button": return X360Controls.DpadLeft;

                case "Left Bumper": return X360Controls.LB;
                case "Right Bumper": return X360Controls.RB;
                case "Y Button": return X360Controls.Y;
                case "B Button": return X360Controls.B;
                case "A Button": return X360Controls.A;
                case "X Button": return X360Controls.X;

                case "Guide": return X360Controls.Guide;
                case "Left X-Axis-": return X360Controls.LXNeg;
                case "Left Y-Axis-": return X360Controls.LYNeg;
                case "Right X-Axis-": return X360Controls.RXNeg;
                case "Right Y-Axis-": return X360Controls.RYNeg;

                case "Left X-Axis+": return X360Controls.LXPos;
                case "Left Y-Axis+": return X360Controls.LYPos;
                case "Right X-Axis+": return X360Controls.RXPos;
                case "Right Y-Axis+": return X360Controls.RYPos;
                case "Left Trigger": return X360Controls.LT;
                case "Right Trigger": return X360Controls.RT;

                case "Left Mouse Button": return X360Controls.LeftMouse;
                case "Right Mouse Button": return X360Controls.RightMouse;
                case "Middle Mouse Button": return X360Controls.MiddleMouse;
                case "4th Mouse Button": return X360Controls.FourthMouse;
                case "5th Mouse Button": return X360Controls.FifthMouse;
                case "Mouse Wheel Up": return X360Controls.WheelUp;
                case "Mouse Wheel Down": return X360Controls.WheelDown;
                case "Mouse Up": return X360Controls.MouseUp;
                case "Mouse Down": return X360Controls.MouseDown;
                case "Mouse Left": return X360Controls.MouseLeft;
                case "Mouse Right": return X360Controls.MouseRight;
                case "Unbound": return X360Controls.Unbound;
                default: break;
            }

            return X360Controls.Unbound;
        }

        /// <summary>
        /// Map DS4 Buttons/Axes to other DS4 Buttons/Axes (largely the same as Xinput ones) and to keyboard and mouse buttons.
        /// </summary>
        public static void MapCustom(
            int device, 
            DS4State cState, 
            DS4State MappedState,
            DS4StateExposed eState,
            Mouse tp, 
            ControlService ctrl)
        {
            /* TODO: This method is slow sauce. Find ways to speed up action execution */
            double tempMouseDeltaX = 0.0;
            double tempMouseDeltaY = 0.0;
            int mouseDeltaX = 0;
            int mouseDeltaY = 0;

            cState.CalculateStickAngles();
            DS4StateFieldMapping fieldMapping = FieldMappings[device];
            fieldMapping.PopulateFieldMapping(cState, eState, tp);
            DS4StateFieldMapping outputfieldMapping = OutputFieldMappings[device];
            outputfieldMapping.PopulateFieldMapping(cState, eState, tp);
            //DS4StateFieldMapping fieldMapping = new DS4StateFieldMapping(cState, eState, tp);
            //DS4StateFieldMapping outputfieldMapping = new DS4StateFieldMapping(cState, eState, tp);

            SyntheticState deviceState = DeviceState[device];
            if (GetProfileActionCount(device) > 0 || UseTempProfile[device])
                MapCustomAction(device, cState, MappedState, eState, tp, ctrl, fieldMapping, outputfieldMapping);
            //if (ctrl.DS4Controllers[device] == null) return;

            //cState.CopyTo(MappedState);

            //Dictionary<DS4Controls, DS4Controls> tempControlDict = new Dictionary<DS4Controls, DS4Controls>();
            //MultiValueDict<DS4Controls, DS4Controls> tempControlDict = new MultiValueDict<DS4Controls, DS4Controls>();
            DS4Controls usingExtra = DS4Controls.None;
            List<DS4ControlSettings> tempSettingsList = getDS4CSettings(device);
            //foreach (DS4ControlSettings dcs in getDS4CSettings(device))
            //for (int settingIndex = 0, arlen = tempSettingsList.Count; settingIndex < arlen; settingIndex++)
            for (var settingEnum = tempSettingsList.GetEnumerator(); settingEnum.MoveNext();)
            {
                //DS4ControlSettings dcs = tempSettingsList[settingIndex];
                DS4ControlSettings dcs = settingEnum.Current;
                object action = null;
                DS4ControlSettings.EActionType actionType = 0;
                DS4KeyType keyType = DS4KeyType.None;
                if (dcs.ShiftAction != null && ShiftTrigger2(dcs.shiftTrigger, device, cState, eState, tp, fieldMapping))
                {
                    action = dcs.ShiftAction;
                    actionType = dcs.shiftActionType;
                    keyType = dcs.shiftKeyType;
                }
                else if (dcs.Action != null)
                {
                    action = dcs.Action;
                    actionType = dcs.ActionType;
                    keyType = dcs.keyType;
                }

                if (usingExtra == DS4Controls.None || usingExtra == dcs.Control)
                {
                    bool shiftE = !string.IsNullOrEmpty(dcs.shiftExtras) && ShiftTrigger2(dcs.shiftTrigger, device, cState, eState, tp, fieldMapping);
                    bool regE = !string.IsNullOrEmpty(dcs.extras);
                    if ((regE || shiftE) && GetBoolActionMapping2(device, dcs.Control, cState, eState, tp, fieldMapping))
                    {
                        usingExtra = dcs.Control;
                        string p;
                        if (shiftE)
                            p = dcs.shiftExtras;
                        else
                            p = dcs.extras;

                        string[] extraS = p.Split(',');
                        int extrasSLen = extraS.Length;
                        int[] extras = new int[extrasSLen];
                        for (int i = 0; i < extrasSLen; i++)
                        {
                            int b;
                            if (int.TryParse(extraS[i], out b))
                                extras[i] = b;
                        }

                        Held[device] = true;
                        try
                        {
                            if (!(extras[0] == extras[1] && extras[1] == 0))
                                ctrl.SetRumble((byte)extras[0], (byte)extras[1], device);

                            if (extras[2] == 1)
                            {
                                DS4Color color = new DS4Color { Red = (byte)extras[3], Green = (byte)extras[4], Blue = (byte)extras[5] };
                                DS4LightBar.ForcedColor[device] = color;
                                DS4LightBar.ForcedFlash[device] = (byte)extras[6];
                                DS4LightBar.ForceLight[device] = true;
                            }

                            if (extras[7] == 1)
                            {
                                if (OldMouse[device] == -1)
                                    OldMouse[device] = ButtonMouseSensitivity[device];
                                ButtonMouseSensitivity[device] = extras[8];
                            }
                        }
                        catch { }
                    }
                    else if ((regE || shiftE) && Held[device])
                    {
                        DS4LightBar.ForceLight[device] = false;
                        DS4LightBar.ForcedFlash[device] = 0;
                        if (OldMouse[device] != -1)
                        {
                            ButtonMouseSensitivity[device] = OldMouse[device];
                            OldMouse[device] = -1;
                        }

                        ctrl.SetRumble(0, 0, device);
                        Held[device] = false;
                        usingExtra = DS4Controls.None;
                    }
                }

                if (action != null)
                {
                    if (actionType == DS4ControlSettings.EActionType.Macro)
                    {
                        bool active = GetBoolMapping2(device, dcs.Control, cState, eState, tp, fieldMapping);
                        if (active)
                            PlayMacro(device, macroControl, string.Empty, null, (int[])action, dcs.Control, keyType);
                        else
                            EndMacro(device, macroControl, (int[])action, dcs.Control);
                        
                        // erase default mappings for things that are remapped
                        ResetToDefaultValue2(dcs.Control, MappedState, outputfieldMapping);
                    }
                    else if (actionType == DS4ControlSettings.EActionType.Key)
                    {
                        ushort value = Convert.ToUInt16(action);
                        if (GetBoolActionMapping2(device, dcs.Control, cState, eState, tp, fieldMapping))
                        {
                            if (!deviceState.KeyPressStates.TryGetValue(value, out SyntheticState.KeyPresses kp))
                                deviceState.KeyPressStates[value] = kp = new SyntheticState.KeyPresses();

                            if (keyType.HasFlag(DS4KeyType.ScanCode))
                                kp.Current.ScanCodeCount++;
                            else
                                kp.Current.VkCount++;

                            if (keyType.HasFlag(DS4KeyType.Toggle))
                            {
                                if (!PressedOnce[value])
                                {
                                    kp.Current.Toggle = !kp.Current.Toggle;
                                    PressedOnce[value] = true;
                                }
                                kp.Current.ToggleCount++;
                            }
                            kp.Current.RepeatCount++;
                        }
                        else
                            PressedOnce[value] = false;

                        // erase default mappings for things that are remapped
                        ResetToDefaultValue2(dcs.Control, MappedState, outputfieldMapping);
                    }
                    else if (actionType == DS4ControlSettings.EActionType.Button)
                    {
                        int keyvalue = 0;
                        bool isAnalog = false;

                        if (dcs.Control >= DS4Controls.LXNeg && dcs.Control <= DS4Controls.RYPos)
                        {
                            isAnalog = true;
                        }
                        else if (dcs.Control == DS4Controls.L2 || dcs.Control == DS4Controls.R2)
                        {
                            isAnalog = true;
                        }
                        else if (dcs.Control >= DS4Controls.GyroXPos && dcs.Control <= DS4Controls.GyroZNeg)
                        {
                            isAnalog = true;
                        }

                        X360Controls xboxControl = X360Controls.None;
                        if (action is X360Controls)
                        {
                            xboxControl = (X360Controls)action;
                        }
                        else if (action is string)
                        {
                            xboxControl = GetX360ControlsByName(action.ToString());
                        }

                        if (xboxControl >= X360Controls.LXNeg && xboxControl <= X360Controls.Start)
                        {
                            DS4Controls tempDS4Control = ReverseX360ButtonMapping[(int)xboxControl];
                            CustomMapQueue[device].Enqueue(new ControlToXInput(dcs.Control, tempDS4Control));
                            //tempControlDict.Add(dcs.control, tempDS4Control);
                        }
                        else if (xboxControl >= X360Controls.LeftMouse && xboxControl <= X360Controls.WheelDown)
                        {
                            switch (xboxControl)
                            {
                                case X360Controls.LeftMouse:
                                {
                                    keyvalue = 256;
                                    if (GetBoolActionMapping2(device, dcs.Control, cState, eState, tp, fieldMapping))
                                        deviceState.CurrentClicks.LeftCount++;

                                    break;
                                }
                                case X360Controls.RightMouse:
                                {
                                    keyvalue = 257;
                                    if (GetBoolActionMapping2(device, dcs.Control, cState, eState, tp, fieldMapping))
                                        deviceState.CurrentClicks.RightCount++;

                                    break;
                                }
                                case X360Controls.MiddleMouse:
                                {
                                    keyvalue = 258;
                                    if (GetBoolActionMapping2(device, dcs.Control, cState, eState, tp, fieldMapping))
                                        deviceState.CurrentClicks.MiddleCount++;

                                    break;
                                }
                                case X360Controls.FourthMouse:
                                {
                                    keyvalue = 259;
                                    if (GetBoolActionMapping2(device, dcs.Control, cState, eState, tp, fieldMapping))
                                        deviceState.CurrentClicks.FourthCount++;

                                    break;
                                }
                                case X360Controls.FifthMouse:
                                {
                                    keyvalue = 260;
                                    if (GetBoolActionMapping2(device, dcs.Control, cState, eState, tp, fieldMapping))
                                        deviceState.CurrentClicks.FifthCount++;

                                    break;
                                }
                                case X360Controls.WheelUp:
                                {
                                    if (GetBoolActionMapping2(device, dcs.Control, cState, eState, tp, fieldMapping))
                                    {
                                        if (isAnalog)
                                            GetMouseWheelMapping(device, dcs.Control, cState, eState, tp, false);
                                        else
                                            deviceState.CurrentClicks.WheelUpCount++;
                                    }

                                    break;
                                }
                                case X360Controls.WheelDown:
                                {
                                    if (GetBoolActionMapping2(device, dcs.Control, cState, eState, tp, fieldMapping))
                                    {
                                        if (isAnalog)
                                            GetMouseWheelMapping(device, dcs.Control, cState, eState, tp, true);
                                        else
                                            deviceState.CurrentClicks.WheelDownCount++;
                                    }

                                    break;
                                }

                                default: break;
                            }
                        }
                        else if (xboxControl >= X360Controls.MouseUp && xboxControl <= X360Controls.MouseRight)
                        {
                            switch (xboxControl)
                            {
                                case X360Controls.MouseUp:
                                {
                                    if (tempMouseDeltaY == 0)
                                    {
                                        tempMouseDeltaY = GetMouseMapping(device, dcs.Control, cState, eState, fieldMapping, 0, ctrl);
                                        tempMouseDeltaY = -Math.Abs(tempMouseDeltaY == -2147483648 ? 0 : tempMouseDeltaY);
                                    }

                                    break;
                                }
                                case X360Controls.MouseDown:
                                {
                                    if (tempMouseDeltaY == 0)
                                    {
                                        tempMouseDeltaY = GetMouseMapping(device, dcs.Control, cState, eState, fieldMapping, 1, ctrl);
                                        tempMouseDeltaY = Math.Abs(tempMouseDeltaY == -2147483648 ? 0 : tempMouseDeltaY);
                                    }

                                    break;
                                }
                                case X360Controls.MouseLeft:
                                {
                                    if (tempMouseDeltaX == 0)
                                    {
                                        tempMouseDeltaX = GetMouseMapping(device, dcs.Control, cState, eState, fieldMapping, 2, ctrl);
                                        tempMouseDeltaX = -Math.Abs(tempMouseDeltaX == -2147483648 ? 0 : tempMouseDeltaX);
                                    }

                                    break;
                                }
                                case X360Controls.MouseRight:
                                {
                                    if (tempMouseDeltaX == 0)
                                    {
                                        tempMouseDeltaX = GetMouseMapping(device, dcs.Control, cState, eState, fieldMapping, 3, ctrl);
                                        tempMouseDeltaX = Math.Abs(tempMouseDeltaX == -2147483648 ? 0 : tempMouseDeltaX);
                                    }

                                    break;
                                }

                                default: break;
                            }
                        }

                        if (keyType.HasFlag(DS4KeyType.Toggle))
                        {
                            if (GetBoolActionMapping2(device, dcs.Control, cState, eState, tp, fieldMapping))
                            {
                                if (!PressedOnce[keyvalue])
                                {
                                    deviceState.CurrentClicks.Toggle = !deviceState.CurrentClicks.Toggle;
                                    PressedOnce[keyvalue] = true;
                                }
                                deviceState.CurrentClicks.ToggleCount++;
                            }
                            else
                            {
                                PressedOnce[keyvalue] = false;
                            }
                        }

                        // erase default mappings for things that are remapped
                        ResetToDefaultValue2(dcs.Control, MappedState, outputfieldMapping);
                    }
                }
                else
                {
                    DS4StateFieldMapping.ControlType controlType = DS4StateFieldMapping.MappedType[(int)dcs.Control];
                    if (controlType == DS4StateFieldMapping.ControlType.AxisDir)
                    //if (dcs.control > DS4Controls.None && dcs.control < DS4Controls.L1)
                    {
                        //int current = (int)dcs.control;
                        //outputfieldMapping.axisdirs[current] = fieldMapping.axisdirs[current];
                        CustomMapQueue[device].Enqueue(new ControlToXInput(dcs.Control, dcs.Control));
                    }
                }
            }

            Queue<ControlToXInput> tempControl = CustomMapQueue[device];
            unchecked
            {
                for (int i = 0, len = tempControl.Count; i < len; i++)
                //while(tempControl.Any())
                {
                    ControlToXInput tempMap = tempControl.Dequeue();
                    int controlNum = (int)tempMap.DS4Input;
                    int tempOutControl = (int)tempMap.XOutput;
                    if (tempMap.XOutput >= DS4Controls.LXNeg && tempMap.XOutput <= DS4Controls.RYPos)
                    {
                        const byte axisDead = 128;
                        DS4StateFieldMapping.ControlType controlType = DS4StateFieldMapping.MappedType[tempOutControl];
                        bool alt = controlType == DS4StateFieldMapping.ControlType.AxisDir && tempOutControl % 2 == 0 ? true : false;
                        byte axisMapping = GetXYAxisMapping2(device, tempMap.DS4Input, cState, eState, tp, fieldMapping, alt);
                        if (axisMapping != axisDead)
                        {
                            int controlRelation = tempOutControl % 2 == 0 ? tempOutControl - 1 : tempOutControl + 1;
                            outputfieldMapping.AxisDirs[tempOutControl] = axisMapping;
                            outputfieldMapping.AxisDirs[controlRelation] = axisMapping;
                        }
                    }
                    else
                    {
                        if (tempMap.XOutput == DS4Controls.L2 || tempMap.XOutput == DS4Controls.R2)
                        {
                            const byte axisZero = 0;
                            byte axisMapping = getByteMapping2(device, tempMap.DS4Input, cState, eState, tp, fieldMapping);
                            if (axisMapping != axisZero)
                                outputfieldMapping.Triggers[tempOutControl] = axisMapping;
                        }
                        else
                        {
                            bool value = GetBoolMapping2(device, tempMap.DS4Input, cState, eState, tp, fieldMapping);
                            if (value)
                                outputfieldMapping.Buttons[tempOutControl] = value;
                        }
                    }
                }
            }

            outputfieldMapping.PopulateState(MappedState);

            if (macroCount > 0)
            {
                if (macroControl[00]) MappedState.Cross = true;
                if (macroControl[01]) MappedState.Circle = true;
                if (macroControl[02]) MappedState.Square = true;
                if (macroControl[03]) MappedState.Triangle = true;
                if (macroControl[04]) MappedState.Options = true;
                if (macroControl[05]) MappedState.Share = true;
                if (macroControl[06]) MappedState.DpadUp = true;
                if (macroControl[07]) MappedState.DpadDown = true;
                if (macroControl[08]) MappedState.DpadLeft = true;
                if (macroControl[09]) MappedState.DpadRight = true;
                if (macroControl[10]) MappedState.PS = true;
                if (macroControl[11]) MappedState.L1 = true;
                if (macroControl[12]) MappedState.R1 = true;
                if (macroControl[13]) MappedState.L2 = 255;
                if (macroControl[14]) MappedState.R2 = 255;
                if (macroControl[15]) MappedState.L3 = true;
                if (macroControl[16]) MappedState.R3 = true;
                if (macroControl[17]) MappedState.LX = 255;
                if (macroControl[18]) MappedState.LX = 0;
                if (macroControl[19]) MappedState.LY = 255;
                if (macroControl[20]) MappedState.LY = 0;
                if (macroControl[21]) MappedState.RX = 255;
                if (macroControl[22]) MappedState.RX = 0;
                if (macroControl[23]) MappedState.RY = 255;
                if (macroControl[24]) MappedState.RY = 0;
            }

            if (GetSASteeringWheelEmulationAxis(device) != SASteeringWheelEmulationAxisType.None)
            {
                MappedState.SASteeringWheelEmulationUnit = Scale360DegGyroAxis(device, eState, ctrl);
            }

            ref byte gyroTempX = ref GyroStickX[device];
            if (gyroTempX != 128)
            {
                if (MappedState.RX != 128)
                    MappedState.RX = Math.Abs(gyroTempX - 128) > Math.Abs(MappedState.RX - 128) ?
                        gyroTempX : MappedState.RX;
                else
                    MappedState.RX = gyroTempX;
            }

            ref byte gyroTempY = ref GyroStickY[device];
            if (gyroTempY != 128)
            {
                if (MappedState.RY != 128)
                    MappedState.RY = Math.Abs(gyroTempY - 128) > Math.Abs(MappedState.RY - 128) ?
                        gyroTempY : MappedState.RY;
                else
                    MappedState.RY = gyroTempY;
            }

            gyroTempX = gyroTempY = 128;

            CalcFinalMouseMovement(ref tempMouseDeltaX, ref tempMouseDeltaY, out mouseDeltaX, out mouseDeltaY);
            if (mouseDeltaX != 0 || mouseDeltaY != 0)
                InputMethods.MoveCursorBy(mouseDeltaX, mouseDeltaY);
        }

        private static bool IfAxisIsNotModified(int device, bool shift, DS4Controls dc) 
            => shift ? false : GetDS4Action(device, dc, false) == null;

        private static async void MapCustomAction(int device, DS4State cState, DS4State MappedState,
            DS4StateExposed eState, Mouse tp, ControlService ctrl, DS4StateFieldMapping fieldMapping, DS4StateFieldMapping outputfieldMapping)
        {
            /* TODO: This method is slow sauce. Find ways to speed up action execution */
            try
            {
                int actionDoneCount = actionDone.Count;
                int totalActionCount = GetActions().Count;
                DS4StateFieldMapping previousFieldMapping = null;
                List<string> profileActions = getProfileActions(device);
                //foreach (string actionname in profileActions)
                for (int actionIndex = 0, profileListLen = profileActions.Count;
                     actionIndex < profileListLen; actionIndex++)
                {
                    //DS4KeyType keyType = getShiftCustomKeyType(device, customKey.Key);
                    //SpecialAction action = GetAction(actionname);
                    //int index = GetActionIndexOf(actionname);
                    string actionname = profileActions[actionIndex];
                    SpecialAction action = GetProfileAction(device, actionname);
                    int index = GetProfileActionIndexOf(device, actionname);

                    if (actionDoneCount < index + 1)
                    {
                        actionDone.Add(new ActionState());
                        actionDoneCount++;
                    }
                    else if (actionDoneCount > totalActionCount)
                    {
                        actionDone.RemoveAt(actionDoneCount - 1);
                        actionDoneCount--;
                    }

                    if (action == null)
                    {
                        continue;
                    }

                    double time = 0.0;
                    //If a key or button is assigned to the trigger, a key special action is used like
                    //a quick tap to use and hold to use the regular custom button/key
                    bool triggerToBeTapped = action.typeID == SpecialAction.ActionTypeId.None && action.Triggers.Count == 1 &&
                            GetDS4Action(device, action.Triggers[0], false) == null;
                    if (!(action.typeID == SpecialAction.ActionTypeId.None || index < 0))
                    {
                        bool triggeractivated = true;
                        if (action.delayTime > 0.0)
                        {
                            triggeractivated = false;
                            bool subtriggeractivated = true;
                            //foreach (DS4Controls dc in action.trigger)
                            for (int i = 0, arlen = action.Triggers.Count; i < arlen; i++)
                            {
                                DS4Controls dc = action.Triggers[i];
                                if (!getBoolSpecialActionMapping(device, dc, cState, eState, tp, fieldMapping))
                                {
                                    subtriggeractivated = false;
                                    break;
                                }
                            }
                            if (subtriggeractivated)
                            {
                                time = action.delayTime;
                                nowAction[device] = DateTime.UtcNow;
                                if (nowAction[device] >= oldnowAction[device] + TimeSpan.FromSeconds(time))
                                    triggeractivated = true;
                            }
                            else if (nowAction[device] < DateTime.UtcNow - TimeSpan.FromMilliseconds(100))
                                oldnowAction[device] = DateTime.UtcNow;
                        }
                        else if (triggerToBeTapped && oldnowKeyAct[device] == DateTime.MinValue)
                        {
                            triggeractivated = false;
                            bool subtriggeractivated = true;
                            //foreach (DS4Controls dc in action.trigger)
                            for (int i = 0, arlen = action.Triggers.Count; i < arlen; i++)
                            {
                                DS4Controls dc = action.Triggers[i];
                                if (!getBoolSpecialActionMapping(device, dc, cState, eState, tp, fieldMapping))
                                {
                                    subtriggeractivated = false;
                                    break;
                                }
                            }
                            if (subtriggeractivated)
                            {
                                oldnowKeyAct[device] = DateTime.UtcNow;
                            }
                        }
                        else if (triggerToBeTapped && oldnowKeyAct[device] != DateTime.MinValue)
                        {
                            triggeractivated = false;
                            bool subtriggeractivated = true;
                            //foreach (DS4Controls dc in action.trigger)
                            for (int i = 0, arlen = action.Triggers.Count; i < arlen; i++)
                            {
                                DS4Controls dc = action.Triggers[i];
                                if (!getBoolSpecialActionMapping(device, dc, cState, eState, tp, fieldMapping))
                                {
                                    subtriggeractivated = false;
                                    break;
                                }
                            }
                            DateTime now = DateTime.UtcNow;
                            if (!subtriggeractivated && now <= oldnowKeyAct[device] + TimeSpan.FromMilliseconds(250))
                            {
                                await Task.Delay(3); //if the button is assigned to the same key use a delay so the key down is the last action, not key up
                                triggeractivated = true;
                                oldnowKeyAct[device] = DateTime.MinValue;
                            }
                            else if (!subtriggeractivated)
                                oldnowKeyAct[device] = DateTime.MinValue;
                        }
                        else
                        {
                            //foreach (DS4Controls dc in action.trigger)
                            for (int i = 0, arlen = action.Triggers.Count; i < arlen; i++)
                            {
                                DS4Controls dc = action.Triggers[i];
                                if (!getBoolSpecialActionMapping(device, dc, cState, eState, tp, fieldMapping))
                                {
                                    triggeractivated = false;
                                    break;
                                }
                            }

                            // If special action macro is set to run on key release then activate the trigger status only when the trigger key is released
                            if (action.typeID == SpecialAction.ActionTypeId.Macro && action.pressRelease && action.firstTouch)
                                triggeractivated = !triggeractivated;
                        }

                        bool utriggeractivated = true;
                        int uTriggerCount = action.uTrigger.Count;
                        if (action.typeID == SpecialAction.ActionTypeId.Key && uTriggerCount > 0)
                        {
                            //foreach (DS4Controls dc in action.uTrigger)
                            for (int i = 0, arlen = action.uTrigger.Count; i < arlen; i++)
                            {
                                DS4Controls dc = action.uTrigger[i];
                                if (!getBoolSpecialActionMapping(device, dc, cState, eState, tp, fieldMapping))
                                {
                                    utriggeractivated = false;
                                    break;
                                }
                            }
                            if (action.pressRelease) utriggeractivated = !utriggeractivated;
                        }

                        bool actionFound = false;
                        if (triggeractivated)
                        {
                            if (action.typeID == SpecialAction.ActionTypeId.Program)
                            {
                                actionFound = true;

                                if (!actionDone[index].dev[device])
                                {
                                    actionDone[index].dev[device] = true;
                                    if (!string.IsNullOrEmpty(action.extra))
                                        Process.Start(action.details, action.extra);
                                    else
                                        Process.Start(action.details);
                                }
                            }
                            else if (action.typeID == SpecialAction.ActionTypeId.Profile)
                            {
                                actionFound = true;

                                if (!actionDone[index].dev[device] && (!UseTempProfile[device] || untriggeraction[device] == null || untriggeraction[device].typeID != SpecialAction.ActionTypeId.Profile) )
                                {
                                    actionDone[index].dev[device] = true;
                                    // If Loadprofile special action doesn't have untrigger keys or automatic untrigger option is not set then don't set untrigger status. This way the new loaded profile allows yet another loadProfile action key event.
                                    if (action.uTrigger.Count > 0 || action.automaticUntrigger)
                                    {
                                        untriggeraction[device] = action;
                                        untriggerindex[device] = index;

                                        // If the existing profile is a temp profile then store its name, because automaticUntrigger needs to know where to go back (empty name goes back to default regular profile)
                                        untriggeraction[device].prevProfileName = UseTempProfile[device] ? TempProfileName[device] : string.Empty;
                                    }
                                    //foreach (DS4Controls dc in action.trigger)
                                    for (int i = 0, arlen = action.Triggers.Count; i < arlen; i++)
                                    {
                                        DS4Controls dc = action.Triggers[i];
                                        DS4ControlSettings dcs = getDS4CSetting(device, dc);
                                        if (dcs.Action != null)
                                        {
                                            if (dcs.ActionType == DS4ControlSettings.EActionType.Key)
                                                InputMethods.performKeyRelease(ushort.Parse(dcs.Action.ToString()));
                                            else if (dcs.ActionType == DS4ControlSettings.EActionType.Macro)
                                            {
                                                int[] keys = (int[])dcs.Action;
                                                for (int j = 0, keysLen = keys.Length; j < keysLen; j++)
                                                    InputMethods.performKeyRelease((ushort)keys[j]);
                                            }
                                        }
                                    }

                                    string prolog = DS4WinWPF.Properties.Resources.UsingProfile.Replace("*number*", (device + 1).ToString()).Replace("*Profile name*", action.details);
                                    AppLogger.LogToGui(prolog, false);
                                    LoadTempProfile(device, action.details, true, ctrl);

                                    if (action.uTrigger.Count == 0 && !action.automaticUntrigger)
                                    {
                                        // If the new profile has any actions with the same action key (controls) than this action (which doesn't have untrigger keys) then set status of those actions to wait for the release of the existing action key. 
                                        List<string> profileActionsNext = getProfileActions(device);
                                        for (int actionIndexNext = 0, profileListLenNext = profileActionsNext.Count; actionIndexNext < profileListLenNext; actionIndexNext++)
                                        {
                                            string actionnameNext = profileActionsNext[actionIndexNext];
                                            SpecialAction actionNext = GetProfileAction(device, actionnameNext);
                                            int indexNext = GetProfileActionIndexOf(device, actionnameNext);

                                            if (actionNext.controls == action.controls)
                                                actionDone[indexNext].dev[device] = true;
                                        }
                                    }

                                    return;
                                }
                            }
                            else if (action.typeID == SpecialAction.ActionTypeId.Macro)
                            {
                                actionFound = true;
                                if (!action.pressRelease)
                                {
                                    // Macro run when trigger keys are pressed down (the default behaviour)
                                    if (!actionDone[index].dev[device])
                                    {
                                        DS4KeyType keyType = action.keyType;
                                        actionDone[index].dev[device] = true;
                                        for (int i = 0, arlen = action.Triggers.Count; i < arlen; i++)
                                        {
                                            DS4Controls dc = action.Triggers[i];
                                            ResetToDefaultValue2(dc, MappedState, outputfieldMapping);
                                        }

                                        PlayMacro(device, macroControl, string.Empty, action.macro, null, DS4Controls.None, keyType, action, actionDone[index]);
                                    }
                                    else
                                    {
                                        if (!action.keyType.HasFlag(DS4KeyType.RepeatMacro))
                                            EndMacro(device, macroControl, action.macro, DS4Controls.None);
                                    }
                                }
                                else 
                                {
                                    // Macro is run when trigger keys are released (optional behaviour of macro special action))
                                    if (action.firstTouch)
                                    {
                                        action.firstTouch = false;
                                        if (!actionDone[index].dev[device])
                                        {
                                            DS4KeyType keyType = action.keyType;
                                            actionDone[index].dev[device] = true;
                                            for (int i = 0, arlen = action.Triggers.Count; i < arlen; i++)
                                            {
                                                DS4Controls dc = action.Triggers[i];
                                                ResetToDefaultValue2(dc, MappedState, outputfieldMapping);
                                            }

                                            PlayMacro(device, macroControl, string.Empty, action.macro, null, DS4Controls.None, keyType, action, null);
                                        }
                                    }
                                    else
                                        action.firstTouch = true;
                                }
                            }
                            else if (action.typeID == SpecialAction.ActionTypeId.Key)
                            {
                                actionFound = true;

                                if (uTriggerCount == 0 || (uTriggerCount > 0 && untriggerindex[device] == -1 && !actionDone[index].dev[device]))
                                {
                                    actionDone[index].dev[device] = true;
                                    untriggerindex[device] = index;
                                    ushort.TryParse(action.details, out ushort key);
                                    if (uTriggerCount == 0)
                                    {
                                        if (!DeviceState[device].KeyPressStates.TryGetValue(key, out SyntheticState.KeyPresses kp))
                                            DeviceState[device].KeyPressStates[key] = kp = new SyntheticState.KeyPresses();

                                        if (action.keyType.HasFlag(DS4KeyType.ScanCode))
                                            kp.Current.ScanCodeCount++;
                                        else
                                            kp.Current.VkCount++;

                                        kp.Current.RepeatCount++;
                                    }
                                    else if (action.keyType.HasFlag(DS4KeyType.ScanCode))
                                        InputMethods.performSCKeyPress(key);
                                    else
                                        InputMethods.performKeyPress(key);
                                }
                            }
                            else if (action.typeID == SpecialAction.ActionTypeId.DisconnectBT)
                            {
                                actionFound = true;

                                DS4Device d = ctrl.Controllers[device].Device;
                                if (d.IsSynced && !d.IsCharging)
                                {
                                    if (d.IsBT)
                                        d.DisconnectBT();
                                    else if (d.IsSONYWA && d.IsExclusive)
                                        d.DisconnectDongle();
                                    
                                    //foreach (DS4Controls dc in action.trigger)
                                    for (int i = 0, arlen = action.Triggers.Count; i < arlen; i++)
                                    {
                                        DS4Controls dc = action.Triggers[i];
                                        DS4ControlSettings dcs = getDS4CSetting(device, dc);
                                        if (dcs.Action != null)
                                        {
                                            if (dcs.ActionType == DS4ControlSettings.EActionType.Key)
                                                InputMethods.performKeyRelease((ushort)dcs.Action);
                                            else if (dcs.ActionType == DS4ControlSettings.EActionType.Macro)
                                            {
                                                int[] keys = (int[])dcs.Action;
                                                for (int j = 0, keysLen = keys.Length; j < keysLen; j++)
                                                    InputMethods.performKeyRelease((ushort)keys[j]);
                                            }
                                        }
                                    }
                                    return;
                                }
                            }
                            else if (action.typeID == SpecialAction.ActionTypeId.BatteryCheck)
                            {
                                actionFound = true;

                                string[] dets = action.details.Split('|');
                                if (dets.Length == 1)
                                    dets = action.details.Split(',');
                                if (bool.Parse(dets[1]) && !actionDone[index].dev[device])
                                {
                                    AppLogger.LogToTray("Controller " + (device + 1) + ": " +
                                        ctrl.getDS4Battery(device), true);
                                }
                                if (bool.Parse(dets[2]))
                                {
                                    DS4Device d = ctrl.Controllers[device].Device;
                                    if (!actionDone[index].dev[device])
                                    {
                                        lastColor[device] = d.LightBarColor;
                                        DS4LightBar.ForceLight[device] = true;
                                    }
                                    DS4Color empty = new DS4Color(byte.Parse(dets[3]), byte.Parse(dets[4]), byte.Parse(dets[5]));
                                    DS4Color full = new DS4Color(byte.Parse(dets[6]), byte.Parse(dets[7]), byte.Parse(dets[8]));
                                    DS4Color trans = LerpDS4Color(ref empty, ref full, d.Battery);
                                    if (fadetimer[device] < 100)
                                        DS4LightBar.ForcedColor[device] = LerpDS4Color(ref lastColor[device], ref trans, fadetimer[device] += 2);
                                }
                                actionDone[index].dev[device] = true;
                            }
                            else if (action.typeID == SpecialAction.ActionTypeId.SASteeringWheelEmulationCalibrate)
                            {
                                actionFound = true;

                                DS4Device d = ctrl.Controllers[device].Device;
                                // If controller is not already in SASteeringWheelCalibration state then enable it now. If calibration is active then complete it (commit calibration values)
                                if (d.WheelRecalibrateActiveState == 0 && DateTime.UtcNow > (action.firstTap + TimeSpan.FromMilliseconds(3000)))
                                {
                                    action.firstTap = DateTime.UtcNow;
                                    d.WheelRecalibrateActiveState = 1;  // Start calibration process
                                }
                                else if (d.WheelRecalibrateActiveState == 2 && DateTime.UtcNow > (action.firstTap + TimeSpan.FromMilliseconds(3000)))
                                {
                                    action.firstTap = DateTime.UtcNow;
                                    d.WheelRecalibrateActiveState = 3;  // Complete calibration process
                                }

                                actionDone[index].dev[device] = true;
                            }
                        }
                        else
                        {
                            if (action.typeID == SpecialAction.ActionTypeId.BatteryCheck)
                            {
                                actionFound = true;
                                if (actionDone[index].dev[device])
                                {
                                    fadetimer[device] = 0;
                                    /*if (prevFadetimer[device] == fadetimer[device])
                                    {
                                        prevFadetimer[device] = 0;
                                        fadetimer[device] = 0;
                                    }
                                    else
                                        prevFadetimer[device] = fadetimer[device];*/
                                    DS4LightBar.ForceLight[device] = false;
                                    actionDone[index].dev[device] = false;
                                }
                            }
                            else if (action.typeID != SpecialAction.ActionTypeId.Key &&
                                     action.typeID != SpecialAction.ActionTypeId.XboxGameDVR &&
                                     action.typeID != SpecialAction.ActionTypeId.MultiAction)
                            {
                                // Ignore
                                actionFound = true;
                                actionDone[index].dev[device] = false;
                            }
                        }

                        if (!actionFound)
                        {
                            if (uTriggerCount > 0 && utriggeractivated && action.typeID == SpecialAction.ActionTypeId.Key)
                            {
                                actionFound = true;

                                if (untriggerindex[device] > -1 && !actionDone[index].dev[device])
                                {
                                    actionDone[index].dev[device] = true;
                                    untriggerindex[device] = -1;
                                    ushort key;
                                    ushort.TryParse(action.details, out key);
                                    if (action.keyType.HasFlag(DS4KeyType.ScanCode))
                                        InputMethods.performSCKeyRelease(key);
                                    else
                                        InputMethods.performKeyRelease(key);
                                }
                            }
                            else if (action.typeID == SpecialAction.ActionTypeId.XboxGameDVR || action.typeID == SpecialAction.ActionTypeId.MultiAction)
                            {
                                actionFound = true;

                                bool tappedOnce = action.tappedOnce, firstTouch = action.firstTouch,
                                    secondtouchbegin = action.secondtouchbegin;
                                //DateTime pastTime = action.pastTime, firstTap = action.firstTap,
                                //    TimeofEnd = action.TimeofEnd;

                                /*if (getCustomButton(device, action.trigger[0]) != X360Controls.Unbound)
                                    getCustomButtons(device)[action.trigger[0]] = X360Controls.Unbound;
                                if (getCustomMacro(device, action.trigger[0]) != "0")
                                    getCustomMacros(device).Remove(action.trigger[0]);
                                if (getCustomKey(device, action.trigger[0]) != 0)
                                    getCustomMacros(device).Remove(action.trigger[0]);*/
                                string[] dets = action.details.Split(',');
                                DS4Device d = ctrl.Controllers[device].Device;
                                //cus

                                DS4State tempPrevState = d.GetPreviousStateRef();
                                // Only create one instance of previous DS4StateFieldMapping in case more than one multi-action
                                // button is assigned
                                if (previousFieldMapping == null)
                                {
                                    previousFieldMapping = PreviousFieldMappings[device];
                                    previousFieldMapping.PopulateFieldMapping(tempPrevState, eState, tp, true);
                                    //previousFieldMapping = new DS4StateFieldMapping(tempPrevState, eState, tp, true);
                                }

                                bool activeCur = getBoolSpecialActionMapping(device, action.Triggers[0], cState, eState, tp, fieldMapping);
                                bool activePrev = getBoolSpecialActionMapping(device, action.Triggers[0], tempPrevState, eState, tp, previousFieldMapping);
                                if (activeCur && !activePrev)
                                {
                                    // pressed down
                                    action.pastTime = DateTime.UtcNow;
                                    if (action.pastTime <= (action.firstTap + TimeSpan.FromMilliseconds(150)))
                                    {
                                        action.tappedOnce = tappedOnce = false;
                                        action.secondtouchbegin = secondtouchbegin = true;
                                        //tappedOnce = false;
                                        //secondtouchbegin = true;
                                    }
                                    else
                                        action.firstTouch = firstTouch = true;
                                        //firstTouch = true;
                                }
                                else if (!activeCur && activePrev)
                                {
                                    // released
                                    if (secondtouchbegin)
                                    {
                                        action.firstTouch = firstTouch = false;
                                        action.secondtouchbegin = secondtouchbegin = false;
                                        //firstTouch = false;
                                        //secondtouchbegin = false;
                                    }
                                    else if (firstTouch)
                                    {
                                        action.firstTouch = firstTouch = false;
                                        //firstTouch = false;
                                        if (DateTime.UtcNow <= (action.pastTime + TimeSpan.FromMilliseconds(150)) && !tappedOnce)
                                        {
                                            action.tappedOnce = tappedOnce = true;
                                            //tappedOnce = true;
                                            action.firstTap = DateTime.UtcNow;
                                            action.TimeofEnd = DateTime.UtcNow;
                                        }
                                    }
                                }

                                int type = 0;
                                string macro = "";
                                if (tappedOnce) //single tap
                                {
                                    if (action.typeID == SpecialAction.ActionTypeId.MultiAction)
                                    {
                                        macro = dets[0];
                                    }
                                    else if (int.TryParse(dets[0], out type))
                                    {
                                        switch (type)
                                        {
                                            case 0: macro = "91/71/71/91"; break;
                                            case 1: macro = "91/164/82/82/164/91"; break;
                                            case 2: macro = "91/164/44/44/164/91"; break;
                                            case 3: macro = dets[3] + "/" + dets[3]; break;
                                            case 4: macro = "91/164/71/71/164/91"; break;
                                        }
                                    }

                                    if ((DateTime.UtcNow - action.TimeofEnd) > TimeSpan.FromMilliseconds(150))
                                    {
                                        if (macro != "")
                                            PlayMacro(device, macroControl, macro, null, null, DS4Controls.None, DS4KeyType.None);

                                        tappedOnce = false;
                                        action.tappedOnce = false;
                                    }
                                    //if it fails the method resets, and tries again with a new tester value (gives tap a delay so tap and hold can work)
                                }
                                else if (firstTouch && (DateTime.UtcNow - action.pastTime) > TimeSpan.FromMilliseconds(500)) //helddown
                                {
                                    if (action.typeID == SpecialAction.ActionTypeId.MultiAction)
                                    {
                                        macro = dets[1];
                                    }
                                    else if (int.TryParse(dets[1], out type))
                                    {
                                        switch (type)
                                        {
                                            case 0: macro = "91/71/71/91"; break;
                                            case 1: macro = "91/164/82/82/164/91"; break;
                                            case 2: macro = "91/164/44/44/164/91"; break;
                                            case 3: macro = dets[3] + "/" + dets[3]; break;
                                            case 4: macro = "91/164/71/71/164/91"; break;
                                        }
                                    }

                                    if (macro != "")
                                        PlayMacro(device, macroControl, macro, null, null, DS4Controls.None, DS4KeyType.None);

                                    firstTouch = false;
                                    action.firstTouch = false;
                                }
                                else if (secondtouchbegin) //if double tap
                                {
                                    if (action.typeID == SpecialAction.ActionTypeId.MultiAction)
                                    {
                                        macro = dets[2];
                                    }
                                    else if (int.TryParse(dets[2], out type))
                                    {
                                        switch (type)
                                        {
                                            case 0: macro = "91/71/71/91"; break;
                                            case 1: macro = "91/164/82/82/164/91"; break;
                                            case 2: macro = "91/164/44/44/164/91"; break;
                                            case 3: macro = dets[3] + "/" + dets[3]; break;
                                            case 4: macro = "91/164/71/71/164/91"; break;
                                        }
                                    }

                                    if (macro != "")
                                        PlayMacro(device, macroControl, macro, null, null, DS4Controls.None, DS4KeyType.None);

                                    secondtouchbegin = false;
                                    action.secondtouchbegin = false;
                                }
                            }
                            else
                            {
                                actionDone[index].dev[device] = false;
                            }
                        }
                    }
                }
            }
            catch { return; }

            if (untriggeraction[device] != null)
            {
                SpecialAction action = untriggeraction[device];
                int index = untriggerindex[device];
                bool utriggeractivated;

                if (!action.automaticUntrigger)
                {
                    // Untrigger keys defined and auto-untrigger (=unload) profile option is NOT set. Unload a temporary profile only when specified untrigger keys have been triggered.
                    utriggeractivated = true;

                    //foreach (DS4Controls dc in action.uTrigger)
                    for (int i = 0, uTrigLen = action.uTrigger.Count; i < uTrigLen; i++)
                    {
                        DS4Controls dc = action.uTrigger[i];
                        if (!getBoolSpecialActionMapping(device, dc, cState, eState, tp, fieldMapping))
                        {
                            utriggeractivated = false;
                            break;
                        }
                    }
                }
                else
                {
                    // Untrigger as soon any of the defined regular trigger keys have been released. 
                    utriggeractivated = false;

                    for (int i = 0, trigLen = action.Triggers.Count; i < trigLen; i++)
                    {
                        DS4Controls dc = action.Triggers[i];
                        if (!getBoolSpecialActionMapping(device, dc, cState, eState, tp, fieldMapping))
                        {
                            utriggeractivated = true;
                            break;
                        }
                    }
                }

                if (utriggeractivated && action.typeID == SpecialAction.ActionTypeId.Profile)
                {
                    if ((action.controls == action.ucontrols && !actionDone[index].dev[device]) || //if trigger and end trigger are the same
                    action.controls != action.ucontrols)
                    {
                        if (UseTempProfile[device])
                        {
                            //foreach (DS4Controls dc in action.uTrigger)
                            for (int i = 0, arlen = action.uTrigger.Count; i < arlen; i++)
                            {
                                DS4Controls dc = action.uTrigger[i];
                                actionDone[index].dev[device] = true;
                                DS4ControlSettings dcs = getDS4CSetting(device, dc);
                                if (dcs.Action != null)
                                {
                                    if (dcs.ActionType == DS4ControlSettings.EActionType.Key)
                                        InputMethods.performKeyRelease((ushort)dcs.Action);
                                    else if (dcs.ActionType == DS4ControlSettings.EActionType.Macro)
                                    {
                                        int[] keys = (int[])dcs.Action;
                                        for (int j = 0, keysLen = keys.Length; j < keysLen; j++)
                                            InputMethods.performKeyRelease((ushort)keys[j]);
                                    }
                                }
                            }

                            string profileName = untriggeraction[device].prevProfileName;
                            string prolog = DS4WinWPF.Properties.Resources.UsingProfile.Replace("*number*", (device + 1).ToString()).Replace("*Profile name*", profileName == string.Empty ? ProfilePath[device] : profileName);
                            AppLogger.LogToGui(prolog, false);

                            untriggeraction[device] = null;

                            if (profileName == string.Empty)
                                LoadProfile(device, false, ctrl); // Previous profile was a regular default profile of a controller
                            else
                                LoadTempProfile(device, profileName, true, ctrl); // Previous profile was a temporary profile, so re-load it as a temp profile
                        }
                    }
                }
                else
                {
                    actionDone[index].dev[device] = false;
                }
            }
        }

        // Play macro as a background task. Optionally the new macro play waits for completion of a previous macro execution (synchronized macro special action). 
        // Macro steps are defined either as macrostr string value, macroLst list<int> object or as macroArr integer array. Only one of these should have a valid macro definition when this method is called.
        // If the macro definition is a macroStr string value then it will be converted as integer array on the fl. If steps are already defined as list or array of integers then there is no need to do type cast conversion.
        private static void PlayMacro(int device, bool[] macrocontrol, string macroStr, List<int> macroLst, int[] macroArr, DS4Controls control, DS4KeyType keyType, SpecialAction action = null, ActionState actionDoneState = null)
        {
            if (action != null && action.synchronized)
            {
                // Run special action macros in synchronized order (ie. FirstIn-FirstOut). The trigger control name string is the execution queue identifier (ie. each unique trigger combination has an own synchronization queue).
                if (!macroTaskQueue[device].TryGetValue(action.controls, out Task prevTask))
                    macroTaskQueue[device].Add(action.controls, Task.Factory.StartNew(() => PlayMacroTask(device, macroControl, macroStr, macroLst, macroArr, control, keyType, action, actionDoneState)) );
                else
                    macroTaskQueue[device][action.controls] = prevTask.ContinueWith((x) => PlayMacroTask(device, macroControl, macroStr, macroLst, macroArr, control, keyType, action, actionDoneState));                       
            }
            else
                // Run macro as "fire and forget" background task. No need to wait for completion of any of the other macros. 
                // If the same trigger macro is re-launched while previous macro is still running then the order of parallel macros is not guaranteed.
                Task.Factory.StartNew(() => PlayMacroTask(device, macroControl, macroStr, macroLst, macroArr, control, keyType, action, actionDoneState));
        }

        // Play through a macro. The macro steps are defined either as string, List or Array object (always only one of those parameters is set to a valid value)
        private static void PlayMacroTask(int device, bool[] macrocontrol, string macroStr, List<int> macroLst, int[] macroArr, DS4Controls control, DS4KeyType keyType, SpecialAction action, ActionState actionDoneState)
        {
            if(!string.IsNullOrEmpty(macroStr))
            {
                string[] skeys;

                skeys = macroStr.Split('/');
                macroArr = new int[skeys.Length];
                for (int i = 0; i < macroArr.Length; i++)
                    macroArr[i] = int.Parse(skeys[i]);
            }

            // macro.StartsWith("164/9/9/164") || macro.StartsWith("18/9/9/18")
            if ( (macroLst != null && macroLst.Count >= 4 && ((macroLst[0] == 164 && macroLst[1] == 9 && macroLst[2] == 9 && macroLst[3] == 164) || (macroLst[0] == 18 && macroLst[1] == 9 && macroLst[2] == 9 && macroLst[3] == 18))) 
              || (macroArr != null && macroArr.Length>= 4 && ((macroArr[0] == 164 && macroArr[1] == 9 && macroArr[2] == 9 && macroArr[3] == 164) || (macroArr[0] == 18 && macroArr[1] == 9 && macroArr[2] == 9 && macroArr[3] == 18)))
            )
            {
                int wait;
                if(macroLst != null)
                    wait = macroLst[macroLst.Count - 1];
                else
                    wait = macroArr[macroArr.Length - 1];

                if (wait <= 300 || wait > ushort.MaxValue)
                    wait = 1000;
                else
                    wait -= 300;

                AltTabSwapping(wait, device);
                if (control != DS4Controls.None)
                    MacroDone[DS4ControlToInt(control)] = true;
            }
            else if(control == DS4Controls.None || !MacroDone[DS4ControlToInt(control)])
            {
                int macroCodeValue;
                bool[] keydown = new bool[286];

                if (control != DS4Controls.None)
                    MacroDone[DS4ControlToInt(control)] = true;

                // Play macro codes and simulate key down/up events (note! The same key may go through several up and down events during the same macro).
                // If the return value is TRUE then this method should do a asynchronized delay (the usual Thread.Sleep doesnt work here because it would block the main gamepad reading thread).
                if (macroLst != null)
                {
                    for (int i = 0; i < macroLst.Count; i++)
                    {
                        macroCodeValue = macroLst[i];
                        if (PlayMacroCodeValue(device, macrocontrol, keyType, macroCodeValue, keydown))
                            Task.Delay(macroCodeValue - 300).Wait();
                    }
                }
                else
                {
                    for (int i = 0; i < macroArr.Length; i++)
                    {
                        macroCodeValue = macroArr[i];
                        if (PlayMacroCodeValue(device, macrocontrol, keyType, macroCodeValue, keydown))
                            Task.Delay(macroCodeValue - 300).Wait();
                    }
                }

                // The macro is finished. If any of the keys is still in down state then release a key state (ie. simulate key up event) unless special action specified to keep the last state as it is left in a macro
                if (action == null || !action.keepKeyState)
                {
                    for (int i = 0, arlength = keydown.Length; i < arlength; i++)
                    {
                        if (keydown[i])
                            PlayMacroCodeValue(device, macrocontrol, keyType, i, keydown);
                    }
                }

                DS4LightBar.ForcedFlash[device] = 0;
                DS4LightBar.ForceLight[device] = false;

                // Commented out rumble reset. No need to zero out rumble after a macro because it may conflict with a game generated rumble events (ie. macro would stop a game generated rumble effect).
                // If macro generates rumble effects then the macro can stop the rumble as a last step or wait for rumble watchdog timer to do it after few seconds.
                //Program.rootHub.DS4Controllers[device].setRumble(0, 0);

                if (keyType.HasFlag(DS4KeyType.HoldMacro))
                {
                    Task.Delay(50).Wait();
                    if (control != DS4Controls.None)
                        MacroDone[DS4ControlToInt(control)] = false;
                }
            }

            // If a special action type of Macro has "Repeat while held" option and actionDoneState object is defined then reset the action back to "not done" status in order to re-fire it if the trigger key is still held down
            if (actionDoneState != null && keyType.HasFlag(DS4KeyType.RepeatMacro))
                actionDoneState.dev[device] = false;
        }

        private static bool PlayMacroCodeValue(int device, bool[] macrocontrol, DS4KeyType keyType, int macroCodeValue, bool[] keydown)
        {
            bool doDelayOnCaller = false;
            if (macroCodeValue >= 261 && macroCodeValue <= 285)
            {
                // Gamepad button up or down macro event. macroCodeValue index value is the button identifier (codeValue-261 = idx in 0..24 range)
                if (!keydown[macroCodeValue])
                {
                    macroControl[macroCodeValue - 261] = keydown[macroCodeValue] = true;
                    macroCount++;
                }
                else
                {
                    macroControl[macroCodeValue - 261] = keydown[macroCodeValue] = false;
                    if (macroCount > 0) macroCount--;
                }
            }
            else if (macroCodeValue < 300)
            {
                // Keyboard key or mouse button macro event
                if (!keydown[macroCodeValue])
                {
                    switch (macroCodeValue)
                    {
                        //anything above 255 is not a keyvalue
                        case 256: InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_LEFTDOWN); break;
                        case 257: InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_RIGHTDOWN); break;
                        case 258: InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_MIDDLEDOWN); break;
                        case 259: InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONDOWN, 1); break;
                        case 260: InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONDOWN, 2); break;

                        default:
                            if (keyType.HasFlag(DS4KeyType.ScanCode)) InputMethods.performSCKeyPress((ushort)macroCodeValue);
                            else InputMethods.performKeyPress((ushort)macroCodeValue);
                            break;
                    }
                    keydown[macroCodeValue] = true;
                }
                else
                {
                    switch (macroCodeValue)
                    {
                        //anything above 255 is not a keyvalue
                        case 256: InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_LEFTUP); break;
                        case 257: InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_RIGHTUP); break;
                        case 258: InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_MIDDLEUP); break;
                        case 259: InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONUP, 1); break;
                        case 260: InputMethods.MouseEvent(InputMethods.MOUSEEVENTF_XBUTTONUP, 2); break;

                        default:
                            if (keyType.HasFlag(DS4KeyType.ScanCode)) InputMethods.performSCKeyRelease((ushort)macroCodeValue);
                            else InputMethods.performKeyRelease((ushort)macroCodeValue);
                            break;
                    }
                    keydown[macroCodeValue] = false;
                }
            }
            else if (macroCodeValue >= 1000000000)
            {
                // Lightbar color event
                if (macroCodeValue > 1000000000)
                {
                    string lb = macroCodeValue.ToString().Substring(1);
                    byte r = (byte)(int.Parse(lb[0].ToString()) * 100 + int.Parse(lb[1].ToString()) * 10 + int.Parse(lb[2].ToString()));
                    byte g = (byte)(int.Parse(lb[3].ToString()) * 100 + int.Parse(lb[4].ToString()) * 10 + int.Parse(lb[5].ToString()));
                    byte b = (byte)(int.Parse(lb[6].ToString()) * 100 + int.Parse(lb[7].ToString()) * 10 + int.Parse(lb[8].ToString()));
                    DS4LightBar.ForceLight[device] = true;
                    DS4LightBar.ForcedFlash[device] = 0;
                    DS4LightBar.ForcedColor[device] = new DS4Color(r, g, b);
                }
                else
                {
                    DS4LightBar.ForcedFlash[device] = 0;
                    DS4LightBar.ForceLight[device] = false;
                }
            }
            else if (macroCodeValue >= 1000000)
            {
                // Rumble event
                DS4Device d = Program.RootHub.Controllers[device].Device;
                string r = macroCodeValue.ToString().Substring(1);
                byte heavy = (byte)(int.Parse(r[0].ToString()) * 100 + int.Parse(r[1].ToString()) * 10 + int.Parse(r[2].ToString()));
                byte light = (byte)(int.Parse(r[3].ToString()) * 100 + int.Parse(r[4].ToString()) * 10 + int.Parse(r[5].ToString()));
                d.SetRumble(light, heavy);
            }
            else
            {
                // Delay specification. Indicate to caller that it should do a delay of macroCodeValue-300 msecs
                doDelayOnCaller = true;
            }

            return doDelayOnCaller;
        }

        private static void EndMacro(int device, bool[] macrocontrol, string macro, DS4Controls control)
        {
            if ((macro.StartsWith("164/9/9/164") || macro.StartsWith("18/9/9/18")) && !altTabDone)
                AltTabSwappingRelease();

            if (control != DS4Controls.None)
                MacroDone[DS4ControlToInt(control)] = false;
        }

        private static void EndMacro(int device, bool[] macrocontrol, List<int> macro, DS4Controls control)
        {
            if(macro.Count >= 4 && ((macro[0] == 164 && macro[1] == 9 && macro[2] == 9 && macro[3] == 164) || (macro[0] == 18 && macro[1] == 9 && macro[2] == 9 && macro[3] == 18)) && !altTabDone)
                AltTabSwappingRelease();

            if (control != DS4Controls.None)
                MacroDone[DS4ControlToInt(control)] = false;
        }

        private static void EndMacro(int device, bool[] macrocontrol, int[] macro, DS4Controls control)
        {
            if (macro.Length >= 4 && ((macro[0] == 164 && macro[1] == 9 && macro[2] == 9 && macro[3] == 164) || (macro[0] == 18 && macro[1] == 9 && macro[2] == 9 && macro[3] == 18)) && !altTabDone)
                AltTabSwappingRelease();

            if (control != DS4Controls.None)
                MacroDone[DS4ControlToInt(control)] = false;
        }

        private static void AltTabSwapping(int wait, int device)
        {
            if (altTabDone)
            {
                altTabDone = false;
                InputMethods.performKeyPress(18);
            }
            else
            {
                altTabNow = DateTime.UtcNow;
                if (altTabNow >= oldAltTabNow + TimeSpan.FromMilliseconds(wait))
                {
                    oldAltTabNow = altTabNow;
                    InputMethods.performKeyPress(9);
                    InputMethods.performKeyRelease(9);
                }
            }
        }

        private static void AltTabSwappingRelease()
        {
            if (altTabNow < DateTime.UtcNow - TimeSpan.FromMilliseconds(10)) //in case multiple controls are mapped to alt+tab
            {
                altTabDone = true;
                InputMethods.performKeyRelease(9);
                InputMethods.performKeyRelease(18);
                altTabNow = DateTime.UtcNow;
                oldAltTabNow = DateTime.UtcNow - TimeSpan.FromDays(1);
            }
        }

        private static void GetMouseWheelMapping(
            int device,
            DS4Controls control,
            DS4State cState,
            DS4StateExposed eState,
            Mouse tp,
            bool down)
        {
            DateTime now = DateTime.UtcNow;
            if (now >= oldnow + TimeSpan.FromMilliseconds(10) && !pressagain)
            {
                oldnow = now;
                InputMethods.MouseWheel((int)(getByteMapping(device, control, cState, eState, tp) / 8.0f * (down ? -1 : 1)), 0);
            }
        }

        private static double GetMouseMapping(
            int device,
            DS4Controls control,
            DS4State cState,
            DS4StateExposed eState,
            DS4StateFieldMapping fieldMapping,
            int mnum,
            ControlService ctrl)
        {
            int controlnum = DS4ControlToInt(control);

            int deadzoneL = 0;
            int deadzoneR = 0;
            if (GetLSDeadzone(device) == 0)
                deadzoneL = 3;
            if (GetRSDeadzone(device) == 0)
                deadzoneR = 3;

            double value = 0.0;
            int speed = ButtonMouseSensitivity[device];
            double root = 1.002;
            double divide = 10000d;

            int controlNum = (int)control;
            DS4StateFieldMapping.ControlType controlType = DS4StateFieldMapping.MappedType[controlNum];

            double timeElapsed = ctrl.Controllers[device].Device.LastTimeElapsedDouble;
            if (controlType == DS4StateFieldMapping.ControlType.Button)
            {
                bool active = fieldMapping.Buttons[controlNum];
                value = active ? Math.Pow(root + speed / divide, 100) - 1 : 0;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.AxisDir)
            {
                //double mouseOffset = 0.025;

                double tempMouseOffsetX;
                double tempMouseOffsetY;

                switch (control)
                {
                    case DS4Controls.LXNeg:
                        {
                            if (cState.LX < 128 - deadzoneL)
                            {
                                double diff = -(cState.LX - 128 - deadzoneL) / (double)(0 - 128 - deadzoneL);
                                //tempMouseOffsetX = Math.Abs(Math.Cos(cState.LSAngleRad)) * MOUSESTICKOFFSET;
                                //tempMouseOffsetX = MOUSESTICKOFFSET;
                                tempMouseOffsetX = cState.LXUnit * MOUSESTICKOFFSET;
                                value = ((speed * MOUSESPEEDFACTOR * (timeElapsed * 0.001)) - tempMouseOffsetX) * diff + (tempMouseOffsetX * -1.0);
                                //value = diff * MOUSESPEEDFACTOR * (timeElapsed * 0.001) * speed;
                                //value = -(cState.LX - 127 - deadzoneL) / 2550d * speed;
                            }

                            break;
                        }
                    case DS4Controls.LXPos:
                        {
                            if (cState.LX > 128 + deadzoneL)
                            {
                                double diff = (cState.LX - 128 + deadzoneL) / (double)(255 - 128 + deadzoneL);
                                tempMouseOffsetX = cState.LXUnit * MOUSESTICKOFFSET;
                                //tempMouseOffsetX = Math.Abs(Math.Cos(cState.LSAngleRad)) * MOUSESTICKOFFSET;
                                //tempMouseOffsetX = MOUSESTICKOFFSET;
                                value = ((speed * MOUSESPEEDFACTOR * (timeElapsed * 0.001)) - tempMouseOffsetX) * diff + tempMouseOffsetX;
                                //value = diff * MOUSESPEEDFACTOR * (timeElapsed * 0.001) * speed;
                                //value = (cState.LX - 127 + deadzoneL) / 2550d * speed;
                            }

                            break;
                        }
                    case DS4Controls.RXNeg:
                        {
                            if (cState.RX < 128 - deadzoneR)
                            {
                                double diff = -(cState.RX - 128 - deadzoneR) / (double)(0 - 128 - deadzoneR);
                                tempMouseOffsetX = cState.RXUnit * MOUSESTICKOFFSET;
                                //tempMouseOffsetX = MOUSESTICKOFFSET;
                                //tempMouseOffsetX = Math.Abs(Math.Cos(cState.RSAngleRad)) * MOUSESTICKOFFSET;
                                value = ((speed * MOUSESPEEDFACTOR * (timeElapsed * 0.001)) - tempMouseOffsetX) * diff + (tempMouseOffsetX * -1.0);
                                //value = diff * MOUSESPEEDFACTOR * (timeElapsed * 0.001) * speed;
                                //value = -(cState.RX - 127 - deadzoneR) / 2550d * speed;
                            }

                            break;
                        }
                    case DS4Controls.RXPos:
                        {
                            if (cState.RX > 128 + deadzoneR)
                            {
                                double diff = (cState.RX - 128 + deadzoneR) / (double)(255 - 128 + deadzoneR);
                                tempMouseOffsetX = cState.RXUnit * MOUSESTICKOFFSET;
                                //tempMouseOffsetX = MOUSESTICKOFFSET;
                                //tempMouseOffsetX = Math.Abs(Math.Cos(cState.RSAngleRad)) * MOUSESTICKOFFSET;
                                value = ((speed * MOUSESPEEDFACTOR * (timeElapsed * 0.001)) - tempMouseOffsetX) * diff + tempMouseOffsetX;
                                //value = diff * MOUSESPEEDFACTOR * (timeElapsed * 0.001) * speed;
                                //value = (cState.RX - 127 + deadzoneR) / 2550d * speed;
                            }

                            break;
                        }
                    case DS4Controls.LYNeg:
                        {
                            if (cState.LY < 128 - deadzoneL)
                            {
                                double diff = -(cState.LY - 128 - deadzoneL) / (double)(0 - 128 - deadzoneL);
                                tempMouseOffsetY = cState.LYUnit * MOUSESTICKOFFSET;
                                //tempMouseOffsetY = MOUSESTICKOFFSET;
                                //tempMouseOffsetY = Math.Abs(Math.Sin(cState.LSAngleRad)) * MOUSESTICKOFFSET;
                                value = ((speed * MOUSESPEEDFACTOR * (timeElapsed * 0.001)) - tempMouseOffsetY) * diff + (tempMouseOffsetY * -1.0);
                                //value = diff * MOUSESPEEDFACTOR * (timeElapsed * 0.001) * speed;
                                //value = -(cState.LY - 127 - deadzoneL) / 2550d * speed;
                            }

                            break;
                        }
                    case DS4Controls.LYPos:
                        {
                            if (cState.LY > 128 + deadzoneL)
                            {
                                double diff = (cState.LY - 128 + deadzoneL) / (double)(255 - 128 + deadzoneL);
                                tempMouseOffsetY = cState.LYUnit * MOUSESTICKOFFSET;
                                //tempMouseOffsetY = MOUSESTICKOFFSET;
                                //tempMouseOffsetY = Math.Abs(Math.Sin(cState.LSAngleRad)) * MOUSESTICKOFFSET;
                                value = ((speed * MOUSESPEEDFACTOR * (timeElapsed * 0.001)) - tempMouseOffsetY) * diff + tempMouseOffsetY;
                                //value = diff * MOUSESPEEDFACTOR * (timeElapsed * 0.001) * speed;
                                //value = (cState.LY - 127 + deadzoneL) / 2550d * speed;
                            }

                            break;
                        }
                    case DS4Controls.RYNeg:
                        {
                            if (cState.RY < 128 - deadzoneR)
                            {
                                double diff = -(cState.RY - 128 - deadzoneR) / (double)(0 - 128 - deadzoneR);
                                tempMouseOffsetY = cState.RYUnit * MOUSESTICKOFFSET;
                                //tempMouseOffsetY = MOUSESTICKOFFSET;
                                //tempMouseOffsetY = Math.Abs(Math.Sin(cState.RSAngleRad)) * MOUSESTICKOFFSET;
                                value = ((speed * MOUSESPEEDFACTOR * (timeElapsed * 0.001)) - tempMouseOffsetY) * diff + (tempMouseOffsetY * -1.0);
                                //value = diff * MOUSESPEEDFACTOR * (timeElapsed * 0.001) * speed;
                                //value = -(cState.RY - 127 - deadzoneR) / 2550d * speed;
                            }

                            break;
                        }
                    case DS4Controls.RYPos:
                        {
                            if (cState.RY > 128 + deadzoneR)
                            {
                                double diff = (cState.RY - 128 + deadzoneR) / (double)(255 - 128 + deadzoneR);
                                tempMouseOffsetY = cState.RYUnit * MOUSESTICKOFFSET;
                                //tempMouseOffsetY = MOUSESTICKOFFSET;
                                //tempMouseOffsetY = Math.Abs(Math.Sin(cState.RSAngleRad)) * MOUSESTICKOFFSET;
                                value = ((speed * MOUSESPEEDFACTOR * (timeElapsed * 0.001)) - tempMouseOffsetY) * diff + tempMouseOffsetY;
                                //value = diff * MOUSESPEEDFACTOR * (timeElapsed * 0.001) * speed;
                                //value = (cState.RY - 127 + deadzoneR) / 2550d * speed;
                            }

                            break;
                        }

                    default: break;
                }
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Trigger)
            {
                byte trigger = fieldMapping.Triggers[controlNum];
                value = Math.Pow(root + speed / divide, trigger / 2d) - 1;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.GyroDir)
            {
                //double SXD = getSXDeadzone(device);
                //double SZD = getSZDeadzone(device);

                switch (control)
                {
                    case DS4Controls.GyroXPos:
                        {
                            int gyroX = fieldMapping.gryodirs[controlNum];
                            value = (byte)(gyroX > 0 ? Math.Pow(root + speed / divide, gyroX) : 0);
                            break;
                        }
                    case DS4Controls.GyroXNeg:
                        {
                            int gyroX = fieldMapping.gryodirs[controlNum];
                            value = (byte)(gyroX < 0 ? Math.Pow(root + speed / divide, -gyroX) : 0);
                            break;
                        }
                    case DS4Controls.GyroZPos:
                        {
                            int gyroZ = fieldMapping.gryodirs[controlNum];
                            value = (byte)(gyroZ > 0 ? Math.Pow(root + speed / divide, gyroZ) : 0);
                            break;
                        }
                    case DS4Controls.GyroZNeg:
                        {
                            int gyroZ = fieldMapping.gryodirs[controlNum];
                            value = (byte)(gyroZ < 0 ? Math.Pow(root + speed / divide, -gyroZ) : 0);
                            break;
                        }
                    default: break;
                }
            }

            if (getMouseAccel(device))
            {
                if (value > 0)
                {
                    mcounter = 34;
                    mouseaccel++;
                }

                if (mouseaccel == prevmouseaccel)
                {
                    mcounter--;
                }

                if (mcounter <= 0)
                {
                    mouseaccel = 0;
                    mcounter = 34;
                }

                value *= 1 + Math.Min(20000, mouseaccel) / 10000d;
                prevmouseaccel = mouseaccel;
            }

            return value;
        }

        private static void CalcFinalMouseMovement(ref double rawMouseX, ref double rawMouseY,
            out int mouseX, out int mouseY)
        {
            if ((rawMouseX > 0.0 && horizontalRemainder > 0.0) || (rawMouseX < 0.0 && horizontalRemainder < 0.0))
            {
                rawMouseX += horizontalRemainder;
            }
            else
            {
                horizontalRemainder = 0.0;
            }

            //double mouseXTemp = rawMouseX - (Math.IEEERemainder(rawMouseX * 1000.0, 1.0) / 1000.0);
            double mouseXTemp = rawMouseX - (remainderCutoff(rawMouseX * 1000.0, 1.0) / 1000.0);
            //double mouseXTemp = rawMouseX - (rawMouseX * 1000.0 - (1.0 * (int)(rawMouseX * 1000.0 / 1.0)));
            mouseX = (int)mouseXTemp;
            horizontalRemainder = mouseXTemp - mouseX;
            //mouseX = (int)rawMouseX;
            //horizontalRemainder = rawMouseX - mouseX;

            if ((rawMouseY > 0.0 && verticalRemainder > 0.0) || (rawMouseY < 0.0 && verticalRemainder < 0.0))
            {
                rawMouseY += verticalRemainder;
            }
            else
            {
                verticalRemainder = 0.0;
            }

            //double mouseYTemp = rawMouseY - (Math.IEEERemainder(rawMouseY * 1000.0, 1.0) / 1000.0);
            double mouseYTemp = rawMouseY - (remainderCutoff(rawMouseY * 1000.0, 1.0) / 1000.0);
            mouseY = (int)mouseYTemp;
            verticalRemainder = mouseYTemp - mouseY;
            //mouseY = (int)rawMouseY;
            //verticalRemainder = rawMouseY - mouseY;
        }

        private static double remainderCutoff(double dividend, double divisor)
        {
            return dividend - (divisor * (int)(dividend / divisor));
        }

        public static bool compare(byte b1, byte b2)
        {
            bool result = true;
            if (Math.Abs(b1 - b2) > 10)
            {
                result = false;
            }

            return result;
        }

        private static byte getByteMapping2(int device, DS4Controls control, DS4State cState, DS4StateExposed eState, Mouse tp,
            DS4StateFieldMapping fieldMap)
        {
            byte result = 0;

            int controlNum = (int)control;
            DS4StateFieldMapping.ControlType controlType = DS4StateFieldMapping.MappedType[controlNum];
            if (controlType == DS4StateFieldMapping.ControlType.Button)
            {
                result = (byte)(fieldMap.Buttons[controlNum] ? 255 : 0);
            }
            else if (controlType == DS4StateFieldMapping.ControlType.AxisDir)
            {
                byte axisValue = fieldMap.AxisDirs[controlNum];

                switch (control)
                {
                    case DS4Controls.LXNeg: result = (byte)(axisValue - 128.0f >= 0 ? 0 : -(axisValue - 128.0f) * 1.9921875f); break;
                    case DS4Controls.LYNeg: result = (byte)(axisValue - 128.0f >= 0 ? 0 : -(axisValue - 128.0f) * 1.9921875f); break;
                    case DS4Controls.RXNeg: result = (byte)(axisValue - 128.0f >= 0 ? 0 : -(axisValue - 128.0f) * 1.9921875f); break;
                    case DS4Controls.RYNeg: result = (byte)(axisValue - 128.0f >= 0 ? 0 : -(axisValue - 128.0f) * 1.9921875f); break;
                    default: result = (byte)(axisValue - 128.0f < 0 ? 0 : (axisValue - 128.0f) * 2.0078740157480315f); break;
                }
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Trigger)
            {
                result = fieldMap.Triggers[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Touch)
            {
                result = (byte)(tp != null && fieldMap.Buttons[controlNum] ? 255 : 0);
            }
            else if (controlType == DS4StateFieldMapping.ControlType.SwipeDir)
            {
                result = (byte)(tp != null ? fieldMap.swipedirs[controlNum] : 0);
            }
            else if (controlType == DS4StateFieldMapping.ControlType.GyroDir)
            {
                bool sOff = IsUsingSAforMouse(device);

                switch (control)
                {
                    case DS4Controls.GyroXPos:
                    {
                        int gyroX = fieldMap.gryodirs[controlNum];
                        result = (byte)(sOff == false ? Math.Min(255, gyroX * 2) : 0);
                        break;
                    }
                    case DS4Controls.GyroXNeg:
                    {
                        int gyroX = fieldMap.gryodirs[controlNum];
                        result = (byte)(sOff == false ? Math.Min(255, -gyroX * 2) : 0);
                        break;
                    }
                    case DS4Controls.GyroZPos:
                    {
                        int gyroZ = fieldMap.gryodirs[controlNum];
                        result = (byte)(sOff == false ? Math.Min(255, gyroZ * 2) : 0);
                        break;
                    }
                    case DS4Controls.GyroZNeg:
                    {
                        int gyroZ = fieldMap.gryodirs[controlNum];
                        result = (byte)(sOff == false ? Math.Min(255, -gyroZ * 2) : 0);
                        break;
                    }
                    default: break;
                }
            }

            return result;
        }

        public static byte getByteMapping(int device, DS4Controls control, DS4State cState, DS4StateExposed eState, Mouse tp)
        {
            byte result = 0;

            if (control >= DS4Controls.Square && control <= DS4Controls.Cross)
            {
                switch (control)
                {
                    case DS4Controls.Cross: result = (byte)(cState.Cross ? 255 : 0); break;
                    case DS4Controls.Square: result = (byte)(cState.Square ? 255 : 0); break;
                    case DS4Controls.Triangle: result = (byte)(cState.Triangle ? 255 : 0); break;
                    case DS4Controls.Circle: result = (byte)(cState.Circle ? 255 : 0); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.L1 && control <= DS4Controls.R3)
            {
                switch (control)
                {
                    case DS4Controls.L1: result = (byte)(cState.L1 ? 255 : 0); break;
                    case DS4Controls.L2: result = cState.L2; break;
                    case DS4Controls.L3: result = (byte)(cState.L3 ? 255 : 0); break;
                    case DS4Controls.R1: result = (byte)(cState.R1 ? 255 : 0); break;
                    case DS4Controls.R2: result = cState.R2; break;
                    case DS4Controls.R3: result = (byte)(cState.R3 ? 255 : 0); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.DpadUp && control <= DS4Controls.DpadLeft)
            {
                switch (control)
                {
                    case DS4Controls.DpadUp: result = (byte)(cState.DpadUp ? 255 : 0); break;
                    case DS4Controls.DpadDown: result = (byte)(cState.DpadDown ? 255 : 0); break;
                    case DS4Controls.DpadLeft: result = (byte)(cState.DpadLeft ? 255 : 0); break;
                    case DS4Controls.DpadRight: result = (byte)(cState.DpadRight ? 255 : 0); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.LXNeg && control <= DS4Controls.RYPos)
            {
                switch (control)
                {
                    case DS4Controls.LXNeg: result = (byte)(cState.LX - 128.0f >= 0 ? 0 : -(cState.LX - 128.0f) * 1.9921875f); break;
                    case DS4Controls.LYNeg: result = (byte)(cState.LY - 128.0f >= 0 ? 0 : -(cState.LY - 128.0f) * 1.9921875f); break;
                    case DS4Controls.RXNeg: result = (byte)(cState.RX - 128.0f >= 0 ? 0 : -(cState.RX - 128.0f) * 1.9921875f); break;
                    case DS4Controls.RYNeg: result = (byte)(cState.RY - 128.0f >= 0 ? 0 : -(cState.RY - 128.0f) * 1.9921875f); break;
                    case DS4Controls.LXPos: result = (byte)(cState.LX - 128.0f < 0 ? 0 : (cState.LX - 128.0f) * 2.0078740157480315f); break;
                    case DS4Controls.LYPos: result = (byte)(cState.LY - 128.0f < 0 ? 0 : (cState.LY - 128.0f) * 2.0078740157480315f); break;
                    case DS4Controls.RXPos: result = (byte)(cState.RX - 128.0f < 0 ? 0 : (cState.RX - 128.0f) * 2.0078740157480315f); break;
                    case DS4Controls.RYPos: result = (byte)(cState.RY - 128.0f < 0 ? 0 : (cState.RY - 128.0f) * 2.0078740157480315f); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.TouchLeft && control <= DS4Controls.TouchRight)
            {
                switch (control)
                {
                    case DS4Controls.TouchLeft: result = (byte)(tp != null && tp.leftDown ? 255 : 0); break;
                    case DS4Controls.TouchRight: result = (byte)(tp != null && tp.rightDown ? 255 : 0); break;
                    case DS4Controls.TouchMulti: result = (byte)(tp != null && tp.multiDown ? 255 : 0); break;
                    case DS4Controls.TouchUpper: result = (byte)(tp != null && tp.upperDown ? 255 : 0); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.SwipeLeft && control <= DS4Controls.SwipeDown)
            {
                switch (control)
                {
                    case DS4Controls.SwipeUp: result = (byte)(tp != null ? tp.swipeUpB : 0); break;
                    case DS4Controls.SwipeDown: result = (byte)(tp != null ? tp.swipeDownB : 0); break;
                    case DS4Controls.SwipeLeft: result = (byte)(tp != null ? tp.swipeLeftB : 0); break;
                    case DS4Controls.SwipeRight: result = (byte)(tp != null ? tp.swipeRightB : 0); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.GyroXPos && control <= DS4Controls.GyroZNeg)
            {
                double SXD = GetSXDeadzone(device);
                double SZD = GetSZDeadzone(device);
                bool sOff = IsUsingSAforMouse(device);
                double sxsens = getSXSens(device);
                double szsens = getSZSens(device);

                switch (control)
                {
                    case DS4Controls.GyroXPos:
                    {
                        int gyroX = -eState.AccelX;
                        result = (byte)(!sOff && sxsens * gyroX > SXD * 10 ? Math.Min(255, sxsens * gyroX * 2) : 0);
                        break;
                    }
                    case DS4Controls.GyroXNeg:
                    {
                        int gyroX = -eState.AccelX;
                        result = (byte)(!sOff && sxsens * gyroX < -SXD * 10 ? Math.Min(255, sxsens * -gyroX * 2) : 0);
                        break;
                    }
                    case DS4Controls.GyroZPos:
                    {
                        int gyroZ = eState.AccelZ;
                        result = (byte)(!sOff && szsens * gyroZ > SZD * 10 ? Math.Min(255, szsens * gyroZ * 2) : 0);
                        break;
                    }
                    case DS4Controls.GyroZNeg:
                    {
                        int gyroZ = eState.AccelZ;
                        result = (byte)(!sOff && szsens * gyroZ < -SZD * 10 ? Math.Min(255, szsens * -gyroZ * 2) : 0);
                        break;
                    }
                    default: break;
                }
            }
            else
            {
                switch (control)
                {
                    case DS4Controls.Share: result = (byte)(cState.Share ? 255 : 0); break;
                    case DS4Controls.Options: result = (byte)(cState.Options ? 255 : 0); break;
                    case DS4Controls.PS: result = (byte)(cState.PS ? 255 : 0); break;
                    default: break;
                }
            }

            return result;
        }

        /* TODO: Possibly remove usage of this version of the method */
        public static bool GetBoolMapping(int device, DS4Controls control, DS4State cState, DS4StateExposed eState, Mouse tp)
        {
            bool result = false;

            if (control >= DS4Controls.Square && control <= DS4Controls.Cross)
            {
                switch (control)
                {
                    case DS4Controls.Cross: result = cState.Cross; break;
                    case DS4Controls.Square: result = cState.Square; break;
                    case DS4Controls.Triangle: result = cState.Triangle; break;
                    case DS4Controls.Circle: result = cState.Circle; break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.L1 && control <= DS4Controls.R3)
            {
                switch (control)
                {
                    case DS4Controls.L1: result = cState.L1; break;
                    case DS4Controls.R1: result = cState.R1; break;
                    case DS4Controls.L2: result = cState.L2 > 100; break;
                    case DS4Controls.R2: result = cState.R2 > 100; break;
                    case DS4Controls.L3: result = cState.L3; break;
                    case DS4Controls.R3: result = cState.R3; break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.DpadUp && control <= DS4Controls.DpadLeft)
            {
                switch (control)
                {
                    case DS4Controls.DpadUp: result = cState.DpadUp; break;
                    case DS4Controls.DpadDown: result = cState.DpadDown; break;
                    case DS4Controls.DpadLeft: result = cState.DpadLeft; break;
                    case DS4Controls.DpadRight: result = cState.DpadRight; break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.LXNeg && control <= DS4Controls.RYPos)
            {
                switch (control)
                {
                    case DS4Controls.LXNeg: result = cState.LX < 128 - 55; break;
                    case DS4Controls.LYNeg: result = cState.LY < 128 - 55; break;
                    case DS4Controls.RXNeg: result = cState.RX < 128 - 55; break;
                    case DS4Controls.RYNeg: result = cState.RY < 128 - 55; break;
                    case DS4Controls.LXPos: result = cState.LX > 128 + 55; break;
                    case DS4Controls.LYPos: result = cState.LY > 128 + 55; break;
                    case DS4Controls.RXPos: result = cState.RX > 128 + 55; break;
                    case DS4Controls.RYPos: result = cState.RY > 128 + 55; break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.TouchLeft && control <= DS4Controls.TouchRight)
            {
                switch (control)
                {
                    case DS4Controls.TouchLeft: result = tp != null ? tp.leftDown : false; break;
                    case DS4Controls.TouchRight: result = tp != null ? tp.rightDown : false; break;
                    case DS4Controls.TouchMulti: result = tp != null ? tp.multiDown : false; break;
                    case DS4Controls.TouchUpper: result = tp != null ? tp.upperDown : false; break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.SwipeLeft && control <= DS4Controls.SwipeDown)
            {
                switch (control)
                {
                    case DS4Controls.SwipeUp: result = tp != null && tp.swipeUp; break;
                    case DS4Controls.SwipeDown: result = tp != null && tp.swipeDown; break;
                    case DS4Controls.SwipeLeft: result = tp != null && tp.swipeLeft; break;
                    case DS4Controls.SwipeRight: result = tp != null && tp.swipeRight; break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.GyroXPos && control <= DS4Controls.GyroZNeg)
            {
                bool sOff = IsUsingSAforMouse(device);

                switch (control)
                {
                    case DS4Controls.GyroXPos: result = !sOff ? SXSens[device] * -eState.AccelX > 67 : false; break;
                    case DS4Controls.GyroXNeg: result = !sOff ? SXSens[device] * -eState.AccelX < -67 : false; break;
                    case DS4Controls.GyroZPos: result = !sOff ? SZSens[device] * eState.AccelZ > 67 : false; break;
                    case DS4Controls.GyroZNeg: result = !sOff ? SZSens[device] * eState.AccelZ < -67 : false; break;
                    default: break;
                }
            }
            else
            {
                switch (control)
                {
                    case DS4Controls.PS: result = cState.PS; break;
                    case DS4Controls.Share: result = cState.Share; break;
                    case DS4Controls.Options: result = cState.Options; break;
                    default: break;
                }
            }

            return result;
        }

        private static bool GetBoolMapping2(int device, DS4Controls control,
            DS4State cState, DS4StateExposed eState, Mouse tp, DS4StateFieldMapping fieldMap)
        {
            bool result = false;

            int controlNum = (int)control;
            DS4StateFieldMapping.ControlType controlType = DS4StateFieldMapping.MappedType[controlNum];
            if (controlType == DS4StateFieldMapping.ControlType.Button)
            {
                result = fieldMap.Buttons[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.AxisDir)
            {
                byte axisValue = fieldMap.AxisDirs[controlNum];

                switch (control)
                {
                    case DS4Controls.LXNeg: result = cState.LX < 128 - 55; break;
                    case DS4Controls.LYNeg: result = cState.LY < 128 - 55; break;
                    case DS4Controls.RXNeg: result = cState.RX < 128 - 55; break;
                    case DS4Controls.RYNeg: result = cState.RY < 128 - 55; break;
                    default: result = axisValue > 128 + 55; break;
                }
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Trigger)
            {
                result = fieldMap.Triggers[controlNum] > 100;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Touch)
            {
                result = fieldMap.Buttons[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.SwipeDir)
            {
                result = fieldMap.swipedirbools[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.GyroDir)
            {
                bool sOff = IsUsingSAforMouse(device);
                bool safeTest = false;

                switch (control)
                {
                    case DS4Controls.GyroXPos: safeTest = fieldMap.gryodirs[controlNum] > 0; break;
                    case DS4Controls.GyroXNeg: safeTest = fieldMap.gryodirs[controlNum] < -0; break;
                    case DS4Controls.GyroZPos: safeTest = fieldMap.gryodirs[controlNum] > 0; break;
                    case DS4Controls.GyroZNeg: safeTest = fieldMap.gryodirs[controlNum] < -0; break;
                    default: break;
                }

                result = sOff == false ? safeTest : false;
            }

            return result;
        }

        private static bool getBoolSpecialActionMapping(int device, DS4Controls control,
            DS4State cState, DS4StateExposed eState, Mouse tp, DS4StateFieldMapping fieldMap)
        {
            bool result = false;

            int controlNum = (int)control;
            DS4StateFieldMapping.ControlType controlType = DS4StateFieldMapping.MappedType[controlNum];
            if (controlType == DS4StateFieldMapping.ControlType.Button)
            {
                result = fieldMap.Buttons[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.AxisDir)
            {
                byte axisValue = fieldMap.AxisDirs[controlNum];

                switch (control)
                {
                    case DS4Controls.LXNeg: result = cState.LX < 128 - 55; break;
                    case DS4Controls.LYNeg: result = cState.LY < 128 - 55; break;
                    case DS4Controls.RXNeg: result = cState.RX < 128 - 55; break;
                    case DS4Controls.RYNeg: result = cState.RY < 128 - 55; break;
                    default: result = axisValue > 128 + 55; break;
                }
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Trigger)
            {
                result = fieldMap.Triggers[controlNum] > 100;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Touch)
            {
                result = fieldMap.Buttons[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.SwipeDir)
            {
                result = fieldMap.swipedirbools[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.GyroDir)
            {
                bool sOff = IsUsingSAforMouse(device);
                bool safeTest = false;

                switch (control)
                {
                    case DS4Controls.GyroXPos: safeTest = fieldMap.gryodirs[controlNum] > 67; break;
                    case DS4Controls.GyroXNeg: safeTest = fieldMap.gryodirs[controlNum] < -67; break;
                    case DS4Controls.GyroZPos: safeTest = fieldMap.gryodirs[controlNum] > 67; break;
                    case DS4Controls.GyroZNeg: safeTest = fieldMap.gryodirs[controlNum] < -67; break;
                    default: break;
                }

                result = sOff == false ? safeTest : false;
            }

            return result;
        }

        private static bool GetBoolActionMapping2(int device, DS4Controls control,
            DS4State cState, DS4StateExposed eState, Mouse tp, DS4StateFieldMapping fieldMap, bool analog = false)
        {
            bool result = false;

            int controlNum = (int)control;
            DS4StateFieldMapping.ControlType controlType = DS4StateFieldMapping.MappedType[controlNum];
            if (controlType == DS4StateFieldMapping.ControlType.Button)
            {
                result = fieldMap.Buttons[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.AxisDir)
            {
                switch (control)
                {
                    case DS4Controls.LXNeg:
                    {
                        double angle = cState.LSAngle;
                        result = cState.LX < 128 && angle >= 112.5 && angle <= 247.5;
                        break;
                    }
                    case DS4Controls.LYNeg:
                    {
                        double angle = cState.LSAngle;
                        result = cState.LY < 128 && angle >= 22.5 && angle <= 157.5;
                        break;
                    }
                    case DS4Controls.RXNeg:
                    {
                        double angle = cState.RSAngle;
                        result = cState.RX < 128 && angle >= 112.5 && angle <= 247.5;
                        break;
                    }
                    case DS4Controls.RYNeg:
                    {
                        double angle = cState.RSAngle;
                        result = cState.RY < 128 && angle >= 22.5 && angle <= 157.5;
                        break;
                    }
                    case DS4Controls.LXPos:
                    {
                        double angle = cState.LSAngle;
                        result = cState.LX > 128 && (angle <= 67.5 || angle >= 292.5);
                        break;
                    }
                    case DS4Controls.LYPos:
                    {
                        double angle = cState.LSAngle;
                        result = cState.LY > 128 && angle >= 202.5 && angle <= 337.5;
                        break;
                    }
                    case DS4Controls.RXPos:
                    {
                        double angle = cState.RSAngle;
                        result = cState.RX > 128 && (angle <= 67.5 || angle >= 292.5);
                        break;
                    }
                    case DS4Controls.RYPos:
                    {
                        double angle = cState.RSAngle;
                        result = cState.RY > 128 && angle >= 202.5 && angle <= 337.5;
                        break;
                    }
                    default: break;
                }
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Trigger)
            {
                result = fieldMap.Triggers[controlNum] > 0;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Touch)
            {
                result = fieldMap.Buttons[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.SwipeDir)
            {
                result = fieldMap.swipedirbools[controlNum];
            }
            else if (controlType == DS4StateFieldMapping.ControlType.GyroDir)
            {
                bool sOff = IsUsingSAforMouse(device);
                bool safeTest = false;

                switch (control)
                {
                    case DS4Controls.GyroXPos: safeTest = fieldMap.gryodirs[controlNum] > 0; break;
                    case DS4Controls.GyroXNeg: safeTest = fieldMap.gryodirs[controlNum] < 0; break;
                    case DS4Controls.GyroZPos: safeTest = fieldMap.gryodirs[controlNum] > 0; break;
                    case DS4Controls.GyroZNeg: safeTest = fieldMap.gryodirs[controlNum] < 0; break;
                    default: break;
                }

                result = sOff == false ? safeTest : false;
            }

            return result;
        }

        public static bool getBoolButtonMapping(bool stateButton)
        {
            return stateButton;
        }

        public static bool getBoolAxisDirMapping(byte stateAxis, bool positive)
        {
            return positive ? stateAxis > 128 + 55 : stateAxis < 128 - 55;
        }

        public static bool GetBoolTriggerMapping(byte stateAxis)
        {
            return stateAxis > 100;
        }

        public static bool GetBoolTouchMapping(bool touchButton)
        {
            return touchButton;
        }

        private static byte GetXYAxisMapping2(int device, DS4Controls control, DS4State cState, DS4StateExposed eState, Mouse tp, DS4StateFieldMapping fieldMap, bool alt = false)
        {
            const byte falseVal = 128;
            byte result = 0;
            byte trueVal = 0;

            if (alt)
                trueVal = 255;

            int controlNum = (int)control;
            DS4StateFieldMapping.ControlType controlType = DS4StateFieldMapping.MappedType[controlNum];

            if (controlType == DS4StateFieldMapping.ControlType.Button)
            {
                result = fieldMap.Buttons[controlNum] ? trueVal : falseVal;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.AxisDir)
            {
                byte axisValue = fieldMap.AxisDirs[controlNum];

                switch (control)
                {
                    case DS4Controls.LXNeg: if (!alt) result = axisValue < falseVal ? axisValue : falseVal; else result = axisValue < falseVal ? (byte)(255 - axisValue) : falseVal; break;
                    case DS4Controls.LYNeg: if (!alt) result = axisValue < falseVal ? axisValue : falseVal; else result = axisValue < falseVal ? (byte)(255 - axisValue) : falseVal; break;
                    case DS4Controls.RXNeg: if (!alt) result = axisValue < falseVal ? axisValue : falseVal; else result = axisValue < falseVal ? (byte)(255 - axisValue) : falseVal; break;
                    case DS4Controls.RYNeg: if (!alt) result = axisValue < falseVal ? axisValue : falseVal; else result = axisValue < falseVal ? (byte)(255 - axisValue) : falseVal; break;
                    default: if (!alt) result = axisValue > falseVal ? (byte)(255 - axisValue) : falseVal; else result = axisValue > falseVal ? axisValue : falseVal; break;
                }
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Trigger)
            {
                if (alt)
                {
                    result = (byte)(128.0f + fieldMap.Triggers[controlNum] / 2.0078740157480315f);
                }
                else
                {
                    result = (byte)(128.0f - fieldMap.Triggers[controlNum] / 2.0078740157480315f);
                }
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Touch)
            {
                result = fieldMap.Buttons[controlNum] ? trueVal : falseVal;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.SwipeDir)
            {
                if (alt)
                {
                    result = (byte)(tp != null ? 127.5f + fieldMap.swipedirs[controlNum] / 2f : 0);
                }
                else
                {
                    result = (byte)(tp != null ? 127.5f - fieldMap.swipedirs[controlNum] / 2f : 0);
                }
            }
            else if (controlType == DS4StateFieldMapping.ControlType.GyroDir)
            {
                bool sOff = IsUsingSAforMouse(device);

                switch (control)
                {
                    case DS4Controls.GyroXPos:
                    {
                        if (sOff == false && fieldMap.gryodirs[controlNum] > 0)
                        {
                            if (alt) result = (byte)Math.Min(255, 127 + fieldMap.gryodirs[controlNum]); else result = (byte)Math.Max(0, 127 - fieldMap.gryodirs[controlNum]);
                        }
                        else result = falseVal;
                        break;
                    }
                    case DS4Controls.GyroXNeg:
                    {
                        if (sOff == false && fieldMap.gryodirs[controlNum] < 0)
                        {
                            if (alt) result = (byte)Math.Min(255, 127 + -fieldMap.gryodirs[controlNum]); else result = (byte)Math.Max(0, 127 - -fieldMap.gryodirs[controlNum]);
                        }
                        else result = falseVal;
                        break;
                    }
                    case DS4Controls.GyroZPos:
                    {
                        if (sOff == false && fieldMap.gryodirs[controlNum] > 0)
                        {
                            if (alt) result = (byte)Math.Min(255, 127 + fieldMap.gryodirs[controlNum]); else result = (byte)Math.Max(0, 127 - fieldMap.gryodirs[controlNum]);
                        }
                        else return falseVal;
                        break;
                    }
                    case DS4Controls.GyroZNeg:
                    {
                        if (sOff == false && fieldMap.gryodirs[controlNum] < 0)
                        {
                            if (alt) result = (byte)Math.Min(255, 127 + -fieldMap.gryodirs[controlNum]); else result = (byte)Math.Max(0, 127 - -fieldMap.gryodirs[controlNum]);
                        }
                        else result = falseVal;
                        break;
                    }
                    default: break;
                }
            }

            return result;
        }

        /* TODO: Possibly remove usage of this version of the method */
        public static byte GetXYAxisMapping(int device, DS4Controls control, DS4State cState, DS4StateExposed eState, Mouse tp, bool alt = false)
        {
            byte result = 0;
            byte trueVal = 0;
            byte falseVal = 127;

            if (alt)
                trueVal = 255;

            if (control >= DS4Controls.Square && control <= DS4Controls.Cross)
            {
                switch (control)
                {
                    case DS4Controls.Cross: result = (byte)(cState.Cross ? trueVal : falseVal); break;
                    case DS4Controls.Square: result = (byte)(cState.Square ? trueVal : falseVal); break;
                    case DS4Controls.Triangle: result = (byte)(cState.Triangle ? trueVal : falseVal); break;
                    case DS4Controls.Circle: result = (byte)(cState.Circle ? trueVal : falseVal); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.L1 && control <= DS4Controls.R3)
            {
                switch (control)
                {
                    case DS4Controls.L1: result = (byte)(cState.L1 ? trueVal : falseVal); break;
                    case DS4Controls.L2: if (alt) result = (byte)(128.0f + cState.L2 / 2.0078740157480315f); else result = (byte)(128.0f - cState.L2 / 2.0078740157480315f); break;
                    case DS4Controls.L3: result = (byte)(cState.L3 ? trueVal : falseVal); break;
                    case DS4Controls.R1: result = (byte)(cState.R1 ? trueVal : falseVal); break;
                    case DS4Controls.R2: if (alt) result = (byte)(128.0f + cState.R2 / 2.0078740157480315f); else result = (byte)(128.0f - cState.R2 / 2.0078740157480315f); break;
                    case DS4Controls.R3: result = (byte)(cState.R3 ? trueVal : falseVal); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.DpadUp && control <= DS4Controls.DpadLeft)
            {
                switch (control)
                {
                    case DS4Controls.DpadUp: result = (byte)(cState.DpadUp ? trueVal : falseVal); break;
                    case DS4Controls.DpadDown: result = (byte)(cState.DpadDown ? trueVal : falseVal); break;
                    case DS4Controls.DpadLeft: result = (byte)(cState.DpadLeft ? trueVal : falseVal); break;
                    case DS4Controls.DpadRight: result = (byte)(cState.DpadRight ? trueVal : falseVal); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.LXNeg && control <= DS4Controls.RYPos)
            {
                switch (control)
                {
                    case DS4Controls.LXNeg: if (!alt) result = cState.LX; else result = (byte)(255 - cState.LX); break;
                    case DS4Controls.LYNeg: if (!alt) result = cState.LY; else result = (byte)(255 - cState.LY); break;
                    case DS4Controls.RXNeg: if (!alt) result = cState.RX; else result = (byte)(255 - cState.RX); break;
                    case DS4Controls.RYNeg: if (!alt) result = cState.RY; else result = (byte)(255 - cState.RY); break;
                    case DS4Controls.LXPos: if (!alt) result = (byte)(255 - cState.LX); else result = cState.LX; break;
                    case DS4Controls.LYPos: if (!alt) result = (byte)(255 - cState.LY); else result = cState.LY; break;
                    case DS4Controls.RXPos: if (!alt) result = (byte)(255 - cState.RX); else result = cState.RX; break;
                    case DS4Controls.RYPos: if (!alt) result = (byte)(255 - cState.RY); else result = cState.RY; break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.TouchLeft && control <= DS4Controls.TouchRight)
            {
                switch (control)
                {
                    case DS4Controls.TouchLeft: result = (byte)(tp != null && tp.leftDown ? trueVal : falseVal); break;
                    case DS4Controls.TouchRight: result = (byte)(tp != null && tp.rightDown ? trueVal : falseVal); break;
                    case DS4Controls.TouchMulti: result = (byte)(tp != null && tp.multiDown ? trueVal : falseVal); break;
                    case DS4Controls.TouchUpper: result = (byte)(tp != null && tp.upperDown ? trueVal : falseVal); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.SwipeLeft && control <= DS4Controls.SwipeDown)
            {
                switch (control)
                {
                    case DS4Controls.SwipeUp: if (alt) result = (byte)(tp != null ? 127.5f + tp.swipeUpB / 2f : 0); else result = (byte)(tp != null ? 127.5f - tp.swipeUpB / 2f : 0); break;
                    case DS4Controls.SwipeDown: if (alt) result = (byte)(tp != null ? 127.5f + tp.swipeDownB / 2f : 0); else result = (byte)(tp != null ? 127.5f - tp.swipeDownB / 2f : 0); break;
                    case DS4Controls.SwipeLeft: if (alt) result = (byte)(tp != null ? 127.5f + tp.swipeLeftB / 2f : 0); else result = (byte)(tp != null ? 127.5f - tp.swipeLeftB / 2f : 0); break;
                    case DS4Controls.SwipeRight: if (alt) result = (byte)(tp != null ? 127.5f + tp.swipeRightB / 2f : 0); else result = (byte)(tp != null ? 127.5f - tp.swipeRightB / 2f : 0); break;
                    default: break;
                }
            }
            else if (control >= DS4Controls.GyroXPos && control <= DS4Controls.GyroZNeg)
            {
                double SXD = GetSXDeadzone(device);
                double SZD = GetSZDeadzone(device);
                bool sOff = IsUsingSAforMouse(device);

                switch (control)
                {
                    case DS4Controls.GyroXPos:
                    {
                        if (!sOff && -eState.AccelX > SXD * 10)
                        {
                            if (alt) result = (byte)Math.Min(255, 127 + SXSens[device] * -eState.AccelX); else result = (byte)Math.Max(0, 127 - SXSens[device] * -eState.AccelX);
                        }
                        else result = falseVal;
                        break;
                    }
                    case DS4Controls.GyroXNeg:
                    {
                        if (!sOff && -eState.AccelX < -SXD * 10)
                        {
                            if (alt) result = (byte)Math.Min(255, 127 + SXSens[device] * eState.AccelX); else result = (byte)Math.Max(0, 127 - SXSens[device] * eState.AccelX);
                        }
                        else result = falseVal;
                        break;
                    }
                    case DS4Controls.GyroZPos:
                    {
                        if (!sOff && eState.AccelZ > SZD * 10)
                        {
                            if (alt) result = (byte)Math.Min(255, 127 + SZSens[device] * eState.AccelZ); else result = (byte)Math.Max(0, 127 - SZSens[device] * eState.AccelZ);
                        }
                        else return falseVal;
                        break;
                    }
                    case DS4Controls.GyroZNeg:
                    {
                        if (!sOff && eState.AccelZ < -SZD * 10)
                        {
                            if (alt) result = (byte)Math.Min(255, 127 + SZSens[device] * -eState.AccelZ); else result = (byte)Math.Max(0, 127 - SZSens[device] * -eState.AccelZ);
                        }
                        else result = falseVal;
                        break;
                    }
                    default: break;
                }
            }
            else
            {
                switch (control)
                {
                    case DS4Controls.Share: result = cState.Share ? trueVal : falseVal; break;
                    case DS4Controls.Options: result = cState.Options ? trueVal : falseVal; break;
                    case DS4Controls.PS: result = cState.PS ? trueVal : falseVal; break;
                    default: break;
                }
            }

            return result;
        }

        private static void ResetToDefaultValue2(DS4Controls control, DS4State cState, DS4StateFieldMapping fieldMap)
        {
            int controlNum = (int)control;
            DS4StateFieldMapping.ControlType controlType = DS4StateFieldMapping.MappedType[controlNum];
            if (controlType == DS4StateFieldMapping.ControlType.Button)
            {
                fieldMap.Buttons[controlNum] = false;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.AxisDir)
            {
                fieldMap.AxisDirs[controlNum] = 128;
                int controlRelation = controlNum % 2 == 0 ? controlNum - 1 : controlNum + 1;
                fieldMap.AxisDirs[controlRelation] = 128;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Trigger)
            {
                fieldMap.Triggers[controlNum] = 0;
            }
            else if (controlType == DS4StateFieldMapping.ControlType.Touch)
            {
                fieldMap.Buttons[controlNum] = false;
            }
        }


        // SA steering wheel emulation mapping

        private const int C_WHEEL_ANGLE_PRECISION = 10; // Precision of SA angle in 1/10 of degrees
        
        private static readonly DS4Color calibrationColor_0 = new DS4Color { Red = 0xA0, Green = 0x00, Blue = 0x00 };
        private static readonly DS4Color calibrationColor_1 = new DS4Color { Red = 0xFF, Green = 0xFF, Blue = 0x00 };
        private static readonly DS4Color calibrationColor_2 = new DS4Color { Red = 0x00, Green = 0x50, Blue = 0x50 };
        private static readonly DS4Color calibrationColor_3 = new DS4Color { Red = 0x00, Green = 0xC0, Blue = 0x00 };

        private static DateTime latestDebugMsgTime;
        private static string latestDebugData;
        private static void LogToGuiSACalibrationDebugMsg(string data, bool forceOutput = false)
        {
            // Print debug calibration log messages only once per 2 secs to avoid flooding the log receiver
            DateTime curTime = DateTime.Now;
            if (forceOutput || ((TimeSpan)(curTime - latestDebugMsgTime)).TotalSeconds > 2)
            {
                latestDebugMsgTime = curTime;
                if (data != latestDebugData)
                {
                    AppLogger.LogToGui(data, false);
                    latestDebugData = data;
                }
            }
        }

        // Return number of bits set in a value
        protected static int CountNumOfSetBits(int bitValue)
        {
            int count = 0;
            while (bitValue != 0)
            {
                count++;
                bitValue &= bitValue - 1;
            }
            return count;
        }

        // Calculate and return the angle of the controller as -180...0...+180 value.
        private static int CalculateControllerAngle(int gyroAccelX, int gyroAccelZ, DS4Device controller)
        {
            int result;

            if (gyroAccelX == controller.WheelCenterPoint.X && Math.Abs(gyroAccelZ - controller.WheelCenterPoint.Y) <= 1)
            {
                // When the current gyro position is "close enough" the wheel center point then no need to go through the hassle of calculating an angle
                result = 0;
            }
            else
            {
                // Calculate two vectors based on "circle center" (ie. circle represents the 360 degree wheel turn and wheelCenterPoint and currentPosition vectors both start from circle center).
                // To improve accuracy both left and right turns use a decicated calibration "circle" because DS4 gyro and DoItYourselfWheelRig may return slightly different SA sensor values depending on the tilt direction (well, only one or two degree difference so nothing major).
                Point vectorAB;
                Point vectorCD;

                if (gyroAccelX >= controller.WheelCenterPoint.X)
                {
                    // "DS4 gyro wheel" tilted to right
                    vectorAB = new Point(controller.WheelCenterPoint.X - controller.WheelCircleCenterPointRight.X, controller.WheelCenterPoint.Y - controller.WheelCircleCenterPointRight.Y);
                    vectorCD = new Point(gyroAccelX - controller.WheelCircleCenterPointRight.X, gyroAccelZ - controller.WheelCircleCenterPointRight.Y);
                }
                else
                {
                    // "DS4 gyro wheel" tilted to left
                    vectorAB = new Point(controller.WheelCenterPoint.X - controller.WheelCircleCenterPointLeft.X, controller.WheelCenterPoint.Y - controller.WheelCircleCenterPointLeft.Y);
                    vectorCD = new Point(gyroAccelX - controller.WheelCircleCenterPointLeft.X, gyroAccelZ - controller.WheelCircleCenterPointLeft.Y);
                }

                // Calculate dot product and magnitude of vectors (center vector and the current tilt vector)
                double dotProduct = vectorAB.X * vectorCD.X + vectorAB.Y * vectorCD.Y;
                double magAB = Math.Sqrt(vectorAB.X * vectorAB.X + vectorAB.Y * vectorAB.Y);
                double magCD = Math.Sqrt(vectorCD.X * vectorCD.X + vectorCD.Y * vectorCD.Y);

                // Calculate angle between vectors and convert radian to degrees
                if (magAB == 0 || magCD == 0)
                {
                    result = 0;
                }
                else
                {
                    double angle = Math.Acos(dotProduct / (magAB * magCD));
                    result = Convert.ToInt32(Global.Clamp(
                            -180.0 * C_WHEEL_ANGLE_PRECISION,
                            Math.Round(angle * (180.0 / Math.PI), 1) * C_WHEEL_ANGLE_PRECISION,
                            180.0 * C_WHEEL_ANGLE_PRECISION));
                }

                // Left turn is -180..0 and right turn 0..180 degrees
                if (gyroAccelX < controller.WheelCenterPoint.X) result = -result;
            }

            return result;
        }

        // Calibrate sixaxis steering wheel emulation. Use DS4Windows configuration screen to start a calibration or press a special action key (if defined)
        private static void SAWheelEmulationCalibration(int device, DS4StateExposed exposedState, ControlService ctrl, DS4State currentDeviceState, DS4Device controller)
        {
            int gyroAccelX, gyroAccelZ;
            int result;

            gyroAccelX = exposedState.AccelX;
            gyroAccelZ = exposedState.AccelZ;

            // State 0=Normal mode (ie. calibration process is not running), 1=Activating calibration, 2=Calibration process running, 3=Completing calibration, 4=Cancelling calibration
            if (controller.WheelRecalibrateActiveState == 1)
            {
                AppLogger.LogToGui($"Controller {1 + device} activated re-calibration of SA steering wheel emulation", false);

                controller.WheelRecalibrateActiveState = 2;

                controller.wheelPrevPhysicalAngle = 0;
                controller.wheelPrevFullAngle = 0;
                controller.wheelFullTurnCount = 0;

                // Clear existing calibration value and use current position as "center" point.
                // This initial center value may be off-center because of shaking the controller while button was pressed. The value will be overriden with correct value once controller is stabilized and hold still few secs at the center point
                controller.WheelCenterPoint.X = gyroAccelX;
                controller.WheelCenterPoint.Y = gyroAccelZ;
                controller.wheel90DegPointRight.X = gyroAccelX + 20;
                controller.wheel90DegPointLeft.X = gyroAccelX - 20;

                // Clear bitmask for calibration points. All three calibration points need to be set before re-calibration process is valid
                controller.wheelCalibratedAxisBitmask = DS4Device.WheelCalibrationPoint.None;

                controller.wheelPrevRecalibrateTime = new DateTime(2500, 1, 1);
            }
            else if (controller.WheelRecalibrateActiveState == 3)
            {
                AppLogger.LogToGui($"Controller {1 + device} completed the calibration of SA steering wheel emulation. center=({controller.WheelCenterPoint.X}, {controller.WheelCenterPoint.Y})  90L=({controller.wheel90DegPointLeft.X}, {controller.wheel90DegPointLeft.Y})  90R=({controller.wheel90DegPointRight.X}, {controller.wheel90DegPointRight.Y})", false);

                // If any of the calibration points (center, left 90deg, right 90deg) are missing then reset back to default calibration values
                if ((controller.wheelCalibratedAxisBitmask & DS4Device.WheelCalibrationPoint.All) == DS4Device.WheelCalibrationPoint.All)
                    SaveControllerConfigs(controller);
                else
                    controller.WheelCenterPoint.X = controller.WheelCenterPoint.Y = 0;

                controller.WheelRecalibrateActiveState = 0;
                controller.wheelPrevRecalibrateTime = DateTime.Now;
            }
            else if (controller.WheelRecalibrateActiveState == 4)
            {
                AppLogger.LogToGui($"Controller {1 + device} cancelled the calibration of SA steering wheel emulation.", false);

                controller.WheelRecalibrateActiveState = 0;
                controller.wheelPrevRecalibrateTime = DateTime.Now;
            }

            if (controller.WheelRecalibrateActiveState > 0)
            {
                // Cross "X" key pressed. Set calibration point when the key is released and controller hold steady for a few seconds
                if (currentDeviceState.Cross == true) controller.wheelPrevRecalibrateTime = DateTime.Now;

                // Make sure controller is hold steady (velocity of gyro axis) to avoid misaligments and set calibration few secs after the "X" key was released
                if (Math.Abs(currentDeviceState.Motion.AngVelPitch) < 0.5 && Math.Abs(currentDeviceState.Motion.AngVelYaw) < 0.5 && Math.Abs(currentDeviceState.Motion.AngVelRoll) < 0.5
                    && ((TimeSpan)(DateTime.Now - controller.wheelPrevRecalibrateTime)).TotalSeconds > 1)
                {
                    controller.wheelPrevRecalibrateTime = new DateTime(2500, 1, 1);

                    if (controller.wheelCalibratedAxisBitmask == DS4Device.WheelCalibrationPoint.None)
                    {
                        controller.WheelCenterPoint.X = gyroAccelX;
                        controller.WheelCenterPoint.Y = gyroAccelZ;

                        controller.wheelCalibratedAxisBitmask |= DS4Device.WheelCalibrationPoint.Center;
                    }
                    else if (controller.wheel90DegPointRight.X < gyroAccelX)
                    {
                        controller.wheel90DegPointRight.X = gyroAccelX;
                        controller.wheel90DegPointRight.Y = gyroAccelZ;
                        controller.WheelCircleCenterPointRight.X = controller.WheelCenterPoint.X;
                        controller.WheelCircleCenterPointRight.Y = controller.wheel90DegPointRight.Y;

                        controller.wheelCalibratedAxisBitmask |= DS4Device.WheelCalibrationPoint.Right90;
                    }
                    else if (controller.wheel90DegPointLeft.X > gyroAccelX)
                    {
                        controller.wheel90DegPointLeft.X = gyroAccelX;
                        controller.wheel90DegPointLeft.Y = gyroAccelZ;
                        controller.WheelCircleCenterPointLeft.X = controller.WheelCenterPoint.X;
                        controller.WheelCircleCenterPointLeft.Y = controller.wheel90DegPointLeft.Y;

                        controller.wheelCalibratedAxisBitmask |= DS4Device.WheelCalibrationPoint.Left90;
                    }
                }

                // Show lightbar color feedback how the calibration process is proceeding.
                //  red / yellow / blue / green = No calibration anchors/one anchor/two anchors/all three anchors calibrated when color turns to green (center, 90DegLeft, 90DegRight).
                int bitsSet = CountNumOfSetBits((int)controller.wheelCalibratedAxisBitmask);
                if (bitsSet >= 3) DS4LightBar.ForcedColor[device] = calibrationColor_3;
                else if (bitsSet == 2) DS4LightBar.ForcedColor[device] = calibrationColor_2;
                else if (bitsSet == 1) DS4LightBar.ForcedColor[device] = calibrationColor_1;
                else DS4LightBar.ForcedColor[device] = calibrationColor_0;

                result = CalculateControllerAngle(gyroAccelX, gyroAccelZ, controller);

                // Force lightbar flashing when controller is currently at calibration point (user can verify the calibration before accepting it by looking at flashing lightbar)
                if (((controller.wheelCalibratedAxisBitmask & DS4Device.WheelCalibrationPoint.Center) != 0 && Math.Abs(result) <= 1 * C_WHEEL_ANGLE_PRECISION)
                 || ((controller.wheelCalibratedAxisBitmask & DS4Device.WheelCalibrationPoint.Left90) != 0 && result <= -89 * C_WHEEL_ANGLE_PRECISION && result >= -91 * C_WHEEL_ANGLE_PRECISION)
                 || ((controller.wheelCalibratedAxisBitmask & DS4Device.WheelCalibrationPoint.Right90) != 0 && result >= 89 * C_WHEEL_ANGLE_PRECISION && result <= 91 * C_WHEEL_ANGLE_PRECISION)
                 || ((controller.wheelCalibratedAxisBitmask & DS4Device.WheelCalibrationPoint.Left90) != 0 && Math.Abs(result) >= 179 * C_WHEEL_ANGLE_PRECISION))
                    DS4LightBar.ForcedFlash[device] = 2;
                else
                    DS4LightBar.ForcedFlash[device] = 0;

                DS4LightBar.ForceLight[device] = true;

                LogToGuiSACalibrationDebugMsg($"Calibration values ({gyroAccelX}, {gyroAccelZ})  angle={result / (1.0 * C_WHEEL_ANGLE_PRECISION)}\n");
            }
            else
            {
                // Re-calibration completed or cancelled. Set lightbar color back to normal color
                DS4LightBar.ForcedFlash[device] = 0;
                DS4LightBar.ForcedColor[device] = GetMainColor(device);
                DS4LightBar.ForceLight[device] = false;
                DS4LightBar.UpdateLightBar(controller, device);
            }
        }

        protected static int Scale360DegGyroAxis(int device, DS4StateExposed exposedState, ControlService ctrl)
        {
            unchecked
            {
                DS4Device controller;
                DS4State currentDeviceState;

                int gyroAccelX, gyroAccelZ;
                int result;

                controller = ctrl.Controllers[device].Device;
                if (controller == null) return 0;

                currentDeviceState = controller.GetCurrentStateRef();

                // If calibration is active then do the calibration process instead of the normal "angle calculation"
                if (controller.WheelRecalibrateActiveState > 0)
                {
                    SAWheelEmulationCalibration(device, exposedState, ctrl, currentDeviceState, controller);

                    // Return center wheel position while SA wheel emuation is being calibrated
                    return 0;
                }

                // Do nothing if connection is active but the actual DS4 controller is still missing or not yet synchronized
                if (!controller.IsSynced)
                    return 0;

                gyroAccelX = exposedState.AccelX;
                gyroAccelZ = exposedState.AccelZ;

                // If calibration values are missing then use "educated guesses" about good starting values
                if (controller.WheelCenterPoint.IsEmpty)
                {
                    if (!LoadControllerConfigs(controller))
                    {
                        AppLogger.LogToGui($"Controller {1 + device} sixaxis steering wheel calibration data missing. It is recommended to run steering wheel calibration process by pressing SASteeringWheelEmulationCalibration special action key. Using estimated values until the controller is calibrated at least once.", false);

                        // Use current controller position as "center point". Assume DS4Windows was started while controller was hold in center position (yes, dangerous assumption but can't do much until controller is calibrated)
                        controller.WheelCenterPoint.X = gyroAccelX;
                        controller.WheelCenterPoint.Y = gyroAccelZ;

                        controller.wheel90DegPointRight.X = controller.WheelCenterPoint.X + 113;
                        controller.wheel90DegPointRight.Y = controller.WheelCenterPoint.Y + 110;

                        controller.wheel90DegPointLeft.X = controller.WheelCenterPoint.X - 127;
                        controller.wheel90DegPointLeft.Y = controller.wheel90DegPointRight.Y;
                    }

                    controller.WheelCircleCenterPointRight.X = controller.WheelCenterPoint.X;
                    controller.WheelCircleCenterPointRight.Y = controller.wheel90DegPointRight.Y;
                    controller.WheelCircleCenterPointLeft.X = controller.WheelCenterPoint.X;
                    controller.WheelCircleCenterPointLeft.Y = controller.wheel90DegPointLeft.Y;

                    AppLogger.LogToGui($"Controller {1 + device} steering wheel emulation calibration values. Center=({controller.WheelCenterPoint.X}, {controller.WheelCenterPoint.Y})  90L=({controller.wheel90DegPointLeft.X}, {controller.wheel90DegPointLeft.Y})  90R=({controller.wheel90DegPointRight.X}, {controller.wheel90DegPointRight.Y})  Range={GetSASteeringWheelEmulationRange(device)}", false);
                    controller.wheelPrevRecalibrateTime = DateTime.Now;
                }


                int maxRangeRight = GetSASteeringWheelEmulationRange(device) / 2 * C_WHEEL_ANGLE_PRECISION;
                int maxRangeLeft = -maxRangeRight;

                result = CalculateControllerAngle(gyroAccelX, gyroAccelZ, controller);

                // Apply deadzone (SA X-deadzone value). This code assumes that 20deg is the max deadzone anyone ever might wanna use (in practice effective deadzone 
                // is probably just few degrees by using SXDeadZone values 0.01...0.05)
                double sxDead = GetSXDeadzone(device);
                if (sxDead > 0)
                {
                    int sxDeadInt = Convert.ToInt32(20.0 * C_WHEEL_ANGLE_PRECISION * sxDead);
                    if (Math.Abs(result) <= sxDeadInt)
                    {
                        result = 0;
                    }
                    else
                    {
                        // Smooth steering angle based on deadzone range instead of just clipping the deadzone gap
                        result -= result < 0 ? -sxDeadInt : sxDeadInt;
                    }
                }

                // If wrapped around from +180 to -180 side (or vice versa) then SA steering wheel keeps on turning beyond 360 degrees (if range is >360).
                // Keep track of how many times the steering wheel has been turned beyond the full 360 circle and clip the result to max range.
                int wheelFullTurnCount = controller.wheelFullTurnCount;
                if (controller.wheelPrevPhysicalAngle < 0 && result > 0)
                {
                    if ((result - controller.wheelPrevPhysicalAngle) > 180 * C_WHEEL_ANGLE_PRECISION)
                    {
                        if (maxRangeRight > 360/2 * C_WHEEL_ANGLE_PRECISION)
                            wheelFullTurnCount--;
                        else
                            result = maxRangeLeft;
                    }
                }
                else if (controller.wheelPrevPhysicalAngle > 0 && result < 0)
                {
                    if ((controller.wheelPrevPhysicalAngle - result) > 180 * C_WHEEL_ANGLE_PRECISION)
                    {
                        if (maxRangeRight > 360/2 * C_WHEEL_ANGLE_PRECISION)
                            wheelFullTurnCount++;
                        else
                            result = maxRangeRight;
                    }
                }
                controller.wheelPrevPhysicalAngle = result;

                if (wheelFullTurnCount != 0)
                {
                    // Adjust value of result (steering wheel angle) based on num of full 360 turn counts
                    result += wheelFullTurnCount * 180 * C_WHEEL_ANGLE_PRECISION * 2;
                }

                // If the new angle is more than 180 degrees further away then this is probably bogus value (controller shaking too much and gyro and velocity sensors went crazy).
                // Accept the new angle only when the new angle is within a "stability threshold", otherwise use the previous full angle value and wait for controller to be stabilized.
                if (Math.Abs(result - controller.wheelPrevFullAngle) <= 180 * C_WHEEL_ANGLE_PRECISION)
                {
                    controller.wheelPrevFullAngle = result;
                    controller.wheelFullTurnCount = wheelFullTurnCount;
                }
                else
                {
                    result = controller.wheelPrevFullAngle;
                }

                result = Clamp(maxRangeLeft, result, maxRangeRight);

                // Debug log output of SA sensor values
                //LogToGuiSACalibrationDebugMsg($"DBG gyro=({gyroAccelX}, {gyroAccelZ})  output=({exposedState.OutputAccelX}, {exposedState.OutputAccelZ})  PitRolYaw=({currentDeviceState.Motion.gyroPitch}, {currentDeviceState.Motion.gyroRoll}, {currentDeviceState.Motion.gyroYaw})  VelPitRolYaw=({currentDeviceState.Motion.angVelPitch}, {currentDeviceState.Motion.angVelRoll}, {currentDeviceState.Motion.angVelYaw})  angle={result / (1.0 * C_WHEEL_ANGLE_PRECISION)}  fullTurns={controller.wheelFullTurnCount}", false);

                // Apply anti-deadzone (SA X-antideadzone value)
                double sxAntiDead = GetSXAntiDeadzone(device);

                int outputAxisMax, outputAxisMin, outputAxisZero;
                if (OutContType[device] == OutControllerType.DS4)
                {
                    // DS4 analog stick axis supports only 0...255 output value range (not the best one for steering wheel usage)
                    outputAxisMax = 255;
                    outputAxisMin = 0;
                    outputAxisZero = 128;
                }
                else
                {
                    // x360 (xinput) analog stick axis supports -32768...32767 output value range (more than enough for steering wheel usage)
                    outputAxisMax = 32767;
                    outputAxisMin = -32768;
                    outputAxisZero = 0;
                }

                switch (GetSASteeringWheelEmulationAxis(device))
                {
                    case SASteeringWheelEmulationAxisType.LX:
                    case SASteeringWheelEmulationAxisType.LY:
                    case SASteeringWheelEmulationAxisType.RX:
                    case SASteeringWheelEmulationAxisType.RY:
                        // DS4 thumbstick axis output (-32768..32767 raw value range)
                        //return (((result - maxRangeLeft) * (32767 - (-32768))) / (maxRangeRight - maxRangeLeft)) + (-32768);
                        if (result == 0) return outputAxisZero;

                        if (sxAntiDead > 0)
                        {
                            sxAntiDead *= outputAxisMax - outputAxisZero;
                            if (result < 0) return ((result - maxRangeLeft) * (outputAxisZero - Convert.ToInt32(sxAntiDead) - outputAxisMin) / (0 - maxRangeLeft)) + outputAxisMin;
                            else return ((result - 0) * (outputAxisMax - (outputAxisZero + Convert.ToInt32(sxAntiDead))) / (maxRangeRight - 0)) + outputAxisZero + Convert.ToInt32(sxAntiDead);
                        }
                        else
                        {
                            return ((result - maxRangeLeft) * (outputAxisMax - outputAxisMin) / (maxRangeRight - maxRangeLeft)) + outputAxisMin;
                        }
                        
                    case SASteeringWheelEmulationAxisType.L2R2:
                        // DS4 Trigger axis output. L2+R2 triggers share the same axis in x360 xInput/DInput controller, 
                        // so L2+R2 steering output supports only 360 turn range (-255..255 raw value range in the shared trigger axis)
                        if (result == 0) return 0;

                        result = Convert.ToInt32(Math.Round(result / (1.0 * C_WHEEL_ANGLE_PRECISION)));
                        if (result < 0) result = -181 - result;

                        if (sxAntiDead > 0)
                        {
                            sxAntiDead *= 255;
                            if (result < 0) return ((result - (-180)) * (-Convert.ToInt32(sxAntiDead) - (-255)) / (0 - (-180))) + (-255);
                            else return ((result - 0) * (255 - Convert.ToInt32(sxAntiDead)) / (180 - 0)) + Convert.ToInt32(sxAntiDead);
                        }
                        else
                        {
                            return ((result - (-180)) * (255 - (-255)) / (180 - (-180))) + (-255);
                        }

                    case SASteeringWheelEmulationAxisType.VJoy1X:
                    case SASteeringWheelEmulationAxisType.VJoy1Y:
                    case SASteeringWheelEmulationAxisType.VJoy1Z:
                    case SASteeringWheelEmulationAxisType.VJoy2X:
                    case SASteeringWheelEmulationAxisType.VJoy2Y:
                    case SASteeringWheelEmulationAxisType.VJoy2Z:
                        // SASteeringWheelEmulationAxisType.VJoy1X/VJoy1Y/VJoy1Z/VJoy2X/VJoy2Y/VJoy2Z VJoy axis output (0..32767 raw value range by default)
                        if (result == 0) return 16384;

                        if (sxAntiDead > 0)
                        {
                            sxAntiDead *= 16384;
                            if (result < 0) return ((result - maxRangeLeft) * (16384 - Convert.ToInt32(sxAntiDead) - (-0)) / (0 - maxRangeLeft)) + (-0);
                            else return ((result - 0) * (32767 - (16384 + Convert.ToInt32(sxAntiDead))) / (maxRangeRight - 0)) + 16384 + Convert.ToInt32(sxAntiDead);
                        }
                        else
                        {
                            return ((result - maxRangeLeft) * (32767 - (-0)) / (maxRangeRight - maxRangeLeft)) + (-0);
                        }

                    default:
                        // Should never come here, but C# case statement syntax requires DEFAULT handler
                        return 0;
                }
            }
        }

    }
}
