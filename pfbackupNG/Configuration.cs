using System;
using Newtonsoft.Json;
using System.Globalization;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json.Converters;
using System.Net;
using System.Security;
using System.Security.Cryptography;

namespace pfbackupNG
{
    public class Configuration
    {

        GlobalConfiguration _global = new GlobalConfiguration();
        private string _global_path = String.Empty;
        DeviceConfiguration[] _device = new DeviceConfiguration[0];
        private string _device_path = String.Empty;
        public GlobalConfiguration Global { get { return _global; } set { _global = value; } }
        public DeviceConfiguration[] Devices { get { return _device; } set { _device = value; } }
        public Configuration()
        {
            _global = new GlobalConfiguration();
            _device = new DeviceConfiguration[0];
        }
        public Configuration(string GlobalPath, string DevicePath)
        {
            try
            {
                if (File.Exists(GlobalPath))
                {
                    try
                    {
                        _global = JsonConvert.DeserializeObject<GlobalConfiguration>(File.ReadAllText(GlobalPath));
                        _global_path = GlobalPath;
                    }
                    catch (Exception _ex)
                    {
                        throw new FileLoadException("Failed to load Global configuration, see inner exception for details.", _ex);
                    }
                }
                else
                    throw new FileNotFoundException("Global configuration path does not exist.");

                if (File.Exists(DevicePath))
                {
                    try
                    {
                        _device = JsonConvert.DeserializeObject<DeviceConfiguration[]>(File.ReadAllText(DevicePath));
                        _device_path = DevicePath;
                    }
                    catch (Exception _ex)
                    {
                        throw new FileLoadException("Failed to load Device configuration, see inner exception for details.", _ex);
                    }
                }
                else
                    throw new FileNotFoundException("Device configuration path does not exist.");
            }
            catch (Exception _ex) { throw new Exception("Failed to load configuration, see inner exception for details.", _ex); }
        }

        public void Save(bool SaveGlobal = false, bool SaveDevices = false)
        {
            try
            {
                if (SaveGlobal)
                {
                    if (File.Exists(_global_path))
                    {
                        try
                        {
                            File.WriteAllText(_global_path, JsonConvert.SerializeObject(_global, Formatting.Indented));
                        }
                        catch (Exception _ex)
                        {
                            throw new FileLoadException("Failed to save Global configuration, see inner exception for details.", _ex);
                        }
                    }
                    else
                        throw new FileNotFoundException("Global configuration path does not exist.");
                }
                if (SaveDevices)
                {
                    if (File.Exists(_device_path))
                    {
                        try
                        {
                            File.WriteAllText(_device_path, JsonConvert.SerializeObject(_device, Formatting.Indented));
                        }
                        catch (Exception _ex)
                        {
                            throw new FileLoadException("Failed to save Device configuration, see inner exception for details.", _ex);
                        }
                    }
                    else
                        throw new FileNotFoundException("Device configuration path does not exist.");
                }
            }
            catch (Exception _ex) { throw new Exception("Failed to save configuration, see inner exception for details.", _ex); }
        }

