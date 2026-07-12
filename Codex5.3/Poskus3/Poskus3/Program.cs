using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Poskus3.Data;
using Poskus3.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var jwtSection = builder.Configuration.GetSection("Jwt");
//vse JWT spremeljivke potrebne
var secret = jwtSection["Secret"];
var issuer = jwtSection["Issuer"];
var audience = jwtSection["Audience"];
// dodaj še svojo spremeljivko za duration
var durationMinutes = int.TryParse(jwtSection["DurationMinutes"], out var parsedDuration) ? parsedDuration : 60;
if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience) || durationMinutes <= 0)
{
    throw new InvalidOperationException("JWT configuration is invalid.");
}


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
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<UserStatisticsService>();
builder.Services.AddSingleton<GlobalChatWebSocketService>();
builder.Services.AddHostedService<GameSessionTimeoutService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userIdClaim = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                    ?? context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var jtiClaim = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Jti);

                if (!int.TryParse(userIdClaim, out var userId) || string.IsNullOrWhiteSpace(jtiClaim))
                {
                    context.Fail("Token claims are invalid.");
                    return;
                }

                using var scope = context.HttpContext.RequestServices.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var user = await dbContext.Users.SingleOrDefaultAsync(u => u.id == userId);

                if (user is null ||
                    user.currentTokenJti != jtiClaim ||
                    user.currentTokenExpiresAtUtc is null ||
                    user.currentTokenExpiresAtUtc <= DateTime.UtcNow)
                {
                    context.Fail("Token is no longer active.");
                }
            }
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseCors("AllowAll");
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbBootstrapper.EnsureAuthAndGameTablesAsync(dbContext);
}

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

app.Map("/chat", async context =>
{
    var chatService = context.RequestServices.GetRequiredService<GlobalChatWebSocketService>();
    await chatService.HandleAsync(context);
});

app.MapControllers();

app.Run();
