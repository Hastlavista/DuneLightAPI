using System.Collections.Generic;

namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Jedinstven ugovor za sve greške (validacijske i poslovne) koje API vraća.
/// Svi budući moduli koriste isti oblik: { "error": { "code", "message", "details" } }.
/// </summary>
public class ErrorResponse
{
    public ErrorResponse(ErrorDetail error)
    {
        Error = error;
    }

    public ErrorDetail Error { get; set; }
}

public class ErrorDetail
{
    public ErrorDetail(string code, string message, IDictionary<string, string[]> details = null)
    {
        Code = code;
        Message = message;
        Details = details;
    }

    public string Code { get; set; }
    public string Message { get; set; }
    public IDictionary<string, string[]> Details { get; set; }
}
