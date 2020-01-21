using System;
using System.Windows;
using System.Windows.Media;
using DS4Windows;

namespace DS4WinWPF.DS4Forms.ViewModels
{
    public class BindingWindowViewModel
    {
        private int deviceNum;
        private bool use360Mode;
        private DS4ControlSettings settings;
        private OutBinding currentOutBind;
        private OutBinding shiftOutBind;
        private OutBinding actionBinding;
        private bool showShift;
        private bool rumbleActive;

        public bool Using360Mode
        {
            get => use360Mode;
        }
        public int DeviceNum { get => deviceNum; }
        public OutBinding CurrentOutBind { get => currentOutBind; }
        public OutBinding ShiftOutBind { get => shiftOutBind; }
        public OutBinding ActionBinding
        {
            get => actionBinding;
            set => actionBinding = value;
        }

        public bool ShowShift { get => showShift; set => showShift = value; }
        public bool RumbleActive { get => rumbleActive; set => rumbleActive = value; }
        public DS4ControlSettings Settings { get => settings; }

        public BindingWindowViewModel(int deviceNum, DS4ControlSettings settings)
        {
            this.deviceNum = deviceNum;
            use360Mode = Global.outDevTypeTemp[deviceNum] == OutControllerType.X360;
            this.settings = settings;
            currentOutBind = new OutBinding();
            shiftOutBind = new OutBinding();
            shiftOutBind.shiftBind = true;
            PopulateCurrentBinds();
        }

        public void PopulateCurrentBinds()
        {
            DS4ControlSettings setting = settings;
            bool sc = setting.keyType.HasFlag(DS4KeyType.ScanCode);
            bool toggle = setting.keyType.HasFlag(DS4KeyType.Toggle);
            currentOutBind.input = setting.control;
            shiftOutBind.input = setting.control;
            if (setting.action != null)
            {
                switch(setting.actionType)
                {
                    case DS4ControlSettings.ActionType.Button:
                        currentOutBind.outputType = OutBinding.OutType.Button;
                        currentOutBind.control = (X360Controls)setting.action;
                        break;
                    case DS4ControlSettings.ActionType.Default:
                        currentOutBind.outputType = OutBinding.OutType.Default;
                        break;
                    case DS4ControlSettings.ActionType.Key:
                        currentOutBind.outputType = OutBinding.OutType.Key;
                        currentOutBind.outkey = Convert.ToInt32(setting.action);
                        currentOutBind.hasScanCode = sc;
                        currentOutBind.toggle = toggle;
                        break;
                    case DS4ControlSettings.ActionType.Macro:
                        currentOutBind.outputType = OutBinding.OutType.Macro;
                        currentOutBind.macro = (int[])setting.action;
                        break;
                }
            }
            else
            {
                currentOutBind.outputType = OutBinding.OutType.Default;
            }

            if (!string.IsNullOrEmpty(setting.extras))
            {
                currentOutBind.ParseExtras(setting.extras);
            }

            if (setting.shiftAction != null)
            {
                sc = setting.shiftKeyType.HasFlag(DS4KeyType.ScanCode);
                toggle = setting.shiftKeyType.HasFlag(DS4KeyType.Toggle);
                shiftOutBind.shiftTrigger = setting.shiftTrigger;
                switch (setting.shiftActionType)
                {
                    case DS4ControlSettings.ActionType.Button:
                        shiftOutBind.outputType = OutBinding.OutType.Button;
                        shiftOutBind.control = (X360Controls)setting.shiftAction;
                        break;
                    case DS4ControlSettings.ActionType.Default:
                        shiftOutBind.outputType = OutBinding.OutType.Default;
                        break;
                    case DS4ControlSettings.ActionType.Key:
                        shiftOutBind.outputType = OutBinding.OutType.Key;
                        shiftOutBind.outkey = Convert.ToInt32(setting.shiftAction);
                        shiftOutBind.hasScanCode = sc;
                        shiftOutBind.toggle = toggle;
                        break;
                    case DS4ControlSettings.ActionType.Macro:
                        shiftOutBind.outputType = OutBinding.OutType.Macro;
                        shiftOutBind.macro = (int[])setting.shiftAction;
                        break;
                }
            }

            if (!string.IsNullOrEmpty(setting.shiftExtras))
            {
                shiftOutBind.ParseExtras(setting.shiftExtras);
            }
        }

