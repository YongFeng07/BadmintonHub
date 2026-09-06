using SportHub.Data;
using SportHub.Services;
using DNTCaptcha.Core;
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
// The data file is pinned inside the project's App_Data folder: LocalDB's
// default data location is the user profile and its master registration does
// not survive every restart — an unpinned database breaks F5 runs with
// "Cannot create file ... because it already exists". Substituting {DbFile}
// here means Visual Studio and dotnet run always use the same file.
// The database name is derived from the file path (SHA-256, 8 chars) so that
// two copies of the solution on the same machine (e.g. a clean clone next to
// the working copy) never collide in the LocalDB master registration.
var dataDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDir);
var dbFile = Path.Combine(dataDir, "SportHub.mdf");
var dbName = "SportHub_" +
    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dbFile)))[..8].ToLowerInvariant();
var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection")!
    .Replace("{DbFile}", dbFile)
    .Replace("{DbName}", dbName);
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(defaultConnection));

// Manual cookie-based authentication (assignment explicitly forbids ASP.NET Core Identity).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "SportHub.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

// Captcha (3rd-party library integration, revised spec): DNTCaptcha.Core renders the
// challenge image and stores the answer encrypted in a cookie, so no server-side session
// state is needed. The keys below only protect that captcha token — they are not real
// credentials. Validation is applied by ValidateCaptchaAttribute, which honours the
// "Security:EnableCaptcha" setting (disabled only for the automated e2e suite).
builder.Services.AddDNTCaptcha(options =>
{
    options.UseCookieStorageProvider()
        .AbsoluteExpiration(minutes: 7)
        .ShowThousandsSeparators(false)
        .WithNoise(0.01f, 0.01f, 1, 0.0f)
        .WithEncryptionKey("SportHubCaptcha2026")
        .WithNonceKey("SportHubCaptchaNonce2026");
});

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<ICourtService, CourtService>();
builder.Services.AddScoped<IReservationService, ReservationService>();
builder.Services.AddScoped<IImageService, ImageService>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<IWishlistService, WishlistService>();
builder.Services.AddScoped<IVoucherService, VoucherService>();
builder.Services.AddScoped<ICheckoutService, CheckoutService>();

// ToyyibPay gateway: config is bound from the "ToyyibPay" section whose key
// fields are EMPTY PLACEHOLDERS — with no credentials the service runs its
// simulated fallback so the flow is demonstrable without a real account.
builder.Services.AddSingleton(sp =>
{
    var options = new ToyyibPayOptions();
    sp.GetRequiredService<IConfiguration>().GetSection("ToyyibPay").Bind(options);
    return options;
});
builder.Services.AddScoped<IToyyibPayService, ToyyibPayService>();

// Outbound email: real SMTP (MailKit) only when Email:Smtp:Host is configured —
// otherwise a demo fallback captures the mail in-app so the assignment demo works
// without committing any real credentials (see README).
builder.Services.AddScoped<IEmailService>(sp =>
    EmailServiceFactory.Create(
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<ApplicationDbContext>()));

// M2 additional feature: automatic reservation status updates.
builder.Services.AddHostedService<ReservationStatusUpdaterService>();

// G-M2: overdue vouchers flip to Expired automatically (startup + every 6 hours).
builder.Services.AddHostedService<VoucherExpiryWorker>();

// G-M3: expired 15-minute cart holds are released back to "Open".
builder.Services.AddHostedService<CartHoldWorker>();

// G-M4: wishlisted courts that reopened are announced to the waiting members.
builder.Services.AddHostedService<WishlistNotifyWorker>();

// G-M5: 24-hour "your booking starts soon" reminders (email + notification).
builder.Services.AddScoped<ReminderService>();
builder.Services.AddHostedService<ReservationReminderWorker>();

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
