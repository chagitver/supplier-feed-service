namespace SupplierFeedService.Api.Contracts;

public enum IngestOutcome
{
    Created,
    Updated,
    UnchangedDuplicate,
    IgnoredStale,
}
