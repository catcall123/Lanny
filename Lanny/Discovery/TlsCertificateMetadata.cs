namespace Lanny.Discovery;

public sealed record TlsCertificateMetadata(string? Subject, List<string> SubjectAlternativeNames);