namespace Synthtax.Vsix.Package;

/// <summary>Centrala GUID-konstanter för VSIX-paketet.</summary>
internal static class SynthtaxPackageGuids
{
    // Package
    public const string PackageGuidString = "4a7b1c2d-3e4f-5a6b-7c8d-9e0f1a2b3c4d";
    public static readonly Guid PackageGuid = new(PackageGuidString);

    // Tool Window
    public const string BacklogToolWindowGuidString = "9e0f1a2b-3c4d-5e6f-7a8b-9c0d1e2f3a4b";
    public static readonly Guid BacklogToolWindowGuid = new(BacklogToolWindowGuidString);

    // Command Set
    public const string CommandSetGuidString = "1b2c3d4e-5f6a-7b8c-9d0e-1f2a3b4c5d6e";
    public static readonly Guid CommandSetGuid = new(CommandSetGuidString);

    // Command IDs (matchar .vsct-fil)
    public const int OpenBacklogCommandId    = 0x0100;
    public const int RefreshBacklogCommandId = 0x0101;
    public const int LoginCommandId          = 0x0102;
}
