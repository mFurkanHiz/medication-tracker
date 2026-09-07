var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "medication-tracker-api",
    version = "0.1.0"
}));

app.Run();

public partial class Program;
