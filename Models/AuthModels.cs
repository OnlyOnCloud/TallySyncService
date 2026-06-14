namespace TallySyncService.Models;

public class AuthConfig
{
    public string BackendUrl { get; set; } = "https://dhub-backend.onlyoncloud.com";
    public string? JwtToken { get; set; }
    public uint? OrganisationId { get; set; }
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
}

public class ValidateOtpRequest
{
    public string Email { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
}

public class UserOrganisation
{
    [System.Text.Json.Serialization.JsonPropertyName("user_id")]
    public uint UserId { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("organisation_id")]
    public uint OrganisationId { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("OrganisationCode")]
    public string OrganisationCode { get; set; } = string.Empty;
}
