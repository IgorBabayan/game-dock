namespace GameDock.Presentation.ViewModels;

/// <summary>
/// What the toolbar batch buttons (Install all / Update all / Install selected / Update selected) would act on.
/// A value type compared on every card change, so the window only raises notifications when a count really changed.
/// </summary>
public readonly record struct BatchCounts(
    int Installable,
    int Updatable,
    int Selected,
    int SelectedInstallable,
    int SelectedUpdatable)
{
    /// <summary>"Install selected" / "Update selected" appear only from this many selected apps.</summary>
    public const int MinSelection = 2;

    public bool CanInstallAll => Installable > 0;

    public bool CanUpdateAll => Updatable > 0;

    public bool HasMultiSelection => Selected >= MinSelection;

    public bool CanInstallSelected => HasMultiSelection && SelectedInstallable > 0;

    public bool CanUpdateSelected => HasMultiSelection && SelectedUpdatable > 0;
}

/// <summary>Counting and sequential execution behind the batch buttons. Free of UI types so it can be unit tested.</summary>
public static class AppBatch
{
    /// <summary>Tallies <paramref name="items"/> in a single pass.</summary>
    public static BatchCounts Count<T>(
        IReadOnlyList<T> items,
        Func<T, bool> isSelected,
        Func<T, bool> canInstall,
        Func<T, bool> canUpdate)
    {
        int installable = 0, updatable = 0, selected = 0, selectedInstallable = 0, selectedUpdatable = 0;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var install = canInstall(item);
            var update = canUpdate(item);

            if (install)
                installable++;
            if (update)
                updatable++;

            if (!isSelected(item))
                continue;

            selected++;
            if (install)
                selectedInstallable++;
            if (update)
                selectedUpdatable++;
        }

        return new BatchCounts(installable, updatable, selected, selectedInstallable, selectedUpdatable);
    }

    /// <summary>Text of a batch button, e.g. "INSTALL ALL (3)".</summary>
    public static string Label(string action, int count) => $"{action} ({count})";

    /// <summary>
    /// Runs <paramref name="run"/> for the items that can run when the batch starts, one after another: the installer
    /// serializes operations anyway, and one download at a time keeps bandwidth, disk and CPU for the app in front.
    /// <paramref name="canRun"/> is checked again right before each item, as its state may change meanwhile
    /// (installed or removed elsewhere, already busy).
    /// </summary>
    /// <param name="onCompleted">Called after each item that was run (e.g. to clear its selection).</param>
    /// <returns>How many items were run.</returns>
    public static async Task<int> RunAsync<T>(
        IEnumerable<T> items,
        Func<T, bool> canRun,
        Func<T, Task> run,
        Action<T>? onCompleted = null)
    {
        // Snapshot first: running an item moves it between the launcher's sections.
        var targets = items.Where(canRun).ToList();
        var count = 0;

        foreach (var item in targets)
        {
            if (!canRun(item))
                continue;

            await run(item);
            count++;
            onCompleted?.Invoke(item);
        }

        return count;
    }
}
