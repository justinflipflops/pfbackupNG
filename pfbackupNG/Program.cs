using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace pfbackupNG
{
    public class Program
    {
        private static IHost _Host = null;
        private static Configuration _Configuration = new Configuration();
        private static string global_settings_configuration_directory = string.Empty;
        private static string global_settings_configuration_full_path = string.Empty;
        private static string global_settings_configuration_name = (System.Diagnostics.Debugger.IsAttached ? "global.development.json" : "global.json");

        private static string device_settings_configuration_directory = string.Empty;
        private static string device_settings_configuration_full_path = string.Empty;
        private static string device_settings_configuration_name = (System.Diagnostics.Debugger.IsAttached ? "devices.development.json" : "devices.json");
        public static void Main(bool BuildDefaultGlobalConfig = false, bool BuildDefaultDeviceConfig = false, FileInfo GlobalSettingsConfigFile = null, FileInfo DeviceSettingsConfigFile = null, bool EncryptCredentials = false)
        {
            global_settings_configuration_directory = $"{System.IO.Path.GetDirectoryName(new System.Uri(System.Reflection.Assembly.GetExecutingAssembly().Location).LocalPath)}/";
            device_settings_configuration_directory = $"{System.IO.Path.GetDirectoryName(new System.Uri(System.Reflection.Assembly.GetExecutingAssembly().Location).LocalPath)}/";

            if (BuildDefaultGlobalConfig)
            {
                Console.WriteLine(_Configuration.BuildGlobalDefault());
                return;
            }
            if (BuildDefaultDeviceConfig)
            {
                Console.WriteLine(_Configuration.BuildDeviceDefault());
                return;
            }

            if (GlobalSettingsConfigFile?.DirectoryName != null && GlobalSettingsConfigFile?.Name != null)
            {
                global_settings_configuration_directory = $"{GlobalSettingsConfigFile?.DirectoryName}/";
                global_settings_configuration_name = GlobalSettingsConfigFile?.Name;
            }
            global_settings_configuration_full_path = $"{global_settings_configuration_directory}{global_settings_configuration_name}";

            LoggerConfiguration _LoggerConfiguration = new LoggerConfiguration().Enrich.FromLogContext()
                .WriteTo.File($"{global_settings_configuration_directory}logs/.log", retainedFileCountLimit: 14, rollOnFileSizeLimit: true, rollingInterval: RollingInterval.Day)
                .WriteTo.Console();
            if (System.Diagnostics.Debugger.IsAttached)
                _LoggerConfiguration = _LoggerConfiguration.MinimumLevel.Debug();

            Log.Logger = _LoggerConfiguration.CreateLogger();

            if (DeviceSettingsConfigFile?.DirectoryName != null && DeviceSettingsConfigFile?.Name != null)
            {
                device_settings_configuration_directory = $"{DeviceSettingsConfigFile?.DirectoryName}/";
                device_settings_configuration_name = DeviceSettingsConfigFile?.Name;
            }
            device_settings_configuration_full_path = $"{device_settings_configuration_directory}{device_settings_configuration_name}";

            try
            {
                Log.Debug($"Loading application configuration file {global_settings_configuration_full_path}");
                _Configuration = new Configuration(global_settings_configuration_full_path, device_settings_configuration_full_path);
                Log.Information($"Loaded application configuration file {global_settings_configuration_full_path}");
            }
            catch (Exception _ex)
            {
                Log.Error(_ex, $"Error loading application configuration file {global_settings_configuration_full_path}");
                return;
            }

            if (EncryptCredentials)
            {
                Log.Information($"Processing device credentials");
                foreach(DeviceConfiguration _device in _Configuration.Devices)
                {
                    if (_device.Credentials.Encrypted == false)
                    {
                        _device.Credentials.Password = _device.Credentials.Password.pfbackup_Encrypt(_Configuration.Global.EncryptionKey);
                        _device.Credentials.Encrypted = true;
                    }
                }
                Log.Information($"Saving updated device configurations");
                _Configuration.Save(false, true);
                Log.Information($"Saved updated device configurations.\r\nExiting.");
                return;
            }
            Log.Information($"Application starting.");
            _Host = CreateHostBuilder().UseSerilog().Build();
            _Host.Run();
            Log.Information($"Application shut down.");

        }
        public static IHostBuilder CreateHostBuilder()
        {
            Log.Debug($"Starting backup workers.");
            IHostBuilder _builder = Host.CreateDefaultBuilder()
            .ConfigureServices((hostContext, services) =>
            {
                foreach (DeviceConfiguration _device in _Configuration.Devices)
                {
                    if (_device.Enabled)
                    {
                        services.AddSingleton<IHostedService>(sp => new BackupWorker(_Configuration.Global, _device));
                        Log.Debug($"Added backup worker for {_device.Name}.");
                    }
                    else
                        Log.Debug($"Skipped backup worker for {_device.Name}, disabled.");
                }
            });
            Log.Debug($"Backup workers started.");
            IHostBuilder _result = null;
            Log.Debug($"Detected {OperatingSystem.Type} operating system.");
            switch (OperatingSystem.Type)
            {
                case OperatingSystemType.Linux:
                    _result = _builder.UseSystemd();
                    break;
                case OperatingSystemType.Windows:
                    _result = _builder.UseWindowsService();
                    break;
                case OperatingSystemType.macOS:
                default:
                    _result = _builder.UseConsoleLifetime();
                    break;
            }
            return _result;
        }

    }
}
