using System.Text.Json;
using FluentValidation;
using Nexo.Application.Modules.Service.Public;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Shared FluentValidation rule sets for the Service create/update requests. Defining the
/// rules once per entity (keyed off the ISvc*Fields interfaces) keeps the create and update
/// validators identical without copy-paste.
/// </summary>
internal static class SvcValidationRules
{
    public static void ApplyProfessionalRules<T>(AbstractValidator<T> v) where T : ISvcProfessionalFields
    {
        v.RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Professional name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");
        v.RuleFor(x => x.Role).MaximumLength(100).When(x => x.Role is not null);
        v.RuleFor(x => x.Specialty).MaximumLength(150).When(x => x.Specialty is not null);
        v.RuleFor(x => x.Color).MaximumLength(20).When(x => x.Color is not null);
        v.RuleFor(x => x.Phone).MaximumLength(30).When(x => x.Phone is not null);
        v.RuleFor(x => x.Email).EmailAddress().MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        v.RuleFor(x => x.DefaultCommissionPercent).InclusiveBetween(0m, 100m)
            .When(x => x.DefaultCommissionPercent.HasValue);
        v.RuleFor(x => x.WorkingHoursJson)
            .Must(BeValidJsonOrNull).WithMessage("WorkingHoursJson must be valid JSON.");
    }

    public static void ApplyCatalogRules<T>(AbstractValidator<T> v) where T : ISvcCatalogItemFields
    {
        v.RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Catalog item name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");
        v.RuleFor(x => x.DurationMinutes)
            .GreaterThan(0).WithMessage("Duration must be greater than zero minutes.");
        v.RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0m).WithMessage("Price cannot be negative.");
        v.RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        v.RuleFor(x => x.Category).MaximumLength(100).When(x => x.Category is not null);
        v.RuleFor(x => x.CommissionPercent).InclusiveBetween(0m, 100m)
            .When(x => x.CommissionPercent.HasValue);
    }

    /// <summary>True when the string is null/blank or parses as a JSON document — guards jsonb columns from 500s.</summary>
    public static bool BeValidJsonOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;
        try { using var _ = JsonDocument.Parse(json); return true; }
        catch (JsonException) { return false; }
    }

    public static void ApplySubjectRules<T>(AbstractValidator<T> v) where T : ISvcSubjectFields
    {
        v.RuleFor(x => x.Kind).IsInEnum().WithMessage("Invalid subject kind.");
        v.RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Subject display name is required.")
            .MaximumLength(200).WithMessage("Display name must not exceed 200 characters.");
        v.RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
        v.RuleFor(x => x.MetadataJson)
            .Must(BeValidJsonOrNull).WithMessage("MetadataJson must be valid JSON.");
    }

    public static void ApplyAppointmentRules<T>(AbstractValidator<T> v) where T : ISvcAppointmentFields
    {
        v.RuleFor(x => x.CustomerId).NotEmpty().WithMessage("CustomerId is required.");
        v.RuleFor(x => x.ProfessionalId).NotEmpty().WithMessage("ProfessionalId is required.");
        v.RuleFor(x => x.CatalogItemId).NotEmpty().WithMessage("CatalogItemId is required.");
        v.RuleFor(x => x).Must(r => r.StartsAt < r.EndsAt)
            .WithMessage("StartsAt must be before EndsAt.");
        v.RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
    }
}

public class CreateSvcProfessionalRequestValidator : AbstractValidator<CreateSvcProfessionalRequest>
{
    public CreateSvcProfessionalRequestValidator() => SvcValidationRules.ApplyProfessionalRules(this);
}

public class UpdateSvcProfessionalRequestValidator : AbstractValidator<UpdateSvcProfessionalRequest>
{
    public UpdateSvcProfessionalRequestValidator() => SvcValidationRules.ApplyProfessionalRules(this);
}

public class CreateSvcCatalogItemRequestValidator : AbstractValidator<CreateSvcCatalogItemRequest>
{
    public CreateSvcCatalogItemRequestValidator() => SvcValidationRules.ApplyCatalogRules(this);
}

