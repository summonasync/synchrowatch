using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.Core.History;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class SyncEventBusTests
{
    [Fact]
    public void Publish_DeliversNotificationToSubscriber()
    {
        var bus = new SyncEventBus();
        var received = new List<SyncNotification>();
        bus.Received += received.Add;
        var notification = new SyncNotification.SyncCompleted(
            DateTimeOffset.UtcNow, new SyncCycleReport(DateTimeOffset.UtcNow, null, null));

        bus.Publish(notification);

        received.ShouldHaveSingleItem().ShouldBeOfType<SyncNotification.SyncCompleted>();
    }

    [Fact]
    public void Publish_ThrowingSubscriber_DoesNotBlockOtherSubscribers()
    {
        var bus = new SyncEventBus();
        bus.Received += _ => throw new InvalidOperationException("toast renderer exploded");
        var received = new List<SyncNotification>();
        bus.Received += received.Add;

        bus.Publish(new SyncNotification.SyncFailed(DateTimeOffset.UtcNow, SyncError.Conflict("diverged")));

        received.ShouldHaveSingleItem();
    }

    [Fact]
    public void Publish_WithoutSubscribers_CompletesSilently()
    {
        var bus = new SyncEventBus();

        bus.Publish(new SyncNotification.AutoMerged(
            DateTimeOffset.UtcNow, "main", ["notes.txt"]));
    }
}
