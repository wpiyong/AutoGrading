using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gia405ServiceLib;

namespace DplLib.Model
{
    class Cover
    {
        const byte NR_OF_DIGITAL_INPUTS = 3;
        const byte DIG_IN0 = 0;
        const byte DIG_IN1 = 1;
        const byte DIG_IN2 = 2;

        const byte PinCoverStatus = DIG_IN0;
        const byte PinCoverReset = 1;//D02
        const byte PinCoverAuto = 2;//D03
        const byte PinCoverOpen = 4;//D05
        const byte PinCoverClose = 3;//D04

        Gia405Service _gia405Service;

        public Cover(Gia405Service service)
        {
            _gia405Service = service;
        }

        public bool Init()
        {
            if (_gia405Service.SetSpectrometerPin(PinCoverAuto, 1)//enable motor
                != Gia405ServiceLib.Gia405ServiceReference.GIAMELEE405_RESULT.SUCCESS)
                return false;
            if (_gia405Service.SetSpectrometerPin(PinCoverClose, 1)
                != Gia405ServiceLib.Gia405ServiceReference.GIAMELEE405_RESULT.SUCCESS)
                return false;
            if (_gia405Service.SetSpectrometerPin(PinCoverOpen, 1)
                != Gia405ServiceLib.Gia405ServiceReference.GIAMELEE405_RESULT.SUCCESS)
                return false;

            if (_gia405Service.SetSpectrometerPin(PinCoverReset, 1)//reset
                != Gia405ServiceLib.Gia405ServiceReference.GIAMELEE405_RESULT.SUCCESS)
                return false;

            return true;
        }

        public bool? Closed()
        {
            byte val = 0;

            if (_gia405Service.GetSpectrometerPin(PinCoverStatus, out val)
                != Gia405ServiceLib.Gia405ServiceReference.GIAMELEE405_RESULT.SUCCESS)
                return null;

            return val == 0;
        }


        public bool Open()
        {
            if (_gia405Service.SetSpectrometerPin(PinCoverOpen, 0)
                != Gia405ServiceLib.Gia405ServiceReference.GIAMELEE405_RESULT.SUCCESS)
                return false;
            System.Threading.Thread.Sleep(100);
            if (_gia405Service.SetSpectrometerPin(PinCoverOpen, 1)
                != Gia405ServiceLib.Gia405ServiceReference.GIAMELEE405_RESULT.SUCCESS)
                return false;

            return true;
        }

        public bool Close()
        {
            if (_gia405Service.SetSpectrometerPin(PinCoverClose, 0)
                != Gia405ServiceLib.Gia405ServiceReference.GIAMELEE405_RESULT.SUCCESS)
                return false;
            System.Threading.Thread.Sleep(100);
            if (_gia405Service.SetSpectrometerPin(PinCoverClose, 1)
                != Gia405ServiceLib.Gia405ServiceReference.GIAMELEE405_RESULT.SUCCESS)
                return false;

            return true;
        }
    }
}
