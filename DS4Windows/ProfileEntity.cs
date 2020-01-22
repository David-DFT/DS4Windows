using System;
using System.IO;

namespace DS4WinWPF
{
    public class ProfileEntity
    {
        private string name;
        public string Name
        {
            get => name;
            set
            {
                if (name == value) return;
                name = value;
                NameChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler NameChanged;
        public event EventHandler ProfileSaved;
        public event EventHandler ProfileDeleted;

        public void DeleteFile()
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                string filepath = DS4Windows.Global.AppDataPath + @"\Profiles\" + name + ".xml";
                if (File.Exists(filepath))
                {
                    File.Delete(filepath);
                    ProfileDeleted?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public void SaveProfile(int deviceNum)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                DS4Windows.Global.SaveProfile(deviceNum, name);
                DS4Windows.Global.calculateProfileActionCount(deviceNum);
                DS4Windows.Global.calculateProfileActionDicts(deviceNum);
                DS4Windows.Global.cacheProfileCustomsFlags(deviceNum);
            }
        }

        public void FireSaved()
        {
            ProfileSaved?.Invoke(this, EventArgs.Empty);
        }
    }
}
