using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace MareSynchronos.Utils
{
    public class ProfiledScope : IDisposable
    {
        readonly Stopwatch _stopwatch = new Stopwatch();

        bool _logWhenDone = false;
        ILogger? _logger = null;
        string? _logDescription = null;

        public double TotalSeconds { get; private set; }

        public void Start()
        {
            _stopwatch.Start();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            TotalSeconds = _stopwatch.Elapsed.TotalSeconds;
            if (_logWhenDone)
            {
                string message;
                if (_logDescription != null)
                {
                    message = $"[{_logDescription}]: {TotalSeconds} seconds.";
                }
                else
                {
                    message = $"{TotalSeconds} seconds.";
                }

                if (_logger != null)
                {
                    _logger.LogInformation(message);
                }
                else
                {
                    Debug.WriteLine(message);
                }
            }
        }

        public static ProfiledScope BeginScope()
        {
            var scope = new ProfiledScope();
            scope.Start();
            return scope;
        }

        public static ProfiledScope BeginLoggedScope(ILogger? logger, [CallerMemberName] string description = null)
        {
            if (description != null)
            {
                string startMessage = $"[{description}]: Starting.";
                if (logger != null)
                {
                    logger.LogInformation(startMessage);
                }
                else
                {
                    Debug.WriteLine(startMessage);
                }
            }

            var scope = BeginScope();
            scope._logWhenDone = true;
            scope._logDescription = description;
            scope._logger = logger;
            return scope;
        }
    }
}