        public void WriteBinds()
        {
            currentOutBind.WriteBind(settings);
            shiftOutBind.WriteBind(settings);
        }

        public void StartForcedColor(Color color)
        {
            if (deviceNum < 4)
            {
                DS4Color dcolor = new DS4Color() { Red = color.R, Green = color.G, Blue = color.B };
                DS4LightBar.ForcedColor[deviceNum] = dcolor;
                DS4LightBar.ForcedFlash[deviceNum] = 0;
                DS4LightBar.Forcelight[deviceNum] = true;
            }
        }

        public void EndForcedColor()
        {
            if (deviceNum < 4)
            {
                DS4LightBar.ForcedColor[deviceNum] = new DS4Color(0, 0, 0);
                DS4LightBar.ForcedFlash[deviceNum] = 0;
                DS4LightBar.Forcelight[deviceNum] = false;
            }
        }

        public void UpdateForcedColor(Color color)
        {
            if (deviceNum < 4)
            {
                DS4Color dcolor = new DS4Color() { Red = color.R, Green = color.G, Blue = color.B };
                DS4LightBar.ForcedColor[deviceNum] = dcolor;
                DS4LightBar.ForcedFlash[deviceNum] = 0;
                DS4LightBar.Forcelight[deviceNum] = true;
            }
        }
    }

    public class BindAssociation
    {
        public enum OutType : uint
        {
            Default,
            Key,
            Button,
            Macro
        }

        public OutType outputType;
        public X360Controls control;
        public int outkey;

        public bool IsMouse()
        {
            return outputType == OutType.Button && (control >= X360Controls.LeftMouse && control < X360Controls.Unbound);
        }

        public static bool IsMouseRange(X360Controls control)
        {
            return control >= X360Controls.LeftMouse && control < X360Controls.Unbound;
        }
    }

    public class OutBinding
    {
        public enum OutType : uint
        {
            Default,
            Key,
            Button,
            Macro
        }

        public DS4Controls input;
        public bool toggle;
        public bool hasScanCode;
        public OutType outputType;
        public int outkey;
        public int[] macro;
        public X360Controls control;
        public bool shiftBind;
        public int shiftTrigger;
        private int heavyRumble = 0;
        private int lightRumble = 0;
        private int flashRate;
        private int mouseSens = 25;
        private DS4Color extrasColor = new DS4Color(255,255,255);

