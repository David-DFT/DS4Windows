using System;

namespace DS4Windows
{
    interface ITouchpadBehaviour
    {
        void TouchesBegan(DS4Touchpad sender, TouchpadEventArgs arg);
        void TouchesMoved(DS4Touchpad sender, TouchpadEventArgs arg);
        void TouchButtonUp(DS4Touchpad sender, TouchpadEventArgs arg);
        void TouchButtonDown(DS4Touchpad sender, TouchpadEventArgs arg);
        void TouchesEnded(DS4Touchpad sender, TouchpadEventArgs arg);
        void SixAxisMoved(DS4SixAxis sender, SixAxisEventArgs unused);
        void TouchUnchanged(DS4Touchpad sender, EventArgs unused);
    }
}
