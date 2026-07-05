using Microsoft.EntityFrameworkCore;
using YPoskusX.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var jwtSection = builder.Configuration.GetSection("Jwt");
//vse JWT spremeljivke potrebne
var secret = jwtSection["Secret"];
var issuer = jwtSection["Issuer"];
var audience = jwtSection["Audience"];
// dodaj še svojo spremeljivko za duration


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

app.Run();
