using ModernFormsNext;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Organizer;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Views;

/// <summary>Explicit, bounded dry-run and recovery UI over the shared runtime organizer.</summary>
internal sealed class OrganizerForm : Form
{
    private readonly AppServices services;
    private readonly Panel surface = new() { Dock = DockStyle.Fill }, setup = new(), results = new(), inspector = new(), footer = new();
    private readonly ExperienceScrollPanel body = new() { AutoScroll = true }, rows = new() { AutoScroll = true, AccessibleAutomationId = "OrganizerRows" };
    private readonly Label heading = AppUi.Heading("", 20), description = AppUi.Muted(), sourceLabel = AppUi.Muted(), destinationLabel = AppUi.Muted(),
        summary = AppUi.Muted(), status = AppUi.Muted(), pageLabel = AppUi.Muted(), detailHeading = AppUi.Heading("", 12), journalLabel = AppUi.Muted();
    private readonly TextBox source = new() { AccessibleAutomationId = "OrganizerSource" }, destination = new() { AccessibleAutomationId = "OrganizerDestination" },
        details = new() { MultiLine = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, AccessibleAutomationId = "OrganizerDetails" };
    private readonly ComboBox listChoice = new() { AccessibleAutomationId = "OrganizerLists" }, filter = new() { AccessibleAutomationId = "OrganizerFilter" },
        candidateChoice = new() { AccessibleAutomationId = "OrganizerCandidates" }, journals = new() { AccessibleAutomationId = "OrganizerJournals" };
    private readonly CheckBox recursive = new() { Checked = true, AccessibleAutomationId = "OrganizerRecursive" }, requireJson = new() { Checked = true, AccessibleAutomationId = "OrganizerRequireJson" };
    private readonly Button add = Button("OrganizerAddLists"), remove = Button("OrganizerRemoveList"), browseSource = Button("OrganizerBrowseSource"),
        browseDestination = Button("OrganizerBrowseDestination"), planButton = Button("OrganizerPlan"), selectPage = Button("OrganizerSelectPage"),
        previous = Button("OrganizerPrevious"), next = Button("OrganizerNext"), resolve = Button("OrganizerResolve"),
        move = Button("OrganizerMove"), pause = Button("OrganizerPause"), cancel = Button("OrganizerCancel"), close = Button("OrganizerClose"),
        refreshJournals = Button("OrganizerRefreshJournals"), recover = Button("OrganizerRecover"), undo = Button("OrganizerUndo");
    private readonly DashboardProgress progress = new() { Minimum = 0, Maximum = 1000, AccessibleAutomationId = "OrganizerProgress" };
    private readonly List<OrganizerSourceList> lists = [];
    private readonly List<OrganizerJournalSummary> recent = [];
    private readonly List<(Panel Panel, CheckBox Selected, Label Title, Label State, Button Details)> visibleRows = [];
    private readonly HashSet<Guid> selection = [];
    private OrganizerPlan? plan;
    private OrganizerMatch? selectedMatch;
    private CancellationTokenSource? operation;
    private bool disposed, arranging, arrangingRows, rebuilding, closeRequested;
    private int page, selectedList = -1, progressQueued;
    private OrganizerProgress? latestProgress;
    public Func<Form, IDisposable?>? RegisterDialog { get; set; }
    private static Button Button(string id) => new DashboardButton { AccessibleAutomationId = id };

