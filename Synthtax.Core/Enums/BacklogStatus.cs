namespace Synthtax.Core.Enums;

public enum BacklogStatus
{
    Todo = 0,
    InProgress = 1,
    Done = 2,         // Denna saknades
    Cancelled = 3,    // Denna saknades
    Resolved = 4,     // Används av SyncWriter
    Open = 5,
    Acknowledged = 6,
    Accepted = 7,
    FalsePositive = 8
}