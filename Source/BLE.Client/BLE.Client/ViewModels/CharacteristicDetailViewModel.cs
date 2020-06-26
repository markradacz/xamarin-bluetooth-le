using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Acr.UserDialogs;
using MvvmCross;
using MvvmCross.Commands;
using MvvmCross.Navigation;
using MvvmCross.ViewModels;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Extensions;
using Xamarin.Forms;

namespace BLE.Client.ViewModels
{
    public class CharacteristicDetailViewModel : BaseViewModel
    {
        private readonly IUserDialogs _userDialogs;
        private bool _updatesStarted;
        private IService _service;

        public ICharacteristic Characteristic { get; private set; }
        public IReadOnlyList<IDescriptor> Descriptors { get; private set; }
        public ICharacteristic WriteCharacteristic { get; private set; }

        public static Guid SKY_HUBSERVICE_GUID = new Guid("3e520001-1368-b682-4440-d7dd234c45bc");
        public static Guid SKY_HUB_WRITE_CHAR_GUID = new Guid("3e520002-1368-b682-4440-d7dd234c45bc");
        public static Guid SKY_HUB_NOTIFY_CHAR_GUID = new Guid("3e520003-1368-b682-4440-d7dd234c45bc");

        public string CharacteristicValue => Characteristic?.Value.ToHexString().Replace("-", " ");
        public string WriteCharacteristicValue => WriteCharacteristic?.Value.ToHexString().Replace("-", " ");

        public ObservableCollection<string> Messages { get; } = new ObservableCollection<string>();

        public string UpdateButtonText => _updatesStarted ? "Stop updates" : "Start updates";

        public string WritePermissions
        {
            get
            {
                if (WriteCharacteristic == null)
                    return string.Empty;

                return (WriteCharacteristic.CanRead ? "Read " : "") +
                       (WriteCharacteristic.CanWrite ? "Write " : "") +
                       (WriteCharacteristic.CanUpdate ? "Update" : "");
            }
        }

        public string Permissions
        {
            get
            {
                if (Characteristic == null)
                    return string.Empty;

                return (Characteristic.CanRead ? "Read " : "") +
                       (Characteristic.CanWrite ? "Write " : "") +
                       (Characteristic.CanUpdate ? "Update" : "");
            }
        }

        public CharacteristicDetailViewModel(IAdapter adapter, IUserDialogs userDialogs, IMvxNavigationService navigation) : base(adapter)
        {
            _userDialogs = userDialogs;
        }

        public override async void Prepare(MvxBundle parameters)
        {
            base.Prepare(parameters);

            //Characteristic = await GetCharacteristicFromBundleAsync(parameters);

            _service = await GetServiceFromBundleAsync(parameters);

            WriteCharacteristic = await _service.GetCharacteristicAsync(SKY_HUB_WRITE_CHAR_GUID);
            Characteristic = await _service.GetCharacteristicAsync(SKY_HUB_NOTIFY_CHAR_GUID);
            Descriptors = await Characteristic.GetDescriptorsAsync();

        }

        public override void ViewAppeared()
        {
            base.ViewAppeared();

            if (Characteristic != null)
            {
                return;
            }

        }
        public override void ViewDisappeared()
        {
            base.ViewDisappeared();

            if (Characteristic != null)
            {
                StopUpdates();
            }
            
        }

        public Command ReadCommand => new Command(ReadValueAsync);

        private async void ReadValueAsync()
        {
            if (Characteristic == null)
                return;

            try
            {
                _userDialogs.ShowLoading("Reading characteristic value...");

                await Characteristic.ReadAsync();

                await RaisePropertyChanged(() => CharacteristicValue);

                Messages.Insert(0, $"Read value {CharacteristicValue}");
            }
            catch (Exception ex)
            {
                _userDialogs.HideLoading();
                await _userDialogs.AlertAsync(ex.Message);

                Messages.Insert(0, $"Error {ex.Message}");

            }
            finally
            {
                _userDialogs.HideLoading();
            }

        }

        public MvxCommand WriteCommand => new MvxCommand(WriteValueAsync);

        private async void WriteValueAsync()
        {
            var hexStr = "00 23 00 02 10 00 00 01 10 00 00 00 00 00 00 00 00 00 00 00 00 00 00 01 00 00 00 78 2D";
            try
            {
                var result =
                    await
                        _userDialogs.PromptAsync("Input a value (as hex whitespace separated)", "Write value",
                            placeholder: hexStr);

                if (!result.Ok)
                    return;

                //var data = GetBytes(result.Text);
                var data = GetBytes(hexStr);

                _userDialogs.ShowLoading("Write characteristic value");

                Device.BeginInvokeOnMainThread(async () => 
                {
                    await WriteCharacteristic.WriteAsync(data);
                });

                _userDialogs.HideLoading();

                await RaisePropertyChanged(() => WriteCharacteristicValue);
                Messages.Insert(0, $"Wrote value {WriteCharacteristicValue}");
            }
            catch (Exception ex)
            {
                _userDialogs.HideLoading();
                await _userDialogs.AlertAsync(ex.Message);
            }

        }

        private static byte[] GetBytes(string text)
        {
            return text.Split(' ').Where(token => !string.IsNullOrEmpty(token)).Select(token => Convert.ToByte(token, 16)).ToArray();
        }

        public MvxCommand ToggleUpdatesCommand => new MvxCommand((() =>
        {
            if (_updatesStarted)
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await StopUpdates();
                });
            }
            else
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await StartUpdates();
                });
            }
        }));

        private async Task StartUpdates()
        {
            try
            {
                _updatesStarted = true;

                Characteristic.ValueUpdated -= CharacteristicOnValueUpdated;
                Characteristic.ValueUpdated += CharacteristicOnValueUpdated;
                await Characteristic.StartUpdatesAsync();
         

                Messages.Insert(0, $"Start updates");

                Descriptors = await Characteristic.GetDescriptorsAsync();

                await RaisePropertyChanged(() => UpdateButtonText);

            }
            catch (Exception ex)
            {
                await _userDialogs.AlertAsync(ex.Message);
            }
        }

        private async Task StopUpdates()
        {
            try
            {
                _updatesStarted = false;

                await Characteristic.StopUpdatesAsync();
                Characteristic.ValueUpdated -= CharacteristicOnValueUpdated;

                Messages.Insert(0, $"Stop updates");

                await RaisePropertyChanged(() => UpdateButtonText);

            }
            catch (Exception ex)
            {
                await _userDialogs.AlertAsync(ex.Message);
            }
        }

        private void CharacteristicOnValueUpdated(object sender, CharacteristicUpdatedEventArgs characteristicUpdatedEventArgs)
        {
            Messages.Insert(0, $"{DateTime.Now.TimeOfDay} - Updated: {CharacteristicValue}");
            RaisePropertyChanged(() => CharacteristicValue);
        }
    }
}