using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using System.Net;
using System.Text.RegularExpressions;
using System.Linq.Expressions;
using System.Security.Authentication;
using System.Xml;
using System.Security.Cryptography;
using System.Diagnostics.Eventing.Reader;
using Azure.Storage.Blobs.Models;
using System.Collections;
using System.IO;

namespace pfbackupNG
{
    public class BackupWorker : BackgroundService
    {
        //private readonly ILogger<BackupWorker> _logger;
        private readonly GlobalConfiguration _Global;
        private readonly DeviceConfiguration _Device;
        private readonly HttpClient _HttpClient;
        private readonly HttpClientHandler _HttpClientHandler;
        private readonly BlobContainerClient _BlobContainerClient;
        
        public BackupWorker(GlobalConfiguration Global, DeviceConfiguration Device)
        {
            if (Global == null || Device == null)
                throw new ArgumentNullException("BackupWorker Constructor");
            _Global = Global;
            _Device = Device;
            _HttpClientHandler = new HttpClientHandler();
            _HttpClientHandler.CookieContainer = new CookieContainer();
            _HttpClientHandler.UseCookies = true;
            _HttpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            _HttpClient = new HttpClient(_HttpClientHandler);
            _HttpClient.DefaultRequestHeaders.Add("User-Agent", "pfBackupNG v1.0-ALPHACENTAURI");
            _BlobContainerClient = new BlobContainerClient(_Global.Azure.ConnectionString, _Global.Azure.Container);
        }

