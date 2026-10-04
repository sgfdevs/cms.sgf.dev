#nullable enable
using System.Collections.Generic;

namespace SGFDevs.ViewModels;

public class PublicJobDto
{
    public string Name { get; init; } = string.Empty;
    public string CompanyName { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string? Location { get; init; }
    public string? EmploymentType { get; init; }
    public string? Compensation { get; init; }
    public string Posted { get; init; } = string.Empty;
    public string? DescriptionHtml { get; init; }
    public string? ApplyUrl { get; init; }
    public IReadOnlyList<string> Skills { get; init; } = [];
}
