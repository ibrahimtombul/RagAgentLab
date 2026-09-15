namespace RagAgentLab.Configuration;

/// <summary>
/// Settings for the sample e-commerce database, bound from the "Shop" section of
/// appsettings.json.
/// </summary>
public sealed class ShopOptions
{
    /// <summary>Configuration section name this class is bound from.</summary>
    public const string SectionName = "Shop";

    /// <summary>
    /// Database file path, resolved against the application directory when relative. The file is
    /// created and seeded on first use, so nothing has to be set up before the demo runs.
    /// </summary>
    public string DatabasePath { get; set; } = "shop.db";
}