        public bool HasScanCode { get => hasScanCode; set => hasScanCode = value; }
        public bool Toggle { get => toggle; set => toggle = value; }
        public int ShiftTrigger { get => shiftTrigger; set => shiftTrigger = value; }
        public int HeavyRumble { get => heavyRumble; set => heavyRumble = value; }
        public int LightRumble { get => lightRumble; set => lightRumble = value; }
        public int FlashRate
        {
            get => flashRate;
            set
            {
                flashRate = value;
                FlashRateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler FlashRateChanged;

        public int MouseSens
        {
            get => mouseSens;
            set
            {
                mouseSens = value;
                MouseSensChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler MouseSensChanged;

        private bool useMouseSens;
        public bool UseMouseSens
        {
            get => useMouseSens;
            set
            {
                useMouseSens = value;
                UseMouseSensChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler UseMouseSensChanged;

        private bool useExtrasColor;
        public bool UseExtrasColor
        {
            get => useExtrasColor;
            set
            {
                useExtrasColor = value;
                UseExtrasColorChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler UseExtrasColorChanged;

        public int ExtrasColorR
        {
            get => extrasColor.Red;
            set
            {
                extrasColor.Red = (byte)value;
                ExtrasColorRChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler ExtrasColorRChanged;

        public string ExtrasColorRString
        {
            get
            {
                string temp = $"#{extrasColor.Red.ToString("X2")}FF0000";
                return temp;
            }
        }
        public event EventHandler ExtrasColorRStringChanged;
        public int ExtrasColorG
        {
            get => extrasColor.Green;
            set
            {
                extrasColor.Green = (byte)value;
                ExtrasColorGChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler ExtrasColorGChanged;

        public string ExtrasColorGString
        {
            get
            {
                string temp = $"#{ extrasColor.Green.ToString("X2")}00FF00";
                return temp;
            }
        }
        public event EventHandler ExtrasColorGStringChanged;

        public int ExtrasColorB
        {
            get => extrasColor.Blue;
            set
            {
                extrasColor.Blue = (byte)value;
                ExtrasColorBChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler ExtrasColorBChanged;

        public string ExtrasColorBString
        {
            get
            {
                string temp = $"#{extrasColor.Blue.ToString("X2")}0000FF";
                return temp;
            }
        }
        public event EventHandler ExtrasColorBStringChanged;

        public string ExtrasColorString
        {
            get => $"#FF{extrasColor.Red.ToString("X2")}{extrasColor.Green.ToString("X2")}{extrasColor.Blue.ToString("X2")}";
        }
        public event EventHandler ExtrasColorStringChanged;

        public Color ExtrasColorMedia
        {
            get
            {
                return new Color()
                {
                    A = 255,
                    R = extrasColor.Red,
                    B = extrasColor.Blue,
                    G = extrasColor.Green
                };
            }
        }

        private int shiftTriggerIndex;
        public int ShiftTriggerIndex { get => shiftTriggerIndex; set => shiftTriggerIndex = value; }

        public string DefaultColor
        {
            get
            {
                string color;
                if (outputType == OutType.Default)
                {
                    color =  Colors.LimeGreen.ToString();
                }
                else
                {
                    color = SystemColors.ControlBrush.Color.ToString();
                }

                return color;
            }
        }

        public string UnboundColor
        {
            get
            {
                string color;
                if (outputType == OutType.Button && control == X360Controls.Unbound)
                {
                    color = Colors.LimeGreen.ToString();
                }
                else
                {
                    color = SystemColors.ControlBrush.Color.ToString();
                }

                return color;
            }
        }

        public string DefaultBtnString
        {
            get
            {
                string result = "Default";
                if (shiftBind)
                {
                    result = Properties.Resources.FallBack;
                }

                return result;
            }
        }

        public Visibility MacroLbVisible
        {
            get
            {
                return outputType == OutType.Macro ? Visibility.Visible : Visibility.Hidden;
            }
        }

        public OutBinding()
        {
            ExtrasColorRChanged += OutBinding_ExtrasColorRChanged;
            ExtrasColorGChanged += OutBinding_ExtrasColorGChanged;
            ExtrasColorBChanged += OutBinding_ExtrasColorBChanged;
            UseExtrasColorChanged += OutBinding_UseExtrasColorChanged;
        }

        private void OutBinding_ExtrasColorBChanged(object sender, EventArgs e)
        {
            ExtrasColorStringChanged?.Invoke(this, EventArgs.Empty);
            ExtrasColorBStringChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OutBinding_ExtrasColorGChanged(object sender, EventArgs e)
        {
            ExtrasColorStringChanged?.Invoke(this, EventArgs.Empty);
            ExtrasColorGStringChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OutBinding_ExtrasColorRChanged(object sender, EventArgs e)
        {
            ExtrasColorStringChanged?.Invoke(this, EventArgs.Empty);
            ExtrasColorRStringChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OutBinding_UseExtrasColorChanged(object sender, EventArgs e)
        {
            if (!useExtrasColor)
            {
                ExtrasColorR = 255;
                ExtrasColorG = 255;
                ExtrasColorB = 255;
            }
        }

        public bool IsShift => shiftBind;
        
        public bool IsMouse()
        {
            return outputType == OutType.Button && (control >= X360Controls.LeftMouse && control < X360Controls.Unbound);
        }

        public static bool IsMouseRange(X360Controls control)
        {
            return control >= X360Controls.LeftMouse && control < X360Controls.Unbound;
        }

        public void ParseExtras(string extras)
        {
            string[] temp = extras.Split(',');
            if (temp.Length == 9)
            {
                int.TryParse(temp[0], out heavyRumble);
                int.TryParse(temp[1], out lightRumble);
                int.TryParse(temp[2], out int useColor);
                if (useColor == 1)
                {
                    byte.TryParse(temp[3], out extrasColor.Red);
                    byte.TryParse(temp[4], out extrasColor.Green);
                    byte.TryParse(temp[5], out extrasColor.Blue);
                    int.TryParse(temp[6], out flashRate);
                }
                else
                {
                    extrasColor.Red = extrasColor.Green = extrasColor.Blue = 255;
                    flashRate = 0;
                }

                int.TryParse(temp[7], out int useM);
                if (useM == 1)
                {
                    useMouseSens = true;
                    int.TryParse(temp[8], out mouseSens);
                }
                else
                {
                    useMouseSens = false;
                    mouseSens = 25;
                }
            }
        }

        public string CompileExtras()
        {
            string result = $"{heavyRumble},{lightRumble},";
            if (useExtrasColor)
            {
                result += $"1,{extrasColor.Red},{extrasColor.Green},{extrasColor.Blue},{flashRate},";
            }
            else
            {
                result += "0,0,0,0,0,";
            }

            if (useMouseSens)
            {
                result += $"1,{mouseSens}";
            }
            else
            {
                result += "0,0";
            }

            return result;
        }

        public bool IsUsingExtras()
        {
            bool result = false;
            result = result || (heavyRumble != 0);
            result = result || (lightRumble != 0);
            result = result || useExtrasColor;
            result = result ||
                (extrasColor.Red != 255 && extrasColor.Green != 255 &&
                extrasColor.Blue != 255);

            result = result || (flashRate != 0);
            result = result || useMouseSens;
            result = result || (mouseSens != 25);
            return result;
        }

        public void WriteBind(DS4ControlSettings settings)
        {
            if (!shiftBind)
            {
                settings.keyType = DS4KeyType.None;

                if (outputType == OutType.Default)
                {
                    settings.action = null;
                    settings.actionType = DS4ControlSettings.ActionType.Default;
                }
                else if (outputType == OutType.Button)
                {
                    settings.action = control;
                    settings.actionType = DS4ControlSettings.ActionType.Button;
                    if (control == X360Controls.Unbound)
                    {
                        settings.keyType |= DS4KeyType.Unbound;
                    }
                }
                else if (outputType == OutType.Key)
                {
                    settings.action = outkey;
                    settings.actionType = DS4ControlSettings.ActionType.Key;
                    if (hasScanCode)
                    {
                        settings.keyType |= DS4KeyType.ScanCode;
                    }

                    if (toggle)
                    {
                        settings.keyType |= DS4KeyType.Toggle;
                    }
                }
                else if (outputType == OutType.Macro)
                {
                    settings.action = macro;
                    settings.actionType = DS4ControlSettings.ActionType.Macro;
                    settings.keyType |= DS4KeyType.Macro;
                }

                if (IsUsingExtras())
                {
                    settings.extras = CompileExtras();
                }
                else
                {
                    settings.extras = string.Empty;
                }
            }
            else
            {
                settings.shiftKeyType = DS4KeyType.None;
                settings.shiftTrigger = shiftTrigger;

                if (outputType == OutType.Default || shiftTrigger == 0)
                {
                    settings.shiftAction = null;
                    settings.shiftActionType = DS4ControlSettings.ActionType.Default;
                }
                else if (outputType == OutType.Button)
                {
                    settings.shiftAction = control;
                    settings.shiftActionType = DS4ControlSettings.ActionType.Button;
                    if (control == X360Controls.Unbound)
                    {
                        settings.shiftKeyType |= DS4KeyType.Unbound;
                    }
                }
                else if (outputType == OutType.Key)
                {
                    settings.shiftAction = outkey;
                    settings.shiftActionType = DS4ControlSettings.ActionType.Key;
                    if (hasScanCode)
                    {
                        settings.shiftKeyType |= DS4KeyType.ScanCode;
                    }

                    if (toggle)
                    {
                        settings.shiftKeyType |= DS4KeyType.Toggle;
                    }
                }
                else if (outputType == OutType.Macro)
                {
                    settings.shiftAction = macro;
                    settings.shiftActionType = DS4ControlSettings.ActionType.Macro;
                    settings.shiftKeyType |= DS4KeyType.Macro;
                }

                if (IsUsingExtras())
                {
                    settings.shiftExtras = CompileExtras();
                }
                else
                {
                    settings.shiftExtras = string.Empty;
                }
            }
        }

        public void UpdateExtrasColor(Color color)
        {
            ExtrasColorR = color.R;
            ExtrasColorG = color.G;
            ExtrasColorB = color.B;
        }
    }
}
