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
    private readonly Label[] destinationLabels = Enumerable.Range(0, 4).Select(index =>
        { var label = AppUi.Muted(); label.AccessibleAutomationId = "BatchDestinationLabel" + index; label.Multiline = true; return label; }).ToArray();
    private readonly Panel workflowCard = new() { AccessibleAutomationId = "BatchWorkflowProgress" };
    private readonly Label workflowStage = AppUi.Heading("", 11), workflowFacts = AppUi.Muted(), workflowCurrent = AppUi.Muted();
    private readonly ProgressBar[] workflowSegments = Enumerable.Range(0, 5).Select(_ => (ProgressBar)new DashboardProgress { Minimum = 0, Maximum = 100 }).ToArray();
    private readonly Label[] workflowCaptions = Enumerable.Range(0, 5).Select(index =>
        { var label = AppUi.Muted(); label.AccessibleAutomationId = "BatchWorkflowCaption" + (index + 1); label.Multiline = true; return label; }).ToArray();
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
        destinationCard.Controls.AddRange(destinationLabels); workflowCard.Controls.AddRange(workflowCaptions);
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
        var imported = await services.Batch.ImportFilesAsync(files, QualityPreset.All[quality.SelectedIndex].Id,
            (PreferredVideoContainer)container.SelectedIndex, (int)workers.Value, OperationToken, OperationProgress());
        foreach (var value in imported)
        {
            session = value; RememberSession(value);
        }
        path.Text = files.Last(); selection.Clear(); page = 0;
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
        if (!disposed) { signature = ""; Render(); }
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
#if DEBUG
        using var traceScope = TraceBatchScope("RefreshWorkflow");
