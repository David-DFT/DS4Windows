using System;

namespace DS4Windows
{
    public class Mouse : ITouchpadBehaviour
    {
        protected DateTime pastTime, firstTap, TimeofEnd;
        protected Touch firstTouch, secondTouch;
        private DS4State state = new DS4State();
        protected int deviceNum;
        private DS4Device dev = null;
        private readonly MouseCursor cursor;
        private readonly MouseWheel wheel;
        private bool tappedOnce = false, secondtouchbegin = false;
        public bool swipeLeft, swipeRight, swipeUp, swipeDown;
        public bool priorSwipeLeft, priorSwipeRight, priorSwipeUp, priorSwipeDown;
        public byte swipeLeftB, swipeRightB, swipeUpB, swipeDownB, swipedB;
        public byte priorSwipeLeftB, priorSwipeRightB, priorSwipeUpB, priorSwipeDownB, priorSwipedB;
        public bool slideleft, slideright;
        public bool priorSlideLeft, priorSlideright;
        // touch area stuff
        public bool leftDown, rightDown, upperDown, multiDown;
        public bool priorLeftDown, priorRightDown, priorUpperDown, priorMultiDown;
        protected DS4Controls pushed = DS4Controls.None;
        protected Mapping.EClick clicked = Mapping.EClick.None;
        public int CursorGyroDead { get => cursor.GyroCursorDeadZone; set => cursor.GyroCursorDeadZone = value; }

        internal const int TRACKBALL_INIT_FICTION = 10;
        internal const int TRACKBALL_MASS = 45;
        internal const double TRACKBALL_RADIUS = 0.0245;

        private double TRACKBALL_INERTIA = 2.0 * (TRACKBALL_MASS * TRACKBALL_RADIUS * TRACKBALL_RADIUS) / 5.0;
        private double TRACKBALL_SCALE = 0.004;
        private const int TRACKBALL_BUFFER_LEN = 8;
        private double[] trackballXBuffer = new double[TRACKBALL_BUFFER_LEN];
        private double[] trackballYBuffer = new double[TRACKBALL_BUFFER_LEN];
        private int trackballBufferTail = 0;
        private int trackballBufferHead = 0;
        private double trackballAccel = 0.0;
        private double trackballXVel = 0.0;
        private double trackballYVel = 0.0;
        private bool trackballActive = false;
        private double trackballDXRemain = 0.0;
        private double trackballDYRemain = 0.0;

        public Mouse(int deviceID, DS4Device d)
        {
            deviceNum = deviceID;
            dev = d;
            cursor = new MouseCursor(deviceNum);
            wheel = new MouseWheel(deviceNum);
            trackballAccel = TRACKBALL_RADIUS * TRACKBALL_INIT_FICTION / TRACKBALL_INERTIA;
            firstTouch = new Touch(0, 0, 0, null);
        }

        public void ResetTrackAccel(double friction)
        {
            trackballAccel = TRACKBALL_RADIUS * friction / TRACKBALL_INERTIA;
        }

        public void ResetToggleGyroM()
        {
            currentToggleGyroM = false;
            previousTriggerActivated = false;
            triggeractivated = false;
        }

        bool triggeractivated = false;
        bool previousTriggerActivated = false;
        bool useReverseRatchet = false;
        bool toggleGyroMouse = true;
        public bool ToggleGyroMouse { get => toggleGyroMouse;
            set { toggleGyroMouse = value; ResetToggleGyroM(); } }
        bool currentToggleGyroM = false;

