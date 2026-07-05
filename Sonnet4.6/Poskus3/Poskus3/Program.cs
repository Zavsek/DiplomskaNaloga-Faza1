using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Poskus3.Data;
using Poskus3.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var jwtSection = builder.Configuration.GetSection("Jwt");
//vse JWT spremeljivke potrebne
var secret = jwtSection["Secret"]!;
var issuer = jwtSection["Issuer"]!;
var audience = jwtSection["Audience"]!;
var durationMinutes = int.Parse(jwtSection["DurationMinutes"] ?? "60");

builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<GameSessionService>();
builder.Services.AddSingleton<GameProgressWebSocketHandler>();
builder.Services.AddSingleton<StatisticsService>();
builder.Services.AddSingleton<ChatWebSocketHandler>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

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
var app = builder.Build();
app.UseCors("AllowAll");
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();

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

app.MapControllers();

// WebSocket endpoint za real-time napredek kviza
app.Map("/api/game/progress", async context =>
{
    var handler = context.RequestServices.GetRequiredService<GameProgressWebSocketHandler>();
    await handler.HandleAsync(context);
});

// WebSocket endpoint za globalni chat
app.Map("/chat", async context =>
{
    var handler = context.RequestServices.GetRequiredService<ChatWebSocketHandler>();
    await handler.HandleAsync(context);
});

app.Run();
