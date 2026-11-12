using Shouldly;
using Syncrowatch.Core.Config;
using Xunit;

namespace Syncrowatch.Tests;

public class CommitIdentityTests
{
    [Fact]
    public void Default_UsesSyncrowatchNoreplyIdentity()
    {
        CommitIdentity.Default.Name.ShouldBe("Syncrowatch");
        CommitIdentity.Default.Email.ShouldBe("syncrowatch@users.noreply.github.com");
    }

    [Fact]
    public void CustomIdentity_ComparesByNameAndEmail()
    {
        var first = new CommitIdentity { Name = "Peer", Email = "peer@users.noreply.github.com" };
        var second = new CommitIdentity { Name = "Peer", Email = "peer@users.noreply.github.com" };
        var other = new CommitIdentity { Name = "Khadgar", Email = "peer@users.noreply.github.com" };

        first.ShouldBe(second);
        first.ShouldNotBe(other);
    }
}
