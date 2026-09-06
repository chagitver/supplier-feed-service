namespace SupplierFeedService.Api.Options;

public sealed class SupplierFeedOptions
{
    public const string SectionName = "RateLimit";

    public int MaxRequestsPerWindow { get; set; } = 100;
    public int WindowSeconds { get; set; } = 60;
}
