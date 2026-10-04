#nullable enable

using System.Collections.Generic;

namespace SGFDevs.ViewModels;

// Not an identity/member model. Never include the request in logs or responses.
public sealed class MemberRegistrationRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? ChallengeQuestion { get; set; }
}

public sealed record MemberRegistrationResult(bool Succeeded, Dictionary<string, string[]> Errors);
