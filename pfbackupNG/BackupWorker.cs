using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace pfbackupNG
{
    public class BackupWorker : BackgroundService
    {
        //private readonly ILogger<BackupWorker> _logger;
        private readonly GlobalConfiguration _Global;
        private readonly DeviceConfiguration _Device;

        public BackupWorker(GlobalConfiguration Global, DeviceConfiguration Device)
        {
            if (Global == null || Device == null)
                throw new ArgumentNullException("BackupWorker Constructor");
            _Global = Global;
            _Device = Device;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Log.Information("Worker running at: {time}", DateTimeOffset.Now);
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
