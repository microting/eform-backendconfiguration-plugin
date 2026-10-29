#nullable enable
namespace BackendConfiguration.Pn.Services.Leder;

/// <summary>
/// Who is saving a Medarbejdere row, worked out by the service because the Helper is static: an admin may grant or revoke
/// Leder, a Leder or admin may change worker tags, and <see cref="UserId"/> is the actor written to LederAssignments.
/// </summary>
public sealed record LederCaller(bool IsAdmin, bool IsLederOrAdmin, int UserId);
