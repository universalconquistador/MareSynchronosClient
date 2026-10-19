using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.Models;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI;

public partial class HelpUi : WindowMediatorSubscriberBase
{
    private void DrawDiagnosticsNetwork()
    {
        _uiShared.BigText("Network Diagnostics");
        ImGuiHelpers.ScaledDummy(2);

        // drain the queue to draw out the results
        while (_pendingResultTexts.TryDequeue(out var pendingText))
        {
            _resultTexts.Add(pendingText);
        }

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
                _finalResults = await DiagnosticTesting.RunAllDiagnosticTests(_diagnosticsProgress, _httpClientProvider.GetHttpClient(), diagnosticsCancellationToken).ConfigureAwait(false);
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
}
