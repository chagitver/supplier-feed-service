using System.Text.Json.Serialization;
using SupplierFeedService.Api.Data;
using SupplierFeedService.Api.Diagnostics;
using SupplierFeedService.Api.Options;
using SupplierFeedService.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.Configure<SupplierFeedOptions>(builder.Configuration.GetSection(SupplierFeedOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();

builder.Services.AddScoped<IReservationRepository, ReservationRepository>();
builder.Services.AddScoped<IRateLimitLogRepository, RateLimitLogRepository>();
builder.Services.AddScoped<ISupplierStatsRepository, SupplierStatsRepository>();
builder.Services.AddScoped<ISlidingWindowRateLimiter, SlidingWindowRateLimiter>();
builder.Services.AddScoped<IReservationIngestService, ReservationIngestService>();

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services.GetRequiredService<ISqliteConnectionFactory>());

app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program
{
}
