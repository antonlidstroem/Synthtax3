namespace Synthtax.Domain.Enums;

public enum LanguageType
{
    Unknown = 0, CSharp = 1, JavaScript = 2, TypeScript = 3,
    Python = 4, Java = 5, Css = 6, Html = 7, Mixed = 99
}

public enum TierLevel
{
    Tier4 = 4, Tier3 = 3, Tier2 = 2, Tier1 = 1
}

public enum BacklogStatus
{
    Open = 0, Acknowledged = 1, InProgress = 2,
    Resolved = 3, Accepted = 4, FalsePositive = 5
}
