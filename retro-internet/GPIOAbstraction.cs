using System.Device.Gpio;
using System.Runtime.InteropServices;

namespace retro_internet
{
    public class GPIOAbstraction
    {
        GpioController gpioController = null;

        public GpioController Controller
        {
            get
            {
                if (gpioController == null && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    gpioController = new GpioController();
                    return gpioController;
                }
                else
                {
                    return null;
                }
            }
        }
    }
}