    public OrganizerForm(AppServices services, BatchImportSession? session = null)
    {
        this.services = services;
        Text = services.Localization["Organizer.Title"]; Size = new(1220, 900); MinimumSize = new(760, 620);
        AccessibilityObject.AutomationId = "OrganizerWindow"; AppBranding.Apply(this); ExperienceVisual.ApplyWindow(this);
        AppTypography.SetDashboardPresentation(surface, true); ExperienceVisual.Surface(surface); ExperienceVisual.Surface(body);
        Controls.Add(surface); surface.Controls.AddRange([body, footer]); body.Controls.AddRange([heading, description, setup, results, inspector]);
        foreach (var panel in new[] { setup, results, inspector, footer }) DownloadsVisual.Card(panel);
        AppUi.BindGradient(rows, DownloadsVisual.Background, DownloadsVisual.BackgroundEnd);
        foreach (var check in new[] { recursive, requireJson })
        {
            DashboardVisual.Flat(check); AppUi.BindBackground(check, DownloadsVisual.Panel); AppUi.BindForeground(check);
            AppTypography.Bind(check, 10.5f);
        }
        foreach (var input in new[] { source, destination, details }) AppUi.Input(input);
        foreach (var input in new[] { listChoice, filter, candidateChoice, journals }) AppUi.Input(input);
        foreach (var button in new[] { add, remove, browseSource, browseDestination, planButton, selectPage, previous, next, resolve, move, pause, cancel, close, refreshJournals, recover, undo })
            ExperienceVisual.Action(button, button == planButton || button == move);
        description.Multiline = summary.Multiline = status.Multiline = true;
        setup.Controls.AddRange([add, listChoice, remove, sourceLabel, source, browseSource, destinationLabel, destination, browseDestination, recursive, requireJson, planButton]);
        results.Controls.AddRange([summary, filter, selectPage, rows, previous, pageLabel, next]);
        inspector.Controls.AddRange([detailHeading, details, candidateChoice, resolve, journalLabel, journals, refreshJournals, recover, undo]);
        footer.Controls.AddRange([status, progress, move, pause, cancel, close]);
        source.Text = services.Settings.Current.FinalOutputDirectory;
        add.Click += async (_, _) => await AddListsAsync(); remove.Click += (_, _) => RemoveList();
        listChoice.SelectedIndexChanged += (_, _) => SelectList();
        destination.TextChanged += (_, _) => { if (!rebuilding && selectedList >= 0 && selectedList < lists.Count) { lists[selectedList] = lists[selectedList] with { DestinationDirectory = destination.Text }; InvalidatePlan(); } };
        source.TextChanged += (_, _) => InvalidatePlan(); recursive.CheckedChanged += (_, _) => InvalidatePlan(); requireJson.CheckedChanged += (_, _) => InvalidatePlan();
        browseSource.Click += async (_, _) => await PickFolderAsync(source); browseDestination.Click += async (_, _) => await PickFolderAsync(destination);
        planButton.Click += async (_, _) => await RunAsync(async token =>
        {
            plan = await services.Organizer.BuildPlanAsync(lists.ToArray(), source.Text, recursive.Checked, requireJson.Checked, token);
            selection.Clear(); selectedMatch = null; page = 0; RenderRows();
            if ((plan.Matches.FirstOrDefault(match => match.Status == OrganizerMatchStatus.Found) ?? plan.Matches.FirstOrDefault()) is { } first) ShowDetails(first);
        });
        filter.SelectedIndexChanged += (_, _) => { if (!rebuilding) { page = 0; RenderRows(); } };
        selectPage.Click += (_, _) => { foreach (var item in Filtered().Skip(page * DownloadOrganizerService.PageSize).Take(DownloadOrganizerService.PageSize).Where(item => item.Status == OrganizerMatchStatus.Found)) selection.Add(item.Id); RenderRows(); };
        previous.Click += (_, _) => { page = Math.Max(0, page - 1); RenderRows(); }; next.Click += (_, _) => { page++; RenderRows(); };
        resolve.Click += async (_, _) =>
        {
            if (plan is null || selectedMatch is null || candidateChoice.SelectedIndex < 0) return;
            var match = selectedMatch; var candidate = match.Candidates[candidateChoice.SelectedIndex];
            await RunAsync(async token => { await services.Organizer.ResolveCandidateAsync(plan, match.Id, candidate, token); selection.RemoveWhere(id => plan.Matches.First(value => value.Id == id).Status != OrganizerMatchStatus.Found); ShowDetails(match); RenderRows(); });
        };
        move.Click += async (_, _) => await MoveAsync();
        pause.Click += (_, _) => { services.Organizer.SetPaused(!services.Organizer.IsPaused); Localize(); };
        cancel.Click += (_, _) => operation?.Cancel(); close.Click += (_, _) => Close();
        refreshJournals.Click += async (_, _) => await RunAsync(RefreshJournalsAsync);
        recover.Click += async (_, _) => await JournalActionAsync(false); undo.Click += async (_, _) => await JournalActionAsync(true);
        journals.SelectedIndexChanged += (_, _) => RefreshEnabled();
        services.Organizer.ProgressChanged += ProgressChanged; services.Localization.LanguageChanged += LanguageChanged;
        Closing += (_, args) => { if (operation is not null) { args.Cancel = true; closeRequested = true; operation.Cancel(); } };
        SizeChanged += (_, _) => Arrange(); body.SizeChanged += (_, _) => Arrange(); body.ViewportChanged += (_, _) => Arrange();
        rows.ViewportChanged += (_, _) => ArrangeRows(); AppTypography.RegisterLayout(surface, Arrange);
        if (session is not null)
        {
            var folder = session.OutputDirectory ?? DownloadOrganizerService.SuggestDestination(services.Settings.Current.FinalOutputDirectory, Path.GetFileNameWithoutExtension(session.FileName));
            lists.Add(new(session.FileName, folder, Entries: session.Snapshot()));
        }
        Localize(); RenderLists(); RenderRows(); Arrange();
        Shown += async (_, _) => await RunAsync(RefreshJournalsAsync);
    }

