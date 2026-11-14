using ModernFormsNext;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Theming;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Views;

internal sealed partial class BatchImportView
{
    private readonly Panel destinationCard = new() { AccessibleAutomationId = "BatchDestinationCard" };
    private readonly ComboBox workflowMode = new() { AccessibleAutomationId = "BatchWorkflowMode" };
    private readonly ComboBox sessionChoice = new() { AccessibleAutomationId = "BatchSessionChoice" };
    private readonly TextBox destinationRoot = new() { AccessibleAutomationId = "BatchDestinationRoot" };
    private readonly TextBox listName = new() { AccessibleAutomationId = "BatchListName" };
    private readonly CheckBox listSubfolder = new() { AccessibleAutomationId = "BatchListSubfolder" };
    private readonly Button destinationBrowse = new DashboardButton { AccessibleAutomationId = "BatchDestinationBrowse" };
    private readonly Button destinationSave = new DashboardButton { AccessibleAutomationId = "BatchDestinationSave" };
    private readonly Label destinationTitle = AppUi.Heading("", 11), destinationResolved = AppUi.Muted();
    private readonly Panel workflowCard = new() { AccessibleAutomationId = "BatchWorkflowProgress" };
    private readonly Label workflowStage = AppUi.Heading("", 11), workflowFacts = AppUi.Muted(), workflowCurrent = AppUi.Muted();
    private readonly ProgressBar[] workflowSegments = Enumerable.Range(0, 5).Select(_ => (ProgressBar)new DashboardProgress { Minimum = 0, Maximum = 100 }).ToArray();
    private readonly List<BatchImportSession> visibleSessions = [];
    private bool selectingSession;

    private void InitializeWorkflow()
    {
        AppUi.Card(destinationCard); AppUi.Card(workflowCard);
        foreach (var control in new Control[] { workflowMode, sessionChoice, destinationRoot, listName }) AppUi.Input(control);
        AppUi.BindForeground(listSubfolder); AppUi.Secondary(destinationBrowse); AppUi.Secondary(destinationSave);
        destinationResolved.AutoEllipsis = true; destinationResolved.AccessibleAutomationId = "BatchResolvedDestination";
        workflowStage.AutoEllipsis = workflowCurrent.AutoEllipsis = true; workflowFacts.Multiline = true;
        workflowStage.AccessibleAutomationId = "BatchWorkflowStage"; workflowFacts.AccessibleAutomationId = "BatchWorkflowFacts";
        workflowCurrent.AccessibleAutomationId = "BatchWorkflowCurrent";
        destinationCard.Controls.AddRange([destinationTitle, workflowMode, sessionChoice, destinationRoot, listName, listSubfolder, destinationBrowse, destinationSave, destinationResolved]);
        workflowCard.Controls.AddRange([workflowStage, workflowFacts, workflowCurrent, .. workflowSegments]);
        body.Controls.AddRange([destinationCard, workflowCard]);
        destinationBrowse.Click += (_, _) => _ = RunAsync(async () =>
        {
            if (FindForm() is not { } owner) return;
            var dialog = new FolderBrowserDialog { Title = services.Localization["Batch.OutputDirectory"], InitialDirectory = destinationRoot.Text };
            if (await dialog.ShowDialog(owner) == DialogResult.OK && dialog.SelectedPath is { } value) destinationRoot.Text = value;
        });
        destinationSave.Click += (_, _) => _ = RunAsync(SaveDestinationAsync);
        sessionChoice.SelectedIndexChanged += (_, _) =>
        {
            if (!selectingSession && sessionChoice.SelectedIndex >= 0 && sessionChoice.SelectedIndex < visibleSessions.Count)
                Present(visibleSessions[sessionChoice.SelectedIndex]);
        };
        if (session is { } current) RememberSession(current);
    }

    private void ApplyWorkflowText()
    {
        var t = services.Localization;
        var mode = workflowMode.SelectedIndex; workflowMode.Items.Clear();
        workflowMode.Items.Add(t["Batch.Mode.Manual"]); workflowMode.Items.Add(t["Batch.Mode.Automatic"]); workflowMode.SelectedIndex = Math.Max(0, mode);
        destinationTitle.Text = destinationRoot.AccessibleName = t["Batch.OutputDirectory"];
        listName.AccessibleName = listName.Placeholder = t["Batch.ListName"];
        listSubfolder.Text = t["Batch.CreateSubfolder"];
        destinationBrowse.Text = t["Batch.ChooseDestination"]; destinationSave.Text = t["Batch.SaveDestination"];
        workflowMode.AccessibleName = t["Batch.WorkflowMode"]; sessionChoice.AccessibleName = t["Batch.Lists"];
    }

