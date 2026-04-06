using System.Net;
using Lanny.Models;

namespace Lanny.Discovery;

public interface ISnmpClient
{
    Task<SnmpSystemData?> GetSystemDataAsync(IPAddress ipAddress, SnmpSettings settings, CancellationToken cancellationToken);
}