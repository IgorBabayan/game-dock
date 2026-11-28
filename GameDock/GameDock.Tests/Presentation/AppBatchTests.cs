using GameDock.Presentation.ViewModels;

namespace GameDock.Tests.Presentation;

public class AppBatchTests
{
    private sealed class Item(
        string name,
        bool selected = false,
        bool installable = false,
        bool updatable = false,
        bool removable = false)
    {
        public string Name { get; } = name;
        public bool Selected { get; set; } = selected;
        public bool Installable { get; set; } = installable;
        public bool Updatable { get; set; } = updatable;
        public bool Removable { get; set; } = removable;
        public override string ToString() => Name;
    }

    private static BatchCounts Count(params Item[] items) =>
        AppBatch.Count(items, static i => i.Selected, static i => i.Installable, static i => i.Updatable,
            static i => i.Removable);

    // ===== Count =====

    [Fact]
    public void Count_Empty_IsAllZeros()
    {
        var counts = Count();

        Assert.Equal(default(BatchCounts), counts);
        Assert.False(counts.CanInstallAll);
        Assert.False(counts.CanUpdateAll);
        Assert.False(counts.CanInstallSelected);
        Assert.False(counts.CanUpdateSelected);
        Assert.False(counts.CanRemoveAll);
        Assert.False(counts.CanRemoveSelected);
    }

    [Fact]
    public void Count_TalliesEveryCategory()
    {
        var counts = Count(
            new Item("a", installable: true),
            new Item("b", selected: true, installable: true),
            new Item("c", updatable: true, removable: true),
            new Item("d", selected: true, updatable: true, removable: true),
            new Item("e", selected: true, removable: true));

        Assert.Equal(
            new BatchCounts(Installable: 2, Updatable: 2, Removable: 3, Selected: 3, SelectedInstallable: 1,
                SelectedUpdatable: 1, SelectedRemovable: 2),
            counts);
    }

    [Fact]
    public void CanInstallAll_And_CanUpdateAll_FollowTheTotals()
    {
        Assert.True(Count(new Item("a", installable: true)).CanInstallAll);
        Assert.False(Count(new Item("a", installable: true)).CanUpdateAll);
        Assert.True(Count(new Item("a", updatable: true)).CanUpdateAll);
        Assert.False(Count(new Item("a", updatable: true)).CanInstallAll);
    }

    [Fact]
    public void CanRemoveAll_FollowsTheInstalledApps()
    {
        Assert.True(Count(new Item("a", removable: true), new Item("b", installable: true)).CanRemoveAll);
        Assert.False(Count(new Item("a", installable: true)).CanRemoveAll);
    }

    [Fact]
    public void RemoveSelected_NeedsTwoSelectedApps_AndOneInstalled()
    {
        Assert.False(Count(new Item("a", selected: true, removable: true), new Item("b", removable: true))
            .CanRemoveSelected);
        Assert.True(Count(new Item("a", selected: true, removable: true), new Item("b", selected: true, installable: true))
            .CanRemoveSelected);
        Assert.False(Count(new Item("a", selected: true, installable: true), new Item("b", selected: true, installable: true))
            .CanRemoveSelected);
    }

    [Fact]
    public void SelectedButtons_NeedAtLeastTwoSelectedApps()
    {
        var one = Count(
            new Item("a", selected: true, installable: true),
            new Item("b", installable: true));

        Assert.False(one.HasMultiSelection);
        Assert.False(one.CanInstallSelected);
        Assert.False(one.CanUpdateSelected);
        Assert.False(one.CanRemoveSelected);
    }

    [Fact]
    public void InstallSelected_AppearsForTwoSelectedApps_WhenOneCanBeInstalled()
    {
        var counts = Count(
            new Item("a", selected: true, installable: true),
            new Item("b", selected: true, updatable: true));

        Assert.Equal(BatchCounts.MinSelection, counts.Selected);
        Assert.True(counts.CanInstallSelected);
        Assert.True(counts.CanUpdateSelected);
    }

    [Fact]
    public void SelectedButtons_StayHidden_WhenNothingSelectedCanRun()
    {
        // Two selected apps that are installed and up to date.
        var counts = Count(new Item("a", selected: true), new Item("b", selected: true));

        Assert.True(counts.HasMultiSelection);
        Assert.False(counts.CanInstallSelected);
        Assert.False(counts.CanUpdateSelected);
    }

    [Fact]
    public void Counts_AreEqual_WhenNothingChanged()
    {
        // The window relies on value equality to skip redundant notifications.
        Assert.Equal(Count(new Item("a", selected: true, installable: true)),
            Count(new Item("b", selected: true, installable: true)));
        Assert.NotEqual(Count(new Item("a", selected: true)), Count(new Item("a")));
    }

    // ===== Label =====

