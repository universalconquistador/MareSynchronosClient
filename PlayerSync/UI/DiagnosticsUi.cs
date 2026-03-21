using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.Models;
using MareSynchronos.UI.ModernUi;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Numerics;

namespace MareSynchronos.UI;

public class DiagnosticsUi : WindowMediatorSubscriberBase
{
    private readonly Progress<(DiagnosticsTestState State, string Status)> _diagnosticsProgress = new();
    private readonly HttpClient _httpClient;
    private readonly ConcurrentQueue<(DiagnosticsTestState State, string Status)> _pendingResultTexts = new();
    private CancellationTokenSource? _diagnosticsCancellationTokenSource;
    private Task? _diagnosticsRunTask;
    private bool _isDiagTaskRunning = false;
    private readonly List<(DiagnosticsTestState State, string Status)> _resultTexts = new();
    private string _finalResults = "";
    private readonly UiTheme _theme;

    public DiagnosticsUi(ILogger<DiagnosticsUi> logger, MareMediator mediator, 
        PerformanceCollectorService performanceCollectorService, HttpClient httpClient, UiTheme theme)
        : base(logger, mediator, "PlayerSync Diagnostics", performanceCollectorService)
    {
        _httpClient = httpClient;
        _theme = theme;

        SizeConstraints = new()
        {
            MinimumSize = new()
            {
                X = 600,
                Y = 400
            },
            MaximumSize = new()
            {
                X = 600,
                Y = 400
            }
        };

        Flags |= ImGuiWindowFlags.NoResize;

        _diagnosticsProgress.ProgressChanged += DiagnosticsProgress_ProgressChanged;

    }

    protected override void DrawInternal()
    {
        using var _ = _theme.PushWindowStyle();

        // drain the queue to draw out the results
        while (_pendingResultTexts.TryDequeue(out var pendingText))
            _resultTexts.Add(pendingText);

        var btnText = _isDiagTaskRunning ? "Cancel Run" : "Run Diagnostics";
        if (ImGui.Button(btnText))
        {
            if (_isDiagTaskRunning)
            {
                CancelDiagnostics();
            }
            else
            {
                StartDiagnostics();
            }
        }

        ImGui.TextUnformatted("Status: " + (_isDiagTaskRunning ? "Running..." : "Idle"));

        var buttonHeight = ImGui.GetFrameHeight();
        var spacingY = ImGui.GetStyle().ItemSpacing.Y;
        float childHeight = ImGui.GetContentRegionAvail().Y - spacingY - buttonHeight;
        childHeight = MathF.Max(childHeight, 1f);

        ImGui.BeginChild("results", new Vector2(0, childHeight), true);

        foreach (var result in _resultTexts)
        {
            if (result.State == DiagnosticsTestState.Passed || result.State == DiagnosticsTestState.Failed)
            {
                var color = result.State == DiagnosticsTestState.Passed ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed;
                ImGui.TextColoredWrapped(color, result.Status);
                ImGuiHelpers.ScaledDummy(5f);
            }
            else
            {
                ImGui.TextUnformatted(result.Status);
            }
        }

        ImGui.EndChild();

        using (ImRaii.Disabled(_diagnosticsRunTask == null || !_diagnosticsRunTask.IsCompleted || _finalResults == String.Empty))
        {
            if (ImGui.Button("Copy Results"))
            {
                ImGui.SetClipboardText(_finalResults);
            }
        }
    }

    private void StartDiagnostics()
    {
        if (_isDiagTaskRunning) return;

        _isDiagTaskRunning = true;

        _diagnosticsCancellationTokenSource?.Cancel();
        _diagnosticsCancellationTokenSource?.Dispose();
        _diagnosticsCancellationTokenSource = new CancellationTokenSource();
        CancellationToken diagnosticsCancellationToken = _diagnosticsCancellationTokenSource.Token;

        _resultTexts.Clear();

        _diagnosticsRunTask = Task.Run(async () =>
        {
            try
            {
                _finalResults = await DiagnosticTesting.RunAllDiagnosticTests(_diagnosticsProgress, _httpClient, diagnosticsCancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                //
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Diagnostics test failed.");
            }
            finally
            {
                _isDiagTaskRunning = false;
            }
        }, diagnosticsCancellationToken);
    }

    private void CancelDiagnostics()
    {
        _diagnosticsCancellationTokenSource?.Cancel();
        _isDiagTaskRunning = false;
    }

    private void DiagnosticsProgress_ProgressChanged(object? sender, (DiagnosticsTestState State, string Status) pending)
    {
        // keep the UI thread safe
        _pendingResultTexts.Enqueue(pending);
    }

    public override void OnClose()
    {
        CancelDiagnostics();
        _resultTexts.Clear();
        _pendingResultTexts.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _resultTexts.Clear();
        _pendingResultTexts.Clear();
        CancelDiagnostics();
        _diagnosticsProgress.ProgressChanged -= DiagnosticsProgress_ProgressChanged;
    }
}
