﻿using System;
using System.Drawing;
using static System.Math;
using static DS4Windows.Global;
using System.Diagnostics;

namespace DS4Windows
{
    public class DS4LightBar
    {
        private readonly static byte[/* Light On duration */, /* Light Off duration */] BatteryIndicatorDurations =
        {
            { 28, 252 }, // on 10% of the time at 0
            { 28, 252 },
            { 56, 224 },
            { 84, 196 },
            { 112, 168 },
            { 140, 140 },
            { 168, 112 },
            { 196, 84 },
            { 224, 56 }, // on 80% of the time at 80, etc.
            { 252, 28 }, // on 90% of the time at 90
            { 0, 0 }     // use on 100%. 0 is for "charging" OR anything sufficiently-"charged"
        };

        static double[] counters = new double[4] { 0, 0, 0, 0 };
        public static Stopwatch[] fadewatches = new Stopwatch[4] { new Stopwatch(), new Stopwatch(), new Stopwatch(), new Stopwatch() };

        static bool[] fadedirection = new bool[4] { false, false, false, false };
        static DateTime[] oldnow = new DateTime[4] { DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow };

        public static bool[] ForceLight = new bool[4] { false, false, false, false };
        public static DS4Color[] ForcedColor = new DS4Color[4];
        public static byte[] ForcedFlash = new byte[4];
        internal const int PULSE_FLASH_DURATION = 2000;
        internal const double PULSE_FLASH_SEGMENTS = PULSE_FLASH_DURATION / 40;
        internal const int PULSE_CHARGING_DURATION = 4000;
        internal const double PULSE_CHARGING_SEGMENTS = (PULSE_CHARGING_DURATION / 40) - 2;