        public virtual void SixAxisMoved(DS4SixAxis sender, SixAxisEventArgs arg)
        {
            GyroOutMode outMode = Global.GetGyroOutMode(deviceNum);
            if (outMode == GyroOutMode.Mouse && Global.getGyroSensitivity(deviceNum) > 0)
            {
                state = dev.GetCurrentStateRef();

                useReverseRatchet = Global.getGyroTriggerTurns(deviceNum);
                int i = 0;
                string[] ss = Global.getSATriggers(deviceNum).Split(',');
                bool andCond = Global.getSATriggerCond(deviceNum);
                triggeractivated = andCond ? true : false;
                if (!string.IsNullOrEmpty(ss[0]))
                {
                    string s = string.Empty;
                    for (int index = 0, arlen = ss.Length; index < arlen; index++)
                    {
                        s = ss[index];
                        if (andCond && !(int.TryParse(s, out i) && GetDS4ControlsByName(i)))
                        {
                            triggeractivated = false;
                            break;
                        }
                        else if (!andCond && int.TryParse(s, out i) && GetDS4ControlsByName(i))
                        {
                            triggeractivated = true;
                            break;
                        }
                    }
                }

                if (toggleGyroMouse)
                {
                    if (triggeractivated && triggeractivated != previousTriggerActivated)
                    {
                        currentToggleGyroM = !currentToggleGyroM;
                    }

                    previousTriggerActivated = triggeractivated;
                    triggeractivated = currentToggleGyroM;
                }
                else
                {
                    previousTriggerActivated = triggeractivated;
                }

                if (useReverseRatchet && triggeractivated)
                    cursor.sixaxisMoved(arg);
                else if (!useReverseRatchet && !triggeractivated)
                    cursor.sixaxisMoved(arg);
                else
                    cursor.mouseRemainderReset();

            }
            else if (outMode == GyroOutMode.MouseJoystick)
            {
                state = dev.GetCurrentStateRef();

                useReverseRatchet = Global.GetGyroMouseStickTriggerTurns(deviceNum);
                int i = 0;
                string[] ss = Global.GetSAMouseStickTriggers(deviceNum).Split(',');
                bool andCond = Global.GetSAMouseStickTriggerCond(deviceNum);
                triggeractivated = andCond ? true : false;
                if (!string.IsNullOrEmpty(ss[0]))
                {
                    string s = string.Empty;
                    for (int index = 0, arlen = ss.Length; index < arlen; index++)
                    {
                        s = ss[index];
                        if (andCond && !(int.TryParse(s, out i) && GetDS4ControlsByName(i)))
                        {
                            triggeractivated = false;
                            break;
                        }
                        else if (!andCond && int.TryParse(s, out i) && GetDS4ControlsByName(i))
                        {
                            triggeractivated = true;
                            break;
                        }
                    }
                }

                if (toggleGyroMouse)
                {
                    if (triggeractivated && triggeractivated != previousTriggerActivated)
                    {
                        currentToggleGyroM = !currentToggleGyroM;
                    }

                    previousTriggerActivated = triggeractivated;
                    triggeractivated = currentToggleGyroM;
                }
                else
                {
                    previousTriggerActivated = triggeractivated;
                }

                if (useReverseRatchet && triggeractivated)
                    SixMouseStick(arg);
                else if (!useReverseRatchet && !triggeractivated)
                    SixMouseStick(arg);
                else
                    SixMouseReset(arg);
            }
        }

        private const int SMOOTH_BUFFER_LEN = 3;
        private int[] xSmoothBuffer = new int[SMOOTH_BUFFER_LEN];
        private int[] ySmoothBuffer = new int[SMOOTH_BUFFER_LEN];
        private int smoothBufferTail = 0;

        private void SixMouseReset(SixAxisEventArgs args)
        {
            int iIndex = smoothBufferTail % SMOOTH_BUFFER_LEN;
            xSmoothBuffer[iIndex] = 0;
            ySmoothBuffer[iIndex] = 0;
            smoothBufferTail = iIndex + 1;
        }

