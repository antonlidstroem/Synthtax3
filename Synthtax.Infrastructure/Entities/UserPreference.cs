namespace Synthtax.Infrastructure.Entities;

public class UserPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Theme { get; set; } = "Light";
    public string Language { get; set; } = "sv-SE";
    public bool EmailNotifications { get; set; } = true;
    public bool ShowMetricsTrend { get; set; } = true;
    public int DefaultPageSize { get; set; } = 50;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // FK – 1:1 med ApplicationUser
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
}
