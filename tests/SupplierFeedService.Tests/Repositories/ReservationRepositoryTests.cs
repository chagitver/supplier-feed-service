using SupplierFeedService.Api.Contracts;
using SupplierFeedService.Api.Data;
using SupplierFeedService.Tests.Fixtures;

namespace SupplierFeedService.Tests.Repositories;

public sealed class ReservationRepositoryTests : IAsyncLifetime
{
    private readonly TempSqliteFixture _fixture = new();
    private IReservationRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _repository = new ReservationRepository(_fixture.ConnectionFactory);
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static IngestReservationRequest BuildRequest(
        string supplierId = "supplier-1",
        string reservationId = "res-1",
        string roomId = "room-1",
        decimal price = 450.00m,
        DateTimeOffset? updatedAtUtc = null) => new(
        supplierId,
        reservationId,
        roomId,
        new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero),
        price,
        updatedAtUtc ?? new DateTimeOffset(2026, 7, 1, 10, 15, 0, TimeSpan.Zero));

    [Fact]
    public async Task New_key_is_created()
    {
        var request = BuildRequest();

        var result = await _repository.UpsertAsync(request, nowUtcMs: 1_000);

        Assert.Equal(IngestOutcome.Created, result.Outcome);
        Assert.Equal(request.UpdatedAtUtc.ToUnixTimeMilliseconds(), result.StoredUpdatedAtUtcMs);
    }

    [Fact]
    public async Task Identical_business_fields_are_unchanged_duplicate_even_with_newer_timestamp()
    {
        var initial = BuildRequest();
        await _repository.UpsertAsync(initial, nowUtcMs: 1_000);

        var resend = initial with { UpdatedAtUtc = initial.UpdatedAtUtc.AddMinutes(5) };
        var result = await _repository.UpsertAsync(resend, nowUtcMs: 2_000);

        Assert.Equal(IngestOutcome.UnchangedDuplicate, result.Outcome);
        // No write happened, so the stored timestamp is still the original one, not the resend's.
        Assert.Equal(initial.UpdatedAtUtc.ToUnixTimeMilliseconds(), result.StoredUpdatedAtUtcMs);
    }

    [Fact]
    public async Task Differing_field_with_newer_timestamp_is_updated()
    {
        var initial = BuildRequest(price: 450.00m);
        await _repository.UpsertAsync(initial, nowUtcMs: 1_000);

        var changed = initial with { Price = 500.00m, UpdatedAtUtc = initial.UpdatedAtUtc.AddHours(1) };
        var result = await _repository.UpsertAsync(changed, nowUtcMs: 2_000);

        Assert.Equal(IngestOutcome.Updated, result.Outcome);
        Assert.Equal(changed.UpdatedAtUtc.ToUnixTimeMilliseconds(), result.StoredUpdatedAtUtcMs);
    }

    [Fact]
    public async Task Differing_field_with_older_timestamp_is_ignored_as_stale_and_row_is_untouched()
    {
        var initial = BuildRequest(price: 450.00m);
        await _repository.UpsertAsync(initial, nowUtcMs: 1_000);

        var stale = initial with { Price = 999.00m, UpdatedAtUtc = initial.UpdatedAtUtc.AddHours(-1) };
        var result = await _repository.UpsertAsync(stale, nowUtcMs: 2_000);

        Assert.Equal(IngestOutcome.IgnoredStale, result.Outcome);
        // Stored timestamp is still the original (newer) one - proves no regression happened.
        Assert.Equal(initial.UpdatedAtUtc.ToUnixTimeMilliseconds(), result.StoredUpdatedAtUtcMs);

        // Re-submitting the original values still reads back as unchanged, proving the stale
        // write above never actually touched the row.
        var followUp = await _repository.UpsertAsync(initial, nowUtcMs: 3_000);
        Assert.Equal(IngestOutcome.UnchangedDuplicate, followUp.Outcome);
    }

    [Fact]
    public async Task Same_reservationId_from_different_suppliers_are_independent()
    {
        var supplierA = BuildRequest(supplierId: "supplier-A", reservationId: "shared-id", roomId: "roomA", price: 100.00m);
        var supplierB = BuildRequest(supplierId: "supplier-B", reservationId: "shared-id", roomId: "roomB", price: 200.00m);

        var resultA = await _repository.UpsertAsync(supplierA, nowUtcMs: 1_000);
        var resultB = await _repository.UpsertAsync(supplierB, nowUtcMs: 1_000);

        // Both are "Created" - if ReservationId alone were the key, B would collide with A's
        // row and be misclassified as Updated/Unchanged/IgnoredStale instead.
        Assert.Equal(IngestOutcome.Created, resultA.Outcome);
        Assert.Equal(IngestOutcome.Created, resultB.Outcome);

        // Changing supplier A's reservation must not affect supplier B's row for the same ID.
        var updateA = supplierA with { Price = 999.00m, UpdatedAtUtc = supplierA.UpdatedAtUtc.AddHours(1) };
        var updateAResult = await _repository.UpsertAsync(updateA, nowUtcMs: 2_000);
        Assert.Equal(IngestOutcome.Updated, updateAResult.Outcome);

        // Supplier B's original values are still intact - resubmitting them reads back as
        // unchanged, not updated (which would indicate cross-supplier contamination).
        var checkB = await _repository.UpsertAsync(supplierB, nowUtcMs: 3_000);
        Assert.Equal(IngestOutcome.UnchangedDuplicate, checkB.Outcome);
    }

    [Fact]
    public async Task Concurrent_upserts_for_same_key_do_not_throw_and_converge_to_newer_timestamp()
    {
        var initial = BuildRequest();
        await _repository.UpsertAsync(initial, nowUtcMs: 1_000);

        var repositoryA = new ReservationRepository(_fixture.ConnectionFactory);
        var repositoryB = new ReservationRepository(_fixture.ConnectionFactory);

        var updateA = initial with { RoomId = "room-A", UpdatedAtUtc = initial.UpdatedAtUtc.AddHours(1) };
        var updateB = initial with { RoomId = "room-B", UpdatedAtUtc = initial.UpdatedAtUtc.AddHours(2) };

        await Task.WhenAll(
            repositoryA.UpsertAsync(updateA, nowUtcMs: 2_000),
            repositoryB.UpsertAsync(updateB, nowUtcMs: 2_000));

        // Whichever finished last, the durable state must reflect the newer timestamp (updateB).
        var finalCheck = await _repository.UpsertAsync(updateB, nowUtcMs: 3_000);
        Assert.Equal(IngestOutcome.UnchangedDuplicate, finalCheck.Outcome);
    }
}