        private void SixMouseStick(SixAxisEventArgs arg)
        {
            int deltaX = 0, deltaY = 0;
            deltaX = Global.getGyroMouseStickHorizontalAxis(0) == 0 ? arg.SixAxis.GyroYawFull :
                arg.SixAxis.GyroRollFull;
            deltaY = -arg.SixAxis.GyroPitchFull;
            //int inputX = deltaX, inputY = deltaY;
            int maxDirX = deltaX >= 0 ? 127 : -128;
            int maxDirY = deltaY >= 0 ? 127 : -128;

            GyroMouseStickInfo msinfo = Global.GetGyroMouseStickInfo(deviceNum);

            double tempAngle = Math.Atan2(-deltaY, deltaX);
            double normX = Math.Abs(Math.Cos(tempAngle));
            double normY = Math.Abs(Math.Sin(tempAngle));
            int signX = Math.Sign(deltaX);
            int signY = Math.Sign(deltaY);

            int deadzoneX = (int)Math.Abs(normX * msinfo.DeadZone);
            int deadzoneY = (int)Math.Abs(normY * msinfo.DeadZone);

            int maxValX = signX * msinfo.MaxZone;
            int maxValY = signY * msinfo.MaxZone;

            double xratio = 0.0, yratio = 0.0;
            double antiX = msinfo.AntiDeadX * normX;
            double antiY = msinfo.AntiDeadY * normY;

            if (Math.Abs(deltaX) > deadzoneX)
            {
                deltaX -= signX * deadzoneX;
                deltaX = (deltaX < 0 && deltaX < maxValX) ? maxValX :
                    (deltaX > 0 && deltaX > maxValX) ? maxValX : deltaX;
                //if (deltaX != maxValX) deltaX -= deltaX % (signX * GyroMouseFuzz);
            }
            else
            {
                deltaX = 0;
            }

            if (Math.Abs(deltaY) > deadzoneY)
            {
                deltaY -= signY * deadzoneY;
                deltaY = (deltaY < 0 && deltaY < maxValY) ? maxValY :
                    (deltaY > 0 && deltaY > maxValY) ? maxValY : deltaY;
                //if (deltaY != maxValY) deltaY -= deltaY % (signY * GyroMouseFuzz);
            }
            else
            {
                deltaY = 0;
            }

            if (msinfo.UseSmoothing)
            {
                int iIndex = smoothBufferTail % SMOOTH_BUFFER_LEN;
                xSmoothBuffer[iIndex] = deltaX;
                ySmoothBuffer[iIndex] = deltaY;
                smoothBufferTail = iIndex + 1;

                double currentWeight = 1.0;
                double finalWeight = 0.0;
                double x_out = 0.0, y_out = 0.0;
                int idx = 0;
                for (int i = 0; i < SMOOTH_BUFFER_LEN; i++)
                {
                    idx = (smoothBufferTail - i - 1 + SMOOTH_BUFFER_LEN) % SMOOTH_BUFFER_LEN;
                    x_out += xSmoothBuffer[idx] * currentWeight;
                    y_out += ySmoothBuffer[idx] * currentWeight;
                    finalWeight += currentWeight;
                    currentWeight *= msinfo.SmoothWeight;
                }

                x_out /= finalWeight;
                deltaX = (int)x_out;
                y_out /= finalWeight;
                deltaY = (int)y_out;

                maxValX = deltaX < 0 ? -msinfo.MaxZone : msinfo.MaxZone;
                maxValY = deltaY < 0 ? -msinfo.MaxZone : msinfo.MaxZone;
                maxDirX = deltaX >= 0 ? 127 : -128;
                maxDirY = deltaY >= 0 ? 127 : -128;
            }

            if (msinfo.VertScale != 100)
            {
                double verticalScale = msinfo.VertScale * 0.01;
                deltaY = (int)(deltaY * verticalScale);
            }

            if (deltaX != 0) xratio = deltaX / (double)maxValX;
            if (deltaY != 0) yratio = deltaY / (double)maxValY;

            double xNorm = 0.0, yNorm = 0.0;
            if (xratio != 0.0)
            {
                xNorm = (1.0 - antiX) * xratio + antiX;
            }

            if (yratio != 0.0)
            {
                yNorm = (1.0 - antiY) * yratio + antiY;
            }

            if (msinfo.Inverted != 0)
            {
                if ((msinfo.Inverted & 1) == 1)
                {
                    // Invert max dir value
                    maxDirX = deltaX >= 0 ? -128 : 127;
                }

                if ((msinfo.Inverted & 2) == 2)
                {
                    // Invert max dir value
                    maxDirY = deltaY >= 0 ? -128 : 127;
                }
            }

            byte axisXOut = (byte)(xNorm * maxDirX + 128.0);
            byte axisYOut = (byte)(yNorm * maxDirY + 128.0);
            Mapping.GyroStickX[deviceNum] = axisXOut;
            Mapping.GyroStickY[deviceNum] = axisYOut;
        }

