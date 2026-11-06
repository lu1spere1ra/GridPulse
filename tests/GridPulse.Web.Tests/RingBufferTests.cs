using GridPulse.Web.Services;

namespace GridPulse.Web.Tests;

public sealed class RingBufferTests
{
    [Fact]
    public void Keeps_only_the_newest_items_in_order()
    {
        var buffer = new RingBuffer<int>(3);

        for (var i = 1; i <= 5; i++)
        {
            buffer.Add(i);
        }

        buffer.ShouldBe([3, 4, 5]);
        buffer.Count.ShouldBe(3);
    }

    [Fact]
    public void Below_capacity_it_behaves_like_a_list()
    {
        var buffer = new RingBuffer<int>(10);
        buffer.Add(1);
        buffer.Add(2);

        buffer.ShouldBe([1, 2]);
    }
}
