using System.Collections.Generic;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace BlueDragon.DuneLight.API.Swagger;

/// <summary>
/// T1-6 — opisuje jedinstven oblik grešaka (ExceptionHandlingMiddleware) na svakoj operaciji, da frontend generira i tipove
/// grešaka: 400/404/409 <see cref="ErrorResponse"/> ({ error: { code, message, details } }), 403 <see cref="ForbiddenErrorResponse"/>
/// (details: reason, requiredGrants, match...). Ne dira odgovore koje operacija već sama opisuje.
/// </summary>
public sealed class ErrorResponsesOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Responses ??= new OpenApiResponses();
        IOpenApiSchema error = context.SchemaGenerator.GenerateSchema(typeof(ErrorResponse), context.SchemaRepository);
        IOpenApiSchema forbidden = context.SchemaGenerator.GenerateSchema(typeof(ForbiddenErrorResponse), context.SchemaRepository);

        Add(operation, "400", "Neispravan zahtjev (VALIDATION_ERROR ili kod pravila).", error);
        Add(operation, "403", "Odbijeno: details.reason i svi grantovi koji nedostaju (details.requiredGrants).", forbidden);
        Add(operation, "404", "Ne postoji.", error);
        Add(operation, "409", "Poslovno pravilo (kod iz ErrorCodes).", error);
    }

    private static void Add(OpenApiOperation operation, string status, string description, IOpenApiSchema schema)
    {
        if (operation.Responses!.ContainsKey(status))
            return;
        operation.Responses[status] = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new() { Schema = schema }
            }
        };
    }
}