        private bool GetDS4ControlsByName(int key)
        {
            switch (key)
            {
                case -1: return true;
                case 0: return state.Cross;
                case 1: return state.Circle;
                case 2: return state.Square;
                case 3: return state.Triangle;
                case 4: return state.L1;
                case 5: return state.L2 > 128;
                case 6: return state.R1;
                case 7: return state.R2 > 128;
                case 8: return state.DpadUp;
                case 9: return state.DpadDown;
                case 10: return state.DpadLeft;
                case 11: return state.DpadRight;
                case 12: return state.L3;
                case 13: return state.R3;
                case 14: return state.Touch1Finger;
                case 15: return state.Touch2Fingers;
                case 16: return state.Options;
                case 17: return state.Share;
                case 18: return state.PS;
                default: break;
            }

            return false;
        }

        private bool tempBool = false;
        public virtual void TouchesMoved(DS4Touchpad sender, TouchpadEventArgs arg)
        {
            state = dev.GetCurrentStateRef();

            if (Global.GetUseTouchPadForControls(deviceNum) == false)
            {
                if (Global.GetTouchActive(deviceNum))
                {
                    int[] disArray = Global.getTouchDisInvertTriggers(deviceNum);
                    tempBool = true;
                    for (int i = 0, arlen = disArray.Length; tempBool && i < arlen; i++)
                    {
                        if (GetDS4ControlsByName(disArray[i]) == false)
                            tempBool = false;
                    }

                    if (Global.getTrackballMode(deviceNum))
                    {
                        int iIndex = trackballBufferTail;
                        trackballXBuffer[iIndex] = (arg.touches[0].deltaX * TRACKBALL_SCALE) / dev.GetCurrentStateRef().ElapsedTime;
                        trackballYBuffer[iIndex] = (arg.touches[0].deltaY * TRACKBALL_SCALE) / dev.GetCurrentStateRef().ElapsedTime;
                        trackballBufferTail = (iIndex + 1) % TRACKBALL_BUFFER_LEN;
                        if (trackballBufferHead == trackballBufferTail)
                            trackballBufferHead = (trackballBufferHead + 1) % TRACKBALL_BUFFER_LEN;
                    }

                    cursor.touchesMoved(arg, dragging || dragging2, tempBool);
                    wheel.touchesMoved(arg, dragging || dragging2);
                }
                else
                {
                    if (Global.getTrackballMode(deviceNum))
                    {
                        int iIndex = trackballBufferTail;
                        trackballXBuffer[iIndex] = 0;
                        trackballYBuffer[iIndex] = 0;
                        trackballBufferTail = (iIndex + 1) % TRACKBALL_BUFFER_LEN;
                        if (trackballBufferHead == trackballBufferTail)
                            trackballBufferHead = (trackballBufferHead + 1) % TRACKBALL_BUFFER_LEN;
                    }
                }
            }
            else
            {
                if (!(swipeUp || swipeDown || swipeLeft || swipeRight) && arg.touches.Length == 1)
                {
                    if (arg.touches[0].hwX - firstTouch.hwX > 400) swipeRight = true;
                    if (arg.touches[0].hwX - firstTouch.hwX < -400) swipeLeft = true;
                    if (arg.touches[0].hwY - firstTouch.hwY > 300) swipeDown = true;
                    if (arg.touches[0].hwY - firstTouch.hwY < -300) swipeUp = true;
                }

                swipeUpB = (byte)Math.Min(255, Math.Max(0, (firstTouch.hwY - arg.touches[0].hwY) * 1.5f));
                swipeDownB = (byte)Math.Min(255, Math.Max(0, (arg.touches[0].hwY - firstTouch.hwY) * 1.5f));
                swipeLeftB = (byte)Math.Min(255, Math.Max(0, firstTouch.hwX - arg.touches[0].hwX));
                swipeRightB = (byte)Math.Min(255, Math.Max(0, arg.touches[0].hwX - firstTouch.hwX));
            }

            if (Math.Abs(firstTouch.hwY - arg.touches[0].hwY) < 50 && arg.touches.Length == 2)
            {
                if (arg.touches[0].hwX - firstTouch.hwX > 200 && !slideleft)
                    slideright = true;
                else if (firstTouch.hwX - arg.touches[0].hwX > 200 && !slideright)
                    slideleft = true;
            }

            SynthesizeMouseButtons();
        }

