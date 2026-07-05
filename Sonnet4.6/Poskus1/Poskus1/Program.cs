using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Poskus1.Data;
using Poskus1.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var jwtSection = builder.Configuration.GetSection("Jwt");
var secret = jwtSection["Secret"];
var issuer = jwtSection["Issuer"];
var audience = jwtSection["Audience"];
var durationMinutes = int.Parse(jwtSection["DurationMinutes"] ?? "60");

builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        //test client
        policy.WithOrigins("http://localhost:4444")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders("X-Server-Id");
    });
});
builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgresSQL")));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret!)),
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<GameService>();
builder.Services.AddScoped<StatisticsService>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddSingleton<QuizProgressTracker>();
builder.Services.AddSingleton<ChatWebSocketManager>();

var app = builder.Build();
app.UseCors("AllowAll");

//Ne dotikaj se tega dela
var serverId = builder.Configuration["ServerInfo:ServerId"];
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.Append("X-Server-Id", serverId);
        return Task.CompletedTask;
    });

    await next(context);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
// --------

app.UseWebSockets();

// WebSocket endpoint za globalni chat
app.Map("/chat", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Zahteva ni WebSocket.");
        return;
    }

    var ws = await context.WebSockets.AcceptWebSocketAsync();
    var chatManager = context.RequestServices.GetRequiredService<ChatWebSocketManager>();
    chatManager.AddClient(ws);
    await chatManager.ListenUntilClosedAsync(ws);
});

// WebSocket endpoint za real-time napredek kviza
app.Map("/ws/quiz-progress", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Zahteva ni WebSocket.");
        return;
    }

    var ws = await context.WebSockets.AcceptWebSocketAsync();
    var tracker = context.RequestServices.GetRequiredService<QuizProgressTracker>();
    var db = context.RequestServices.GetRequiredService<AppDbContext>();

    await QuizWebSocketHandler.HandleAsync(context, ws, tracker, db);
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
