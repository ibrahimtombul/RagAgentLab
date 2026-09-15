namespace RagAgentLab.Configuration;

/// <summary>
/// Settings for the SQLite-backed vector store, bound from the "Sqlite" section of
/// appsettings.json. Only used when <see cref="RagOptions.VectorStore"/> is <c>Sqlite</c>.
/// </summary>
public sealed class SqliteOptions
{
    /// <summary>Configuration section name this class is bound from.</summary>
    public const string SectionName = "Sqlite";

    /// <summary>
    /// Database file path. A relative path is resolved against the application directory, so
    /// the default keeps the file next to the binary and needs no configuration to work.
    /// </summary>
    public string DatabasePath { get; set; } = "ragagentlab.db";
}