        public static void UpdateLightBar(DS4Device device, int deviceNum)
        {
            DS4Color color;
            if (!DefaultLight && !ForceLight[deviceNum])
            {
                if (GetUseCustomLed(deviceNum))
                {
                    if (GetLedAsBatteryIndicator(deviceNum))
                    {
                        ref DS4Color fullColor = ref GetCustomColor(deviceNum);
                        ref DS4Color lowColor = ref GetLowColor(deviceNum);
                        color = LerpDS4Color(ref lowColor, ref fullColor, device.Battery);
                    }
                    else
                        color = GetCustomColor(deviceNum);
                }
                else
                {
                    double rainbow = GetRainbow(deviceNum);
                    if (rainbow > 0)
                    {
                        // Display rainbow
                        DateTime now = DateTime.UtcNow;
                        if (now >= oldnow[deviceNum] + TimeSpan.FromMilliseconds(10)) //update by the millisecond that way it's a smooth transtion
                        {
                            oldnow[deviceNum] = now;
                            if (device.IsCharging)
                                counters[deviceNum] -= 1.5 * 3 / rainbow;
                            else
                                counters[deviceNum] += 1.5 * 3 / rainbow;
                        }

                        if (counters[deviceNum] < 0)
                            counters[deviceNum] = 180000;
                        else if (counters[deviceNum] > 180000)
                            counters[deviceNum] = 0;

                        double maxSat = GetMaxSatRainbow(deviceNum);
                        if (GetLedAsBatteryIndicator(deviceNum))
                        {
                            byte useSat = (byte)(maxSat == 1.0 ?
                                device.Battery * 2.55 :
                                device.Battery * 2.55 * maxSat);
                            color = HuetoRGB((float)counters[deviceNum] % 360, useSat);
                        }
                        else
                            color = HuetoRGB((float)counters[deviceNum] % 360,
                                (byte)(maxSat == 1.0 ? 255 : 255 * maxSat));

                    }
                    else if (GetLedAsBatteryIndicator(deviceNum))
                    {
                        ref DS4Color fullColor = ref GetMainColor(deviceNum);
                        ref DS4Color lowColor = ref GetLowColor(deviceNum);
                        color = LerpDS4Color(ref lowColor, ref fullColor, device.Battery);
                    }
                    else
                    {
                        color = GetMainColor(deviceNum);
                    }
                }

                if (device.Battery <= GetFlashAt(deviceNum) && !DefaultLight && !device.IsCharging)
                {
                    ref DS4Color flashColor = ref getFlashColor(deviceNum);
                    if (!(flashColor.Red == 0 &&
                        flashColor.Green == 0 &&
                        flashColor.Blue == 0))
                        color = flashColor;

                    if (GetFlashType(deviceNum) == 1)
                    {
                        double ratio;

                        if (!fadewatches[deviceNum].IsRunning)
                        {
                            bool temp = fadedirection[deviceNum];
                            fadedirection[deviceNum] = !temp;
                            fadewatches[deviceNum].Restart();
                            ratio = temp ? 100.0 : 0.0;
                        }
                        else
                        {
                            long elapsed = fadewatches[deviceNum].ElapsedMilliseconds;

                            if (fadedirection[deviceNum])
                            {
                                if (elapsed < PULSE_FLASH_DURATION)
                                {
                                    elapsed /= 40;
                                    ratio = 100.0 * (elapsed / PULSE_FLASH_SEGMENTS);
                                }
                                else
                                {
                                    ratio = 100.0;
                                    fadewatches[deviceNum].Stop();
                                }
                            }
                            else
                            {
                                if (elapsed < PULSE_FLASH_DURATION)
                                {
                                    elapsed /= 40;
                                    ratio = (0 - 100.0) * (elapsed / PULSE_FLASH_SEGMENTS) + 100.0;
                                }
                                else
                                {
                                    ratio = 0.0;
                                    fadewatches[deviceNum].Stop();
                                }
                            }
                        }

                        DS4Color tempCol = new DS4Color(0, 0, 0);
                        color = LerpDS4Color(ref color, ref tempCol, ratio);
                    }
                }

                int idleDisconnectTimeout = GetIdleDisconnectTimeout(deviceNum);
                if (idleDisconnectTimeout > 0 && GetLedAsBatteryIndicator(deviceNum) &&
                    (!device.IsCharging || device.Battery >= 100))
                {
                    //Fade lightbar by idle time
                    TimeSpan timeratio = new TimeSpan(DateTime.UtcNow.Ticks - device.lastActive.Ticks);
                    double botratio = timeratio.TotalMilliseconds;
                    double topratio = TimeSpan.FromSeconds(idleDisconnectTimeout).TotalMilliseconds;
                    double ratio = 100.0 * (botratio / topratio), elapsed = ratio;
                    if (ratio >= 50.0 && ratio < 100.0)
                    {
                        DS4Color emptyCol = new DS4Color(0, 0, 0);
                        color = LerpDS4Color(ref color, ref emptyCol,
                            (uint)(-100.0 * (elapsed = 0.02 * (ratio - 50.0)) * (elapsed - 2.0)));
                    }
                    else if (ratio >= 100.0)
                    {
                        DS4Color emptyCol = new DS4Color(0, 0, 0);
                        color = LerpDS4Color(ref color, ref emptyCol, 100.0);
                    }
                        
                }

                if (device.IsCharging && device.Battery < 100)
                {
                    switch (getChargingType(deviceNum))
                    {
                        case 1:
                        {
                            double ratio = 0.0;

                            if (!fadewatches[deviceNum].IsRunning)
                            {
                                bool temp = fadedirection[deviceNum];
                                fadedirection[deviceNum] = !temp;
                                fadewatches[deviceNum].Restart();
                                ratio = temp ? 100.0 : 0.0;
                            }
                            else
                            {
                                long elapsed = fadewatches[deviceNum].ElapsedMilliseconds;

                                if (fadedirection[deviceNum])
                                {
                                    if (elapsed < PULSE_CHARGING_DURATION)
                                    {
                                        elapsed = elapsed / 40;
                                        if (elapsed > PULSE_CHARGING_SEGMENTS)
                                            elapsed = (long)PULSE_CHARGING_SEGMENTS;
                                        ratio = 100.0 * (elapsed / PULSE_CHARGING_SEGMENTS);
                                    }
                                    else
                                    {
                                        ratio = 100.0;
                                        fadewatches[deviceNum].Stop();
                                    }
                                }
                                else
                                {
                                    if (elapsed < PULSE_CHARGING_DURATION)
                                    {
                                        elapsed = elapsed / 40;
                                        if (elapsed > PULSE_CHARGING_SEGMENTS)
                                            elapsed = (long)PULSE_CHARGING_SEGMENTS;
                                        ratio = (0 - 100.0) * (elapsed / PULSE_CHARGING_SEGMENTS) + 100.0;
                                    }
                                    else
                                    {
                                        ratio = 0.0;
                                        fadewatches[deviceNum].Stop();
                                    }
                                }
                            }

                            DS4Color emptyCol = new DS4Color(0, 0, 0);
                            color = LerpDS4Color(ref color, ref emptyCol, ratio);
                            break;
                        }
                        case 2:
                        {
                            counters[deviceNum] += 0.167;
                            color = HuetoRGB((float)counters[deviceNum] % 360, 255);
                            break;
                        }
                        case 3:
                        {
                            color = getChargingColor(deviceNum);
                            break;
                        }
                        default: break;
                    }
                }
            }
            else if (ForceLight[deviceNum])
            {
                color = ForcedColor[deviceNum];
            }
            else if (shuttingdown)
                color = new DS4Color(0, 0, 0);
            else
            {
                if (device.IsBT)
                    color = new DS4Color(32, 64, 64);
                else
                    color = new DS4Color(0, 0, 0);
            }

            bool distanceprofile = DistanceProfiles[deviceNum] || TempProfileDistance[deviceNum];
            //distanceprofile = (ProfilePath[deviceNum].ToLower().Contains("distance") || tempprofilename[deviceNum].ToLower().Contains("distance"));
            if (distanceprofile && !DefaultLight)
            {
                // Thing I did for Distance
                float rumble = device.getLeftHeavySlowRumble() / 2.55f;
                byte max = Max(color.Red, Max(color.Green, color.Blue));
                if (device.getLeftHeavySlowRumble() > 100)
                {
                    DS4Color maxCol = new DS4Color(max, max, 0);
                    DS4Color redCol = new DS4Color(255, 0, 0);
                    color = LerpDS4Color(ref maxCol, ref redCol, rumble);
                }
                    
                else
                {
                    DS4Color maxCol = new DS4Color(max, max, 0);
                    DS4Color redCol = new DS4Color(255, 0, 0);
                    DS4Color tempCol = LerpDS4Color(ref maxCol,
                        ref redCol, 39.6078f);
                    color = LerpDS4Color(ref color, ref tempCol,
                        device.getLeftHeavySlowRumble());
                }
                    
            }

            DS4HapticState haptics = new DS4HapticState
            {
                LightBarColor = color
            };

            if (haptics.IsLightBarSet())
            {
                if (ForceLight[deviceNum] && ForcedFlash[deviceNum] > 0)
                {
                    haptics.LightBarFlashDurationOff = haptics.LightBarFlashDurationOn = (byte)(25 - ForcedFlash[deviceNum]);
                    haptics.LightBarExplicitlyOff = true;
                }
                else if (device.Battery <= GetFlashAt(deviceNum) && GetFlashType(deviceNum) == 0 && !DefaultLight && !device.IsCharging)
                {
                    int level = device.Battery / 10;
                    if (level >= 10)
                        level = 10; // all values of >~100% are rendered the same

                    haptics.LightBarFlashDurationOn = BatteryIndicatorDurations[level, 0];
                    haptics.LightBarFlashDurationOff = BatteryIndicatorDurations[level, 1];
                }
                else if (distanceprofile && device.getLeftHeavySlowRumble() > 155) //also part of Distance
                {
                    haptics.LightBarFlashDurationOff = haptics.LightBarFlashDurationOn = (byte)((-device.getLeftHeavySlowRumble() + 265));
                    haptics.LightBarExplicitlyOff = true;
                }
                else
                {
                    //haptics.LightBarFlashDurationOff = haptics.LightBarFlashDurationOn = 1;
                    haptics.LightBarFlashDurationOff = haptics.LightBarFlashDurationOn = 0;
                    haptics.LightBarExplicitlyOff = true;
                }
            }
            else
            {
                haptics.LightBarExplicitlyOff = true;
            }

            byte tempLightBarOnDuration = device.getLightBarOnDuration();
            if (tempLightBarOnDuration != haptics.LightBarFlashDurationOn && tempLightBarOnDuration != 1 && haptics.LightBarFlashDurationOn == 0)
                haptics.LightBarFlashDurationOff = haptics.LightBarFlashDurationOn = 1;

            device.SetHapticState(ref haptics);
            //device.pushHapticState(ref haptics);
        }

        public static bool DefaultLight = false, shuttingdown = false;
      
        public static DS4Color HuetoRGB(float hue, byte sat)
        {
            byte C = sat;
            int X = (int)((C * (float)(1 - Abs((hue / 60) % 2 - 1))));
            if (0 <= hue && hue < 60)
                return new DS4Color(C, (byte)X, 0);
            else if (60 <= hue && hue < 120)
                return new DS4Color((byte)X, C, 0);
            else if (120 <= hue && hue < 180)
                return new DS4Color(0, C, (byte)X);
            else if (180 <= hue && hue < 240)
                return new DS4Color(0, (byte)X, C);
            else if (240 <= hue && hue < 300)
                return new DS4Color((byte)X, 0, C);
            else if (300 <= hue && hue < 360)
                return new DS4Color(C, 0, (byte)X);
            else
                return new DS4Color(Color.Red);
        }
    }
}
