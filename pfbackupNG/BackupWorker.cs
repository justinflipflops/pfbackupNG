using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace pfbackupNG
{
    internal class BackupWorker : IHostedService, IDisposable
    {
        private Timer _globalTimer = null;
        private readonly GlobalConfiguration _Global;
        private readonly DeviceConfiguration _Device;
        private readonly HttpClient _HttpClient;
        private readonly HttpClientHandler _HttpClientHandler;
        private readonly BlobContainerClient _BlobContainerClient;

        private enum BackupWorkerRunState
        {
            Phase1,
            Phase2,
            Phase3,
            Phase4,
            Phase5
        }
        public BackupWorker(GlobalConfiguration Global, DeviceConfiguration Device)
        {
            if (Global == null || Device == null)
                throw new ArgumentNullException("BackupTimer Constructor");
            _Global = Global;
            _Device = Device;
            _HttpClientHandler = new HttpClientHandler();
            _HttpClientHandler.CookieContainer = new CookieContainer();
            _HttpClientHandler.UseCookies = true;
            _HttpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            _HttpClient = new HttpClient(_HttpClientHandler);
            //_HttpClient.DefaultRequestHeaders.Add("User-Agent", "pfBackupNG v1.0-ALPHACENTAURI");
            _BlobContainerClient = new BlobContainerClient(_Global.Azure.ConnectionString, _Global.Azure.Container);
            Log.Information($"Backup[{_Device.Name}] Worker Service - Created.");
        }
        private TimeSpan GetTimeLeftTillNextRun(bool ExecuteIfMissed = false)
        {
            DateTime _now = DateTime.Now;
            DateTime _nextRun = new DateTime(_now.Year, _now.Month, _now.Day, _Device.Backup.Time.Hour, _Device.Backup.Time.Minute, _Device.Backup.Time.Second, _Device.Backup.Time.Millisecond);
            if (_now > _nextRun)
                _nextRun = _nextRun.AddDays(1);
            TimeSpan _timeLeftUntilNextRun = new TimeSpan(0, 0, 0, 0);
            if (!ExecuteIfMissed)
                _timeLeftUntilNextRun = (_nextRun - _now)+ _Device.Backup.Jitter.Next();

            return _timeLeftUntilNextRun;
        }
        public Task StartAsync(CancellationToken _cancellationToken)
        {
            _globalTimer = new Timer(ExecuteAsync, _cancellationToken, GetTimeLeftTillNextRun(true), Timeout.InfiniteTimeSpan);
            Log.Information($"Backup[{_Device.Name}] Worker Service - Started.");
            return Task.CompletedTask;
        }

        private async void ExecuteAsync(object _cancellationToken)
        {
            CancellationToken _stoppingToken = (CancellationToken)_cancellationToken;
            Log.Information($"Backup[{_Device.Name}] Task - Started");
            Dictionary<string, string> _HttpClientRequestParameters = new Dictionary<string, string>();
            HttpResponseMessage _httpResponseMessage = null;
            BackupWorkerRunState _backupWorkerRunState = BackupWorkerRunState.Phase1;
            //Phase 1
            try
            {
                Log.Information($"Backup[{_Device.Name}] Phase 1 - Started.");
                Log.Debug($"Backup[{_Device.Name}] Phase 1 - Obtaining __csrf_magic.");
                _httpResponseMessage = await _HttpClient.GetAsync(_Device.GetRequestUri(), _stoppingToken);
                if (_httpResponseMessage.StatusCode == HttpStatusCode.OK)
                {
                    string _httpResponseMessageAsString = await _httpResponseMessage.Content.ReadAsStringAsync();
                    _HttpClientRequestParameters["__csrf_magic"] = GetCsrfMagic(_httpResponseMessageAsString);
                    if (_HttpClientRequestParameters["__csrf_magic"] == String.Empty)
                    {
                        Log.Debug($"Backup[{_Device.Name}] Phase 1 - __csrf_magic missing may not needed. See additional log messages.");
                        _HttpClientRequestParameters.Remove("__csrf_magic");
                    }
                    else
                        Log.Debug($"Backup[{_Device.Name}] Phase 1 - Obtained __csrf_magic.");
                    _backupWorkerRunState = BackupWorkerRunState.Phase2;
                }
                else
                    throw new HttpRequestException("Invalid HTTP Response Message Status Code", null, _httpResponseMessage.StatusCode);
            }
            catch (Exception _Phase1Exception)
            {
                _HttpClientRequestParameters.Remove("__csrf_magic");
                Log.Error(_Phase1Exception, $"Backup[{_Device.Name}] Phase 1 - Failed. Reason: See Exception.");
            }
            Log.Information($"Backup[{_Device.Name}] Phase 1 - Completed.");

            //Phase 2
            if (_backupWorkerRunState == BackupWorkerRunState.Phase2)
            {
                try
                {
                    Log.Information($"Backup[{_Device.Name}] Phase 2 - Started");
                    Log.Debug($"Backup[{_Device.Name}] Phase 2 - Attempting pfSense login.");
                    _HttpClientRequestParameters.Add("login", "Login");
                    _HttpClientRequestParameters.Add("usernamefld", _Device.Credentials.Username);
                    _HttpClientRequestParameters.Add("passwordfld", _Device.Credentials.Encrypted ? _Device.Credentials.Password.pfbackup_Decrypt(_Global.EncryptionKey) : _Device.Credentials.Password);
                    _httpResponseMessage = await _HttpClient.PostAsync(_Device.GetRequestUri(), new FormUrlEncodedContent(_HttpClientRequestParameters), _stoppingToken);
                    if (_httpResponseMessage.StatusCode == HttpStatusCode.OK)
                    {
                        string _httpResponseMessageAsString = await _httpResponseMessage.Content.ReadAsStringAsync();

                        if (_httpResponseMessageAsString.Contains("Username or Password incorrect"))
                            throw new InvalidCredentialException("Invalid pfSense login information. Please check and confirm username, password, and encryption settings.");
                        Log.Debug($"Backup[{_Device.Name}] Phase 2 - pfSense login successful.");

                        _HttpClientRequestParameters["__csrf_magic"] = GetCsrfMagic(_httpResponseMessageAsString);
                        if (_HttpClientRequestParameters["__csrf_magic"] == String.Empty)
                        {
                            _HttpClientRequestParameters.Remove("__csrf_magic");
                            Log.Debug($"Backup[{_Device.Name}] Phase 2 - __csrf_magic missing may not needed. See additional log messages.");
                        }
                        else
                            Log.Debug($"Backup[{_Device.Name}] Phase 2 - Obtained __csrf_magic.");

                        _backupWorkerRunState = BackupWorkerRunState.Phase3;
                        _HttpClientRequestParameters.Remove("login");
                        _HttpClientRequestParameters.Remove("usernamefld");
                        _HttpClientRequestParameters.Remove("passwordfld");
                    }
                    else
                        throw new HttpRequestException("Invalid HTTP Response Message Status Code", null, _httpResponseMessage.StatusCode);
                }
                catch (Exception _Phase2Exception)
                {
                    _HttpClientRequestParameters.Remove("__csrf_magic");
                    Log.Error(_Phase2Exception, $"Backup[{_Device.Name}] Phase 2 - Failed. Reason: See Exception.");
                }
                Log.Information($"Backup[{_Device.Name}] Phase 2 - Completed.");
            }
            else
                Log.Debug($"Backup[{_Device.Name}] Phase 2 - Skipped. Reason: Check log for previous errors.");

            //Phase 3
            XmlDocument _currentDeviceBackup = null;
            string _currentDeviceBackupName = String.Empty;
            if (_backupWorkerRunState == BackupWorkerRunState.Phase3)
            {
                try
                {
                    Log.Information($"Backup[{_Device.Name}] Phase 3 - Started.");
                    Log.Debug($"Backup[{_Device.Name}] Phase 3 - Attempting pfSense backup.");

                    _HttpClientRequestParameters.Add("backuparea", "");
                    switch (_Device.Version)
                    {
                        case DeviceConfiguration.DeviceConfigurationVersion.V20X_V225:
                            _HttpClientRequestParameters.Add("donotbackuprrd", "on");
                            _HttpClientRequestParameters.Add("Submit", "Download configuration");
                            break;
                        case DeviceConfiguration.DeviceConfigurationVersion.V226_V232P1:
                            _HttpClientRequestParameters.Add("donotbackuprrd", "on");
                            _HttpClientRequestParameters.Add("Submit", "Download configuration as XML");
                            break;
                        case DeviceConfiguration.DeviceConfigurationVersion.V233_LATER:
                            _HttpClientRequestParameters.Add("donotbackuprrd", "yes");
                            _HttpClientRequestParameters.Add("download", "Download configuration as XML");
                            break;
                    }
                    _HttpClientRequestParameters.Add("encrypt_password", "");
                    _HttpClientRequestParameters.Add("encrypt_passconf", "");
                    _currentDeviceBackup = null;
                    _currentDeviceBackupName = String.Empty;
                    _httpResponseMessage = await _HttpClient.PostAsync(_Device.GetRequestBackupUri(), new FormUrlEncodedContent(_HttpClientRequestParameters), _stoppingToken);
                    if (_httpResponseMessage.StatusCode == HttpStatusCode.OK)
                    {
                        string _httpResponseMessageAsString = await _httpResponseMessage.Content.ReadAsStringAsync();
                        _currentDeviceBackup = StringToXML(_httpResponseMessageAsString);
                        _currentDeviceBackupName = _httpResponseMessage.Content.Headers.ContentDisposition.FileName;

                        if (_currentDeviceBackup == null || String.IsNullOrWhiteSpace(_currentDeviceBackupName))
                            throw new XmlException("Invalid backup file type returned. Expected XML.");

                        Log.Debug($"Backup[{_Device.Name}] Phase 3 - pfSense Backup Received.");
                        _backupWorkerRunState = BackupWorkerRunState.Phase4;
                        _HttpClientRequestParameters.Remove("backuparea");
                        _HttpClientRequestParameters.Remove("donotbackuprrd");
                        _HttpClientRequestParameters.Remove("Submit");
                        _HttpClientRequestParameters.Remove("download");
                        _HttpClientRequestParameters.Remove("encrypt_password");
                        _HttpClientRequestParameters.Remove("encrypt_passconf");
                        _HttpClientRequestParameters.Remove("__csrf_magic");
                    }
                    else
                        throw new HttpRequestException("Invalid HTTP Response Message", null, _httpResponseMessage.StatusCode);
                }
                catch (Exception _Phase3Exception)
                {
                    _currentDeviceBackup = null;
                    _currentDeviceBackupName = String.Empty;
                    _HttpClientRequestParameters.Remove("__csrf_magic");
                    Log.Error(_Phase3Exception, $"Backup[{_Device.Name}] Phase 3 - Failed. Reason: See Exception.");
                }
                Log.Information($"Backup[{_Device.Name}] Phase 3 - Completed.");
            }
            else
                Log.Debug($"Backup[{_Device.Name}] Phase 3 - Skipped. Reason: Check log for previous errors.");

            //Phase 4 - Upload to Azure!!!
            Azure.Response<Azure.Storage.Blobs.Models.BlobContentInfo> _blobClientUploadResult = null;
            if (_backupWorkerRunState == BackupWorkerRunState.Phase4)
            {
                try
                {
                    Log.Information($"Backup[{_Device.Name}] Phase 4 - Started.");
                    Log.Debug($"Backup[{_Device.Name}] Phase 4 - Attempting pfSense backup upload.");
                    BlobClient _blobClient = _BlobContainerClient.GetBlobClient($"{_Device.Name}/{_currentDeviceBackupName.GetSafeString()}");
                    _blobClientUploadResult = await _blobClient.UploadAsync(new BinaryData(XMLToString(_currentDeviceBackup).ToArray()), _stoppingToken);
                    if (_blobClientUploadResult != null && _blobClientUploadResult.GetRawResponse().Status == (int)HttpStatusCode.Created)
                    {
                        Log.Debug($"Backup[{_Device.Name}] Phase 4 - pfSense Backup uploaded.");
                        _backupWorkerRunState = BackupWorkerRunState.Phase5;
                        _currentDeviceBackup = null;
                        _currentDeviceBackupName = String.Empty;
                    }
                    else
                        throw new Azure.RequestFailedException("Client Upload Result was NULL or INVALID");
                }
                catch (Exception _Phase4Exception)
                {
                    _currentDeviceBackup = null;
                    _currentDeviceBackupName = String.Empty;
                    _blobClientUploadResult = null;
                    Log.Error(_Phase4Exception, $"Backup[{_Device.Name}] Phase 4 - Failed. Reason: See Exception.");
                }
                Log.Information($"Backup[{_Device.Name}] Phase 4 - Completed.");
            }
            else
                Log.Debug($"Backup[{_Device.Name}] Phase 4 - Skipped. Reason: Invalid pfSense backup file. Check log for previous errors.");

            //Phase 5 - We Prune
            if (_backupWorkerRunState == BackupWorkerRunState.Phase5)
            {
                try
                {
                    Log.Information($"Backup[{_Device.Name}] Phase 5 - Started.");
                    Log.Debug($"Backup[{_Device.Name}] Phase 5 - Pruning pfSense backups");
                    IAsyncEnumerable<Azure.Page<BlobItem>> _blobItemPages = _BlobContainerClient.GetBlobsAsync(default, BlobStates.All, $"{_Device.Name}/", _stoppingToken).AsPages(default, default);
                    List<BlobItem> _blobItemsList = new List<BlobItem>();
                    await foreach (Azure.Page<BlobItem> _blobItemPage in _blobItemPages)
                    {
                        foreach (var _blobItem in _blobItemPage.Values)
                        {
                            _blobItemsList.Add(_blobItem);
                        }
                    }
                    if (_blobItemsList.Count > _Device.Backup.Retention)
                    {
                        _blobItemsList.Sort(delegate (BlobItem x, BlobItem y)
                        {
                            return x.Properties.LastModified.Value.CompareTo(y.Properties.LastModified.Value);
                        });
                        while (_blobItemsList.Count > _Device.Backup.Retention)
                        {
                            Azure.Response _blobDeleteResult = await _BlobContainerClient.DeleteBlobAsync(_blobItemsList[0].Name, DeleteSnapshotsOption.IncludeSnapshots, default, _stoppingToken);
                            if (_blobDeleteResult == null && _blobDeleteResult.Status != (int)HttpStatusCode.Accepted)
                            {
                                throw new Azure.RequestFailedException("Blob deleteion response was NULL or INVALID");
                            }
                            else
                                _blobItemsList.RemoveAt(0);
                        }
                        Log.Debug($"Backup[{_Device.Name}] Phase 5 - pfSense Backups pruned.");
                    }
                    else
                        Log.Debug($"Backup[{_Device.Name}] Phase 5 - Pruning pfSense Backups unnecessary.");
                }
                catch (Exception _Phase5Exception)
                {
                    _blobClientUploadResult = null;
                    Log.Error(_Phase5Exception, $"Backup[{_Device.Name}] Phase 5 - Failed. Reason: See Exception.");
                }
                Log.Information($"Backup[{_Device.Name}] Phase 5 - Completed.");
            }
            else
                Log.Debug($"Backup[{_Device.Name}] Phase 5 - Skipped. Reason: pfSense Backup Upload Failed. Check log for previous errors.");

            //restart timer
            _globalTimer?.Change(GetTimeLeftTillNextRun(false), Timeout.InfiniteTimeSpan);
        }

        public Task StopAsync(CancellationToken _cancellationToken)
        {
            _globalTimer?.Change(Timeout.Infinite, 0);
            Log.Information($"Backup[{_Device.Name}] Worker Service - Stopped");
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _globalTimer?.Dispose();
        }

        private XmlDocument StringToXML(string XmlDataAsString)
        {
            try
            {

                XmlDocument _backupXML = new XmlDocument();
                _backupXML.LoadXml(XmlDataAsString);
                if (_backupXML.DocumentElement.Name == "pfsense")
                    return _backupXML;
            }
            catch
            {
                return null;
            }
            return null;
        }
        private string XMLToString(XmlDocument XmlDataAsXmlDocument)
        {
            string _result = String.Empty;
            try
            {
                using (StringWriter _stringWriter = new StringWriter())
                using (XmlTextWriter _xmlTextWriter = new XmlTextWriter(_stringWriter))
                {
                    XmlDataAsXmlDocument.WriteTo(_xmlTextWriter);
                    _result = _stringWriter.ToString();
                }
            }
            catch
            {
                _result = String.Empty;
            }
            return _result;
        }
        private string GetCsrfMagic(string Response)
        {
            const string csrf_marker = "name='__csrf_magic' value=";
            string szResult = "";
            if (Response.Contains(csrf_marker))
            {
                try
                {
                    string szParse = Response.Substring(Response.IndexOf(csrf_marker));
                    Regex regExCsrf1 = new Regex(@"(s)(i)(d)(:)(\w*)(,)(\d*)(;)(i)(p)(:)(\w*)(,)(\d*)");
                    Regex regExCsrf2 = new Regex(@"(s)(i)(d)(:)(\w*)(,)(\d*)");
                    Match regExMatch1 = regExCsrf1.Match(szParse);
                    Match regExMatch2 = regExCsrf2.Match(szParse);
                    if (regExMatch1.Success)
                        szResult = regExMatch1.Value;
                    else if (regExMatch2.Success)
                        szResult = regExMatch2.Value;
                    else
                        szResult = String.Empty;
                }
                catch
                {

                    szResult = String.Empty;
                }
            }
            return szResult;
        }

    }
}
