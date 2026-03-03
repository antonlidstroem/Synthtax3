namespace Synthtax.Admin.Models;

public class UserModel
{
    public string Id       { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email    { get; set; } = string.Empty;
    public bool   IsAdmin       { get; set; }
    public bool   IsSuperAdmin  { get; set; }
    public bool   IsLocked      { get; set; }
    public string DisplayRoles  => IsSuperAdmin ? "SuperAdmin" : IsAdmin ? "Admin" : "User";
}
