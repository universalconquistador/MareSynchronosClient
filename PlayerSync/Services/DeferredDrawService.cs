using System.Collections.Concurrent;

namespace MareSynchronos.Services;

/// <summary>
/// Lets non-UI code schedule actions that must run inside the ImGui draw context.
/// UiService drains this queue each frame.
/// </summary>
public sealed class DeferredDrawService
{
    private readonly ConcurrentQueue<Action> _actions = new();

    public void Enqueue(Action action) => _actions.Enqueue(action);

    public void Execute()
    {
        while (_actions.TryDequeue(out var action))
            action();
    }
}
using System.Collections.Concurrent;

namespace MareSynchronos.Services;

/// <summary>
/// Lets non-UI code schedule actions that must run inside the ImGui draw context.
/// UiService drains this queue each frame.
/// </summary>
public sealed class DeferredDrawService
{
    private readonly ConcurrentQueue<Action> _actions = new();

    public void Enqueue(Action action) => _actions.Enqueue(action);

    public void Execute()
    {
        while (_actions.TryDequeue(out var action))
            action();
    }
}
