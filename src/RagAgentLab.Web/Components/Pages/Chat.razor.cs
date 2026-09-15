using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using RagAgentLab.Agents;
using RagAgentLab.Rag;
using RagAgentLab.Web.Services;

namespace RagAgentLab.Web.Components.Pages;

/// <summary>
/// The chat screen: a conversation with the tool-calling agent, with the agent's tool calls
/// shown as they happen and the answer streamed in as it is generated.
/// <para>
/// Code-behind rather than an <c>@code</c> block because this component owns real state — a
/// conversation, a cancellation token, a streaming loop — and that is easier to read, and
/// possible to document, as ordinary C#.
/// </para>
/// </summary>
public partial class Chat : IDisposable
{
    /// <summary>Starter questions, chosen to exercise different routes through the tool set.</summary>
    private static readonly string[] Suggestions =
    [
        "Yurt dışından yılda en fazla kaç iş günü çalışabilirim?",
        "Aylık 750 TL internet katkısı bir yılda kaç TL eder?",
        "Eğitim bütçem ne kadar ve bir sonraki yıla devreder mi?",
    ];

    private readonly List<ChatTurn> _turns = [];
    private ElementReference _messagesElement;
    private CancellationTokenSource? _cancellation;
    private string _input = string.Empty;

    /// <summary>True while an answer is being generated.</summary>
    private bool IsBusy => _cancellation is not null;

    /// <summary>The knowledge base must be indexed before a question can be answered.</summary>
    private bool CanSend => KnowledgeBase.Status == KnowledgeBaseStatus.Ready && !IsBusy;

    private string StatusText => KnowledgeBase.Status switch
    {
        KnowledgeBaseStatus.Ready =>
            $"{KnowledgeBase.DocumentCount} doküman · {KnowledgeBase.ChunkCount} chunk hazır",
        KnowledgeBaseStatus.Failed => "bilgi tabanı yüklenemedi",
        _ => "bilgi tabanı hazırlanıyor…",
    };

    private string StatusCssClass => KnowledgeBase.Status switch
    {
        KnowledgeBaseStatus.Ready => "ok",
        KnowledgeBaseStatus.Failed => "error",
        _ => "loading",
    };

    /// <inheritdoc />
    protected override void OnInitialized() => KnowledgeBase.Changed += OnKnowledgeBaseChanged;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender) => await ScrollToBottomAsync();

    /// <summary>
    /// Sends on Enter; Shift+Enter inserts a newline instead.
    /// <para>
    /// Deliberately on key<em>up</em> rather than keydown. Enter in a textarea inserts a
    /// newline, and Blazor's <c>preventDefault</c> is a render-time attribute rather than a
    /// per-event decision, so suppressing it for Enter but not for Shift+Enter would need
    /// JavaScript. Handling keyup instead lets the newline land, lets the bound value settle,
    /// and leaves it to the Trim in <see cref="SendAsync"/> to remove — the box is cleared
    /// immediately afterwards, so the stray newline is never visible.
    /// </para>
    /// </summary>
    private async Task OnKeyUpAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Enter" && !args.ShiftKey && CanSend)
        {
            await SendAsync(_input);
        }
    }

    /// <summary>Runs one turn: append the question, stream the answer, record tool calls.</summary>
    private async Task SendAsync(string question)
    {
        if (string.IsNullOrWhiteSpace(question) || !CanSend)
        {
            return;
        }

        _input = string.Empty;
        _turns.Add(new ChatTurn { Role = ConversationRole.User, Content = question.Trim() });

        var answer = new ChatTurn { Role = ConversationRole.Assistant, IsStreaming = true };
        _turns.Add(answer);

        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;

        // The conversation sent to the model excludes the placeholder being streamed into.
        var conversation = _turns
            .Take(_turns.Count - 1)
            .Select(turn => new ConversationMessage(turn.Role, turn.Content))
            .ToArray();

        var options = new AgentRunOptions
        {
            // These fire on Semantic Kernel's thread, so every UI change is marshalled back
            // onto the renderer's synchronisation context.
            OnToolCallStarted = (tool, arguments) => InvokeAsync(() =>
            {
                answer.ToolCalls.Add(new ToolCallView(tool, arguments));
                StateHasChanged();
            }),
            OnToolCallCompleted = step => InvokeAsync(() =>
            {
                var call = answer.ToolCalls.LastOrDefault(c => c.Result is null);
                call?.Complete(step.Result, step.Duration.TotalMilliseconds);
                StateHasChanged();
            }),
            // A search happens inside a tool call, so it is attached to the call that is
            // currently running.
            OnRetrieval = record => InvokeAsync(() =>
            {
                var call = answer.ToolCalls.LastOrDefault(c => c.Result is null)
                           ?? answer.ToolCalls.LastOrDefault();
                call?.AddRetrieval(record);
                StateHasChanged();
            }),
        };

        try
        {
            await foreach (var fragment in Agent.RunStreamingAsync(conversation, options, token))
            {
                answer.Content += fragment;
                StateHasChanged();
                await ScrollToBottomAsync();
            }
        }
        catch (OperationCanceledException)
        {
            answer.Content += answer.Content.Length > 0 ? " […durduruldu]" : "[durduruldu]";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "The agent failed while answering.");
            answer.Content = $"Hata: {ex.Message}";
        }
        finally
        {
            // The detail panels open themselves while the agent works, so the search is
            // visible as it happens, and fold away once the answer has arrived. They stay
            // one click from being reopened.
            foreach (var call in answer.ToolCalls)
            {
                call.IsExpanded = false;
            }

            answer.IsStreaming = false;
            _cancellation?.Dispose();
            _cancellation = null;
            StateHasChanged();
        }
    }

    /// <summary>Cancels the in-flight generation.</summary>
    private void Cancel() => _cancellation?.Cancel();

    private void OnKnowledgeBaseChanged() => _ = InvokeAsync(StateHasChanged);

    private async Task ScrollToBottomAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("ragAgentLab.scrollToBottom", _messagesElement);
        }
        catch (JSException)
        {
            // The circuit can be mid-teardown; scrolling is cosmetic.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        KnowledgeBase.Changed -= OnKnowledgeBaseChanged;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
    }

    /// <summary>One message in the on-screen conversation.</summary>
    private sealed class ChatTurn
    {
        public required ConversationRole Role { get; init; }

        public string Content { get; set; } = string.Empty;

        public bool IsStreaming { get; set; }

        public List<ToolCallView> ToolCalls { get; } = [];
    }

    /// <summary>A tool call as shown in the transcript: created on start, filled in on completion.</summary>
    private sealed class ToolCallView
    {
        public ToolCallView(string tool, string arguments)
        {
            Tool = tool;
            Arguments = arguments;
        }

        public string Tool { get; }

        public string Arguments { get; }

        public string? Result { get; private set; }

        public double Milliseconds { get; private set; }

        /// <summary>Vector searches performed inside this tool call.</summary>
        public List<RetrievalRecord> Retrievals { get; } = [];

        /// <summary>Whether the search-detail panel is open.</summary>
        public bool IsExpanded { get; set; }

        public void Complete(string result, double milliseconds)
        {
            Result = result;
            Milliseconds = milliseconds;
        }

        /// <summary>Attaches a search to this call and opens the panel so it is seen live.</summary>
        public void AddRetrieval(RetrievalRecord record)
        {
            Retrievals.Add(record);
            IsExpanded = true;
        }
    }
}
