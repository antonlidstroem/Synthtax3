using Synthtax.Vsix.Client;

namespace Synthtax.Vsix.Services;

public interface IToolWindowRefreshTarget
{
    Task ApplyIncrementalUpdateAsync(
        IReadOnlyList<BacklogItemDto> added,
        IReadOnlyList<Guid>           removedIds);

    void RemoveIssue(Guid issueId);
    void UpdateSubscriptionPlan(string newPlan);
}
