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
    /// <summary>Oblik details ovisi o code: kod VALIDATION_ERROR je IDictionary&lt;string, string[]&gt; (polje-po-polje), za druge kodove je slobodniji oblik (npr. RECURRING_CONFLICT nosi { conflicts: [...] }).</summary>
    public ErrorDetail(string code, string message, object details = null)
    {
        Code = code;
        Message = message;
        Details = details;
    }

    public string Code { get; set; }
    public string Message { get; set; }
    public object Details { get; set; }
}
