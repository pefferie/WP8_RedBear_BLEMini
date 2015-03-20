using Microsoft.Phone.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

//tested on the Nokia Lumia 920 and the Lumia 521 both running the Windows Phone 8.1 Cyan update
//RedBear BLE Mini board was connected directly to a CP2102 USB UART and communication was tested using CoolTerm http://freeware.the-meiers.org/
//Try http://www.themethodology.net for more information

// tested on Lumia 1520 and RedBear BLE Shield

namespace WPRedBearBLE2
{
    public partial class MainPage : PhoneApplicationPage
    {
        DeviceInformationCollection bleDevices;
        GattDeviceService selectedService;
        BluetoothLEDevice _device;
        IReadOnlyList<GattDeviceService> _services;
        GattDeviceService _service;
        GattCharacteristic _characteristicTX;
        GattCharacteristic _characteristicRX;

        // Constructor
        public MainPage()
        {
            InitializeComponent();

            RedBearConnect();

            btnSendMessage.Click += btnSendMessage_Click;
        }

        async void btnSendMessage_Click(object sender, RoutedEventArgs e)
        {
            await SendMessage(txtMessage.Text);
            txtMessage.Text = String.Empty;
        }

        async void RedBearConnect()
        {
            if (await RefreshDeviceList() == 0)
                return;

            if (await ConnectToRedBear() == false)
                return;

            if (FindService() == false)
                return;

            if (await FindCharacteristic() == false)
                return;

            txtMessage.IsEnabled = true;
            btnSendMessage.IsEnabled = true;
        }

        async Task<int> RefreshDeviceList()
        {
            try
            {
                bleDevices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(GattDeviceService.GetDeviceSelectorFromUuid(GattServiceUuids.GenericAccess));

                OutputMessage("Found " + bleDevices.Count + " device(s)");

                if (bleDevices.Count == 0)
                {
                    OutputMessage("No BLE Devices found - make sure you've paired your device");
                    await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings-bluetooth:", UriKind.RelativeOrAbsolute));
                }
            }
            catch (Exception ex)
            {
                OutputMessage("Failed to find BLE devices: " + ex.Message);
            }

            return bleDevices.Count;
        }

        async Task<bool> ConnectToRedBear()
        {
            try
            {
                for (int i = 0; i < bleDevices.Count; i++)
                {
                    if (bleDevices[i].Name == "Biscuit" || bleDevices[i].Name == "BLE Mini" || bleDevices[i].Name == "BlendMicro" || bleDevices[i].Name == "BLE Shield")
                    {
                        _device = await BluetoothLEDevice.FromIdAsync(bleDevices[i].Id);
                        _services = _device.GattServices;
                        OutputMessage("Found Device: " + _device.Name);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                OutputMessage("Connection failed: " + ex.Message);
                return false;
            }

            OutputMessage("Unable to find device Biscuit - has it been paired?");

            return true;
        }

        bool FindService()
        {
            foreach (GattDeviceService s in _services)
            {
                if (s.Uuid == new Guid("713d0000-503e-4c75-ba94-3148f18d941e"))
                {
                    _service = s;
                    OutputMessage("Found Service: " + s.Uuid);
                    return true;
                }

            }
            OutputMessage("Unable to find Biscuit Service 713d0000");
            return false;
        }

        async Task<bool> FindCharacteristic()
        {
            bool result = false;
            foreach (var c in _service.GetCharacteristics(new Guid("713d0003-503e-4c75-ba94-3148f18d941e")))
            {
                //"unauthorized access" without proper permissions
                _characteristicTX = c;

                OutputMessage("Found characteristic: " + c.Uuid);
                result = true;
                break;
            }

            foreach (var c in _service.GetCharacteristics(new Guid("713d0002-503e-4c75-ba94-3148f18d941e")))
            {
                //"unauthorized access" without proper permissions
                _characteristicRX = c;
                foreach (var i in c.GetDescriptors(new Guid("00002902-0000-1000-8000-00805f9b34fb")))
                {
                    try
                    {
                        _characteristicRX.ValueChanged += OnDataReceived;
                        var newDesc = GattClientCharacteristicConfigurationDescriptorValue.Notify;
                        await _characteristicRX.WriteClientCharacteristicConfigurationDescriptorAsync(newDesc);
                    }
                    catch (Exception ex)
                    {
                        OutputMessage("Exception: " + ex);
                    }
                }

                OutputMessage("Found characteristic: " + c.Uuid);
                result = true;
                break;
            }


            if (!result)
            {
                OutputMessage("Could not find characteristic or permissions are incorrrect");
            }

            return result;
        }

        async Task<bool> SendMessage(string msg)
        {
            DataWriter data = new DataWriter();
            data.WriteString(msg);

            var buffer = data.DetachBuffer();

            try
            {
                var result = await _characteristicTX.WriteValueAsync(buffer, GattWriteOption.WriteWithoutResponse);
                Debug.Assert(result == GattCommunicationStatus.Success);
                OutputMessage("Sent message: " + msg);
            }
            catch (Exception ex)
            {
                OutputMessage("Unable to send message: " + ex.Message);
                return false;
            }
            return true;
        }

        private void OnDataReceived(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var bytes = args.CharacteristicValue.ToArray();

            Dispatcher.BeginInvoke(() =>
                {
                    OutputMessage("r: " + FormatMessage(bytes));
                });
        }

        private static string FormatMessage(byte[] bytes)
        {
            var builder = new StringBuilder();

            var allAScii = bytes.All(c => IsAscii(c));

            if (allAScii)
            {
                return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
            }
                // The BLEControllerSketch echoes back a one-letter cmmand
                // followed by a byte array of raw data. We want 
                // to special-case this situation
            else if (IsAscii(bytes[0]))
            {
                builder.Append((char)bytes[0]);
                builder.Append("(0x" + bytes[0].ToString("x") + ")");

                var result = String.Join(", ", new string[] { builder.ToString() }.Concat(bytes.Skip(1).Select(i => "0x" + i)));

                return result;
            }
            else
            {
                return String.Join(", ", bytes.Select(i => "0x" + i));
            }
        }

        private static bool IsAscii(byte c)
        {
            return c >= 0x20 && c <= 0x7F;
        }

        void OutputMessage(string msg)
        {
            Debug.WriteLine(msg);
            txtOutput.Text += msg + "\r\n";
        }
    }
}