using System.ComponentModel.DataAnnotations;

namespace SupplierFeedService.Api.Contracts;

public sealed record IngestReservationRequest(
    [Required, MinLength(1), RegularExpression("^[^/]+$", ErrorMessage = "SupplierId must not contain '/'.")]
    string SupplierId,
    [Required, MinLength(1)] string ReservationId,
    [Required, MinLength(1)] string RoomId,
    DateTimeOffset CheckIn,
    DateTimeOffset CheckOut,
    decimal Price,
    DateTimeOffset UpdatedAtUtc) : IValidatableObject
{
    // [Required] does not catch a missing value for these non-nullable value types - the model
    // binder fills in default(DateTimeOffset)/0 rather than leaving them null, so an omitted
    // field would otherwise bind silently instead of failing validation.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CheckIn == default)
        {
            yield return new ValidationResult("CheckIn is required.", [nameof(CheckIn)]);
        }

        if (CheckOut == default)
        {
            yield return new ValidationResult("CheckOut is required.", [nameof(CheckOut)]);
        }

        if (UpdatedAtUtc == default)
        {
            yield return new ValidationResult("UpdatedAtUtc is required.", [nameof(UpdatedAtUtc)]);
        }

        if (Price <= 0)
        {
            yield return new ValidationResult("Price must be greater than zero.", [nameof(Price)]);
        }
    }
}
