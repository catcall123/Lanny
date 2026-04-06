namespace Lanny.Models;

public class SnmpSettings
{
    public bool Enabled { get; set; }

    public string Community { get; set; } = string.Empty;

    public int Port { get; set; } = 161;

    public int TimeoutMilliseconds { get; set; } = 1000;
}