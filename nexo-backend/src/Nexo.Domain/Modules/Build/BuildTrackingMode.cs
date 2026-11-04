namespace Nexo.Domain.Modules.Build;

/// <summary>How a project is followed: by weighted stages (default) or by a 3D model (Digital Twin).</summary>
public enum BuildTrackingMode
{
    Standard = 1,
    ThreeD   = 2,
}
