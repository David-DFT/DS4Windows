﻿using System;

namespace DS4Windows
{
    public class AppLogger
    {
        public static event EventHandler<DebugEventArgs> TrayIconLog;
        public static event EventHandler<DebugEventArgs> GuiLog;

        public static void LogToGui(string data, bool warning)
        {
            GuiLog?.Invoke(null, new DebugEventArgs(data, warning));
        }

        public static void LogToTray(string data, bool warning = false, bool ignoreSettings = false)
        {
            if (TrayIconLog != null)
            {
                if (ignoreSettings)
                    TrayIconLog(ignoreSettings, new DebugEventArgs(data, warning));
                else
                    TrayIconLog(null, new DebugEventArgs(data, warning));
            }
        }
    }
}

