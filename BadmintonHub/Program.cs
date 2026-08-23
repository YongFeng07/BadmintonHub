using BadmintonHub.Data;
using BadmintonHub.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using System.Globalization;

// QuestPDF Community licence (free within the vendor's published revenue limits).
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// ASP.NET Core MVC (assignment architecture).
// View localization (P6): IViewLocalizer reads Resources/Views/<view>.resx per culture.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddControllersWithViews().AddViewLocalization();

// EF Core Code First against SQL Server Express LocalDB (file-based).
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Manual cookie-based authentication (assignment explicitly forbids ASP.NET Core Identity).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "BadmintonHub.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<ICourtService, CourtService>();
builder.Services.AddScoped<IReservationService, ReservationService>();

// M2 additional feature: automatic reservation status updates.
builder.Services.AddHostedService<ReservationStatusUpdaterService>();

var app = builder.Build();

// Apply pending migrations and seed demo data when the database is empty.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
    DbSeeder.Seed(db, app.Environment.WebRootPath);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Multi-language (P6): English is the default — this also stops a non-English server
// culture from leaking into views ("8月" instead of "Aug", see CourtService's invariant
// day abbreviation). Users can switch to 中文 or Bahasa Melayu; views without
// translations fall back to English (honest partial coverage).
var supportedCultures = new[] { "en-US", "zh-CN", "ms-MY" }
    .Select(c => new CultureInfo(c)).ToArray();
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en-US"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures,
    RequestCultureProviders = new IRequestCultureProvider[]
    {
        new QueryStringRequestCultureProvider(),  // ?culture=zh-CN — handy for demos
        new CookieRequestCultureProvider(),       // the navbar switcher stores the choice
        new AcceptLanguageHeaderRequestCultureProvider() // browser language as the fallback
    }
});

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// UseStaticFiles (instead of MapStaticAssets): court photos and uploaded files are
// created at runtime, so they cannot be baked into the build-time asset manifest.
app.UseStaticFiles();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
