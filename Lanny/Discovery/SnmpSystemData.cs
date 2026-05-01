namespace Lanny.Discovery;

public sealed record SnmpSystemData(
    string? SystemName,
    string? SystemDescription,
    string? SystemObjectId,
    long? SystemUptime,
    int? InterfaceCount);
