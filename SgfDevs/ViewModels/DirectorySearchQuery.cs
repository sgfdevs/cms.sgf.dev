#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace SGFDevs.ViewModels;

public class DirectorySearchQuery
{
    public const int MaxTake = 500;
    public const int MaxSkillTerms = 50;
    public const int MaxSkillTermLength = 128;

    public IReadOnlyList<string> SkillTerms { get; private set; } = [];
    public int Skip { get; private set; }
    public int? Take { get; private set; }

    public static bool TryCreate(
        string? skills,
        int? skip,
        int? take,
        out DirectorySearchQuery? query,
        out string? error)
    {
        query = null;
        error = null;

        if (skip is < 0)
        {
            error = "skip must be zero or greater.";
            return false;
        }

        if (take is < 1 or > MaxTake)
        {
            error = $"take must be between 1 and {MaxTake}.";
            return false;
        }

        var skillTerms = string.IsNullOrWhiteSpace(skills)
            ? Array.Empty<string>()
            : skills
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (skillTerms.Length > MaxSkillTerms)
        {
            error = $"skills may include at most {MaxSkillTerms} values.";
            return false;
        }

        if (skillTerms.Any(term => term.Length > MaxSkillTermLength))
        {
            error = $"skill values may be at most {MaxSkillTermLength} characters.";
            return false;
        }

        query = new DirectorySearchQuery
        {
            SkillTerms = skillTerms,
            Skip = skip ?? 0,
            Take = take
        };
        return true;
    }

    public IEnumerable<T> ApplyTo<T>(IEnumerable<T> items)
    {
        var pagedItems = items.Skip(Skip);
        return Take.HasValue ? pagedItems.Take(Take.Value) : pagedItems;
    }
}