        public virtual void TouchesBegan(DS4Touchpad sender, TouchpadEventArgs arg)
        {
            if (!Global.UseTouchPadForControls[deviceNum])
            {
                Array.Clear(trackballXBuffer, 0, TRACKBALL_BUFFER_LEN);
                Array.Clear(trackballYBuffer, 0, TRACKBALL_BUFFER_LEN);
                trackballXVel = 0.0;
                trackballYVel = 0.0;
                trackballActive = false;
                trackballBufferTail = 0;
                trackballBufferHead = 0;
                trackballDXRemain = 0.0;
                trackballDYRemain = 0.0;

                cursor.touchesBegan(arg);
                wheel.touchesBegan(arg);
            }

            pastTime = arg.timeStamp;
            firstTouch.populate(arg.touches[0].hwX, arg.touches[0].hwY, arg.touches[0].touchID,
                arg.touches[0].previousTouch);

            if (Global.getDoubleTap(deviceNum))
            {
                DateTime test = arg.timeStamp;
                if (test <= (firstTap + TimeSpan.FromMilliseconds((double)Global.TapSensitivity[deviceNum] * 1.5)) && !arg.touchButtonPressed)
                    secondtouchbegin = true;
            }

            state = dev.GetCurrentStateRef();
            SynthesizeMouseButtons();
        }

        public virtual void TouchesEnded(DS4Touchpad sender, TouchpadEventArgs arg)
        {
            state = dev.GetCurrentStateRef();
            slideright = slideleft = false;
            swipeUp = swipeDown = swipeLeft = swipeRight = false;
            swipeUpB = swipeDownB = swipeLeftB = swipeRightB = 0;
            byte tapSensitivity = Global.getTapSensitivity(deviceNum);
            if (tapSensitivity != 0 && !Global.GetUseTouchPadForControls(deviceNum))
            {
                if (secondtouchbegin)
                {
                    tappedOnce = false;
                    secondtouchbegin = false;
                }

                DateTime test = arg.timeStamp;
                if (test <= (pastTime + TimeSpan.FromMilliseconds((double)tapSensitivity * 2)) && !arg.touchButtonPressed && !tappedOnce)
                {
                    if (Math.Abs(firstTouch.hwX - arg.touches[0].hwX) < 10 && Math.Abs(firstTouch.hwY - arg.touches[0].hwY) < 10)
                    {
                        if (Global.getDoubleTap(deviceNum))
                        {
                            tappedOnce = true;
                            firstTap = arg.timeStamp;
                            TimeofEnd = DateTime.Now; //since arg can't be used in synthesizeMouseButtons
                        }
                        else
                            Mapping.MapClick(deviceNum, Mapping.EClick.Left); //this way no delay if disabled
                    }
                }
            }
            else
            {
                if (Global.GetUseTouchPadForControls(deviceNum) == false)
                {
                    int[] disArray = Global.getTouchDisInvertTriggers(deviceNum);
                    tempBool = true;
                    for (int i = 0, arlen = disArray.Length; tempBool && i < arlen; i++)
                    {
                        if (GetDS4ControlsByName(disArray[i]) == false)
                            tempBool = false;
                    }

                    if (Global.getTrackballMode(deviceNum))
                    {
                        if (!trackballActive)
                        {
                            double currentWeight = 1.0;
                            double finalWeight = 0.0;
                            double x_out = 0.0, y_out = 0.0;
                            int idx = -1;
                            for (int i = 0; i < TRACKBALL_BUFFER_LEN && idx != trackballBufferHead; i++)
                            {
                                idx = (trackballBufferTail - i - 1 + TRACKBALL_BUFFER_LEN) % TRACKBALL_BUFFER_LEN;
                                x_out += trackballXBuffer[idx] * currentWeight;
                                y_out += trackballYBuffer[idx] * currentWeight;
                                finalWeight += currentWeight;
                                currentWeight *= 1.0;
                            }

                            x_out /= finalWeight;
                            trackballXVel = x_out;
                            y_out /= finalWeight;
                            trackballYVel = y_out;

                            trackballActive = true;
                        }

                        double tempAngle = Math.Atan2(-trackballYVel, trackballXVel);
                        double normX = Math.Abs(Math.Cos(tempAngle));
                        double normY = Math.Abs(Math.Sin(tempAngle));
                        int signX = Math.Sign(trackballXVel);
                        int signY = Math.Sign(trackballYVel);
                        
                        double trackXvDecay = Math.Min(Math.Abs(trackballXVel), trackballAccel * state.ElapsedTime * normX);
                        double trackYvDecay = Math.Min(Math.Abs(trackballYVel), trackballAccel * state.ElapsedTime * normY);
                        double xVNew = trackballXVel - (trackXvDecay * signX);
                        double yVNew = trackballYVel - (trackYvDecay * signY);
                        double xMotion = (xVNew * state.ElapsedTime) / TRACKBALL_SCALE;
                        double yMotion = (yVNew * state.ElapsedTime) / TRACKBALL_SCALE;
                        if (xMotion != 0.0)
                        {
                            xMotion += trackballDXRemain;
                        }
                        else
                        {
                            trackballDXRemain = 0.0;
                        }

                        int dx = (int)xMotion;
                        trackballDXRemain = xMotion - dx;

                        if (yMotion != 0.0)
                        {
                            yMotion += trackballDYRemain;
                        }
                        else
                        {
                            trackballDYRemain = 0.0;
                        }

                        int dy = (int)yMotion;
                        trackballDYRemain = yMotion - dy;

                        trackballXVel = xVNew;
                        trackballYVel = yVNew;

                        if (dx == 0 && dy == 0)
                        {
                            trackballActive = false;
                        }
                        else
                        {
                            cursor.TouchMoveCursor(dx, dy, tempBool);
                        }
                    }
                }
            }

            SynthesizeMouseButtons();
        }