    [Theory]
    [InlineData("INSTALL ALL", 3, "INSTALL ALL (3)")]
    [InlineData("UPDATE ALL", 1, "UPDATE ALL (1)")]
    [InlineData("INSTALL SELECTED", 2, "INSTALL SELECTED (2)")]
    [InlineData("UPDATE SELECTED", 0, "UPDATE SELECTED (0)")]
    [InlineData("REMOVE ALL", 4, "REMOVE ALL (4)")]
    [InlineData("REMOVE SELECTED", 2, "REMOVE SELECTED (2)")]
    public void Label_AppendsTheCount(string action, int count, string expected)
    {
        Assert.Equal(expected, AppBatch.Label(action, count));
    }

    // ===== RemoveConfirmation =====

    [Fact]
    public void RemoveConfirmation_OneApp_NamesIt()
    {
        var (title, message) = AppBatch.RemoveConfirmation(new[] { "GD Save Editor" });

        Assert.Equal("Remove GD Save Editor?", title);
        Assert.StartsWith("GD Save Editor will be uninstalled.", message);
        Assert.EndsWith("Your saves and settings are not deleted.", message);
    }

    [Fact]
    public void RemoveConfirmation_SeveralApps_ListsEveryApp()
    {
        var (title, message) = AppBatch.RemoveConfirmation(new[] { "Navigator", "Exile Lens", "ED Assistant" });

        Assert.Equal("Remove 3 apps?", title);
        Assert.Contains("•  Navigator\n•  Exile Lens\n•  ED Assistant", message);
        Assert.EndsWith("Your saves and settings are not deleted.", message);
    }

    // ===== RunAsync =====

    [Fact]
    public async Task RunAsync_RunsEligibleItemsInOrder()
    {
        var items = new[] { new Item("a", installable: true), new Item("b"), new Item("c", installable: true) };
        var ran = new List<string>();

        var count = await AppBatch.RunAsync(items, static i => i.Installable, i =>
        {
            ran.Add(i.Name);
            return Task.CompletedTask;
        });

        Assert.Equal(2, count);
        Assert.Equal(new[] { "a", "c" }, ran);
    }

    [Fact]
    public async Task RunAsync_RunsOneAtATime()
    {
        var items = Enumerable.Range(0, 4).Select(i => new Item($"{i}", installable: true)).ToList();
        var running = 0;
        var maxRunning = 0;

        await AppBatch.RunAsync(items, static i => i.Installable, async _ =>
        {
            maxRunning = Math.Max(maxRunning, Interlocked.Increment(ref running));
            await Task.Delay(5);
            Interlocked.Decrement(ref running);
        });

        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public async Task RunAsync_SkipsItems_ThatCanNoLongerRunWhenTheirTurnComes()
    {
        var a = new Item("a", installable: true);
        var b = new Item("b", installable: true);
        var ran = new List<string>();

        var count = await AppBatch.RunAsync(new[] { a, b }, static i => i.Installable, i =>
        {
            ran.Add(i.Name);
            // e.g. b was installed outside of the batch while a was installing.
            b.Installable = false;
            return Task.CompletedTask;
        });

        Assert.Equal(1, count);
        Assert.Equal(new[] { "a" }, ran);
    }

    [Fact]
    public async Task RunAsync_DoesNotPickUpItems_ThatBecomeEligibleLater()
    {
        var a = new Item("a", installable: true);
        var b = new Item("b");
        var ran = new List<string>();

        await AppBatch.RunAsync(new[] { a, b }, static i => i.Installable, i =>
        {
            ran.Add(i.Name);
            b.Installable = true;
            return Task.CompletedTask;
        });

        Assert.Equal(new[] { "a" }, ran);
    }

    [Fact]
    public async Task RunAsync_CallsOnCompleted_AfterEachRunItem()
    {
        var items = new[]
        {
            new Item("a", selected: true, updatable: true),
            new Item("b", selected: true),
            new Item("c", selected: true, updatable: true)
        };
        var order = new List<string>();

        await AppBatch.RunAsync(
            items,
            static i => i.Updatable,
            i =>
            {
                order.Add($"run {i.Name}");
                return Task.CompletedTask;
            },
            i =>
            {
                order.Add($"done {i.Name}");
                i.Selected = false;
            });

        Assert.Equal(new[] { "run a", "done a", "run c", "done c" }, order);
        Assert.False(items[0].Selected);
        Assert.True(items[1].Selected); // not run: keeps its selection
        Assert.False(items[2].Selected);
    }

    [Fact]
    public async Task RunAsync_NothingEligible_RunsNothing()
    {
        var count = await AppBatch.RunAsync(new[] { new Item("a") }, static i => i.Installable,
            _ => throw new InvalidOperationException("must not run"));

        Assert.Equal(0, count);
    }
}
