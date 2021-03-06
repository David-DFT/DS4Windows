﻿
namespace DS4Windows
{
    public class DS4StateFieldMapping
    {
        public enum ControlType: int { Unknown = 0, Button, AxisDir, Trigger, Touch, GyroDir, SwipeDir }

        public bool[] Buttons = new bool[(int)DS4Controls.SwipeDown + 1];
        public byte[] AxisDirs = new byte[(int)DS4Controls.SwipeDown + 1];
        public byte[] Triggers = new byte[(int)DS4Controls.SwipeDown + 1];
        public int[] gryodirs = new int[(int)DS4Controls.SwipeDown + 1];
        public byte[] swipedirs = new byte[(int)DS4Controls.SwipeDown + 1];
        public bool[] swipedirbools = new bool[(int)DS4Controls.SwipeDown + 1];
        public bool touchButton = false;

        public static ControlType[] MappedType = new ControlType[38] { ControlType.Unknown, // DS4Controls.None
            ControlType.AxisDir, // DS4Controls.LXNeg
            ControlType.AxisDir, // DS4Controls.LXPos
            ControlType.AxisDir, // DS4Controls.LYNeg
            ControlType.AxisDir, // DS4Controls.LYPos
            ControlType.AxisDir, // DS4Controls.RXNeg
            ControlType.AxisDir, // DS4Controls.RXPos
            ControlType.AxisDir, // DS4Controls.RYNeg
            ControlType.AxisDir, // DS4Controls.RYPos
            ControlType.Button, // DS4Controls.L1
            ControlType.Trigger, // DS4Controls.L2
            ControlType.Button, // DS4Controls.L3
            ControlType.Button, // DS4Controls.R1
            ControlType.Trigger, // DS4Controls.R2
            ControlType.Button, // DS4Controls.R3
            ControlType.Button, // DS4Controls.Square
            ControlType.Button, // DS4Controls.Triangle
            ControlType.Button, // DS4Controls.Circle
            ControlType.Button, // DS4Controls.Cross
            ControlType.Button, // DS4Controls.DpadUp
            ControlType.Button, // DS4Controls.DpadRight
            ControlType.Button, // DS4Controls.DpadDown
            ControlType.Button, // DS4Controls.DpadLeft
            ControlType.Button, // DS4Controls.PS
            ControlType.Touch, // DS4Controls.TouchLeft
            ControlType.Touch, // DS4Controls.TouchUpper
            ControlType.Touch, // DS4Controls.TouchMulti
            ControlType.Touch, // DS4Controls.TouchRight
            ControlType.Button, // DS4Controls.Share
            ControlType.Button, // DS4Controls.Options
            ControlType.GyroDir, // DS4Controls.GyroXPos
            ControlType.GyroDir, // DS4Controls.GyroXNeg
            ControlType.GyroDir, // DS4Controls.GyroZPos
            ControlType.GyroDir, // DS4Controls.GyroZNeg
            ControlType.SwipeDir, // DS4Controls.SwipeLeft
            ControlType.SwipeDir, // DS4Controls.SwipeRight
            ControlType.SwipeDir, // DS4Controls.SwipeUp
            ControlType.SwipeDir, // DS4Controls.SwipeDown
        };

        public DS4StateFieldMapping()
        {
        }

        public DS4StateFieldMapping(DS4State cState, DS4StateExposed exposeState, Mouse tp, bool priorMouse=false)
        {
            PopulateFieldMapping(cState, exposeState, tp, priorMouse);
        }

