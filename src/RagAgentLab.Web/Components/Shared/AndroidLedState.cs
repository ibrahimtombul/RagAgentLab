namespace RagAgentLab.Web.Components.Shared;

/// <summary>
/// The three states the status ring can show, mirroring what the colour means on an android's
/// temple LED: steady while everything is nominal, agitated while it is working hard, red when
/// something has actually gone wrong.
/// </summary>
public enum AndroidLedState
{
    /// <summary>Ready and waiting. Steady blue.</summary>
    Idle = 0,

    /// <summary>Thinking, calling a tool, or indexing. Yellow, in motion.</summary>
    Processing = 1,

    /// <summary>Something failed. Red.</summary>
    Error = 2,
}