    private void RememberSession(BatchImportSession value)
    {
        selectingSession = true;
        try
        {
            var index = visibleSessions.FindIndex(existing => existing.Id == value.Id);
            if (index < 0) { visibleSessions.Add(value); sessionChoice.Items.Add(value.FileName); index = visibleSessions.Count - 1; }
            sessionChoice.SelectedIndex = index;
            destinationRoot.Text = value.OutputRoot ?? value.OutputDirectory ?? services.Settings.Current.FinalOutputDirectory;
            listName.Text = value.ListName ?? Path.GetFileNameWithoutExtension(value.FileName);
            listSubfolder.Checked = value.CreateListSubfolder;
        }
        finally { selectingSession = false; }
    }

    private async Task SaveDestinationAsync()
    {
        if (session is not { } value) return;
        try { await services.Batch.SetDestinationAsync(value, destinationRoot.Text, listName.Text, listSubfolder.Checked, token: OperationToken); }
        catch (InvalidDataException error) when (error.Message == "Batch.Error.DestinationConflict")
        {
            if (FindForm() is not { } owner) return;
            using var confirmation = AppMessageBox.Confirmation(services.Localization["Batch.OutputDirectory"],
                services.Localization["Batch.DestinationConflict.Confirm"], services.Localization["Batch.UseSharedDestination"],
                services.Localization["Common.Cancel"], "BatchDestinationConflict");
            using var registration = RegisterDialog?.Invoke(confirmation);
            if (await confirmation.ShowDialog(owner) == DialogResult.Yes)
                await services.Batch.SetDestinationAsync(value, destinationRoot.Text, listName.Text, listSubfolder.Checked, true, OperationToken);
        }
    }

