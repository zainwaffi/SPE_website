namespace SPE_website.Features.Authentication.Models;

/// <summary>Member profile fields parsed from the OpenWater prefill JSON response.</summary>
public sealed class OpenWaterMemberProfile
{
    public required string Email { get; init; }
    public required string FullName { get; init; }
    public string? StudentId { get; init; }
    public string? Organization { get; init; }
    public string? DegreeProgramLevel { get; init; }
    public bool IsStudentOfficer { get; init; }
    public bool IsStudentMember { get; init; }
    public required string RawJson { get; init; }
}
