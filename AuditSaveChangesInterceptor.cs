using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Synthtax.Domain.Entities;

namespace Synthtax.Infrastructure.Data.Interceptors;

/// <summary>
/// EF Core-interceptor som automatiskt sätter audit-fält på alla <see cref="AuditableEntity"/>
/// och Soft Delete-fält på <see cref="ISoftDeletable"/> vid varje SaveChanges.
///
/// <para>Registreras som Singleton i DI och injiceras i DbContext via
/// <c>optionsBuilder.AddInterceptors(interceptor)</c>.</para>
///
/// <para>Användaren identifieras via <see cref="ICurrentUserService"/> — ett interface
/// som implementeras i API-lagret och läser från <c>IHttpContextAccessor</c>.</para>
/// </summary>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUser;

    public AuditSaveChangesInterceptor(ICurrentUserService currentUser)
    {
        _currentUser = currentUser;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        UpdateAuditFields(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct = default)
    {
        UpdateAuditFields(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    // ─────────────────────────────────────────────────────────────────────

    private void UpdateAuditFields(DbContext? context)
    {
        if (context is null) return;

        var now  = DateTime.UtcNow;
        var user = _currentUser.UserId ?? "system";

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt  = now;
                    entry.Entity.CreatedBy  = user;
                    // Sätts även på LastModified så att "senast ändrad" alltid är populerad
                    entry.Entity.LastModifiedAt = now;
                    entry.Entity.LastModifiedBy = user;
                    break;

                case EntityState.Modified:
                    // Rör inte CreatedAt/CreatedBy — de ska vara immutabla
                    entry.Property(e => e.CreatedAt).IsModified  = false;
                    entry.Property(e => e.CreatedBy).IsModified  = false;
                    entry.Entity.LastModifiedAt = now;
                    entry.Entity.LastModifiedBy = user;
                    break;
            }

            // Soft Delete: EntityState.Deleted → konvertera till Modified + sätt flaggor
            if (entry.State == EntityState.Deleted &&
                entry.Entity is ISoftDeletable softDeletable)
            {
                entry.State = EntityState.Modified;
                softDeletable.IsDeleted = true;
                softDeletable.DeletedAt = now;
                softDeletable.DeletedBy = user;
                entry.Entity.LastModifiedAt = now;
                entry.Entity.LastModifiedBy = user;
            }
        }
    }
}

/// <summary>
/// Abstraherar identifiering av inloggad användare.
/// Implementeras i Synthtax.API via IHttpContextAccessor.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>Null om anropet sker utanför en HTTP-request (t.ex. bakgrundsjobbbet).</summary>
    string? UserId { get; }
}
