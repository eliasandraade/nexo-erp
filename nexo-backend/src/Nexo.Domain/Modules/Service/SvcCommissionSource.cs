namespace Nexo.Domain.Modules.Service;

/// <summary>
/// What produced a <see cref="SvcCommissionEntry"/>. Each source has its own recognition moment,
/// because Service money does not arrive through a single door:
///   - <see cref="OrderItem"/>: recognised when a payment settles the comanda (regime de caixa);
///   - <see cref="Appointment"/>: recognised when the appointment is completed WITHOUT a linked
///     order — an appointment never receives a payment of its own;
///   - <see cref="PackageUsage"/>: recognised at consumption, since the customer already paid when
///     the package was sold.
/// </summary>
public enum SvcCommissionSource
{
    OrderItem   = 1,
    Appointment = 2,
    PackageUsage = 3,
}
