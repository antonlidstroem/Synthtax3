using System.Text;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Services;

/// <summary>
/// Analyses git changes (working tree or staged) and generates a rule-based
/// Conventional Commits message suggestion without any AI.
///
/// Rules applied (in priority order):
///   1. Type detection via filename patterns and content change ratios
///   2. Scope from the most-changed top-level directory or file prefix
///   3. Subject line built from aggregated change descriptions
///   4. Body lists the top changed files
/// </summary>
public class CommitMessageService : ICommitMessageService
{
    private readonly ILogger<CommitMessageService> _logger;

    public CommitMessageService(ILogger<CommitMessageService> logger) => _logger = logger;

    public async Task<CommitSuggestionDto> SuggestAsync(
        string repositoryPath,
        bool stagedOnly = false,
        CancellationToken ct = default)
    {
        var result = new CommitSuggestionDto();
        try
        {
            await Task.Run(() => Analyse(repositoryPath, stagedOnly, result), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (RepositoryNotFoundException)
        {
            result.Errors.Add($"'{repositoryPath}' is not a valid Git repository.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CommitMessageService failed for {Path}", repositoryPath);
            result.Errors.Add($"Error: {ex.Message}");
        }
        return result;
    }

    // ── Core analysis ─────────────────────────────────────────────────────────

    private static void Analyse(string repoPath, bool stagedOnly, CommitSuggestionDto result)
    {
        using var repo = new Repository(repoPath);

        var changes = GetChanges(repo, stagedOnly);
        if (changes.Count == 0)
        {
            result.Errors.Add("No changes detected. Commit the staged/working-tree changes first.");
            return;
        }

        result.FilesChanged = changes.Count;
        result.Insertions   = changes.Sum(c => c.Insertions);
        result.Deletions    = changes.Sum(c => c.Deletions);
        result.ChangeSummary.AddRange(changes);

        var type  = DetectType(changes);
        var scope = DetectScope(changes);

        var subject  = BuildSubject(type, scope, changes);
        var body     = BuildBody(changes);
        var alts     = BuildAlternatives(type, scope, changes);

        result.Type       = type;
        result.Scope      = scope;
        result.Subject    = subject;
        result.Body       = body;
        result.Confidence = CalcConfidence(type, changes);
        result.Alternatives.AddRange(alts);
    }

    // ── Collect diff entries ──────────────────────────────────────────────────

    private static List<CommitChangeSummaryDto> GetChanges(Repository repo, bool stagedOnly)
    {
        var result = new List<CommitChangeSummaryDto>();

        TreeChanges changes;
        if (stagedOnly)
        {
            // Staged: index vs HEAD
            var head = repo.Head.Tip?.Tree;
            changes = repo.Diff.Compare<TreeChanges>(head, DiffTargets.Index);
        }
        else
        {
            // All: HEAD vs working directory (including staged)
            var head = repo.Head.Tip?.Tree;
            changes = repo.Diff.Compare<TreeChanges>(
                head,
                DiffTargets.Index | DiffTargets.WorkingDirectory);
        }

        foreach (var entry in changes)
        {
            var patchEntry = TryGetPatch(repo, entry, stagedOnly);
            result.Add(new CommitChangeSummaryDto
            {
                FilePath   = entry.Path,
                FileName   = Path.GetFileName(entry.Path),
                ChangeType = MapStatus(entry.Status),
                Insertions = patchEntry?.LinesAdded   ?? 0,
                Deletions  = patchEntry?.LinesDeleted  ?? 0,
                OldPath    = entry.Status == ChangeKind.Renamed ? entry.OldPath : null
            });
        }

        return result.OrderByDescending(c => c.Insertions + c.Deletions).ToList();
    }

    private static PatchEntryChanges? TryGetPatch(Repository repo, TreeEntryChanges entry, bool stagedOnly)
    {
        try
        {
            Patch patch;
            var head = repo.Head.Tip?.Tree;
            patch = stagedOnly
                ? repo.Diff.Compare<Patch>(head, DiffTargets.Index)
                : repo.Diff.Compare<Patch>(head, DiffTargets.Index | DiffTargets.WorkingDirectory);
            return patch.FirstOrDefault(p => p.Path == entry.Path);
        }
        catch { return null; }
    }

    private static string MapStatus(ChangeKind kind) => kind switch
    {
        ChangeKind.Added    => "Added",
        ChangeKind.Deleted  => "Deleted",
        ChangeKind.Renamed  => "Renamed",
        ChangeKind.Copied   => "Copied",
        _                   => "Modified"
    };

    // ── Type detection ────────────────────────────────────────────────────────

    private static string DetectType(List<CommitChangeSummaryDto> changes)
    {
        var scores = new Dictionary<string, double>
        {
            ["feat"]     = 0,
            ["fix"]      = 0,
            ["refactor"] = 0,
            ["style"]    = 0,
            ["docs"]     = 0,
            ["test"]     = 0,
            ["chore"]    = 0,
            ["build"]    = 0,
            ["ci"]       = 0,
            ["perf"]     = 0,
        };

        foreach (var c in changes)
        {
            var f   = c.FilePath.ToLowerInvariant();
            var ext = Path.GetExtension(f);

            // ── Test files
            if (f.Contains("test") || f.Contains("spec") || f.EndsWith(".test.js") ||
                f.EndsWith(".spec.ts") || f.Contains("/tests/") || f.Contains("/__tests__/"))
            { scores["test"] += 2; continue; }

            // ── CI/CD
            if (f.Contains(".github/") || f.Contains(".gitlab-ci") || f.Contains("jenkinsfile") ||
                f.Contains("azure-pipelines") || f.Contains(".circleci/"))
            { scores["ci"] += 3; continue; }

            // ── Build system
            if (f is ".csproj" or ".sln" or "package.json" or "package-lock.json" or
                "yarn.lock" or "nuget.config" or "global.json" or "dockerfile" ||
                f.EndsWith(".csproj") || f.EndsWith(".sln") || f.Contains("build/") ||
                f.Contains("makefile") || f.EndsWith(".yml") || f.EndsWith(".yaml"))
            { scores["build"] += 2; continue; }

            // ── Docs
            if (ext is ".md" or ".txt" or ".rst" or ".adoc" || f.Contains("/docs/") ||
                c.FileName.StartsWith("readme", StringComparison.OrdinalIgnoreCase) ||
                c.FileName.StartsWith("changelog", StringComparison.OrdinalIgnoreCase))
            { scores["docs"] += 2; continue; }

            // ── Style-only (CSS, SCSS, LESS, small HTML tweaks)
            if (ext is ".css" or ".scss" or ".less" or ".sass")
            { scores["style"] += 1.5; continue; }

            // ── Config / chore
            if (ext is ".json" or ".xml" or ".toml" or ".ini" or ".env" ||
                f.Contains("appsettings") || f.Contains(".editorconfig") || f.Contains(".gitignore"))
            { scores["chore"] += 1; continue; }

            // ── Score additions vs modifications for feat vs fix
            if (c.ChangeType == "Added")
                scores["feat"]     += 1.5;
            else if (c.ChangeType == "Deleted")
                scores["refactor"] += 0.8;
            else
            {
                // Heavy deletions relative to insertions hint at refactoring
                var ratio = c.Insertions > 0 ? (double)c.Deletions / c.Insertions : 0;
                if (ratio > 1.5)      scores["refactor"] += 1;
                else if (ratio < 0.3) scores["feat"]     += 0.8;
                else                  scores["fix"]       += 0.5;
            }
        }

        // Majority-new-files → feat
        var added = changes.Count(c => c.ChangeType == "Added");
        if (added > changes.Count / 2.0) scores["feat"] += 2;

        return scores.OrderByDescending(kv => kv.Value).First().Key;
    }

    // ── Scope detection ───────────────────────────────────────────────────────

    private static string? DetectScope(List<CommitChangeSummaryDto> changes)
    {
        if (changes.Count == 0) return null;

        // Count changes per top-level directory
        var dirCounts = changes
            .Select(c =>
            {
                var parts = c.FilePath.Replace('\\', '/').Split('/');
                return parts.Length > 1 ? parts[0] : string.Empty;
            })
            .Where(d => !string.IsNullOrEmpty(d))
            .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        if (dirCounts is null) return null;

        // Only return scope if majority of changes are in one directory
        if (dirCounts.Count() < changes.Count * 0.5) return null;

        var dir = dirCounts.Key.ToLowerInvariant();

        // Map common folder names to conventional scope names
        return dir switch
        {
            "controllers"                                             => "api",
            "services"                                               => "services",
            "repositories"                                           => "data",
            "infrastructure"                                         => "infra",
            "migrations"                                             => "db",
            "tests" or "test" or "__tests__"                        => "tests",
            "wwwroot" or "public" or "static" or "assets"           => "assets",
            ".github"                                                => "ci",
            _                                                        => dir.Length <= 20 ? dir : null
        };
    }

    // ── Subject line ──────────────────────────────────────────────────────────

    private static string BuildSubject(
        string type, string? scope, List<CommitChangeSummaryDto> changes)
    {
        var prefix = scope is not null ? $"{type}({scope})" : type;

        var description = BuildDescription(type, changes);
        return $"{prefix}: {description}";
    }

    private static string BuildDescription(string type, List<CommitChangeSummaryDto> changes)
    {
        var added    = changes.Where(c => c.ChangeType == "Added").ToList();
        var deleted  = changes.Where(c => c.ChangeType == "Deleted").ToList();
        var modified = changes.Where(c => c.ChangeType is "Modified" or "Renamed").ToList();

        // Single file change – be specific
        if (changes.Count == 1)
        {
            var c   = changes[0];
            var ext = Path.GetExtension(c.FileName).TrimStart('.').ToLower();
            var nm  = Path.GetFileNameWithoutExtension(c.FileName);
            return c.ChangeType switch
            {
                "Added"   => $"add {Humanize(nm)}",
                "Deleted" => $"remove {Humanize(nm)}",
                "Renamed" => $"rename {Humanize(Path.GetFileNameWithoutExtension(c.OldPath ?? c.FileName))} to {Humanize(nm)}",
                _         => $"update {Humanize(nm)}"
            };
        }

        // Thematic descriptions
        if (added.Count == changes.Count)
            return SummariseFiles(added, "add");
        if (deleted.Count == changes.Count)
            return SummariseFiles(deleted, "remove");
        if (modified.Count == changes.Count)
            return SummariseFiles(modified, "update");

        // Mixed
        var parts = new List<string>();
        if (added.Count > 0)    parts.Add($"add {SummariseFiles(added)}");
        if (modified.Count > 0) parts.Add($"update {SummariseFiles(modified)}");
        if (deleted.Count > 0)  parts.Add($"remove {SummariseFiles(deleted)}");
        return string.Join(", ", parts);
    }

    private static string SummariseFiles(List<CommitChangeSummaryDto> files, string? verb = null)
    {
        if (files.Count == 0) return string.Empty;
        if (files.Count == 1)
        {
            var nm = Path.GetFileNameWithoutExtension(files[0].FileName);
            return verb is not null ? $"{verb} {Humanize(nm)}" : Humanize(nm);
        }

        // Same extension → "add 3 CSS files"
        var exts = files.Select(f => Path.GetExtension(f.FileName).TrimStart('.').ToUpper())
                        .GroupBy(e => e).OrderByDescending(g => g.Count()).FirstOrDefault();
        if (exts is not null && exts.Count() == files.Count)
        {
            var label = verb is not null ? $"{verb} {files.Count} {exts.Key} files" : $"{files.Count} {exts.Key} files";
            return label;
        }

        // Same directory → "add files in auth/"
        var dir = files.Select(f =>
        {
            var parts = f.FilePath.Replace('\\', '/').Split('/');
            return parts.Length > 1 ? parts[^2] : string.Empty;
        }).GroupBy(d => d).OrderByDescending(g => g.Count()).FirstOrDefault();

        if (dir is not null && !string.IsNullOrEmpty(dir.Key) && dir.Count() > files.Count / 2)
        {
            var label = verb is not null ? $"{verb} files in {dir.Key}/" : $"files in {dir.Key}/";
            return label;
        }

        // Fallback
        return verb is not null ? $"{verb} {files.Count} files" : $"{files.Count} files";
    }

    // ── Body ──────────────────────────────────────────────────────────────────

    private static string BuildBody(List<CommitChangeSummaryDto> changes)
    {
        if (changes.Count <= 1) return string.Empty;

        var sb = new StringBuilder();
        var top = changes.Take(10).ToList();

        foreach (var c in top)
        {
            var stats = c.Insertions + c.Deletions > 0
                ? $" (+{c.Insertions} -{c.Deletions})"
                : string.Empty;
            var marker = c.ChangeType switch
            {
                "Added"   => "+",
                "Deleted" => "-",
                "Renamed" => "→",
                _         => "~"
            };
            sb.AppendLine($"{marker} {c.FilePath}{stats}");
        }

        if (changes.Count > 10)
            sb.AppendLine($"… and {changes.Count - 10} more file(s)");

        return sb.ToString().TrimEnd();
    }

    // ── Alternatives ──────────────────────────────────────────────────────────

    private static List<CommitSuggestionDto> BuildAlternatives(
        string primaryType, string? scope, List<CommitChangeSummaryDto> changes)
    {
        string[] candidateTypes = primaryType switch
        {
            "feat"     => ["fix", "refactor", "chore"],
            "fix"      => ["feat", "refactor"],
            "refactor" => ["fix", "feat", "chore"],
            "style"    => ["refactor", "chore"],
            "docs"     => ["chore"],
            _          => ["feat", "fix", "refactor"]
        };

        return candidateTypes.Take(2).Select(t => new CommitSuggestionDto
        {
            Type       = t,
            Scope      = scope,
            Subject    = BuildSubject(t, scope, changes),
            Body       = BuildBody(changes),
            Confidence = CalcConfidence(t, changes) * 0.7,
            FilesChanged = changes.Count,
            Insertions   = changes.Sum(c => c.Insertions),
            Deletions    = changes.Sum(c => c.Deletions)
        }).ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static double CalcConfidence(string type, List<CommitChangeSummaryDto> changes)
    {
        // Higher when many files of same type, or single file change
        if (changes.Count == 1)  return 0.9;
        var exts = changes.Select(c => Path.GetExtension(c.FilePath)).Distinct().Count();
        if (exts == 1)           return 0.85;
        if (exts <= 2)           return 0.7;
        return 0.55;
    }

    /// <summary>Converts PascalCase/camelCase filenames to lowercase words.</summary>
    private static string Humanize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        // Insert spaces before uppercase letters that follow lowercase
        var spaced = System.Text.RegularExpressions.Regex.Replace(
            name, @"(?<=[a-z])(?=[A-Z])", " ");
        // Replace dots and underscores with spaces
        spaced = spaced.Replace('.', ' ').Replace('_', ' ');
        return spaced.ToLowerInvariant().Trim();
    }
}
