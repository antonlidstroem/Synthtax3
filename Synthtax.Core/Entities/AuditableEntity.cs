namespace Synthtax.Core.Entities;

/// <summary>
/// Basklass som ger alla entiteter automatisk audit-spårning.
/// Värdena sätts av <see cref="Synthtax.Infrastructure.Data.Interceptors.AuditSaveChangesInterceptor"/>
/// — aldrig manuellt i applikationskoden.
/// </summary>
public abstract class AuditableEntity
{
    public DateTime  CreatedAt      { get; set; }
    public string?   CreatedBy      { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    public string?   LastModifiedBy { get; set; }
}

/// <summary>
/// Markör-interface för Soft Delete.
/// Entiteter som implementerar detta filtreras bort av Global Query Filter
/// i DbContext när <c>IsDeleted == true</c>.
/// </summary>
public interface ISoftDeletable
{
    bool      IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    string?   DeletedBy { get; set; }
}
