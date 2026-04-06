using System.Globalization;
using System.Net;
using Lanny.Models;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;

namespace Lanny.Discovery;

public class SharpSnmpClient : ISnmpClient
{
    private const string SysDescriptionOid = "1.3.6.1.2.1.1.1.0";
    private const string SysInterfaceCountOid = "1.3.6.1.2.1.2.1.0";
    private const string SysObjectIdOid = "1.3.6.1.2.1.1.2.0";
    private const string SysNameOid = "1.3.6.1.2.1.1.5.0";
    private const string SysUptimeOid = "1.3.6.1.2.1.1.3.0";

    public async Task<SnmpSystemData?> GetSystemDataAsync(IPAddress ipAddress, SnmpSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);
        ArgumentNullException.ThrowIfNull(settings);

        var port = settings.Port is > 0 and <= 65535
            ? settings.Port
            : 161;
        var timeout = settings.TimeoutMilliseconds > 0
            ? settings.TimeoutMilliseconds
            : 1000;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeout));

        var variables = new List<Variable>
        {
            new(new ObjectIdentifier(SysNameOid)),
            new(new ObjectIdentifier(SysDescriptionOid)),
            new(new ObjectIdentifier(SysObjectIdOid)),
            new(new ObjectIdentifier(SysUptimeOid)),
            new(new ObjectIdentifier(SysInterfaceCountOid)),
        };

        var response = await Messenger.GetAsync(
            VersionCode.V2,
            new IPEndPoint(ipAddress, port),
            new OctetString(settings.Community),
            variables,
            timeoutCts.Token);

        return new SnmpSystemData(
            GetValue(response, SysNameOid),
            GetValue(response, SysDescriptionOid),
            GetValue(response, SysObjectIdOid),
            GetLongValue(response, SysUptimeOid),
            GetIntValue(response, SysInterfaceCountOid));
    }

    private static string? GetValue(IEnumerable<Variable> variables, string oid)
    {
        var value = variables
            .FirstOrDefault(variable => string.Equals(variable.Id.ToString(), oid, StringComparison.Ordinal))?
            .Data?
            .ToString();

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }

    private static int? GetIntValue(IEnumerable<Variable> variables, string oid)
    {
        var value = GetLongValue(variables, oid);
        return value.HasValue && value.Value is >= int.MinValue and <= int.MaxValue
            ? (int)value.Value
            : null;
    }

    private static long? GetLongValue(IEnumerable<Variable> variables, string oid)
    {
        var data = variables
            .FirstOrDefault(variable => string.Equals(variable.Id.ToString(), oid, StringComparison.Ordinal))?
            .Data;

        return data switch
        {
            TimeTicks timeTicks => timeTicks.ToUInt32(),
            Integer32 integer => integer.ToInt32(),
            Gauge32 gauge => gauge.ToUInt32(),
            Counter32 counter => counter.ToUInt32(),
            _ when long.TryParse(data?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }
}