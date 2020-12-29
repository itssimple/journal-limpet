#if !DEBUG
using Hangfire;
using Journal_Limpet.Jobs;
#endif
using Journal_Limpet.Shared;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

namespace Journal_Limpet
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public static IServiceProvider ServiceProvider;

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddExceptional(Configuration.GetSection("Exceptional"), settings =>
            {
#if DEBUG
                settings.UseExceptionalPageOnThrow = true;
#endif
            });

            services.AddDistributedMemoryCache();
            services.AddResponseCaching();
            services.AddResponseCompression();
            services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(20);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
                options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.None;
            });

            services.AddHttpClient();

            services.AddControllers();
            services.AddRazorPages();

            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
                options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
                options.Cookie.Name = "journal-limpet-auth";

                options.ExpireTimeSpan = TimeSpan.FromMinutes(20);

                options.LoginPath = "/Login";
                options.LogoutPath = "/Logout";
                options.AccessDeniedPath = "/AccessDenied";

                options.SlidingExpiration = true;
            });
            services.AddAuthorization();

            services.AddJournalLimpetDependencies(Configuration);

#if !DEBUG
            services.AddHangfire(configuration =>
            {
                configuration
                    .UseSimpleAssemblyNameTypeSerializer()
                    .UseRecommendedSerializerSettings()
                    .UseSqlServerStorage(Configuration["Database:HangfireConnection"]);
            });

            services.AddHangfireServer(options =>
            {
                options.WorkerCount = Environment.ProcessorCount * 5;
            });
#endif
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseResponseCaching();
            app.UseResponseCompression();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.All,
                RequireHeaderSymmetry = false,
                ForwardLimit = null
            });

            app.UseStaticFiles();

            app.UseExceptional();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseSession();

#if !DEBUG
            app.UseHangfireDashboard(options: new DashboardOptions
            {
                Authorization = new[] { new HangfireAuthorizationFilter(Configuration["Hangfire:AuthKey"]) }
            });
#endif
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapRazorPages();
            });

            ServiceProvider = app.ApplicationServices;

#if !DEBUG
            RecurringJob.AddOrUpdate(
                "journal-limpet:update-tokens",
                () => AccountTokenRefresher.RefreshUserTokensAsync(null),
                "*/10 * * * *",
                TimeZoneInfo.Utc
            );

            RecurringJob.AddOrUpdate(
                "journal-limpet:download-journals",
                () => JournalDownloadManager.InitializeJournalDownloadersAsync(null),
                "*/30 * * * *",
                TimeZoneInfo.Utc
            );

            RecurringJob.AddOrUpdate(
                "journal-limpet:tweet-stats",
                () => TweetStatSender.SendStatsTweetAsync(null),
                "30 0 * * *",
                TimeZoneInfo.Utc
            );

            RecurringJob.AddOrUpdate(
                "journal-limpet:upload-to-eddn",
                () => EDDNUploader.UploadAsync(null),
                "*/10 * * * *",
                TimeZoneInfo.Utc
            );
#endif
        }
    }
}
