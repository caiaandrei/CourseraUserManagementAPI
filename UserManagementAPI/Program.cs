using System.Collections.Concurrent;
using System.Text.Json;

ConcurrentDictionary<int, User> users = new();

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();


app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

    try
    {
        await next.Invoke();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled exception occurred.");

        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";

        var errorResponse = new { error = "Internal server error." };
        var json = JsonSerializer.Serialize(errorResponse);

        await context.Response.WriteAsync(json);
    }
});

app.Use(async (context, next) =>
{
    var token = context.Request.Headers["Authorization"].FirstOrDefault();

    if (token != "Bearer secret-token-123")
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";

        var errorResponse = new { error = "Unauthorized" };
        var json = JsonSerializer.Serialize(errorResponse);
        await context.Response.WriteAsync(json);
        return;
    }

    await next.Invoke();
});

app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var method = context.Request.Method;
    var path = context.Request.Path;

    await next.Invoke(); // Let the request process

    var statusCode = context.Response.StatusCode;

    logger.LogInformation("Request: {method} {path} => {statusCode}", method, path, statusCode);
});



app.MapGet("users/{id}", (int id) =>
{
    if (!users.TryGetValue(id, out var user))
    {
        return Results.NotFound($"User with ID {id} not found.");
    }

    return Results.Ok(user);
});

app.MapPost("users", (User user) =>
{
    var validationErrors = ValidateUser(user);
    if (validationErrors.Any())
    {
        return Results.BadRequest(new { Errors = validationErrors });
    }

    if (!users.TryAdd(user.Id, user))
    {
        return Results.Conflict($"User with ID {user.Id} already exists.");
    }

    return Results.Created($"/{user.Id}", user);
});

app.MapPut("users/{id}", (int id, User updatedUser) =>
{
    var validationErrors = ValidateUser(updatedUser);
    if (validationErrors.Any())
    {
        return Results.BadRequest(new { Errors = validationErrors });
    }

    if (id != updatedUser.Id)
    {
        return Results.BadRequest("ID in URL and payload must match.");
    }

    if (!users.ContainsKey(id))
    {
        return Results.NotFound($"User with ID {id} not found.");
    }

    if (!users.TryUpdate(id, updatedUser, users[id]))
    {
        return Results.Conflict("Failed to update user due to concurrent modification.");
    }

    return Results.Ok(updatedUser);
});

app.MapDelete("users/{id}", (int id) =>
{
    if (!users.TryRemove(id, out _))
    {
        return Results.NotFound($"User with ID {id} not found.");
    }

    return Results.NoContent();
});

app.Run();

List<string> ValidateUser(User user)
{
    var errors = new List<string>();

    if (user == null)
    {
        errors.Add("User object is null.");
        return errors;
    }

    if (user.Id <= 0)
    {
        errors.Add("User ID must be a positive integer.");
    }

    if (string.IsNullOrWhiteSpace(user.Name))
    {
        errors.Add("User name cannot be empty.");
    }

    if (user.Name.Length > 100)
    {
        errors.Add("User name cannot exceed 100 characters.");
    }

    if (string.IsNullOrWhiteSpace(user.Email))
    {
        errors.Add("Email cannot be empty.");
    }

    if (!System.Text.RegularExpressions.Regex.IsMatch(user.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
    {
        errors.Add("Email format is invalid.");
    }

    if (user.Email.Length > 100)
    {
        errors.Add("Email cannot exceed 100 characters.");
    }

    return errors;
}

class User
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
}
