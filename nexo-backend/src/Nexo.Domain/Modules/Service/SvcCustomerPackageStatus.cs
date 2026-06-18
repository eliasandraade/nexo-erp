namespace Nexo.Domain.Modules.Service;

/// <summary>Lifecycle of a <see cref="SvcCustomerPackage"/>. Stored as a string. Consumed/Expired/Cancelled are terminal.</summary>
public enum SvcCustomerPackageStatus { Active, Consumed, Expired, Cancelled }
