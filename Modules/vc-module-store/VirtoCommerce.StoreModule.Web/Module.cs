using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtoCommerce.Platform.Core.Modularity;
using VirtoCommerce.Platform.Core.Security;
using VirtoCommerce.StoreModule.Core;
using VirtoCommerce.StoreModule.Core.Services;
using VirtoCommerce.StoreModule.Data.Repositories;
using VirtoCommerce.StoreModule.Data.Services;

namespace VirtoCommerce.StoreModule.Web
{
    public class Module : IModule
    {
        public ManifestModuleInfo ModuleInfo { get; set; }
        public void Initialize(IServiceCollection serviceCollection)
        {
            var snapshot = serviceCollection.BuildServiceProvider();
            var configuration = snapshot.GetService<IConfiguration>();
            serviceCollection.AddDbContext<StoreDbContext>(options => options.UseSqlServer(configuration.GetConnectionString("VirtoCommerce")));
            serviceCollection.AddTransient<IStoreRepository, StoreRepositoryImpl>();
            serviceCollection.AddSingleton<Func<IStoreRepository>>(provider => () => provider.CreateScope().ServiceProvider.GetService<IStoreRepository>());
            serviceCollection.AddSingleton<IStoreService, StoreServiceImpl>();
        }

        public void PostInitialize(IApplicationBuilder appBuilder)
        {
            ModuleInfo.Settings.Add(new ModuleSettingsGroup
            {
                Name = "Store|General",
                Settings = ModuleConstants.Settings.General.AllSettings.ToArray()
            });
            ModuleInfo.Settings.Add(new ModuleSettingsGroup
            {
                Name = "Store|SEO",
                Settings = ModuleConstants.Settings.SEO.AllSettings.ToArray()
            });

            var permissionsProvider = appBuilder.ApplicationServices.GetRequiredService<IKnownPermissionsProvider>();
            permissionsProvider.RegisterPermissions(ModuleConstants.Security.Permissions.AllPermissions.Select(x =>
                new Permission()
                {
                    GroupName = "Store",
                    ModuleId = ModuleInfo.Id,
                    Name = x
                }).ToArray());

            using (var serviceScope = appBuilder.ApplicationServices.CreateScope())
            {
                var dbContext = serviceScope.ServiceProvider.GetRequiredService<StoreDbContext>();
                dbContext.Database.EnsureCreated();
                dbContext.Database.Migrate();
            }
        }

        public void Uninstall()
        {
        }
    }
}
