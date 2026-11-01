using Microsoft.AspNetCore.Mvc;

namespace D365Portal.Api;

/// <summary>RFC 7807 problem responses with a stable machine-readable "code" the frontend can switch on.</summary>
public static class Problems
{
    public static ProblemDetails Create(int status, string code, string detail, string? title = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title ?? DefaultTitle(status),
            Detail = detail,
        };
        problem.Extensions["code"] = code;
        return problem;
    }

    public static IResult Result(int status, string code, string detail, string? title = null) =>
        Results.Problem(Create(status, code, detail, title));

    public static Task Write(HttpContext http, int status, string code, string detail)
    {
        var problem = Create(status, code, detail);
        problem.Extensions["traceId"] = http.TraceIdentifier;
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    }

    private static string DefaultTitle(int status) => status switch
    {
        400 => "Bad request",
        401 => "Not signed in",
        403 => "Not authorized",
        404 => "Not found",
        409 => "Conflict",
        429 => "Too many requests",
        _ => "Server error",
    };
}