public class UpdateSvcCatalogItemRequestValidator : AbstractValidator<UpdateSvcCatalogItemRequest>
{
    public UpdateSvcCatalogItemRequestValidator() => SvcValidationRules.ApplyCatalogRules(this);
}

public class CreateSvcSubjectRequestValidator : AbstractValidator<CreateSvcSubjectRequest>
{
    public CreateSvcSubjectRequestValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("CustomerId is required.");
        SvcValidationRules.ApplySubjectRules(this);
    }
}

public class UpdateSvcSubjectRequestValidator : AbstractValidator<UpdateSvcSubjectRequest>
{
    public UpdateSvcSubjectRequestValidator() => SvcValidationRules.ApplySubjectRules(this);
}

public class CreateSvcRecordEntryRequestValidator : AbstractValidator<CreateSvcRecordEntryRequest>
{
    private static readonly SvcRecordContextType[] Supported =
        { SvcRecordContextType.Customer, SvcRecordContextType.Subject, SvcRecordContextType.Order };

    public CreateSvcRecordEntryRequestValidator()
    {
        RuleFor(x => x.ContextType)
            .NotNull().WithMessage("ContextType is required.")
            .Must(ct => ct is null || Supported.Contains(ct.Value))
            .WithMessage("ContextType is not supported yet. Use Customer or Subject.");

        RuleFor(x => x.ContextId)
            .NotNull().NotEqual(Guid.Empty).WithMessage("ContextId is required.");

        RuleFor(x => x)
            .Must(r => !string.IsNullOrWhiteSpace(r.Text) || r.Attachments is { Count: > 0 })
            .WithMessage("A record must have text or at least one attachment.");

        RuleForEach(x => x.Attachments)
            .Must(a => !string.IsNullOrWhiteSpace(a.StorageKey))
            .WithMessage("Each attachment must have a storageKey.")
            .When(x => x.Attachments is not null);
    }
}

public class CreateSvcAppointmentRequestValidator : AbstractValidator<CreateSvcAppointmentRequest>
{
    public CreateSvcAppointmentRequestValidator() => SvcValidationRules.ApplyAppointmentRules(this);
}

public class UpdateSvcAppointmentRequestValidator : AbstractValidator<UpdateSvcAppointmentRequest>
{
    public UpdateSvcAppointmentRequestValidator() => SvcValidationRules.ApplyAppointmentRules(this);
}

public class ChangeSvcAppointmentStatusRequestValidator : AbstractValidator<ChangeSvcAppointmentStatusRequest>
{
    public ChangeSvcAppointmentStatusRequestValidator()
    {
        RuleFor(x => x.Status).NotNull().WithMessage("Status is required.")
            .IsInEnum().WithMessage("Invalid status.");
        RuleFor(x => x.Reason).MaximumLength(500).When(x => x.Reason is not null);
    }
}

public class CreateSvcOrderRequestValidator : AbstractValidator<CreateSvcOrderRequest>
{
    public CreateSvcOrderRequestValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("CustomerId is required.");
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
    }
}

public class UpdateSvcOrderRequestValidator : AbstractValidator<UpdateSvcOrderRequest>
{
    public UpdateSvcOrderRequestValidator()
        => RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
}

public class ChangeSvcOrderStatusRequestValidator : AbstractValidator<ChangeSvcOrderStatusRequest>
{
    public ChangeSvcOrderStatusRequestValidator()
    {
        RuleFor(x => x.Status).NotNull().WithMessage("Status is required.")
            .IsInEnum().WithMessage("Invalid status.");
        RuleFor(x => x.Reason).MaximumLength(500).When(x => x.Reason is not null);
    }
}

public class AddSvcOrderItemRequestValidator : AbstractValidator<AddSvcOrderItemRequest>
{
    public AddSvcOrderItemRequestValidator()
    {
        RuleFor(x => x.CatalogItemId).NotEmpty().WithMessage("CatalogItemId is required.");
        RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("Quantity must be positive.");
    }
}

