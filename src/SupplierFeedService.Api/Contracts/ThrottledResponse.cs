namespace SupplierFeedService.Api.Contracts;

public sealed record ThrottledResponse(
    string SupplierId,
    int LimitPerWindow,
    int WindowSeconds,
    string Message);
