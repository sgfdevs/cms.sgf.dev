#nullable enable
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace SGFDevs.ViewModels;

public sealed class MemberAvatarRequest
{
    [Required]
    public IFormFile File { get; set; } = null!;
}

public sealed record MemberAvatarResult(string ProfileImageUrl);
