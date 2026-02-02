using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MareSynchronos.UI;

public class DiagnosticsUi : WindowMediatorSubscriberBase
{
    private readonly Progress<string> _diagnosticsProgress = new();
    private readonly HttpClient _httpClient;
    private readonly UiSharedService _uiSharedService;
    private readonly ConcurrentQueue<string> _pendingResultTexts = new();
    private CancellationTokenSource? _diagnosticsCancellationTokenSource;
    private Task? _diagnosticsRunTask;
    private bool _isDiagTaskRunning = false;
    private readonly List<string> _resultTexts = new();
    private string _finalResults = "";

    public DiagnosticsUi(ILogger<DiagnosticsUi> logger, MareMediator mediator, UiSharedService uiSharedService,
        PerformanceCollectorService performanceCollectorService, HttpClient httpClient)
        : base(logger, mediator, "PlayerSync Diagnostics", performanceCollectorService)
    {
        
        _uiSharedService = uiSharedService;
        _httpClient = httpClient;

        SizeConstraints = new()
        {
            MinimumSize = new()
            {
                X = 600,
                Y = 350
            },
            MaximumSize = new()
            {
                X = 600,
                Y = 350
            }
        };

        Flags |= ImGuiWindowFlags.NoResize;

        _diagnosticsProgress.ProgressChanged += DiagnosticsProgress_ProgressChanged;

    }

    protected override void DrawInternal()
    {
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

        ImGui.BeginChild("results", new(0, 350), true);

        // this must be ToArray() else we throw during a subsequent run when we call Clear()
        foreach (var result in _resultTexts.ToArray())
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(result);
            ImGuiHelpers.ScaledDummy(5f);
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

        _diagnosticsRunTask = Task.Run(async () =>
        {
            try
            {
                _resultTexts.Clear();
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

    private void DiagnosticsProgress_ProgressChanged(object? sender, string pending)
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
