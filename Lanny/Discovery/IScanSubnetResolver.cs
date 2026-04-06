namespace Lanny.Discovery;

public interface IScanSubnetResolver
{
    string ResolveSubnet(string? configuredSubnet);
}