        protected override async Task ExecuteAsync(CancellationToken _stoppingToken)
        {
            while (!_stoppingToken.IsCancellationRequested)
            {
                Log.Information($"Backup[{_Device.Name}] Task - Started");
                Dictionary<string, string> _HttpClientRequestParameters = new Dictionary<string, string>();
                HttpResponseMessage _httpResponseMessage = null;
                _HttpClientRequestParameters["__csrf_magic"] = String.Empty;

                //Phase 1
                try
                {
                    Log.Information($"Backup[{_Device.Name}] Phase 1 - Started.");
                    Log.Debug($"Backup[{_Device.Name}] Phase 1 - Obtaining Initial __csrf_magic.");
                    _httpResponseMessage = await _HttpClient.GetAsync(_Device.GetRequestUri(), _stoppingToken);
                    if (_httpResponseMessage.StatusCode == HttpStatusCode.OK)
                    {
                        string _httpResponseMessageAsString = await _httpResponseMessage.Content.ReadAsStringAsync();
                        _HttpClientRequestParameters["__csrf_magic"] = GetCsrfMagic(_httpResponseMessageAsString);
                        if (_HttpClientRequestParameters["__csrf_magic"] == String.Empty)
                            throw new HttpRequestException("Invalid or missing __csrf_magic");
                        Log.Debug($"Backup[{_Device.Name}] Phase 1 - Obtained Initial __csrf_magic.");
                    }
                    else
                        throw new HttpRequestException("Invalid HTTP Response Message Status Code", null, _httpResponseMessage.StatusCode);
                }
                catch(Exception _Phase1Exception)
                {
                    _HttpClientRequestParameters["__csrf_magic"] = String.Empty;
                    Log.Error(_Phase1Exception, $"Backup[{_Device.Name}] Phase 1 - Failed. Reason: See Exception.");
                }
                Log.Information($"Backup[{_Device.Name}] Phase 1 - Completed.");

                //Phase 2
                if (_HttpClientRequestParameters["__csrf_magic"] != String.Empty)
                {
                    try
                    {
                        Log.Information($"Backup[{_Device.Name}] Phase 2 - Started");
                        Log.Debug($"Backup[{_Device.Name}] Phase 2 - Attempting pfSense login.");
                        _HttpClientRequestParameters.Add("login", "Login");
                        _HttpClientRequestParameters.Add("usernamefld", _Device.Credentials.Username);
                        _HttpClientRequestParameters.Add("passwordfld", _Device.Credentials.Encrypted ? _Device.Credentials.Password.pfbackup_Decrypt(_Global.EncryptionKey) : _Device.Credentials.Password);
                        _httpResponseMessage = await _HttpClient.PostAsync(_Device.GetRequestUri(), new FormUrlEncodedContent(_HttpClientRequestParameters),_stoppingToken);
                        if (_httpResponseMessage.StatusCode == HttpStatusCode.OK)
                        {
                            string _httpResponseMessageAsString = await _httpResponseMessage.Content.ReadAsStringAsync();

                            if (_httpResponseMessageAsString.Contains("Username or Password incorrect"))
                                throw new InvalidCredentialException("Invalid pfSense login information. Please check and confirm username, password, and encryption settings.");
                            Log.Debug($"Backup[{_Device.Name}] Phase 2 - pfSense login successful.");

                            _HttpClientRequestParameters["__csrf_magic"] = GetCsrfMagic(_httpResponseMessageAsString);
                            if (_HttpClientRequestParameters["__csrf_magic"] == String.Empty)
                                throw new HttpRequestException("Invalid or missing __csrf_magic");
                            Log.Debug($"Backup[{_Device.Name}] Phase 2 - Obtained __csrf_magic.");

                            _HttpClientRequestParameters.Remove("login");
                            _HttpClientRequestParameters.Remove("usernamefld");
                            _HttpClientRequestParameters.Remove("passwordfld");
                        }
                        else
                            throw new HttpRequestException("Invalid HTTP Response Message Status Code", null, _httpResponseMessage.StatusCode);
                    }
                    catch(Exception _Phase2Exception)
                    {
                        _HttpClientRequestParameters["__csrf_magic"] = String.Empty;
                        Log.Error(_Phase2Exception, $"Backup[{_Device.Name}] Phase 2 - Failed. Reason: See Exception.");
                    }
                    Log.Information($"Backup[{_Device.Name}] Phase 2 - Completed.");
                }
                else
                    Log.Debug($"Backup[{_Device.Name}] Phase 2 - Skipped. Reason: Missing __csrf_magic. Check log for previous errors.");

                //Phase 3
                XmlDocument _currentDeviceBackup = null;
                string _currentDeviceBackupName = String.Empty;
                if (_HttpClientRequestParameters["__csrf_magic"] != String.Empty)
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
                        _httpResponseMessage = await _HttpClient.PostAsync(_Device.GetRequestUri(), new FormUrlEncodedContent(_HttpClientRequestParameters),_stoppingToken);
                        if (_httpResponseMessage.StatusCode == HttpStatusCode.OK)
                        {
                            string _httpResponseMessageAsString = await _httpResponseMessage.Content.ReadAsStringAsync();
                            _currentDeviceBackup = StringToXML(_httpResponseMessageAsString);
                            if (_currentDeviceBackup == null)
                                throw new XmlException("Invalid backup file type returned. Expected XML.");
                            _currentDeviceBackupName = _httpResponseMessage.Content.Headers.ContentDisposition.FileName;
                            Log.Debug($"Backup[{_Device.Name}] Phase 3 - pfSense Backup Received.");
                            _HttpClientRequestParameters.Remove("backuparea");
                            _HttpClientRequestParameters.Remove("donotbackuprrd");
                            _HttpClientRequestParameters.Remove("Submit");
                            _HttpClientRequestParameters.Remove("download");
                            _HttpClientRequestParameters.Remove("encrypt_password");
                            _HttpClientRequestParameters.Remove("encrypt_passconf");
                            _HttpClientRequestParameters["__csrf_magic"] = String.Empty;
                        }
                        else
                            throw new HttpRequestException("Invalid HTTP Response Message Status Code", null, _httpResponseMessage.StatusCode);
                    }
                    catch (Exception _Phase3Exception)
                    {
                        _currentDeviceBackup = null;
                        _currentDeviceBackupName = String.Empty;
                        _HttpClientRequestParameters["__csrf_magic"] = String.Empty;
                        Log.Error(_Phase3Exception, $"Backup[{_Device.Name}] Phase 3 - Failed. Reason: See Exception.");
                    }
                    Log.Information($"Backup[{_Device.Name}] Phase 3 - Completed.");
                }
                else
                    Log.Debug($"Backup[{_Device.Name}] Phase 3 - Skipped. Reason: Missing __csrf_magic. Check log for previous errors.");