        public string BuildGlobalDefault()
        {
            Global.EncryptionKey = $"ChangeThisToSomethingMoreSecure";
            Global.Azure.ConnectionString = $"AzureBlobStorageConnectionString";
            Global.Azure.Container = $"AzureBlobStorageContainer";
            try { return JsonConvert.SerializeObject(Global, Formatting.Indented); }
            catch { return null; }
        }
        public string BuildDeviceDefault()
        {
            Devices = new DeviceConfiguration[] {
                new DeviceConfiguration() {
                    Name = $"Example 1",
                    Address = $"0.0.0.0",
                    Port = 80,
                    UseSSL = false,
                    Credentials = new DeviceConfigurationCredentials("Username 1","Password 1"),
                    Version = DeviceConfiguration.DeviceConfigurationVersion.V233_LATER,
                    Backup = new DeviceConfigurationBackup()
                    {
                        Time = new DateTime(DateTime.Now.Year,DateTime.Now.Month,DateTime.Now.Day,18,0,0,0),
                        Retention = 30,
                        Jitter = new PollInterval(new TimeSpan(0,0,30,0)) 
                    }
                },
                new DeviceConfiguration() {
                    Name = $"Example 2",
                    Address = $"0.0.0.0",
                    Port = 443,
                    UseSSL = true,
                    Credentials = new DeviceConfigurationCredentials("Username 2","Password 2"),
                    Version = DeviceConfiguration.DeviceConfigurationVersion.V226_V232P1,
                    Backup = new DeviceConfigurationBackup()
                    {
                        Time = new DateTime(DateTime.Now.Year,DateTime.Now.Month,DateTime.Now.Day,5,0,0,0),
                        Retention = 30,
                        Jitter = new PollInterval(new TimeSpan(0,1,0,0))
                    }
                }
            };
            try { return JsonConvert.SerializeObject(Devices, Formatting.Indented); }
            catch { return null; }
        }
    }
    public class GlobalConfiguration
    {
        public string EncryptionKey { get; set; }
        public AzureConfiguration Azure { get; set; }
        public GlobalConfiguration()
        {
            EncryptionKey = String.Empty;
            Azure = new AzureConfiguration();
        }
    }

    public class AzureConfiguration
    {
        public string ConnectionString { get; set; }
        public string Container { get; set; }
        public AzureConfiguration()
        {
            ConnectionString = String.Empty;
            Container = String.Empty;
        }
    }
    public class DeviceConfigurationCredentials
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public bool Encrypted { get; set; }
        public DeviceConfigurationCredentials()
        {
            Username = String.Empty;
            Password = String.Empty;
            Encrypted = false;
        }
        public DeviceConfigurationCredentials(string Username, string Password) : this(Username,Password,false)
        {
            return;
        }
        public DeviceConfigurationCredentials(string Username, string Password, bool Encrypted)
        {
            if (String.IsNullOrWhiteSpace(Username))
                throw new ArgumentNullException("Username cannot be null, empty, or whitespace.");
            if (String.IsNullOrEmpty(Password))
                throw new ArgumentNullException("Password cannot be null or empty.");
            this.Username = Username;
            this.Password = Password;
            this.Encrypted = Encrypted;
        }
    }
    public class DeviceConfigurationBackup
    {
        [JsonConverter(typeof(BackupDateTimeConverter))]
        public DateTime Time { get; set; }
        public PollInterval Jitter { get; set; }
        public int Retention { get; set; }
        public DeviceConfigurationBackup()
        {
            Time = new DateTime();
            Jitter = new PollInterval();
            Retention = 0;
        }
    }
    public class DeviceConfiguration
    {
        public enum DeviceConfigurationVersion
        {
            V20X_V225, //0
            V226_V232P1, //1
            V233_LATER //2
        }
        public bool Enabled { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public int Port { get; set; }
        public bool UseSSL { get; set; }
        
        public DeviceConfigurationCredentials Credentials { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public DeviceConfigurationVersion Version { get; set; }

        public DeviceConfigurationBackup Backup { get; set; }

        public DeviceConfiguration()
        {
            Enabled = false;
            Name = string.Empty;
            Address = string.Empty;
            Credentials = new DeviceConfigurationCredentials();
            Backup = new DeviceConfigurationBackup();
        }
        public Uri GetRequestUri()
        {
            StringBuilder _builder = new StringBuilder();
            _ = UseSSL == true ? _builder.Append($"https") :  _builder.Append($"http");
            _builder.Append($"://");
            _builder.Append($"{Address}:{Port}");
            _builder.Append("/diag_backup.php");
            return new Uri(_builder.ToString());
        }
    }
    public class BackupDateTimeConverter : JsonConverter
    {
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.Value == null)
            {
                return null;
            }

            var s = reader.Value.ToString();
            DateTime result;
            if (DateTime.TryParseExact(s, "hh:mm:ss tt", CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            {
                return result;
            }

            return DateTime.Now;
        }
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(((DateTime)value).ToString("hh:mm:ss tt"));
        }
        public override bool CanConvert(Type objectType)
        {
            if (objectType == typeof(DateTime))
                return true;
            else
                return false;
        }
    }
}