    private async Task AddListsAsync()
    {
        var dialog = new OpenFileDialog { AllowMultiple = true, Title = services.Localization["Organizer.AddLists"] };
        dialog.AddFilter("TXT / CSV / JSON", "*.txt", "*.csv", "*.json");
        if (await dialog.ShowDialog(this) != DialogResult.OK) return;
        await RunAsync(_ =>
        {
            foreach (var file in dialog.FileNames)
            {
                if (lists.Count >= 100) throw new IOException("Organizer.Error.Limit");
                if (lists.Any(list => string.Equals(list.SourceFile, file, StringComparison.OrdinalIgnoreCase))) continue;
                lists.Add(new(Path.GetFileName(file), DownloadOrganizerService.SuggestDestination(services.Settings.Current.FinalOutputDirectory, Path.GetFileNameWithoutExtension(file)), file));
            }
            InvalidatePlan(); RenderLists(); return Task.CompletedTask;
        });
    }
    private async Task PickFolderAsync(TextBox target)
    {
        var dialog = new FolderBrowserDialog { Title = target == source ? services.Localization["Organizer.Source"] : services.Localization["Organizer.Destination"], InitialDirectory = target.Text };
        if (await dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedPath is { } path) target.Text = path;
    }
    private void RemoveList() { if (selectedList < 0) return; lists.RemoveAt(selectedList); selectedList = -1; InvalidatePlan(); RenderLists(); }
    private void RenderLists()
    {
        rebuilding = true; listChoice.Items.Clear(); foreach (var list in lists) listChoice.Items.Add(list.Name);
        listChoice.SelectedIndex = lists.Count == 0 ? -1 : Math.Clamp(selectedList, 0, lists.Count - 1); rebuilding = false; SelectList();
    }
    private void SelectList()
    {
        if (rebuilding) return; selectedList = listChoice.SelectedIndex; rebuilding = true;
        destination.Text = selectedList >= 0 ? lists[selectedList].DestinationDirectory : ""; rebuilding = false; RefreshEnabled();
    }
    private void InvalidatePlan()
    {
        if (rebuilding || plan is null) return;
        plan = null; selection.Clear(); selectedMatch = null; details.Text = ""; RenderRows();
    }
    private OrganizerMatch[] Filtered() => plan?.Matches.Where(match => filter.SelectedIndex <= 0 || (int)match.Status == filter.SelectedIndex - 1).ToArray() ?? [];
    private void RenderRows()
    {
        foreach (var row in visibleRows) { rows.Controls.Remove(row.Panel); row.Panel.Dispose(); } visibleRows.Clear();
        var values = Filtered(); var pages = Math.Max(1, (values.Length + DownloadOrganizerService.PageSize - 1) / DownloadOrganizerService.PageSize);
        page = Math.Clamp(page, 0, pages - 1); previous.Enabled = page > 0; next.Enabled = page + 1 < pages;
        foreach (var match in values.Skip(page * DownloadOrganizerService.PageSize).Take(DownloadOrganizerService.PageSize))
        {
            var panel = new Panel(); DownloadsVisual.Card(panel);
            // Rows are assembled before attaching to the live tree. Give their subtree
            // its presentation recipe now so child ParentChanged applies dashboard sizes.
            AppTypography.SetDashboardPresentation(panel, true);
            var selected = new CheckBox { Checked = selection.Contains(match.Id), Enabled = match.Status == OrganizerMatchStatus.Found, AccessibleAutomationId = "OrganizerSelect-" + match.Id.ToString("N"), AccessibleName = match.ListName + " — " + match.Url };
            DashboardVisual.Flat(selected); AppUi.BindBackground(selected, DownloadsVisual.Panel); AppUi.BindForeground(selected);
            var title = AppUi.Heading(match.ListName + " · " + (match.MediaPath is null ? match.Url : Path.GetFileName(match.MediaPath)), 11f); title.AutoEllipsis = true;
            var state = AppUi.Muted(services.Localization["Organizer.Status." + match.Status] + " · " + services.Localization[match.ReasonKey]); state.AutoEllipsis = true;
            AppTypography.Bind(state, 10.5f);
            AppUi.BindForeground(state, match.Status == OrganizerMatchStatus.Found ? AppThemeTokens.Success : match.Status is OrganizerMatchStatus.Missing or OrganizerMatchStatus.AlreadyOrganized ? AppThemeTokens.TextSecondary : AppThemeTokens.Warning);
            var inspect = Button("OrganizerInspect-" + match.Id.ToString("N")); inspect.Text = services.Localization["Organizer.Details"]; ExperienceVisual.Action(inspect);
            selected.CheckedChanged += (_, _) => { if (selected.Checked) selection.Add(match.Id); else selection.Remove(match.Id); RefreshEnabled(); };
            inspect.Click += (_, _) => ShowDetails(match); panel.Controls.AddRange([selected, title, state, inspect]); rows.Controls.Add(panel); visibleRows.Add((panel, selected, title, state, inspect));
        }
        pageLabel.Text = $"{page + 1} / {pages}";
        summary.Text = services.Localization.Get("Organizer.Summary", plan?.Matches.Count ?? 0, plan?.Matches.Count(match => match.Status == OrganizerMatchStatus.Found) ?? 0, selection.Count,
            DashboardStyle.Bytes(plan?.Matches.Sum(match => match.Bytes) ?? 0));
        RefreshEnabled(); Arrange();
    }
    private void ShowDetails(OrganizerMatch match)
    {
        selectedMatch = match; var t = services.Localization;
        detailHeading.Text = t["Organizer.Details"] + " · " + t["Organizer.Status." + match.Status];
        details.Text = string.Join(Environment.NewLine, new[] { match.ListName, match.Url, t[match.ReasonKey], "", match.MediaPath ?? "—", "→ " + match.DestinationDirectory, "" }
            .Concat(match.Files.Select(file => $"{(file.CopyOnly ? t["Organizer.CopyMetadata"] : t["Organizer.MoveFile"])}  {file.Source}\n→ {file.Destination}\n{DashboardStyle.Bytes(file.Length)} · SHA-256 {file.Sha256}")));
        candidateChoice.Items.Clear(); foreach (var candidate in match.Candidates) candidateChoice.Items.Add(candidate); candidateChoice.SelectedIndex = match.Candidates.Length > 0 ? 0 : -1; RefreshEnabled();
    }
    private async Task MoveAsync()
    {
        if (plan is null || selection.Count == 0) return;
        var selected = plan.Matches.Where(match => selection.Contains(match.Id)).ToArray();
        using var confirm = AppMessageBox.Confirmation(services.Localization["Organizer.Title"], services.Localization.Get("Organizer.ConfirmMove", selected.Length, selected.Sum(match => match.Files.Count), DashboardStyle.Bytes(selected.Sum(match => match.Bytes))),
            services.Localization["Organizer.Move"], services.Localization["Common.Cancel"], "OrganizerConfirmMove");
        using var registration = RegisterDialog?.Invoke(confirm);
        if (await confirm.ShowDialog(this) != DialogResult.Yes) return;
        var selectedPlan = plan; var ids = selection.ToArray();
        await RunAsync(async token => { await services.Organizer.MoveSelectedAsync(selectedPlan, ids, token); InvalidatePlan(); await RefreshJournalsAsync(token); });
    }
    private async Task JournalActionAsync(bool reverse)
    {
        if (journals.SelectedIndex < 0 || journals.SelectedIndex >= recent.Count) return;
        var journal = recent[journals.SelectedIndex];
        using var confirm = AppMessageBox.Confirmation(services.Localization["Organizer.Title"], services.Localization[reverse ? "Organizer.ConfirmUndo" : "Organizer.ConfirmRecovery"],
            services.Localization[reverse ? "Organizer.Undo" : "Organizer.Recover"], services.Localization["Common.Cancel"], "OrganizerConfirmJournal");
        using var registration = RegisterDialog?.Invoke(confirm);
        if (await confirm.ShowDialog(this) != DialogResult.Yes) return;
        await RunAsync(async token => { if (reverse) await services.Organizer.UndoAsync(journal.Id, token); else await services.Organizer.RecoverAsync(journal.Id, token); InvalidatePlan(); await RefreshJournalsAsync(token); });
    }
    private async Task RefreshJournalsAsync(CancellationToken token)
    {
        var values = await Task.Run(() => services.Organizer.JournalsAsync(token), token); if (disposed) return;
        recent.Clear(); recent.AddRange(values); journals.Items.Clear();
        foreach (var journal in recent) journals.Items.Add($"{journal.CreatedAt.ToLocalTime():g} · {services.Localization["Organizer.Journal." + journal.State]} · {journal.EntryCount}");
        journals.SelectedIndex = recent.Count > 0 ? 0 : -1; RefreshEnabled();
    }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (operation is not null || disposed) return;
        using var cancellation = new CancellationTokenSource(); operation = cancellation; RefreshEnabled(); status.Text = services.Localization["Organizer.Working"];
        try { await action(cancellation.Token); }
        catch (Exception error) { if (!disposed) status.Text = services.Localization[DownloadOrganizerService.SafeError(error)]; }
        finally { operation = null; if (!disposed) { services.Organizer.SetPaused(false); RefreshEnabled(); Localize(); if (closeRequested) Close(); } }
    }
    private void ProgressChanged(object? sender, OrganizerProgress value)
    {
        latestProgress = value; if (disposed || Interlocked.Exchange(ref progressQueued, 1) != 0) return;
        Application.RunOnUIThread(() =>
        {
            Interlocked.Exchange(ref progressQueued, 0); if (disposed || latestProgress is not { } current) return;
            status.Text = services.Localization[current.Phase] + " · " + current.CompletedFiles + " / " + (current.TotalFiles == 0 ? "—" : current.TotalFiles.ToString()) + " · " + DashboardStyle.Bytes(current.CompletedBytes) + " / " + DashboardStyle.Bytes(current.TotalBytes) + (current.CurrentName is null ? "" : " · " + current.CurrentName);
            progress.Value = current.TotalBytes > 0 ? (int)Math.Clamp(current.CompletedBytes * 1000d / current.TotalBytes, 0, 1000) : current.TotalFiles > 0 ? current.CompletedFiles * 1000 / current.TotalFiles : 0;
        });
    }
    private void RefreshEnabled()
    {
        var idle = operation is null;
        summary.Text = services.Localization.Get("Organizer.Summary", plan?.Matches.Count ?? 0, plan?.Matches.Count(match => match.Status == OrganizerMatchStatus.Found) ?? 0, selection.Count,
            DashboardStyle.Bytes(plan?.Matches.Sum(match => match.Bytes) ?? 0));
        setup.Enabled = filter.Enabled = selectPage.Enabled = rows.Enabled = idle;
        move.Enabled = idle && plan is not null && selection.Count > 0; pause.Enabled = cancel.Enabled = !idle;
        remove.Enabled = destination.Enabled = browseDestination.Enabled = idle && selectedList >= 0; planButton.Enabled = idle && lists.Count > 0;
        resolve.Enabled = idle && selectedMatch is { Candidates.Length: > 0, Status: OrganizerMatchStatus.Ambiguous or OrganizerMatchStatus.Conflict };
        refreshJournals.Enabled = journals.Enabled = idle;
        var current = journals.SelectedIndex >= 0 && journals.SelectedIndex < recent.Count ? recent[journals.SelectedIndex] : null;
        recover.Enabled = idle && current is not null && current.State is not (OrganizerJournalState.Completed or OrganizerJournalState.Undone);
        undo.Enabled = idle && current?.State == OrganizerJournalState.Completed;
    }
    private void Localize()
    {
        var t = services.Localization; Text = heading.Text = t["Organizer.Title"]; description.Text = t["Organizer.Description"];
        add.Text = t["Organizer.AddLists"]; remove.Text = t["Organizer.RemoveList"]; sourceLabel.Text = source.AccessibleName = t["Organizer.Source"];
        destinationLabel.Text = destination.AccessibleName = t["Organizer.Destination"]; browseSource.Text = browseDestination.Text = t["Organizer.Browse"];
        recursive.Text = t["Organizer.Recursive"]; requireJson.Text = t["Organizer.RequireJson"]; planButton.Text = t["Organizer.Plan"]; selectPage.Text = t["Organizer.SelectPage"];
        previous.Text = "‹"; next.Text = "›"; previous.AccessibleName = t["Organizer.Previous"]; next.AccessibleName = t["Organizer.Next"];
        move.Text = t["Organizer.Move"]; pause.Text = t[services.Organizer.IsPaused ? "Organizer.Resume" : "Organizer.Pause"]; cancel.Text = t["Common.Cancel"]; close.Text = t["Common.Close"];
        resolve.Text = t["Organizer.Resolve"]; detailHeading.Text = t["Organizer.Details"]; journalLabel.Text = t["Organizer.Journals"]; refreshJournals.Text = t["Organizer.Refresh"]; recover.Text = t["Organizer.Recover"]; undo.Text = t["Organizer.Undo"];
        rebuilding = true; var selected = filter.SelectedIndex; filter.Items.Clear(); filter.Items.Add(t["Organizer.All"]); foreach (var value in Enum.GetValues<OrganizerMatchStatus>()) filter.Items.Add(t["Organizer.Status." + value]); filter.SelectedIndex = Math.Max(0, selected); rebuilding = false;
        Arrange();
    }
    private void Arrange()
    {
        if (arranging || disposed) return; arranging = true;
        try
        {
            var gap = 12; var line = AppTypography.TextHeight(36); var footerHeight = line * 2 + 65;
            footer.SetBounds(0, Math.Max(0, surface.Height - footerHeight), surface.Width, footerHeight); body.SetBounds(0, 0, surface.Width, footer.Top);
            var origin = body.DisplayRectangle.Location; var width = Math.Max(1, body.DisplayRectangle.Width - 32); var x = origin.X + 16; var y = origin.Y + 16;
            heading.SetBounds(x, y, width, AppTypography.TextHeight(40)); description.SetBounds(x, heading.Bottom + 6, width, DashboardVisual.LabelHeight(description, width, 3));
            setup.SetBounds(x, description.Bottom + 12, width, line * 5 + 96);
            var removeWidth = Math.Min(width / 3, DashboardVisual.ActionWidth(remove));
            var browseWidth = Math.Min(width / 3, DashboardVisual.ActionWidth(browseSource));
            add.SetBounds(14, 12, Math.Min(DashboardVisual.ActionWidth(add), width / 3), line); remove.SetBounds(width - 14 - removeWidth, 12, removeWidth, line); listChoice.SetBounds(add.Right + gap, 12, Math.Max(1, remove.Left - add.Right - gap * 2), line);
            sourceLabel.SetBounds(14, add.Bottom + 10, width - 28, AppTypography.TextHeight(22)); source.SetBounds(14, sourceLabel.Bottom + 4, width - browseWidth - 38, line); browseSource.SetBounds(source.Right + 10, source.Top, browseWidth, line);
            destinationLabel.SetBounds(14, source.Bottom + 8, width - 28, AppTypography.TextHeight(22)); destination.SetBounds(14, destinationLabel.Bottom + 4, width - browseWidth - 38, line); browseDestination.SetBounds(destination.Right + 10, destination.Top, browseWidth, line);
            var checksInline = width >= AppTypography.TextHeight(900);
            recursive.SetBounds(14, destination.Bottom + 12, checksInline ? (width - 28) / 2 : width - 28, line);
            requireJson.SetBounds(checksInline ? width / 2 : 14, checksInline ? recursive.Top : recursive.Bottom + 6, checksInline ? width / 2 - 14 : width - 28, line);
            planButton.SetBounds(14, requireJson.Bottom + 8, Math.Min(width - 28, DashboardVisual.ActionWidth(planButton)), line); setup.Height = planButton.Bottom + 14;
            var wide = width >= AppTypography.TextHeight(1000); var leftWidth = wide ? (int)(width * .63) : width;
            results.SetBounds(x, setup.Bottom + 12, leftWidth, AppTypography.TextHeight(470));
            summary.SetBounds(14, 12, leftWidth - 28, AppTypography.TextHeight(44)); filter.SetBounds(14, summary.Bottom + 8, (leftWidth - 40) / 2, line); selectPage.SetBounds(filter.Right + 12, filter.Top, leftWidth - filter.Right - 26, line);
            previous.SetBounds(14, results.Height - line - 12, 44, line); next.SetBounds(leftWidth - 58, previous.Top, 44, line); pageLabel.SetBounds(previous.Right + 8, previous.Top, Math.Max(1, next.Left - previous.Right - 16), line);
            rows.SetBounds(8, filter.Bottom + 10, leftWidth - 16, Math.Max(1, previous.Top - filter.Bottom - 22));
            inspector.SetBounds(wide ? results.Right + 12 : x, wide ? results.Top : results.Bottom + 12, wide ? width - leftWidth - 12 : width, results.Height);
            var iw = inspector.Width; detailHeading.SetBounds(14, 12, iw - 28, AppTypography.TextHeight(26)); details.SetBounds(14, detailHeading.Bottom + 8, iw - 28, AppTypography.TextHeight(180));
            candidateChoice.SetBounds(14, details.Bottom + 8, Math.Max(1, iw - 28), line); resolve.SetBounds(14, candidateChoice.Bottom + 8, iw - 28, line);
            journalLabel.SetBounds(14, resolve.Bottom + 12, iw - 28, AppTypography.TextHeight(22)); journals.SetBounds(14, journalLabel.Bottom + 4, iw - 28, line);
            var bw = Math.Max(1, (iw - 44) / 3); refreshJournals.SetBounds(14, journals.Bottom + 8, bw, line); recover.SetBounds(refreshJournals.Right + 8, refreshJournals.Top, bw, line); undo.SetBounds(recover.Right + 8, recover.Top, bw, line); inspector.Height = Math.Max(results.Height, undo.Bottom + 14);
            status.SetBounds(16, 10, Math.Max(1, footer.Width - 32), AppTypography.TextHeight(40)); progress.SetBounds(16, status.Bottom + 4, Math.Max(1, footer.Width - 32), 8);
            var actionY = progress.Bottom + 12; var fw = Math.Max(1, (footer.Width - 68) / 4); move.SetBounds(16, actionY, fw, line); pause.SetBounds(move.Right + 12, actionY, fw, line); cancel.SetBounds(pause.Right + 12, actionY, fw, line); close.SetBounds(cancel.Right + 12, actionY, fw, line);
            ArrangeRows();
        }
        finally { arranging = false; }
    }
    private void ArrangeRows()
    {
        if (arrangingRows || disposed) return;
        arrangingRows = true; rows.SuspendLayout();
        try
        {
        var origin = rows.DisplayRectangle.Location; var width = Math.Max(1, rows.DisplayRectangle.Width - 8); var rowHeight = AppTypography.TextHeight(68); var y = origin.Y;
        foreach (var row in visibleRows)
        {
            row.Panel.SetBounds(origin.X, y, width, rowHeight); row.Selected.SetBounds(10, 10, 28, rowHeight - 20);
            var actionWidth = Math.Min(DashboardVisual.ActionWidth(row.Details), width / 3); row.Details.SetBounds(width - actionWidth - 10, 14, actionWidth, rowHeight - 28);
            row.Title.SetBounds(44, 9, Math.Max(1, row.Details.Left - 56), rowHeight / 2 - 6); row.State.SetBounds(44, rowHeight / 2, Math.Max(1, row.Details.Left - 56), rowHeight / 2 - 8); y += rowHeight + 6;
        }
        }
        finally { rows.ResumeLayout(); arrangingRows = false; }
    }
    private void LanguageChanged(object? sender, EventArgs args) => Application.RunOnUIThread(() => { if (!disposed) { Localize(); RenderRows(); if (selectedMatch is not null) ShowDetails(selectedMatch); } });
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed) { disposed = true; operation?.Cancel(); services.Organizer.ProgressChanged -= ProgressChanged; services.Localization.LanguageChanged -= LanguageChanged; }
        base.Dispose(disposing);
    }
}