        public void PopulateFieldMapping(DS4State cState, DS4StateExposed exposeState, Mouse tp, bool priorMouse = false)
        {
            unchecked
            {
                AxisDirs[(int)DS4Controls.LXNeg] = cState.LX;
                AxisDirs[(int)DS4Controls.LXPos] = cState.LX;
                AxisDirs[(int)DS4Controls.LYNeg] = cState.LY;
                AxisDirs[(int)DS4Controls.LYPos] = cState.LY;

                AxisDirs[(int)DS4Controls.RXNeg] = cState.RX;
                AxisDirs[(int)DS4Controls.RXPos] = cState.RX;
                AxisDirs[(int)DS4Controls.RYNeg] = cState.RY;
                AxisDirs[(int)DS4Controls.RYPos] = cState.RY;

                Triggers[(int)DS4Controls.L2] = cState.L2;
                Triggers[(int)DS4Controls.R2] = cState.R2;

                Buttons[(int)DS4Controls.L1] = cState.L1;
                Buttons[(int)DS4Controls.L3] = cState.L3;
                Buttons[(int)DS4Controls.R1] = cState.R1;
                Buttons[(int)DS4Controls.R3] = cState.R3;

                Buttons[(int)DS4Controls.Cross] = cState.Cross;
                Buttons[(int)DS4Controls.Triangle] = cState.Triangle;
                Buttons[(int)DS4Controls.Circle] = cState.Circle;
                Buttons[(int)DS4Controls.Square] = cState.Square;
                Buttons[(int)DS4Controls.PS] = cState.PS;
                Buttons[(int)DS4Controls.Options] = cState.Options;
                Buttons[(int)DS4Controls.Share] = cState.Share;

                Buttons[(int)DS4Controls.DpadUp] = cState.DpadUp;
                Buttons[(int)DS4Controls.DpadRight] = cState.DpadRight;
                Buttons[(int)DS4Controls.DpadDown] = cState.DpadDown;
                Buttons[(int)DS4Controls.DpadLeft] = cState.DpadLeft;

                Buttons[(int)DS4Controls.TouchLeft] = tp != null ? (!priorMouse ? tp.leftDown : tp.priorLeftDown) : false;
                Buttons[(int)DS4Controls.TouchRight] = tp != null ? (!priorMouse ? tp.rightDown : tp.priorRightDown) : false;
                Buttons[(int)DS4Controls.TouchUpper] = tp != null ? (!priorMouse ? tp.upperDown : tp.priorUpperDown) : false;
                Buttons[(int)DS4Controls.TouchMulti] = tp != null ? (!priorMouse ? tp.multiDown : tp.priorMultiDown) : false;

                int sixAxisX = -exposeState.OutputAccelX;
                gryodirs[(int)DS4Controls.GyroXPos] = sixAxisX > 0 ? sixAxisX : 0;
                gryodirs[(int)DS4Controls.GyroXNeg] = sixAxisX < 0 ? sixAxisX : 0;

                int sixAxisZ = exposeState.OutputAccelZ;
                gryodirs[(int)DS4Controls.GyroZPos] = sixAxisZ > 0 ? sixAxisZ : 0;
                gryodirs[(int)DS4Controls.GyroZNeg] = sixAxisZ < 0 ? sixAxisZ : 0;

                swipedirs[(int)DS4Controls.SwipeLeft] = tp != null ? (!priorMouse ? tp.swipeLeftB : tp.priorSwipeLeftB) : (byte)0;
                swipedirs[(int)DS4Controls.SwipeRight] = tp != null ? (!priorMouse ? tp.swipeRightB : tp.priorSwipeRightB) : (byte)0;
                swipedirs[(int)DS4Controls.SwipeUp] = tp != null ? (!priorMouse ? tp.swipeUpB : tp.priorSwipeUpB) : (byte)0;
                swipedirs[(int)DS4Controls.SwipeDown] = tp != null ? (!priorMouse ? tp.swipeDownB : tp.priorSwipeDownB) : (byte)0;

                swipedirbools[(int)DS4Controls.SwipeLeft] = tp != null ? (!priorMouse ? tp.swipeLeft : tp.priorSwipeLeft) : false;
                swipedirbools[(int)DS4Controls.SwipeRight] = tp != null ? (!priorMouse ? tp.swipeRight : tp.priorSwipeRight) : false;
                swipedirbools[(int)DS4Controls.SwipeUp] = tp != null ? (!priorMouse ? tp.swipeUp : tp.priorSwipeUp) : false;
                swipedirbools[(int)DS4Controls.SwipeDown] = tp != null ? (!priorMouse ? tp.swipeDown : tp.priorSwipeDown) : false;
                touchButton = cState.TouchButton;
            }
        }

        public void PopulateState(DS4State state)
        {
            unchecked
            {
                state.LX = AxisDirs[(int)DS4Controls.LXNeg];
                state.LX = AxisDirs[(int)DS4Controls.LXPos];
                state.LY = AxisDirs[(int)DS4Controls.LYNeg];
                state.LY = AxisDirs[(int)DS4Controls.LYPos];

                state.RX = AxisDirs[(int)DS4Controls.RXNeg];
                state.RX = AxisDirs[(int)DS4Controls.RXPos];
                state.RY = AxisDirs[(int)DS4Controls.RYNeg];
                state.RY = AxisDirs[(int)DS4Controls.RYPos];

                state.L2 = Triggers[(int)DS4Controls.L2];
                state.R2 = Triggers[(int)DS4Controls.R2];

                state.L1 = Buttons[(int)DS4Controls.L1];
                state.L3 = Buttons[(int)DS4Controls.L3];
                state.R1 = Buttons[(int)DS4Controls.R1];
                state.R3 = Buttons[(int)DS4Controls.R3];

                state.Cross = Buttons[(int)DS4Controls.Cross];
                state.Triangle = Buttons[(int)DS4Controls.Triangle];
                state.Circle = Buttons[(int)DS4Controls.Circle];
                state.Square = Buttons[(int)DS4Controls.Square];
                state.PS = Buttons[(int)DS4Controls.PS];
                state.Options = Buttons[(int)DS4Controls.Options];
                state.Share = Buttons[(int)DS4Controls.Share];

                state.DpadUp = Buttons[(int)DS4Controls.DpadUp];
                state.DpadRight = Buttons[(int)DS4Controls.DpadRight];
                state.DpadDown = Buttons[(int)DS4Controls.DpadDown];
                state.DpadLeft = Buttons[(int)DS4Controls.DpadLeft];
                state.TouchButton = touchButton;
            }
        }
    }
}