public class UpdateSvcOrderItemRequestValidator : AbstractValidator<UpdateSvcOrderItemRequest>
{
    public UpdateSvcOrderItemRequestValidator()
        => RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("Quantity must be positive.");
}

public class CreateSvcPackageRequestValidator : AbstractValidator<CreateSvcPackageRequest>
{
    public CreateSvcPackageRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Package name is required.").MaximumLength(200);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0m).WithMessage("Price cannot be negative.");
        RuleFor(x => x.ValidityDays).GreaterThan(0).When(x => x.ValidityDays.HasValue)
            .WithMessage("ValidityDays must be positive when set.");
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
    }
}

public class UpdateSvcPackageRequestValidator : AbstractValidator<UpdateSvcPackageRequest>
{
    public UpdateSvcPackageRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Package name is required.").MaximumLength(200);
        RuleFor(x => x.ValidityDays).GreaterThan(0).When(x => x.ValidityDays.HasValue)
            .WithMessage("ValidityDays must be positive when set.");
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
    }
}

public class UpdateSvcPackagePriceRequestValidator : AbstractValidator<UpdateSvcPackagePriceRequest>
{
    public UpdateSvcPackagePriceRequestValidator()
        => RuleFor(x => x.Price).GreaterThanOrEqualTo(0m).WithMessage("Price cannot be negative.");
}

public class AddSvcPackageItemRequestValidator : AbstractValidator<AddSvcPackageItemRequest>
{
    public AddSvcPackageItemRequestValidator()
    {
        RuleFor(x => x.CatalogItemId).NotEmpty().WithMessage("CatalogItemId is required.");
        RuleFor(x => x.IncludedQuantity).GreaterThan(0m).WithMessage("IncludedQuantity must be positive.");
    }
}

public class UpdateSvcPackageItemRequestValidator : AbstractValidator<UpdateSvcPackageItemRequest>
{
    public UpdateSvcPackageItemRequestValidator()
        => RuleFor(x => x.IncludedQuantity).GreaterThan(0m).WithMessage("IncludedQuantity must be positive.");
}

public class AssignSvcCustomerPackageRequestValidator : AbstractValidator<AssignSvcCustomerPackageRequest>
{
    public AssignSvcCustomerPackageRequestValidator()
    {
        RuleFor(x => x.PackageId).NotEmpty().WithMessage("PackageId is required.");
        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("CustomerId is required.");
        RuleFor(x => x.StartsAt).Must(d => d.Kind == DateTimeKind.Utc)
            .WithMessage("StartsAt must be UTC (use a trailing Z).");
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
    }
}

public class ConsumeSvcPackageRequestValidator : AbstractValidator<ConsumeSvcPackageRequest>
{
    public ConsumeSvcPackageRequestValidator()
    {
        RuleFor(x => x.CatalogItemId).NotEmpty().WithMessage("CatalogItemId is required.");
        RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("Quantity must be positive.");
        RuleFor(x => x).Must(r => r.OrderItemId is null || r.OrderId is not null)
            .WithMessage("OrderId is required when OrderItemId is provided.");
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
    }
}

public class CreateSvcPaymentRequestValidator : AbstractValidator<CreateSvcPaymentRequest>
{
    public CreateSvcPaymentRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0m).WithMessage("Amount must be positive.");
        RuleFor(x => x.Method).IsInEnum().WithMessage("Invalid payment method.");
        RuleFor(x => x.PaidAt).Must(d => d.Kind == DateTimeKind.Utc)
            .WithMessage("PaidAt must be UTC (use a trailing Z).");
        RuleFor(x => x).Must(r => (r.OrderId is null) != (r.CustomerPackageId is null))
            .WithMessage("Exactly one of OrderId or CustomerPackageId must be set.");
        RuleFor(x => x.ExternalReference).MaximumLength(200).When(x => x.ExternalReference is not null);
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
    }
}

