using System.IO;
using AIOrchestratorCoreLib.Running.WakeTicket;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE TICKET IS THE WHOLE PROTOCOL BETWEEN THE APP AND A TERMINAL SESSION'S MONITOR, so it has to
/// survive being read at the worst moment: the monitor polls on its own clock and will sometimes read
/// while the app writes. Hence write-temp-then-rename, and a number rather than a timestamp to compare
/// on — two wakes inside one clock tick must still be two wakes.
/// </summary>
public class WakeTicketStoreTests
{
    [Fact]
    public void A_ticket_round_trips()
    {
        var file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var written = WakeTicket_Factory.Create(number: 7, reason: "the owner wrote in 'owner'", statePackFile: null, stampedUtc: new DateTime(2026, 9, 15, 8, 12, 44, DateTimeKind.Utc));

        WakeTicket_Store.Write(file, written);
        var read = WakeTicket_Store.Read_OrNull(file);

        Assert.Equal(7, read!.Number);
        Assert.Equal("the owner wrote in 'owner'", read.Reason);
        Assert.Null(read.StatePackFile);
        File.Delete(file);
    }

    [Fact]
    public void A_missing_ticket_reads_as_null_rather_than_throwing()
    {
        Assert.Null(WakeTicket_Store.Read_OrNull(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())));
    }

    [Fact]
    public void A_corrupt_ticket_reads_as_null_rather_than_throwing()
    {
        var file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(file, "{not json");

        Assert.Null(WakeTicket_Store.Read_OrNull(file));
        File.Delete(file);
    }

    [Fact]
    public void Writing_is_atomic_from_a_reader_point_of_view()
    {
        var file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        WakeTicket_Store.Write(file, WakeTicket_Factory.Create(1, "first", null, DateTime.UtcNow));
        WakeTicket_Store.Write(file, WakeTicket_Factory.Create(2, "second", null, DateTime.UtcNow));

        Assert.Equal(2, WakeTicket_Store.Read_OrNull(file)!.Number);
        Assert.False(File.Exists(file + ".tmp"));
        File.Delete(file);
    }
}
