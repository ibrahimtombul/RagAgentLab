using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using RagAgentLab.Agents;
using RagAgentLab.Rag;
using RagAgentLab.Web.Components.Shared;
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

    /// <summary>Prefix that turns a chat message into a write to the knowledge base.</summary>
    private const string TeachCommand = "/ogren";

    private readonly List<ChatTurn> _turns = [];
    private ElementReference _messagesElement;
    private CancellationTokenSource? _cancellation;
    private string _input = string.Empty;
    private bool _lastTurnFailed;

    /// <summary>True while an answer is being generated.</summary>
    private bool IsBusy => _cancellation is not null;

    /// <summary>The knowledge base must be indexed before a question can be answered.</summary>
    private bool CanSend => KnowledgeBase.Status == KnowledgeBaseStatus.Ready && !IsBusy;

    /// <summary>
    /// What the status ring shows. Loading the corpus counts as work rather than as an idle
    /// state, because from the user's side it is the same thing: the assistant cannot answer yet.
    /// </summary>
    private AndroidLedState LedState
    {
        get
        {
            if (KnowledgeBase.Status == KnowledgeBaseStatus.Failed || _lastTurnFailed)
            {
                return AndroidLedState.Error;
            }

            return IsBusy || KnowledgeBase.Status == KnowledgeBaseStatus.Loading
                ? AndroidLedState.Processing
                : AndroidLedState.Idle;
        }
    }

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

    /// <summary>
    /// Runs one turn. A message beginning with <c>/ogren</c> is handled here instead of being
    /// sent to the agent: teaching writes to the corpus, and that is too consequential to leave
    /// to a model that might decide to do it on its own.
    /// </summary>
    private async Task SendAsync(string question)
    {
        if (string.IsNullOrWhiteSpace(question) || !CanSend)
        {
            return;
        }

        var message = question.Trim();
        _input = string.Empty;

        if (message.StartsWith(TeachCommand, StringComparison.OrdinalIgnoreCase))
        {
            await TeachAsync(message[TeachCommand.Length..].Trim());
            return;
        }

        _turns.Add(new ChatTurn { Role = ConversationRole.User, Content = message });

        var answer = new ChatTurn { Role = ConversationRole.Assistant, IsStreaming = true };
        _turns.Add(answer);

        _lastTurnFailed = false;
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;

        // The conversation sent to the model excludes the placeholder being streamed into.
        var conversation = _turns
            .Take(_turns.Count - 1)
            .Where(turn => !turn.IsSystemNote && !turn.ExcludeFromHistory)
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
            _lastTurnFailed = true;
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

            // A reply with no tool call that reads like one, or no reply at all, is a failure
            // the ring should show even though nothing threw.
            if (!_lastTurnFailed && answer.ToolCalls.Count == 0)
            {
                _lastTurnFailed = answer.Content.Length == 0 ||
                                  answer.Content.Contains("[Not:", StringComparison.Ordinal) ||
                                  answer.Content.Contains("[the model returned an empty response",
                                      StringComparison.Ordinal);
            }

            answer.IsStreaming = false;
            _cancellation?.Dispose();
            _cancellation = null;
            StateHasChanged();
        }
    }

    /// <summary>Writes a note into the knowledge base and reports the result in the transcript.</summary>
    private async Task TeachAsync(string note)
    {
        _lastTurnFailed = false;
        _turns.Add(new ChatTurn
        {
            Role = ConversationRole.User,
            Content = $"{TeachCommand} {note}",
            ExcludeFromHistory = true,
        });

        var confirmation = new ChatTurn { Role = ConversationRole.Assistant, IsSystemNote = true };
        _turns.Add(confirmation);

        try
        {
            var result = await KnowledgeBaseWriter.AddNoteAsync(note);
            confirmation.Content =
                $"Öğrenildi. Not \"{result.SourceName}\" olarak {result.ChunkCount} parça hâlinde " +
                "bilgi tabanına yazıldı ve bundan sonraki sorularda kaynak olarak kullanılabilir.";

            KnowledgeBase.NoteAdded(result.ChunkCount);
        }
        catch (ArgumentException ex)
        {
            // ArgumentException appends " (Parameter 'text')" to its message, which is a detail
            // about this code rather than anything the person typing a note needs to read.
            var reason = ex.Message.Split(" (Parameter", StringComparison.Ordinal)[0];
            confirmation.Content = $"Not eklenemedi: {reason}";
            _lastTurnFailed = true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to add a note to the knowledge base.");
            confirmation.Content = $"Not eklenemedi: {ex.Message}";
            _lastTurnFailed = true;
        }
        finally
        {
            StateHasChanged();
            await ScrollToBottomAsync();
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

        /// <summary>
        /// True for messages the application produced itself, such as the confirmation after a
        /// note is stored. They are shown differently and are never sent back to the model.
        /// </summary>
        public bool IsSystemNote { get; init; }

        /// <summary>
        /// True for turns that are kept out of the prompt.
        /// <para>
        /// A <c>/ogren</c> command is shown in the transcript so the user can see what they
        /// taught, but it must not travel to the model as conversation history: the note text
        /// would then sit in the prompt and the next answer could come straight out of it,
        /// making retrieval look like it works when it has not even been consulted.
        /// </para>
        /// </summary>
        public bool ExcludeFromHistory { get; init; }

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
