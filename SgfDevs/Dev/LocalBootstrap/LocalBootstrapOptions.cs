namespace SgfDevs.Dev.LocalBootstrap;

public sealed class LocalBootstrapOptions
{
    public const string SectionName = "SGFDevs:LocalBootstrap";

    public bool Enabled { get; set; }

    public bool SeedFictionalContent { get; set; }
}
