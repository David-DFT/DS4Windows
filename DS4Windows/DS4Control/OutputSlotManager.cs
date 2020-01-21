using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nefarius.ViGEm.Client;

namespace DS4Windows
{
    public class OutputSlotManager
    {
        private const int DELAY_TIME = 100; // measured in ms

        private Dictionary<int, OutputDevice> devictDict = new Dictionary<int, OutputDevice>();
        private Dictionary<OutputDevice, int> revDevictDict = new Dictionary<OutputDevice, int>();
        private OutputDevice[] outputDevices = new OutputDevice[4];
        private Queue<Action> actions = new Queue<Action>();
        private object actionLock = new object();

        public bool RunningQueue { get; private set; }

        public OutputDevice AllocateController(OutControllerType contType, ViGEmClient client)
        {
            OutputDevice outputDevice = null;
            switch(contType)
            {
                case OutControllerType.X360:
                    outputDevice = new Xbox360OutDevice(client);
                    break;
                case OutControllerType.DS4:
                    outputDevice = new DS4OutDevice(client);
                    break;
                case OutControllerType.None:
                default:
                    break;
            }

            return outputDevice;
        }

        private int FindSlot()
        {
            int result = -1;
            for (int i = 0; i < outputDevices.Length && result == -1; i++)
            {
                OutputDevice tempdev = outputDevices[i];
                if (tempdev == null)
                {
                    result = i;
                }
            }

            return result;
        }

        private void LaunchEvents()
        {
            bool hasItems = false;
            Action act = null;
            lock (actionLock)
            {
                hasItems = actions.Count > 0;
            }

            while (hasItems)
            {
                lock (actionLock)
                {
                    act = actions.Dequeue();
                }

                act.Invoke();

                lock (actionLock)
                {
                    hasItems = actions.Count > 0;
                }
            }
        }

        private void PrepareEventTask()
        {
            if (!RunningQueue)
            {
                RunningQueue = true;
                Task.Run(() =>
                {
                    LaunchEvents();
                    RunningQueue = false;
                });
            }
        }

        public void DeferredPlugin(OutputDevice outputDevice, DS4 controller)
        {
            if (outputDevice is null || controller is null)
                return;

            Action tempAction = new Action(() =>
            {
                int slot = FindSlot();
                if (slot != -1)
                {
                    outputDevice.Connect();
                    outputDevices[slot] = outputDevice;
                    devictDict.Add(slot, outputDevice);
                    revDevictDict.Add(outputDevice, slot);
                    Task.Delay(DELAY_TIME).Wait();
                    controller.Output = outputDevice;
                }
            });

            lock (actionLock)
            {
                actions.Enqueue(tempAction);
            }

            PrepareEventTask();
        }

        public void DeferredRemoval(OutputDevice outputDevice, DS4 controller, bool immediate = false)
        {
            if (outputDevice is null || controller is null)
                return;
            
            Action tempAction = new Action(() =>
            {
                if (revDevictDict.ContainsKey(outputDevice))
                {
                    int slot = revDevictDict[outputDevice];
                    outputDevices[slot] = null;
                    devictDict.Remove(slot);
                    revDevictDict.Remove(outputDevice);
                    outputDevice.Disconnect();
                    controller.Output = null;
                    if (!immediate)
                    {
                        Task.Delay(DELAY_TIME).Wait();
                    }
                }
            });

            lock (actionLock)
            {
                actions.Enqueue(tempAction);
            }

            PrepareEventTask();
        }
    }
}
