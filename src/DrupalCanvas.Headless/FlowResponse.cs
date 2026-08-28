namespace DrupalCanvas.Headless;

/// <summary>
/// A framework-neutral HTTP response produced by the draft flows. The
/// framework binding (ASP.NET Core, or anything else) maps it onto its own
/// response primitive; the JavaScript SDK uses the web <c>Response</c> in this
/// role.
/// </summary>
public sealed record FlowResponse
{
    public required int Status { get; init; }

    public string? Body { get; init; }

    public string ContentType { get; init; } = "text/plain; charset=utf-8";

    /// <summary>Redirect target; the Location header when set.</summary>
    public string? Location { get; init; }

    public static FlowResponse Text(int status, string body)
        => new() { Status = status, Body = body };

    public static FlowResponse Json(int status, string json)
        => new() { Status = status, Body = json, ContentType = "application/json; charset=utf-8" };

    public static FlowResponse Redirect(string location, int status = 307)
        => new() { Status = status, Location = location };
}
