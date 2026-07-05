using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Poskus1.Data;
using System.Text;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

using Poskus1.Services;

var builder = WebApplication.CreateBuilder(args);
var jwtSection = builder.Configuration.GetSection("Jwt");
//vse JWT spremeljivke potrebne
var secret = jwtSection["Secret"] ?? "DefaultSuperSecretKeyThatIsVeryLongAndSecure123!";
var issuer = jwtSection["Issuer"];
var audience = jwtSection["Audience"];
// dodaj še svojo spremeljivko za duration
var durationInMinutes = jwtSection.GetValue<int>("DurationInMinutes", 60);

builder.Services.AddSingleton<WebSocketManagerService>();
builder.Services.AddSingleton<ChatWebSocketManagerService>();
builder.Services.AddScoped<StatisticsService>();
builder.Services.AddHostedService<QuizTimerService>();

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
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && 
                    (path.StartsWithSegments("/game/ws") || path.StartsWithSegments("/chat")))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var dbContext = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var userIdClaim = context.Principal?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);
                var jtiClaim = context.Principal?.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti);

                if (userIdClaim != null && jtiClaim != null && int.TryParse(userIdClaim.Value, out int userId))
                {
                    var user = await dbContext.Users.FindAsync(userId);
                    if (user == null || user.ActiveTokenId != jtiClaim.Value)
                    {
                        context.Fail("Token is no longer valid.");
                    }
                }
                else
                {
                    context.Fail("Invalid token structure.");
                }
            }
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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