                //Phase 4 - Upload to Azure!!!
                Azure.Response<Azure.Storage.Blobs.Models.BlobContentInfo> _blobClientUploadResult = null;
                if (_currentDeviceBackup != null && !String.IsNullOrWhiteSpace(_currentDeviceBackupName))
                {
                    try
                    {
                        Log.Information($"Backup[{_Device.Name}] Phase 4 - Started.");
                        Log.Debug($"Backup[{_Device.Name}] Phase 4 - Attempting pfSense backup upload.");
                        BlobClient _blobClient = _BlobContainerClient.GetBlobClient($"{_Device.Name.GetSafeString()}/{_currentDeviceBackupName.GetSafeString()}");
                        _blobClientUploadResult = await _blobClient.UploadAsync(new BinaryData(XMLToString(_currentDeviceBackup).ToArray()), _stoppingToken);
                        if (_blobClientUploadResult != null && _blobClientUploadResult.GetRawResponse().Status == (int)HttpStatusCode.Created)
                        {
                            Log.Debug($"Backup[{_Device.Name}] Phase 4 - pfSense Backup uploaded.");
                            _currentDeviceBackup = null;
                            _currentDeviceBackupName = String.Empty;
                        }
                        else
                            throw new Azure.RequestFailedException("Client Upload Result was NULL or INVALID");
                    }
                    catch(Exception _Phase4Exception)
                    {
                        _currentDeviceBackup = null;
                        _currentDeviceBackupName = String.Empty;
                        _blobClientUploadResult = null;
                        Log.Error(_Phase4Exception, $"Backup[{_Device.Name}] Phase 4 - Failed. Reason: See Exception.");
                    }
                }
                else
                    Log.Debug($"Backup[{_Device.Name}] Phase 4 - Skipped. Reason: Invalid pfSense backup file. Check log for previous errors.");

                //Phase 5 - We Prune
                if (_blobClientUploadResult != null)
                {
                    try
                    {
                        Log.Information($"Backup[{_Device.Name}] Phase 5 - Started.");
                        Log.Debug($"Backup[{_Device.Name}] Phase 5 - Pruning pfSense backups");
                        IAsyncEnumerable<Azure.Page<BlobItem>> _blobItemPages = _BlobContainerClient.GetBlobsAsync(default, BlobStates.All, $"{_Device.Name}/", _stoppingToken).AsPages(default, _Device.MaxRetention);
                        List<BlobItem> _blobItemsList = new List<BlobItem>();
                        await foreach(Azure.Page<BlobItem> _blobItemPage in _blobItemPages)
                        {
                            foreach(var _blobItem in _blobItemPage.Values)
                            {
                                _blobItemsList.Add(_blobItem);
                            }
                        }
                        if (_blobItemsList.Count > _Device.MaxRetention)
                        {
                            _blobItemsList.Sort(delegate (BlobItem x, BlobItem y)
                            {
                                return x.Properties.LastModified.Value.CompareTo(y.Properties.LastModified.Value);
                            });
                            while(_blobItemsList.Count > _Device.MaxRetention)
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
                }

                TimeSpan _jitter = _Device.PollInterval.Next();
                Log.Debug($"Backup[{_Device.Name}] sleeping for {_jitter}");
                try
                {
                    await Task.Delay(_jitter, _stoppingToken);
                }
                catch { }
            }
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