        private bool IsLeft(Touch t) => t.hwX < 1920 * 2 / 5;
        private bool IsRight(Touch t) => t.hwX >= 1920 * 2 / 5;

        public virtual void TouchUnchanged(DS4Touchpad sender, EventArgs unused)
        {
            state = dev.GetCurrentStateRef();

            if (trackballActive)
            {
                if (Global.GetUseTouchPadForControls(deviceNum) == false)
                {
                    int[] disArray = Global.getTouchDisInvertTriggers(deviceNum);
                    tempBool = true;
                    for (int i = 0, arlen = disArray.Length; tempBool && i < arlen; i++)
                    {
                        if (GetDS4ControlsByName(disArray[i]) == false)
                            tempBool = false;
                    }

                    double tempAngle = Math.Atan2(-trackballYVel, trackballXVel);
                    double normX = Math.Abs(Math.Cos(tempAngle));
                    double normY = Math.Abs(Math.Sin(tempAngle));
                    int signX = Math.Sign(trackballXVel);
                    int signY = Math.Sign(trackballYVel);
                    double trackXvDecay = Math.Min(Math.Abs(trackballXVel), trackballAccel * state.ElapsedTime * normX);
                    double trackYvDecay = Math.Min(Math.Abs(trackballYVel), trackballAccel * state.ElapsedTime * normY);
                    double xVNew = trackballXVel - (trackXvDecay * signX);
                    double yVNew = trackballYVel - (trackYvDecay * signY);
                    double xMotion = (xVNew * state.ElapsedTime) / TRACKBALL_SCALE;
                    double yMotion = (yVNew * state.ElapsedTime) / TRACKBALL_SCALE;
                    if (xMotion != 0.0)
                    {
                        xMotion += trackballDXRemain;
                    }
                    else
                    {
                        trackballDXRemain = 0.0;
                    }

                    int dx = (int)xMotion;
                    trackballDXRemain = xMotion - dx;

                    if (yMotion != 0.0)
                    {
                        yMotion += trackballDYRemain;
                    }
                    else
                    {
                        trackballDYRemain = 0.0;
                    }

                    int dy = (int)yMotion;
                    trackballDYRemain = yMotion - dy;

                    trackballXVel = xVNew;
                    trackballYVel = yVNew;

                    if (dx == 0 && dy == 0)
                    {
                        trackballActive = false;
                    }
                    else
                    {
                        cursor.TouchMoveCursor(dx, dy, tempBool);
                    }
                }
            }

            if (state.TouchButton)
                SynthesizeMouseButtons();
        }

