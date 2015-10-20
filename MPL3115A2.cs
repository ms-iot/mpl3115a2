//-----------------------------------------------------------------------
// <copyright file="MPL3115A2.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
namespace Microsoft.Maker.Devices.I2C.MPL3115A2Device
{
    using System;
    using System.Threading.Tasks;
    using Windows.Devices.Enumeration;
    using Windows.Devices.Gpio;
    using Windows.Devices.I2c;
    using Windows.Foundation;

    /// <summary>
    /// MPL3115A2 precision altimeter IC
    /// http://cache.freescale.com/files/sensors/doc/data_sheet/MPL3115A2.pdf
    /// </summary>
    public sealed class MPL3115A2
    {
        /// <summary>
        /// Device I2C Address
        /// </summary>
        private const ushort Mpl3115a2I2cAddress = 0x0060;

        /// <summary>
        /// Control registers
        /// </summary>
        private const byte ControlRegister1 = 0x26;

        /// <summary>
        /// Control registers
        /// </summary>
        private const byte PressureDataOutMSB = 0x01;

        /// <summary>
        /// Control Flags
        /// </summary>
        private bool enable = false;

        /// <summary>
        /// I2C Device
        /// </summary>
        private I2cDevice i2c;

        /// <summary>
        /// Gets the altitude data
        /// </summary>
        /// <returns>
        /// Calculates the altitude in meters (m) using the US Standard Atmosphere 1976 (NASA) formula
        /// </returns>
        public float Altitude
        {
            get
            {
                if (!this.enable)
                {
                    return 0f;
                }

                double pressure_Pa = this.Pressure;

                // Calculate using US Standard Atmosphere 1976 (NASA)
                double altitude_m = 44330.77 * (1 - Math.Pow(pressure_Pa / 101326, 0.1902632));

                return Convert.ToSingle(altitude_m);
            }
        }

        /// <summary>
        /// Gets pressure data
        /// </summary>
        /// <returns>
        /// The pressure in Pascals (Pa)
        /// </returns>
        public float Pressure
        {
            get
            {
                if (!this.enable)
                {
                    return 0f;
                }

                uint raw_pressure_data = this.RawPressure;
                double pressure_Pa = (raw_pressure_data >> 6) + (((raw_pressure_data >> 4) & 0x03) / 4.0);

                return Convert.ToSingle(pressure_Pa);
            }
        }

        /// <summary>
        /// Gets the raw pressure value from the IC.
        /// </summary>
        private uint RawPressure
        {
            get
            {
                uint pressure = 0;
                byte[] reg_data = new byte[1];
                byte[] raw_pressure_data = new byte[3];

                // Request pressure data from the MPL3115A2
                // MPL3115A2 datasheet: http://dlnmh9ip6v2uc.cloudfront.net/datasheets/Sensors/Pressure/MPL3115A2.pdf
                //
                // Update Control Register 1 Flags
                // - Read data at CTRL_REG1 (0x26) on the MPL3115A2
                // - Update the SBYB (bit 0) and OST (bit 1) flags to STANDBY and initiate measurement, respectively.
                // -- SBYB flag (bit 0)
                // --- off = Part is in STANDBY mode
                // --- on = Part is ACTIVE
                // -- OST flag (bit 1)
                // --- off = auto-clear
                // --- on = initiate measurement
                // - Write the resulting value back to Control Register 1
                this.i2c.WriteRead(new byte[] { MPL3115A2.ControlRegister1 }, reg_data);
                reg_data[0] &= 0xFE;  // ensure SBYB (bit 0) is set to STANDBY
                reg_data[0] |= 0x02;  // ensure OST (bit 1) is set to initiate measurement
                this.i2c.Write(new byte[] { MPL3115A2.ControlRegister1, reg_data[0] });

                // Wait 10ms to allow MPL3115A2 to process the pressure value
                Task.Delay(10);

                // Write the address of the register of the most significant byte for the pressure value, OUT_P_MSB (0x01)
                // Read the three bytes returned by the MPL3115A2
                // - byte 0 - MSB of the pressure
                // - byte 1 - CSB of the pressure
                // - byte 2 - LSB of the pressure
                this.i2c.WriteRead(new byte[] { MPL3115A2.PressureDataOutMSB }, raw_pressure_data);

                // Reconstruct the result using all three bytes returned from the device
                pressure = (uint)(raw_pressure_data[0] << 16);
                pressure |= (uint)(raw_pressure_data[1] << 8);
                pressure |= raw_pressure_data[2];

                return pressure;
            }
        }

