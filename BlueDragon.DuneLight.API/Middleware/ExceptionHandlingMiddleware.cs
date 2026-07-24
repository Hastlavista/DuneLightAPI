using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BlueDragon.DuneLight.API.Middleware;

/// <summary>
/// Jedinstven JSON oblik grešaka za cijeli API (koristi ga i Katalog i svi budući moduli):
/// { "error": { "code", "message", "details" } }.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);

            // UseAuthentication/UseAuthorization postavljaju 401/403 bez tijela (WWW-Authenticate
            // izazov, nedovoljna uloga na [Authorize(Roles = ...)]) — uskladi ih s istim oblikom.
            if (!context.Response.HasStarted && context.Response.StatusCode is 401 or 403)
            {
                (HttpStatusCode status, string code, string message) = context.Response.StatusCode == 401
                    ? (HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized, "Niste prijavljeni ili je token istekao.")
                    : (HttpStatusCode.Forbidden, ErrorCodes.Forbidden, "Nemate ovlasti za ovu radnju.");

                await WriteError(context, status, code, message);
            }
        }
        catch (ValidationAppException ex)
        {
            await WriteError(context, HttpStatusCode.BadRequest, ErrorCodes.ValidationError, ex.Message, ex.Details);
        }
        catch (NotFoundAppException ex)
        {
            await WriteError(context, HttpStatusCode.NotFound, ErrorCodes.NotFound, ex.Message);
        }
        catch (UnauthorizedAppException ex)
        {
            await WriteError(context, HttpStatusCode.Unauthorized, ex.Code, ex.Message);
        }
        catch (BusinessRuleException ex)
        {
            await WriteError(context, HttpStatusCode.Conflict, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while processing {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteError(context, HttpStatusCode.InternalServerError, ErrorCodes.InternalError, "Došlo je do neočekivane greške.");
        }
    }

    private async Task WriteError(
        HttpContext context, HttpStatusCode statusCode, string code, string message,
        System.Collections.Generic.IDictionary<string, string[]> details = null)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        ErrorResponse response = new ErrorResponse(new ErrorDetail(code, message, details));
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, _jsonOptions));
    }
}
