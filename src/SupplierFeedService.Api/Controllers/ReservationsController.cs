using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SupplierFeedService.Api.Contracts;
using SupplierFeedService.Api.Data;
using SupplierFeedService.Api.Options;
using SupplierFeedService.Api.Services;

namespace SupplierFeedService.Api.Controllers;

[ApiController]
[Route("api/reservations")]
public sealed class ReservationsController : ControllerBase
{
    private readonly IReservationIngestService _ingestService;
    private readonly ISupplierStatsRepository _statsRepository;
    private readonly SupplierFeedOptions _options;

    public ReservationsController(
        IReservationIngestService ingestService,
        ISupplierStatsRepository statsRepository,
        IOptions<SupplierFeedOptions> options)
    {
        _ingestService = ingestService;
        _statsRepository = statsRepository;
        _options = options.Value;
    }

    [HttpPost("ingest")]
    public async Task<IActionResult> Ingest([FromBody] IngestReservationRequest request)
    {
        var result = await _ingestService.IngestAsync(request);

        return result switch
        {
            ReservationIngestResult.Throttled => Throttled(request.SupplierId),
            ReservationIngestResult.Processed processed => Processed(request, processed),
            _ => throw new InvalidOperationException("Unknown ingest result type."),
        };
    }

    [HttpGet("stats/{supplierId}")]
    public async Task<ActionResult<SupplierStatsResponse>> GetStats(string supplierId)
    {
        var snapshot = await _statsRepository.GetAsync(supplierId);
        return Ok(new SupplierStatsResponse(
            supplierId,
            snapshot.IngestedCount,
            snapshot.ThrottledCount,
            snapshot.CreatedCount,
            snapshot.UpdatedCount,
            snapshot.UnchangedDuplicateCount,
            snapshot.IgnoredStaleCount));
    }

    private IActionResult Processed(IngestReservationRequest request, ReservationIngestResult.Processed processed)
    {
        var response = new IngestReservationResponse(
            request.SupplierId,
            request.ReservationId,
            processed.Outcome,
            DateTimeOffset.FromUnixTimeMilliseconds(processed.StoredUpdatedAtUtcMs),
            request.UpdatedAtUtc,
            MessageFor(processed.Outcome));

        return processed.Outcome switch
        {
            IngestOutcome.Created => StatusCode(StatusCodes.Status201Created, response),
            IngestOutcome.Updated => Ok(response),
            IngestOutcome.UnchangedDuplicate => Ok(response),
            IngestOutcome.IgnoredStale => Conflict(response),
            _ => throw new InvalidOperationException("Unknown outcome."),
        };
    }

    private IActionResult Throttled(string supplierId)
    {
        Response.Headers["Retry-After"] = _options.WindowSeconds.ToString(CultureInfo.InvariantCulture);
        var body = new ThrottledResponse(
            supplierId,
            _options.MaxRequestsPerWindow,
            _options.WindowSeconds,
            $"Rate limit exceeded: more than {_options.MaxRequestsPerWindow} requests in the last {_options.WindowSeconds}s.");
        return StatusCode(StatusCodes.Status429TooManyRequests, body);
    }

    private static string MessageFor(IngestOutcome outcome) => outcome switch
    {
        IngestOutcome.Created => "Reservation created.",
        IngestOutcome.Updated => "Reservation updated.",
        IngestOutcome.UnchangedDuplicate => "No changes - identical reservation already stored.",
        IngestOutcome.IgnoredStale => "Ignored: incoming update is older than the currently stored version.",
        _ => throw new InvalidOperationException("Unknown outcome."),
    };
}
