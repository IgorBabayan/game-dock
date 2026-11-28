namespace GameDock.Presentation.ViewModels;

/// <summary>
/// What the toolbar batch buttons (Install all / Update all / Install selected / Update selected) would act on.
/// A value type compared on every card change, so the window only raises notifications when a count really changed.
/// </summary>
public readonly record struct BatchCounts(
    int Installable,
    int Updatable,
    int Removable,
    int Selected,
    int SelectedInstallable,
    int SelectedUpdatable,
    int SelectedRemovable)
{
    /// <summary>"Install selected" / "Update selected" appear only from this many selected apps.</summary>
    public const int MinSelection = 2;

    public bool CanInstallAll => Installable > 0;

    public bool CanUpdateAll => Updatable > 0;

    public bool CanRemoveAll => Removable > 0;

    public bool HasMultiSelection => Selected >= MinSelection;

    public bool CanInstallSelected => HasMultiSelection && SelectedInstallable > 0;

    public bool CanUpdateSelected => HasMultiSelection && SelectedUpdatable > 0;

    public bool CanRemoveSelected => HasMultiSelection && SelectedRemovable > 0;
}

/// <summary>Counting and sequential execution behind the batch buttons. Free of UI types so it can be unit tested.</summary>
public static class AppBatch
{
    /// <summary>Tallies <paramref name="items"/> in a single pass.</summary>
    public static BatchCounts Count<T>(
        IReadOnlyList<T> items,
        Func<T, bool> isSelected,
        Func<T, bool> canInstall,
        Func<T, bool> canUpdate,
        Func<T, bool> canRemove)
    {
        int installable = 0, updatable = 0, removable = 0;
        int selected = 0, selectedInstallable = 0, selectedUpdatable = 0, selectedRemovable = 0;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var install = canInstall(item);
            var update = canUpdate(item);
            var remove = canRemove(item);

            if (install)
                installable++;
            if (update)
                updatable++;
            if (remove)
                removable++;

            if (!isSelected(item))
                continue;

            selected++;
            if (install)
                selectedInstallable++;
            if (update)
                selectedUpdatable++;
            if (remove)
                selectedRemovable++;
        }

        return new BatchCounts(
            installable, updatable, removable, selected, selectedInstallable, selectedUpdatable, selectedRemovable);
    }

    /// <summary>Text of the single confirmation shown before "Remove all" / "Remove selected".</summary>
    public static (string Title, string Message) RemoveConfirmation(IReadOnlyList<string> names)
    {
        if (names.Count == 1)
            return ($"Remove {names[0]}?",
                $"{names[0]} will be uninstalled.\n\nYour saves and settings are not deleted.");

        var list = string.Join("\n", names.Select(static n => $"•  {n}"));
        return ($"Remove {names.Count} apps?",
            $"These apps will be uninstalled:\n{list}\n\nYour saves and settings are not deleted.");
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