        /// <summary>
        /// Initialize the altimeter device.
        /// </summary>
        /// <returns>
        /// Async operation object.
        /// </returns>
        public IAsyncOperation<bool> BeginAsync()
        {
            return this.BeginAsyncHelper().AsAsyncOperation<bool>();
        }

        /// <summary>
        /// Private helper to initialize the altimeter device.
        /// </summary>
        /// <remarks>
        /// Setup and instantiate the I2C device object for the MPL3115A2.
        /// </remarks>
        /// <returns>
        /// Task object.
        /// </returns>
        private async Task<bool> BeginAsyncHelper()
        {
            // Acquire the GPIO controller
            // MSDN GPIO Reference: https://msdn.microsoft.com/en-us/library/windows/apps/windows.devices.gpio.aspx
            //
            // Get the default GpioController
            GpioController gpio = GpioController.GetDefault();

            // Test to see if the GPIO controller is available.
            //
            // If the GPIO controller is not available, this is
            // a good indicator the app has been deployed to a
            // computing environment that is not capable of
            // controlling the weather shield. Therefore we
            // will disable the weather shield functionality to
            // handle the failure case gracefully. This allows
            // the invoking application to remain deployable
            // across the Universal Windows Platform.
            if (null == gpio)
            {
                this.enable = false;
                return this.enable;
            }

            // Acquire the I2C device
            // MSDN I2C Reference: https://msdn.microsoft.com/en-us/library/windows/apps/windows.devices.i2c.aspx
            //
            // Use the I2cDevice device selector to create an advanced query syntax string
            // Use the Windows.Devices.Enumeration.DeviceInformation class to create a collection using the advanced query syntax string
            // Take the device id of the first device in the collection
            string advanced_query_syntax = I2cDevice.GetDeviceSelector("I2C1");
            DeviceInformationCollection device_information_collection = await DeviceInformation.FindAllAsync(advanced_query_syntax);
            string deviceId = device_information_collection[0].Id;

            // Establish an I2C connection to the MPL3115A2
            //
            // Instantiate the I2cConnectionSettings using the device address of the MPL3115A2
            // - Set the I2C bus speed of connection to fast mode
            // - Set the I2C sharing mode of the connection to shared
            //
            // Instantiate the the MPL3115A2 I2C device using the device id and the I2cConnectionSettings
            I2cConnectionSettings mpl3115a2_connection = new I2cConnectionSettings(Mpl3115a2I2cAddress);
            mpl3115a2_connection.BusSpeed = I2cBusSpeed.FastMode;
            mpl3115a2_connection.SharingMode = I2cSharingMode.Shared;

            this.i2c = await I2cDevice.FromIdAsync(deviceId, mpl3115a2_connection);

            // Test to see if the I2C devices are available.
            //
            // If the I2C devices are not available, this is
            // a good indicator the weather shield is either
            // missing or configured incorrectly. Therefore we
            // will disable the weather shield functionality to
            // handle the failure case gracefully. This allows
            // the invoking application to remain deployable
            // across the Universal Windows Platform.
            //
            // NOTE: For a more detailed description of the I2C
            // transactions used for testing below, please
            // refer to the "Raw___" functions provided below.
            if (null == this.i2c)
            {
                this.enable = false;
                return this.enable;
            }
            else
            {
                byte[] reg_data = new byte[1];

                try
                {
                    this.i2c.WriteRead(new byte[] { MPL3115A2.ControlRegister1 }, reg_data);

                    // ensure SBYB (bit 0) is set to STANDBY
                    reg_data[0] &= 0xFE;

                    // ensure OST (bit 1) is set to initiate measurement
                    reg_data[0] |= 0x02;
                    this.i2c.Write(new byte[] { MPL3115A2.ControlRegister1, reg_data[0] });
                }
                catch
                {
                    this.enable = false;
                    return this.enable;
                }
            }

            this.enable = true;

            return this.enable;
        }
    }
}
