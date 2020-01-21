using System;

namespace DS4WinWPF
{
    public class LogItem
    {
        private DateTime datetime;
        private string message;
        private bool warning;

        public DateTime Datetime { get => datetime; set => datetime = value; }
        public string Message { get => message; set => message = value; }
        public bool Warning { get => warning; set => warning = value; }
        public string Color
        {
            get
            {
                return warning ? "Red" : "Black";
            }
        }
    }
}