        public bool dragging, dragging2;

        private void SynthesizeMouseButtons()
        {
            if (Global.GetDS4Action(deviceNum, DS4Controls.TouchLeft, false) == null && leftDown)
            {
                Mapping.MapClick(deviceNum, Mapping.EClick.Left);
                dragging2 = true;
            }
            else
            {
                dragging2 = false;
            }

            if (Global.GetDS4Action(deviceNum, DS4Controls.TouchUpper, false) == null && upperDown)
                Mapping.MapClick(deviceNum, Mapping.EClick.Middle);

            if (Global.GetDS4Action(deviceNum, DS4Controls.TouchRight, false) == null && rightDown)
                Mapping.MapClick(deviceNum, Mapping.EClick.Left);

            if (Global.GetDS4Action(deviceNum, DS4Controls.TouchMulti, false) == null && multiDown)
                Mapping.MapClick(deviceNum, Mapping.EClick.Right);

            if (!Global.UseTouchPadForControls[deviceNum])
            {
                if (tappedOnce)
                {
                    DateTime tester = DateTime.Now;
                    if (tester > (TimeofEnd + TimeSpan.FromMilliseconds((double)(Global.TapSensitivity[deviceNum]) * 1.5)))
                    {
                        Mapping.MapClick(deviceNum, Mapping.EClick.Left);
                        tappedOnce = false;
                    }
                    //if it fails the method resets, and tries again with a new tester value (gives tap a delay so tap and hold can work)
                }
                if (secondtouchbegin) //if tap and hold (also works as double tap)
                {
                    Mapping.MapClick(deviceNum, Mapping.EClick.Left);
                    dragging = true;
                }
                else
                {
                    dragging = false;
                }
            }
        }

        public virtual void TouchButtonUp(DS4Touchpad sender, TouchpadEventArgs arg)
        {
            pushed = DS4Controls.None;
            upperDown = leftDown = rightDown = multiDown = false;
            state = dev.GetCurrentStateRef();
            if (state.Touch1 || state.Touch2)
                SynthesizeMouseButtons();
        }

        public virtual void TouchButtonDown(DS4Touchpad sender, TouchpadEventArgs arg)
        {
            if (arg.touches == null)
                upperDown = true;
            else if (arg.touches.Length > 1)
                multiDown = true;
            else
            {
                if ((Global.LowerRCOn[deviceNum] && arg.touches[0].hwX > (1920 * 3) / 4 && arg.touches[0].hwY > (960 * 3) / 4))
                    Mapping.MapClick(deviceNum, Mapping.EClick.Right);

                if (IsLeft(arg.touches[0]))
                    leftDown = true;
                else if (IsRight(arg.touches[0]))
                    rightDown = true;
            }

            state = dev.GetCurrentStateRef();
            SynthesizeMouseButtons();
        }

        public void PopulatePriorButtonStates()
        {
            priorUpperDown = upperDown;
            priorLeftDown = leftDown;
            priorRightDown = rightDown;
            priorMultiDown = multiDown;

            priorSwipeLeft = swipeLeft; priorSwipeRight = swipeRight;
            priorSwipeUp = swipeUp; priorSwipeDown = swipeDown;
            priorSwipeLeftB = swipeLeftB; priorSwipeRightB = swipeRightB; priorSwipeUpB = swipeUpB;
            priorSwipeDownB = swipeDownB; priorSwipedB = swipedB;
        }

        public DS4State GetDS4State() => state;
    }
}
