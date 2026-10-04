#nullable enable

using System.Collections.Generic;

namespace SGFDevs.ViewModels;

public sealed class MemberForgotPasswordRequest
{
    public string Email { get; set; } = "";
}

public sealed class MemberResetPasswordRequest
{
    public string MemberId { get; set; } = "";
    public string Token { get; set; } = "";
    public string Password { get; set; } = "";
    public string ConfirmPassword { get; set; } = "";
}

public sealed record MemberPasswordResetResult(bool Succeeded, Dictionary<string, string[]> Errors);