public class VoidSvcPaymentRequestValidator : AbstractValidator<VoidSvcPaymentRequest>
{
    public VoidSvcPaymentRequestValidator()
        => RuleFor(x => x.Reason).MaximumLength(500).When(x => x.Reason is not null);
}

public class SetServicePresetRequestValidator : AbstractValidator<SetServicePresetRequest>
{
    public SetServicePresetRequestValidator()
    {
        RuleFor(x => x.PresetKey)
            .NotEmpty().WithMessage("PresetKey is required.")
            .Must(ServicePresetRegistry.IsValidPresetKey)
            .WithMessage("Invalid service preset key.");
    }
}

public class UpdatePublicBookingRequestValidator : AbstractValidator<UpdatePublicBookingRequest>
{
    public UpdatePublicBookingRequestValidator()
    {
        RuleFor(x => x.BookingDaysAhead).InclusiveBetween(1, 365)
            .WithMessage("BookingDaysAhead must be between 1 and 365.");
        RuleFor(x => x.MinLeadMinutes).InclusiveBetween(0, 43200)
            .WithMessage("MinLeadMinutes must be between 0 and 43200.");
        RuleFor(x => x.SlotIntervalMinutes).InclusiveBetween(5, 240)
            .WithMessage("SlotIntervalMinutes must be between 5 and 240.");
        RuleFor(x => x.TimeZoneId).NotEmpty().MaximumLength(64)
            .WithMessage("TimeZoneId is required.");
    }
}

public class UpdatePortalBrandingRequestValidator : AbstractValidator<UpdatePortalBrandingRequest>
{
    public UpdatePortalBrandingRequestValidator()
    {
        RuleFor(x => x.BrandColor).Matches("^#[0-9a-fA-F]{6}$")
            .When(x => !string.IsNullOrWhiteSpace(x.BrandColor))
            .WithMessage("BrandColor must be a #rrggbb hex value.");
        RuleFor(x => x.DisplayName).MaximumLength(120).When(x => x.DisplayName is not null);
        RuleFor(x => x.Description).MaximumLength(280).When(x => x.Description is not null);
        RuleFor(x => x.LogoUrl).MaximumLength(500).When(x => x.LogoUrl is not null);
        RuleFor(x => x.CoverImageUrl).MaximumLength(500).When(x => x.CoverImageUrl is not null);
        RuleFor(x => x.WhatsApp).MaximumLength(30).When(x => x.WhatsApp is not null);
        RuleFor(x => x.Address).MaximumLength(200).When(x => x.Address is not null);
    }
}

public class CreatePublicAppointmentRequestValidator : AbstractValidator<CreatePublicAppointmentRequest>
{
    public CreatePublicAppointmentRequestValidator()
    {
        RuleFor(x => x.CustomerName)
            .NotEmpty().WithMessage("Customer name is required.").MaximumLength(200);
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone is required.").MaximumLength(30)
            .Must(p => p is not null && p.Count(char.IsDigit) >= 8)
            .WithMessage("Phone must contain at least 8 digits.");
        RuleFor(x => x.Email).EmailAddress().MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.CatalogItemId).NotEmpty().WithMessage("A service must be selected.");
        RuleFor(x => x.ProfessionalId).NotEmpty().WithMessage("A professional must be selected.");
        RuleFor(x => x.StartsAt).Must(d => d.Kind == DateTimeKind.Utc)
            .WithMessage("StartsAt must be UTC (use a trailing Z).");
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);

        When(x => x.Subject is not null, () =>
        {
            RuleFor(x => x.Subject!.DisplayName)
                .NotEmpty().WithMessage("Subject name is required.").MaximumLength(200);
            RuleFor(x => x.Subject!.Notes).MaximumLength(2000).When(x => x.Subject!.Notes is not null);
            RuleFor(x => x.Subject!.Kind).MaximumLength(30).When(x => x.Subject!.Kind is not null);
        });
    }
}
