using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace gUVLib.Model.LightControl
{
    static class LightControl
    {
        static class MightexUVDriver
        {
            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            struct LEDChannelData
            {
                public int Normal_CurrentMax;
                public int Normal_CurrentSet;
                public int Strobe_CurrentMax;
                public int Strobe_RepeatCnt;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
                public int[] Strobe_Profile;
                public int Trigger_CurrentMax;
                public int Trigger_Polarity;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
                public int[] Trigger_Profile;
            }


            #region Mightex interop functions for accessing Mightex_LEDDriver_SDK.dll file

            //Call this function first, this function communicates with device driver to reserve resources
            //When the system uses NTFS use WINNT, for FAT32 use WINDOWS

            [DllImport("Mightex_LEDDriver_SDK.dll", EntryPoint = "MTUSB_LEDDriverInitDevices",
                CallingConvention = CallingConvention.Cdecl)]
            public static extern int MTUSBLEDDriverInitDevices();

            [DllImport("Mightex_LEDDriver_SDK.dll", EntryPoint = "MTUSB_LEDDriverOpenDevice",
                CallingConvention = CallingConvention.Cdecl)]
            public static extern uint MTUSBLEDDriverOpenDevice(int deviceID);

            [DllImport("Mightex_LEDDriver_SDK.dll", EntryPoint = "MTUSB_LEDDriverCloseDevice",
                CallingConvention = CallingConvention.Cdecl)]
            public static extern uint MTUSBLEDDriverCloseDevice(uint devHandle);

            [DllImport("Mightex_LEDDriver_SDK.dll", EntryPoint = "MTUSB_LEDDriverSerialNumber",
                CallingConvention = CallingConvention.Cdecl)]
            public static extern int MTUSBLEDDriverSerialNumber(uint devHandle, StringBuilder moduleNo, uint size);

            [DllImport("Mightex_LEDDriver_SDK.dll", EntryPoint = "MTUSB_LEDDriverSetMode",
                CallingConvention = CallingConvention.Cdecl)]
            public static extern int MTUSBLEDDriverSetMode(uint devHandle, uint channel, uint mode);

            [DllImport("Mightex_LEDDriver_SDK.dll", EntryPoint = "MTUSB_LEDDriverSetNormalPara",
                CallingConvention = CallingConvention.Cdecl)]
            private static extern int MTUSBLEDDriverSetNormalPara(uint devHandle, uint channel, ref LEDChannelData channelData);

            [DllImport("Mightex_LEDDriver_SDK.dll", EntryPoint = "MTUSB_LEDDriverGetCurrentPara",
                CallingConvention = CallingConvention.Cdecl)]
            private static extern int MTUSBLEDDriverGetCurrentPara(uint DevHandle, uint channel, ref LEDChannelData channelData, ref int moderef);


            #endregion


            public static int SetNormalPara(uint _devHandle, uint channel, int currentMax, int currentSet)
            {
                int res = -1;
                LEDChannelData ledChannelData;
                ledChannelData.Normal_CurrentMax = currentMax;
                ledChannelData.Normal_CurrentSet = currentSet;
                ledChannelData.Strobe_CurrentMax = 0;
                ledChannelData.Strobe_RepeatCnt = 0;
                ledChannelData.Trigger_CurrentMax = 0;
                ledChannelData.Trigger_Polarity = 0;
                ledChannelData.Strobe_Profile = new int[256];
                ledChannelData.Trigger_Profile = new int[256];

                try
                {
                    res = MTUSBLEDDriverSetNormalPara(_devHandle, (uint)channel, ref ledChannelData);
                }
                catch
                {
                    res = -1;
                }
                finally
                {
                }

                return res;
            }

            public static int GetCurrentPara(uint _devHandle, uint channel, out int mode, out int currentSet, out int currentMax)
            {
                int res = -1;
                currentSet = currentMax = mode = -1;
                LEDChannelData ledChannelData;
                ledChannelData.Normal_CurrentMax = 0;
                ledChannelData.Normal_CurrentSet = 0;
                ledChannelData.Strobe_CurrentMax = 0;
                ledChannelData.Strobe_RepeatCnt = 0;
                ledChannelData.Trigger_CurrentMax = 0;
                ledChannelData.Trigger_Polarity = 0;
                ledChannelData.Strobe_Profile = new int[256];
                ledChannelData.Trigger_Profile = new int[256];

                try
                {
                    int rMode = 0;
                    res = MTUSBLEDDriverGetCurrentPara(_devHandle, channel, ref ledChannelData, ref rMode);
                    if (res != -1)
                    {
                        currentSet = ledChannelData.Normal_CurrentSet;
                        currentMax = ledChannelData.Normal_CurrentMax;
                        mode = rMode;
                    }
                }
                catch
                {
                    res = -1;
                }
                finally
                {
                }

                return res;
            }
        }


        static int _mightexHandle = -1;
        const uint FLUORESCENCE_LED_CHANNEL_NUM = 1;
        const uint MAIN_LED_CHANNEL_NUM = 2;
        public const uint FLUORESCENCE_MAX_CURRENT = 1000;
        public const uint MAINLIGHT_MAX_CURRENT = 1000;

        enum WORK_MODE { DISABLE = 0, NORMAL, STROBE, TRIGGER };

        public static bool LightControlInit()
        {
            bool result = true;
            int res = -1;
            try
            {
                //initialize mightex and setup current values if first time
                if (_mightexHandle < 0)
                {
                    res = MightexUVDriver.MTUSBLEDDriverInitDevices();
                    if (res <= 0)
                        throw new Exception("Could not find Mightex controller");
                    if (res > 1)
                        throw new Exception("More then one Mightex controller connected");

                    _mightexHandle = (int)MightexUVDriver.MTUSBLEDDriverOpenDevice(0);//first device
                    //verify handle by trying to get serial number
                    StringBuilder rtnStr = new StringBuilder(32);
                    if (MightexUVDriver.MTUSBLEDDriverSerialNumber((uint)_mightexHandle, rtnStr, 32) < 0)
                        throw new Exception("Could not get serial number for Mightex driver");

                    //keep both channels disabled
                    MightexUVDriver.MTUSBLEDDriverSetMode((uint)_mightexHandle,
                                FLUORESCENCE_LED_CHANNEL_NUM, (uint)WORK_MODE.DISABLE);
                    MightexUVDriver.MTUSBLEDDriverSetMode((uint)_mightexHandle,
                                    MAIN_LED_CHANNEL_NUM, (uint)WORK_MODE.DISABLE);
                }
            }
            catch (Exception ex)
            {
                if (_mightexHandle >= 0)
                {
                    MightexUVDriver.MTUSBLEDDriverSetMode((uint)_mightexHandle,
                                FLUORESCENCE_LED_CHANNEL_NUM, (uint)WORK_MODE.DISABLE);
                    MightexUVDriver.MTUSBLEDDriverSetMode((uint)_mightexHandle,
                                    MAIN_LED_CHANNEL_NUM, (uint)WORK_MODE.DISABLE);
                    MightexUVDriver.MTUSBLEDDriverCloseDevice((uint)_mightexHandle);
                    _mightexHandle = -1;
                }

                MessageBox.Show(ex.Message, "Failed to init UV light");
                result = false;
            }
            return result;
        }

        public static bool SetMainLightCurrent(uint current)
        {
            bool result = true;
            int res = -1;
            try
            {
                if (current > 0)
                {
                    res = MightexUVDriver.SetNormalPara((uint)_mightexHandle, MAIN_LED_CHANNEL_NUM,
                            (int)MAINLIGHT_MAX_CURRENT, (int)current);
                    if (res != -1 && res != 1)
                        res = MightexUVDriver.MTUSBLEDDriverSetMode((uint)_mightexHandle,
                                    MAIN_LED_CHANNEL_NUM, (uint)WORK_MODE.NORMAL);
                }
                else
                {
                    res = MightexUVDriver.MTUSBLEDDriverSetMode((uint)_mightexHandle,
                                    MAIN_LED_CHANNEL_NUM, (uint)WORK_MODE.DISABLE);
                }

                if (res == -1 || res == 1)
                    throw new Exception("Failed to set Mightex parameters");

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Failed to set UV channel 1");
                result = false;
            }
            return result;
        }

        public static bool SetFluorescenceLightCurrent(uint current)
        {
            bool result = true;
            int res = -1;
            try
            {
                if (current > 0)
                {
                    res = MightexUVDriver.SetNormalPara((uint)_mightexHandle, FLUORESCENCE_LED_CHANNEL_NUM,
                            (int)FLUORESCENCE_MAX_CURRENT, (int)current);
                    if (res != -1 && res != 1)
                        res = MightexUVDriver.MTUSBLEDDriverSetMode((uint)_mightexHandle,
                                    FLUORESCENCE_LED_CHANNEL_NUM, (uint)WORK_MODE.NORMAL);
                }
                else
                {
                    res = MightexUVDriver.MTUSBLEDDriverSetMode((uint)_mightexHandle,
                                    FLUORESCENCE_LED_CHANNEL_NUM, (uint)WORK_MODE.DISABLE);
                }

                if (res == -1 || res == 1)
                    throw new Exception("Failed to set Mightex parameters");

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Failed to set UV channel 2");
                result = false;
            }
            return result;
        }


        public static void Close()
        {
            if (_mightexHandle >= 0)
            {
                MightexUVDriver.MTUSBLEDDriverSetMode((uint)_mightexHandle,
                                FLUORESCENCE_LED_CHANNEL_NUM, (uint)WORK_MODE.DISABLE);
                MightexUVDriver.MTUSBLEDDriverSetMode((uint)_mightexHandle,
                                MAIN_LED_CHANNEL_NUM, (uint)WORK_MODE.DISABLE);
                MightexUVDriver.MTUSBLEDDriverCloseDevice((uint)_mightexHandle);
                _mightexHandle = -1;
            }
        }
    }
}
