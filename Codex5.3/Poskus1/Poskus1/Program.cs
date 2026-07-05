using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Poskus1.Configuration;
using Poskus1.Data;
using Poskus1.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var jwtSection = builder.Configuration.GetSection("Jwt");
//vse JWT spremeljivke potrebne
var secret = jwtSection["Secret"];
var issuer = jwtSection["Issuer"];
var audience = jwtSection["Audience"];
// dodaj še svojo spremeljivko za duration
var jwtDurationMinutes = int.TryParse(jwtSection["DurationMinutes"], out var parsedDuration) ? parsedDuration : 60;

if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
{
    throw new InvalidOperationException("Jwt settings Secret, Issuer in Audience morajo biti nastavljeni.");
}

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));


builder.Services.AddControllers();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrWhiteSpace(accessToken) && path.StartsWithSegments("/chat"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();
builder.Services.Configure<JwtOptions>(options =>
{
    options.Secret = secret;
    options.Issuer = issuer;
    options.Audience = audience;
    options.DurationMinutes = jwtDurationMinutes;
});
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
builder.Services.AddHostedService<QuizSessionTimeoutService>();
builder.Services.AddSingleton<IChatConnectionManager, ChatConnectionManager>();
builder.Services.AddScoped<IUserStatisticsService, UserStatisticsService>();
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