#endif
        var t = services.Localization;
        var entries = PresentationEntries();
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
        foreach (var label in destinationLabels) { label.Visible = dashboard; if (!dashboard) label.SetBounds(0, 0, 0, 0); }
        if (dashboard) DownloadsVisual.Card(destinationCard);
        if (dashboard)
        {
            var t = services.Localization;
            var keys = new[] { "Batch.WorkflowMode", "Batch.Lists", "Batch.SubfolderName", "Batch.RootFolder" };
            for (var index = 0; index < keys.Length; index++) destinationLabels[index].Text = t[keys[index]];
            destinationTitle.Multiline = true; destinationTitle.AutoEllipsis = false;
            destinationTitle.SetBounds(12, 10, content, DashboardVisual.LabelHeight(destinationTitle, content, 3));
            var twoColumns = content >= AppTypography.TextHeight(580);
            var fieldWidth = twoColumns ? (content - 12) / 2 : content;
            var fields = new Control[] { workflowMode, sessionChoice, listName, destinationRoot };
            var y = destinationTitle.Bottom + 8;
            for (var index = 0; index < fields.Length; index += twoColumns ? 2 : 1)
            {
                var count = Math.Min(twoColumns ? 2 : 1, fields.Length - index);
                var captionHeight = Enumerable.Range(index, count).Max(i => DashboardVisual.LabelHeight(destinationLabels[i], fieldWidth, 3));
                for (var offset = 0; offset < count; offset++)
                {
                    var x = 12 + offset * (fieldWidth + 12);
                    destinationLabels[index + offset].SetBounds(x, y, fieldWidth, captionHeight);
                    fields[index + offset].SetBounds(x, y + captionHeight + 3, fieldWidth, line);
                }
                y += captionHeight + 3 + line + 8;
            }
            // A checkbox and two independent actions have measured widths; they
            // wrap as a unit instead of being squeezed into a fourth text field.
            DownloadsVisual.Button(destinationBrowse); DownloadsVisual.Button(destinationSave);
            var browseWidth = Math.Min(content, DashboardVisual.ActionWidth(destinationBrowse));
            var saveWidth = Math.Min(content, DashboardVisual.ActionWidth(destinationSave));
            var checkWidth = Math.Min(content, DashboardVisual.TextWidth(listSubfolder, listSubfolder.Text) + AppTypography.TextHeight(32));
            listSubfolder.SetBounds(12, y, checkWidth, line);
            var actionsFit = checkWidth + browseWidth + saveWidth + 3 * gap <= content;
            var actionTop = actionsFit ? y : listSubfolder.Bottom + gap;
            destinationBrowse.SetBounds(actionsFit ? width - 12 - browseWidth - saveWidth - gap : 12, actionTop, browseWidth, line);
            var saveInline = destinationBrowse.Right + gap + saveWidth <= width - 12;
            destinationSave.SetBounds(saveInline ? destinationBrowse.Right + gap : 12, saveInline ? actionTop : destinationBrowse.Bottom + gap, saveWidth, line);
            destinationResolved.SetBounds(12, Math.Max(listSubfolder.Bottom, destinationSave.Bottom) + 6, content, AppTypography.TextHeight(22));
            destinationCard.Height = destinationResolved.Bottom + 10;
            return;
        }
        destinationTitle.Multiline = false; destinationTitle.AutoEllipsis = true;
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
        foreach (var caption in workflowCaptions) { caption.Visible = dashboard; if (!dashboard) caption.SetBounds(0, 0, 0, 0); }
        if (dashboard)
        {
            var t = services.Localization;
            workflowStage.Multiline = workflowFacts.Multiline = workflowCurrent.Multiline = true;
            workflowStage.AutoEllipsis = workflowFacts.AutoEllipsis = false;
            workflowCurrent.AutoEllipsis = true;
            // Reserve every localized phase before work starts. Progress changes
            // never need a layout pass, even when the next instruction wraps.
            var phaseTexts = Enum.GetValues<BatchWorkflowPhase>().Select(phase => t["Batch.Workflow." + phase])
                .Concat(new[] { t.Get("Batch.Operation.Import", int.MaxValue, int.MaxValue), t.Get("Batch.Operation.Add", int.MaxValue, int.MaxValue) });
            workflowStage.SetBounds(12, 10, content, Math.Max(AppTypography.TextHeight(38), phaseTexts.Max(text => MeasureWorkflowText(workflowStage, text, content))));
            var stepWidth = Math.Max(1, (content - 24) / 5);
            for (var index = 0; index < workflowCaptions.Length; index++)
            {
                workflowCaptions[index].Text = (index + 1) + " · " + t["Batch.Workflow.Caption" + (index + 1)];
                workflowCaptions[index].AccessibleName = t["Batch.Workflow.Step" + (index + 1)];
                overallTip.SetToolTip(workflowCaptions[index], workflowCaptions[index].AccessibleName);
            }
            var captionHeight = workflowCaptions.Max(caption => MeasureWorkflowText(caption, caption.Text, stepWidth));
            for (var index = 0; index < workflowSegments.Length; index++)
            {
                var x = 12 + index * (stepWidth + 6);
                workflowCaptions[index].SetBounds(x, workflowStage.Bottom + 10, stepWidth, captionHeight);
                workflowSegments[index].SetBounds(x, workflowCaptions[index].Bottom + 5, stepWidth, 5);
            }
            var largestFacts = t.Get("Batch.Workflow.Facts", int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
            workflowFacts.SetBounds(12, workflowSegments[0].Bottom + 10, content, MeasureWorkflowText(workflowFacts, largestFacts, content));
            workflowCurrent.SetBounds(12, workflowFacts.Bottom + 6, content, AppTypography.TextHeight(40));
            workflowCard.Height = workflowCurrent.Bottom + 10;
            return;
        }
        workflowStage.Multiline = workflowCurrent.Multiline = false;
        workflowStage.AutoEllipsis = workflowCurrent.AutoEllipsis = true;
        workflowStage.SetBounds(12, 8, content, AppTypography.TextHeight(24));
        var segmentWidth = Math.Max(1, (content - 24) / 5);
        for (var index = 0; index < workflowSegments.Length; index++) workflowSegments[index].SetBounds(12 + index * (segmentWidth + 6), workflowStage.Bottom + 5, segmentWidth, 5);
        workflowFacts.SetBounds(12, workflowSegments[0].Bottom + 8, content, dashboard ? DashboardVisual.LabelHeight(workflowFacts, content, 4) : AppTypography.TextHeight(44));
        workflowCurrent.SetBounds(12, workflowFacts.Bottom + 2, content, workflowCurrent.Text.Length == 0 ? 0 : AppTypography.TextHeight(22));
        workflowCard.Height = workflowCurrent.Bottom + 8;
    }

    private static int MeasureWorkflowText(Label label, string text, int width)
    {
        var style = label.CurrentStyle;
        return (int)Math.Ceiling(TextMeasurer.MeasureText(text, style.GetFont(), style.GetFontSize(),
            new System.Drawing.Size(Math.Max(1, width), int.MaxValue), AppTypography.TextStyle(label)).Height) + 4;
    }
}
