using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DS4Windows;

namespace DS4WinWPF.DS4Forms.ViewModels
{
    public class RecordBoxViewModel
    {
        public int DeviceNum { get; }
        public DS4ControlSettings Settings { get; }
        public bool Shift { get; }
        public bool RecordDelays { get; set; }
        public int MacroModeIndex { get; set; }
        public bool Recording { get; set; }
        public bool ToggleLightbar { get; set; }
        public bool ToggleRumble { get; set; }

        private readonly object macroStepsLock = new object();
        public ObservableCollection<MacroStepItem> MacroSteps { get; } = new ObservableCollection<MacroStepItem>();

        private int macroStepIndex;
        public int MacroStepIndex
        {
            get => macroStepIndex;
            set
            {
                if (macroStepIndex == value)
                    return;

                macroStepIndex = value;
                MacroStepIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler MacroStepIndexChanged;
        public Stopwatch Stopwatch { get; } = new Stopwatch();
        public bool Toggle4thMouse { get; set; }
        public bool Toggle5thMouse { get; set; }
        public int AppendIndex { get; set; } = -1;
        public int EditMacroIndex { get; set; } = -1;
        public Dictionary<int, bool> KeysdownMap { get; } = new Dictionary<int, bool>();
        public bool UseScanCode { get; set; }

        public RecordBoxViewModel(int deviceNum, DS4ControlSettings controlSettings, bool shift)
        {
            DeviceNum = deviceNum;
            Settings = controlSettings;
            Shift = shift;

            if (!shift && Settings.keyType.HasFlag(DS4KeyType.HoldMacro))
            {
                MacroModeIndex = 1;
            }
            else if (shift && Settings.shiftKeyType.HasFlag(DS4KeyType.HoldMacro))
            {
                MacroModeIndex = 1;
            }

            if (!shift && Settings.keyType.HasFlag(DS4KeyType.ScanCode))
            {
                UseScanCode = true;
            }
            else if (shift && Settings.shiftKeyType.HasFlag(DS4KeyType.ScanCode))
            {
                UseScanCode = true;
            }

            if (!shift && Settings.Action is int[])
            {
                LoadMacro();
            }
            else if (shift && Settings.ShiftAction is int[])
            {
                LoadMacro();
            }

            BindingOperations.EnableCollectionSynchronization(MacroSteps, macroStepsLock);
            
            // By default RECORD button appends new steps. User must select (click) an existing step to insert new steps in front of the selected step
            MacroStepIndex = -1;
        }

        public void LoadMacro()
        {
            int[] macro;
            if (!Shift)
            {
                macro = (int[])Settings.Action;
            }
            else
            {
                macro = (int[])Settings.ShiftAction;
            }

            MacroParser macroParser = new MacroParser(macro);
            macroParser.LoadMacro();
            foreach(MacroStep step in macroParser.MacroSteps)
            {
                MacroStepItem item = new MacroStepItem(step);
                MacroSteps.Add(item);
            }
        }

        public void ExportMacro()
        {
            int[] outmac = new int[MacroSteps.Count];
            int index = 0;
            foreach(MacroStepItem step in MacroSteps)
            {
                outmac[index] = step.Step.Value;
                index++;
            }

            if (!Shift)
            {
                Settings.Action = outmac;
                Settings.ActionType = DS4ControlSettings.EActionType.Macro;
                Settings.keyType = DS4KeyType.Macro;
                if (MacroModeIndex == 1)
                {
                    Settings.keyType |= DS4KeyType.HoldMacro;
                }
                if (UseScanCode)
                {
                    Settings.keyType |= DS4KeyType.ScanCode;
                }
            }
            else
            {
                Settings.ShiftAction = outmac;
                Settings.shiftActionType = DS4ControlSettings.EActionType.Macro;
                Settings.shiftKeyType = DS4KeyType.Macro;
                if (MacroModeIndex == 1)
                {
                    Settings.shiftKeyType |= DS4KeyType.HoldMacro;
                }
                if (UseScanCode)
                {
                    Settings.shiftKeyType |= DS4KeyType.ScanCode;
                }
            }
        }

        public void WriteCycleProgramsPreset()
        {
            MacroStep step = new MacroStep(18, KeyInterop.KeyFromVirtualKey(18).ToString(),
                MacroStep.StepType.ActDown, MacroStep.StepOutput.Key);
            MacroSteps.Add(new MacroStepItem(step));

            step = new MacroStep(9, KeyInterop.KeyFromVirtualKey(9).ToString(),
                MacroStep.StepType.ActDown, MacroStep.StepOutput.Key);
            MacroSteps.Add(new MacroStepItem(step));

            step = new MacroStep(9, KeyInterop.KeyFromVirtualKey(9).ToString(),
                MacroStep.StepType.ActUp, MacroStep.StepOutput.Key);
            MacroSteps.Add(new MacroStepItem(step));

            step = new MacroStep(18, KeyInterop.KeyFromVirtualKey(18).ToString(),
                MacroStep.StepType.ActUp, MacroStep.StepOutput.Key);
            MacroSteps.Add(new MacroStepItem(step));

            step = new MacroStep(1300, $"Wait 1000ms",
                MacroStep.StepType.Wait, MacroStep.StepOutput.None);
            MacroSteps.Add(new MacroStepItem(step));
        }

        public void LoadPresetFromFile(string filepath)
        {
            string[] macs = File.ReadAllText(filepath).Split('/');
            List<int> tmpmacro = new List<int>();
            int temp;
            foreach (string s in macs)
            {
                if (int.TryParse(s, out temp))
                    tmpmacro.Add(temp);
            }

            MacroParser macroParser = new MacroParser(tmpmacro.ToArray());
            macroParser.LoadMacro();
            foreach (MacroStep step in macroParser.MacroSteps)
            {
                MacroStepItem item = new MacroStepItem(step);
                MacroSteps.Add(item);
            }
        }

        public void SavePreset(string filepath)
        {
            int[] outmac = new int[MacroSteps.Count];
            int index = 0;
            foreach (MacroStepItem step in MacroSteps)
            {
                outmac[index] = step.Step.Value;
                index++;
            }

            string macro = string.Join("/", outmac);
            StreamWriter sw = new StreamWriter(filepath);
            sw.Write(macro);
            sw.Close();
        }

        public void AddMacroStep(MacroStep step)
        {
            if (RecordDelays && MacroSteps.Count > 0)
            {
                int elapsed = (int)Stopwatch.ElapsedMilliseconds + 300;
                MacroStep waitstep = new MacroStep(elapsed, $"Wait {elapsed - 300}ms", MacroStep.StepType.Wait, MacroStep.StepOutput.None);
                MacroStepItem waititem = new MacroStepItem(waitstep);
                if (AppendIndex == -1)
                {
                    MacroSteps.Add(waititem);
                }
                else
                {
                    MacroSteps.Insert(AppendIndex, waititem);
                    AppendIndex++;
                }
            }

            Stopwatch.Restart();
            MacroStepItem item = new MacroStepItem(step);
            if (AppendIndex == -1)
            {
                MacroSteps.Add(item);
            }
            else
            {
                MacroSteps.Insert(AppendIndex, item);
                AppendIndex++;
            }
        }

        public void StartForcedColor(Color color)
        {
            if (DeviceNum < 4)
            {
                DS4Color dcolor = new DS4Color() { Red = color.R, Green = color.G, Blue = color.B };
                DS4LightBar.ForcedColor[DeviceNum] = dcolor;
                DS4LightBar.ForcedFlash[DeviceNum] = 0;
                DS4LightBar.ForceLight[DeviceNum] = true;
            }
        }

        public void EndForcedColor()
        {
            if (DeviceNum < 4)
            {
                DS4LightBar.ForcedColor[DeviceNum] = new DS4Color(0, 0, 0);
                DS4LightBar.ForcedFlash[DeviceNum] = 0;
                DS4LightBar.ForceLight[DeviceNum] = false;
            }
        }

        public void UpdateForcedColor(Color color)
        {
            if (DeviceNum < 4)
            {
                DS4Color dcolor = new DS4Color() { Red = color.R, Green = color.G, Blue = color.B };
                DS4LightBar.ForcedColor[DeviceNum] = dcolor;
                DS4LightBar.ForcedFlash[DeviceNum] = 0;
                DS4LightBar.ForceLight[DeviceNum] = true;
            }
        }

        public void ProcessDS4Tick()
        {
            DS4Device dev = Program.RootHub.Controllers[0].Device;
            if (dev is null)
                return;
            
            DS4State cState = dev.GetCurrentStateRef();
            for (DS4Controls dc = DS4Controls.LXNeg; dc < DS4Controls.GyroXPos; dc++)
            {
                // Ignore Touch controls
                if (dc >= DS4Controls.TouchLeft && dc <= DS4Controls.TouchRight)
                    continue;
                
                int macroValue = Global.MacroDS4Values[dc];
                KeysdownMap.TryGetValue(macroValue, out bool isdown);
                if (!isdown && Mapping.GetBoolMapping(0, dc, cState, null, null))
                {
                    MacroStep step = new MacroStep(macroValue, MacroParser.macroInputNames[macroValue],
                            MacroStep.StepType.ActDown, MacroStep.StepOutput.Button);
                    AddMacroStep(step);
                    KeysdownMap.Add(macroValue, true);
                }
                else if (isdown && !Mapping.GetBoolMapping(0, dc, cState, null, null))
                {
                    MacroStep step = new MacroStep(macroValue, MacroParser.macroInputNames[macroValue],
                            MacroStep.StepType.ActUp, MacroStep.StepOutput.Button);
                    AddMacroStep(step);
                    KeysdownMap.Remove(macroValue);
                }
            }
        }
    }

    public class MacroStepItem
    {
        private static string[] imageSources = new string[]
        {
            "/DS4Windows;component/Resources/keydown.png",
            "/DS4Windows;component/Resources/keyup.png",
            "/DS4Windows;component/Resources/clock.png",
        };

        public string Image { get; }
        public MacroStep Step { get; }
        public int DisplayValue
        {
            get
            {
                int result = Step.Value;
                if (Step.ActType == MacroStep.StepType.Wait)
                {
                    result -= 300;
                }

                return result;
            }
            set
            {
                int result = value;
                if (Step.ActType == MacroStep.StepType.Wait)
                {
                    result += 300;
                }

                Step.Value = result;
            }
        }

        public int RumbleHeavy
        {
            get
            {
                int result = Step.Value;
                result -= 1000000;
                string temp = result.ToString();
                result = int.Parse(temp.Substring(0, 3));
                return result;
            }
            set
            {
                int result = Step.Value;
                result -= 1000000;
                int curHeavy = result / 1000;
                int curLight = result - (curHeavy * 1000);
                result = curLight + (value * 1000) + 1000000;
                Step.Value = result;
            }
        }

        public int RumbleLight
        {
            get
            {
                int result = Step.Value;
                result -= 1000000;
                string temp = result.ToString();
                result = int.Parse(temp.Substring(3, 3));
                return result;
            }
            set
            {
                int result = Step.Value;
                result -= 1000000;
                int curHeavy = result / 1000;
                result = value + (curHeavy * 1000) + 1000000;
                Step.Value = result;
            }
        }

        public MacroStepItem(MacroStep step)
        {
            Step = step;
            Image = imageSources[(int)step.ActType];
        }

        public void UpdateLightbarValue(Color color)
        {
            Step.Value = 1000000000 + (color.R*1000000)+(color.G*1000)+color.B;
        }

        public Color LightbarColorValue()
        {
            int temp = Step.Value - 1000000000;
            int r = temp / 1000000;
            temp -= (r * 1000000);
            int g = temp / 1000;
            temp -= (g * 1000);
            int b = temp;
            return new Color() { A = 255, R = (byte)r, G = (byte)g, B = (byte)b };
        }
    }
}