    private async Task ImportFilesAsync(IReadOnlyList<string> files)
    {
        var imported = new List<BatchImportSession>();
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            OperationToken.ThrowIfCancellationRequested(); path.Text = file; await ImportAsync();
            imported.Add(session!);
        }
        if (workflowMode.SelectedIndex == 1) BeginAutomatic(imported);
    }

    private async Task ImportAndDownloadAsync()
    {
        if (session is null && string.IsNullOrWhiteSpace(path.Text))
        {
            workflowMode.SelectedIndex = 1;
            await BrowseAsync(); return;
        }
        if (session is null || !string.IsNullOrWhiteSpace(path.Text) && Path.GetFileName(path.Text) != session.FileName)
            await ImportAsync();
        if (session is { } current) BeginAutomatic([current]);
    }

    private void BeginAutomatic(IReadOnlyList<BatchImportSession> values)
    {
        if (values.Any(value => !value.DestinationConfirmed)) throw new InvalidDataException("Batch.Error.DestinationConflict");
        // The service owns preparation after this view is closed. Existing QueueProcessor owns every download.
        _ = ObserveAutomaticAsync(services.Batch.RunAutomaticAsync(values));
    }

    private async Task ObserveAutomaticAsync(Task pending)
    {
        try { await pending; }
        catch (Exception error)
        {
            services.Logger.Error("Batch automatic preparation stopped.", error);
            if (!disposed) lastErrorKey = error is InvalidDataException && error.Message.StartsWith("Batch.") ? error.Message : "Batch.Error.Analysis";
        }
        if (!disposed) { signature = ""; Render(); LayoutPage(); }
    }

    private async Task ResumeQueueAsync()
    {
        if (session is not { } current || FindForm() is not { } owner || !services.Queue.IsPaused) return;
        var linked = current.Snapshot().Where(entry => entry.QueueItemId is not null).Select(entry => entry.QueueItemId!.Value).ToHashSet();
        var others = services.Queue.ProgressSnapshot().Count(item => !linked.Contains(item.Id) && item.Status == DownloadStatus.Queued);
        using var confirmation = AppMessageBox.Confirmation(services.Localization["Batch.ResumeQueue"],
            services.Localization.Get("Batch.ResumeQueue.Confirm", others), services.Localization["Batch.ResumeQueue"],
            services.Localization["Common.Cancel"], "BatchResumeGlobalQueue");
        using var registration = RegisterDialog?.Invoke(confirmation);
        if (await confirmation.ShowDialog(owner) == DialogResult.Yes) await services.Queue.ResumeAsync(OperationToken);
    }

    private void RefreshWorkflow()
    {
        var t = services.Localization;
        var entries = session?.Snapshot() ?? [];
        var frozen = session is null || busy || services.Batch.IsOperating || entries.Any(entry => entry.QueueItemId is not null);
        foreach (var control in new Control[] { destinationRoot, listName, listSubfolder, destinationBrowse, destinationSave }) control.Enabled = !frozen;
        workflowMode.Enabled = !busy && !services.Batch.IsOperating;
        sessionChoice.Enabled = !busy && visibleSessions.Count > 1;
        var phase = session is { } current ? services.Batch.CurrentWorkflowPhase(current) : BatchWorkflowPhase.Prepared;
        workflowStage.Text = operation is { } op ? t.Get("Batch.Operation." + op.Phase, op.Completed, op.Total?.ToString(t.Culture) ?? "—") : t["Batch.Workflow." + phase];
        var ready = entries.Count(entry => entry.Status == BatchEntryStatus.Ready);
        var duplicates = entries.Count(entry => entry.Status == BatchEntryStatus.Duplicate);
        var errors = entries.Count(entry => entry.Status is BatchEntryStatus.Failed or BatchEntryStatus.Unsupported or BatchEntryStatus.Cancelled);
        var attention = entries.Count(entry => entry.Status == BatchEntryStatus.AuthRequired);
        var added = entries.Count(entry => entry.QueueItemId is not null);
        workflowFacts.Text = t.Get("Batch.Workflow.Facts", entries.Count, ready, duplicates, errors, attention, added);
        workflowCurrent.Text = entries.FirstOrDefault(entry => entry.Status is BatchEntryStatus.Analyzing or BatchEntryStatus.Downloading)?.Title ?? "";
        var destination = session?.OutputDirectory ?? services.Settings.Current.FinalOutputDirectory;
        destinationResolved.Text = destination + (session?.DestinationConfirmed == false ? "  •  " + t["Batch.Error.DestinationConflict"] : "");
        overallTip.SetToolTip(destinationResolved, destinationResolved.Text);
        buttons["ResumeQueue"].Enabled = !busy && services.Queue.IsPaused && added > 0;
        buttons["ResumeQueue"].Visible = services.Queue.IsPaused && added > 0;
        var settled = entries.Count(entry => entry.Status is not (BatchEntryStatus.Pending or BatchEntryStatus.Analyzing or BatchEntryStatus.Validating));
        workflowSegments[0].Value = session is null ? 0 : 100;
        workflowSegments[1].Value = entries.Count == 0 ? 0 : 100 * settled / entries.Count;
        workflowSegments[2].Value = phase is BatchWorkflowPhase.WaitingForQueue or BatchWorkflowPhase.Downloading or BatchWorkflowPhase.Finalizing or BatchWorkflowPhase.Completed ? 100 : 0;
        workflowSegments[3].Value = added == 0 ? 0 : 100 * entries.Count(entry => entry.Status == BatchEntryStatus.Completed) / added;
        workflowSegments[4].Value = phase == BatchWorkflowPhase.Completed ? 100 : 0;
        for (var index = 0; index < workflowSegments.Length; index++)
        { workflowSegments[index].AccessibleName = t["Batch.Workflow.Step" + (index + 1)]; overallTip.SetToolTip(workflowSegments[index], workflowSegments[index].AccessibleName); }
    }

    private void LayoutDestination(int left, int top, int width)
    {
        var line = AppTypography.TextHeight(34); var gap = 8; var content = Math.Max(1, width - 24);
        destinationCard.SetBounds(left, top, width, 1);
        if (dashboard) DownloadsVisual.Card(destinationCard);
        if (dashboard && width >= AppTypography.TextHeight(780))
        {
            var titleWidth = Math.Min(content / 2, DashboardVisual.TextWidth(destinationTitle, destinationTitle.Text) + 8);
            destinationTitle.SetBounds(12, 8, titleWidth, AppTypography.TextHeight(24));
            destinationTitle.AutoEllipsis = true;
            destinationResolved.SetBounds(destinationTitle.Right + gap, 8, content - titleWidth - gap, destinationTitle.Height);
            var fieldWidth = (content - 3 * gap) * 22 / 100;
            workflowMode.SetBounds(12, destinationTitle.Bottom + 6, fieldWidth, line);
            sessionChoice.SetBounds(workflowMode.Right + gap, workflowMode.Top, fieldWidth, line);
            listName.SetBounds(sessionChoice.Right + gap, workflowMode.Top, fieldWidth, line);
            listSubfolder.SetBounds(listName.Right + gap, workflowMode.Top, width - listName.Right - gap - 12, line);
            var browseWidth = Math.Min(content / 4, DashboardVisual.ActionWidth(destinationBrowse));
            var saveWidth = Math.Min(content / 4, DashboardVisual.ActionWidth(destinationSave));
            destinationRoot.SetBounds(12, workflowMode.Bottom + 6, content - browseWidth - saveWidth - 2 * gap, line);
            destinationBrowse.SetBounds(destinationRoot.Right + gap, destinationRoot.Top, browseWidth, line);
            destinationSave.SetBounds(destinationBrowse.Right + gap, destinationRoot.Top, saveWidth, line);
            destinationCard.Height = destinationRoot.Bottom + 8;
            return;
        }
        destinationTitle.SetBounds(12, 8, content, AppTypography.TextHeight(24));
        var columns = width >= AppTypography.TextHeight(780);
        var modeWidth = columns ? (content - 2 * gap) / 3 : content;
        workflowMode.SetBounds(12, destinationTitle.Bottom + gap, modeWidth, line);
        sessionChoice.SetBounds(columns ? workflowMode.Right + gap : 12, columns ? workflowMode.Top : workflowMode.Bottom + gap, modeWidth, line);
        listName.SetBounds(columns ? sessionChoice.Right + gap : 12, columns ? workflowMode.Top : sessionChoice.Bottom + gap, columns ? content - 2 * (modeWidth + gap) : content, line);
        var actionWidth = Math.Min(content / 3, Math.Max(DashboardVisual.ActionWidth(destinationBrowse), DashboardVisual.ActionWidth(destinationSave)));
        destinationRoot.SetBounds(12, listName.Bottom + gap, columns ? Math.Max(1, content - 2 * (actionWidth + gap)) : content, line);
        destinationBrowse.SetBounds(columns ? destinationRoot.Right + gap : 12, columns ? destinationRoot.Top : destinationRoot.Bottom + gap, columns ? actionWidth : (content - gap) / 2, line);
        destinationSave.SetBounds(destinationBrowse.Right + gap, destinationBrowse.Top, columns ? actionWidth : (content - gap) / 2, line);
        listSubfolder.SetBounds(12, destinationSave.Bottom + gap, content, line);
        destinationResolved.SetBounds(12, listSubfolder.Bottom + 2, content, AppTypography.TextHeight(22));
        destinationCard.Height = destinationResolved.Bottom + 8;
    }

    private void LayoutWorkflow(int left, int top, int width)
    {
        workflowCard.SetBounds(left, top, width, 1);
        if (dashboard) DownloadsVisual.Card(workflowCard);
        var content = Math.Max(1, width - 24);
        workflowStage.SetBounds(12, 8, content, AppTypography.TextHeight(24));
        var segmentWidth = Math.Max(1, (content - 24) / 5);
        for (var index = 0; index < workflowSegments.Length; index++) workflowSegments[index].SetBounds(12 + index * (segmentWidth + 6), workflowStage.Bottom + 5, segmentWidth, 5);
        workflowFacts.SetBounds(12, workflowSegments[0].Bottom + 8, content, dashboard ? DashboardVisual.LabelHeight(workflowFacts, content, 4) : AppTypography.TextHeight(44));
        workflowCurrent.SetBounds(12, workflowFacts.Bottom + 2, content, workflowCurrent.Text.Length == 0 ? 0 : AppTypography.TextHeight(22));
        workflowCard.Height = workflowCurrent.Bottom + 8;
    }
}
