#nullable enable

namespace SGFDevs.ViewModels;

public sealed record MemberLoginRequest(string? Username, string? Password, bool RememberMe = true);
public sealed record MemberLoginResult(bool Succeeded);
public sealed record MemberSessionDto(string Username, string Name);
