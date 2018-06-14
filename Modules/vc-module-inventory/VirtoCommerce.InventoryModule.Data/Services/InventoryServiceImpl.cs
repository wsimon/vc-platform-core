using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VirtoCommerce.InventoryModule.Core.Events;
using VirtoCommerce.InventoryModule.Core.Model;
using VirtoCommerce.InventoryModule.Core.Services;
using VirtoCommerce.InventoryModule.Data.Cashing;
using VirtoCommerce.InventoryModule.Data.Model;
using VirtoCommerce.InventoryModule.Data.Repositories;
using VirtoCommerce.Platform.Core.Caching;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Domain;
using VirtoCommerce.Platform.Core.Events;
using VirtoCommerce.Platform.Data.Infrastructure;

namespace VirtoCommerce.InventoryModule.Data.Services
{
    public class InventoryServiceImpl : IInventoryService
    {
        private readonly Func<IInventoryRepository> _repositoryFactory;
        private readonly IEventPublisher _eventPublisher;
        private readonly IMemoryCache _memoryCache;
        public InventoryServiceImpl(Func<IInventoryRepository> repositoryFactory, IEventPublisher eventPublisher, IMemoryCache memoryCache)
        {
            _repositoryFactory = repositoryFactory;
            _eventPublisher = eventPublisher;
            _memoryCache = memoryCache;
        }

        public async Task<IEnumerable<InventoryInfo>> GetByIdsAsync(string[] itemIds)
        {
            var cacheKey = CacheKey.With(GetType(), "PreloadInventory");
            return await _memoryCache.GetOrCreateExclusiveAsync(cacheKey, async (cacheEntry) =>
            {
                cacheEntry.AddExpirationToken(InventoryCacheRegion.CreateChangeToken());
                using (var repository = _repositoryFactory())
                {
                    repository.DisableChangesTracking();
                    var entity = await repository.Inventories.ToArrayAsync();
                    return entity.Select(e => e.ToModel(AbstractTypeFactory<InventoryInfo>.TryCreateInstance()));
                }
            });
        }

        #region IInventoryService Members
        

        public async Task<IEnumerable<InventoryInfo>> GetProductsInventoryInfosAsync(IEnumerable<string> productIds)
        {
            var cacheKey = CacheKey.With(GetType(), "PreloadProductsInventory");
            return await _memoryCache.GetOrCreateExclusiveAsync(cacheKey, async (cacheEntry) =>
            {
                cacheEntry.AddExpirationToken(InventoryCacheRegion.CreateChangeToken());
                var retVal = new List<InventoryInfo>();
                using (var repository = _repositoryFactory())
                {
                    repository.DisableChangesTracking();
                    var entities = await repository.GetProductsInventories(productIds.ToArray());
                    retVal.AddRange(entities.Select(x =>
                        x.ToModel(AbstractTypeFactory<InventoryInfo>.TryCreateInstance())));
                }
                return retVal;
            });
        }

        public async Task UpsertInventoriesAsync(IEnumerable<InventoryInfo> inventoryInfos)
        {
            if (inventoryInfos == null)
            {
                throw new ArgumentNullException(nameof(inventoryInfos));
            }

            var changedEntries = new List<GenericChangedEntry<InventoryInfo>>();
            using (var repository = _repositoryFactory())
            {
                var dataExistInventories = await repository.GetProductsInventories(inventoryInfos.Select(x=>x.ProductId));
                foreach (var changedInventory in inventoryInfos)
                {               
                    var originalEntity = dataExistInventories.FirstOrDefault(x => x.Sku == changedInventory.ProductId && x.FulfillmentCenterId == changedInventory.FulfillmentCenterId);
            
                    var modifiedEntity = AbstractTypeFactory<InventoryEntity>.TryCreateInstance().FromModel(changedInventory);
                    if (originalEntity != null)
                    {
                        changedEntries.Add(new GenericChangedEntry<InventoryInfo>(changedInventory, originalEntity.ToModel(AbstractTypeFactory<InventoryInfo>.TryCreateInstance()), EntryState.Modified));
                        modifiedEntity?.Patch(originalEntity);
                    }
                    else
                    {
                        repository.Add(modifiedEntity);
                        changedEntries.Add(new GenericChangedEntry<InventoryInfo>(changedInventory, EntryState.Added));
                    }
                }

                //Raise domain events
                await _eventPublisher.Publish(new InventoryChangingEvent(changedEntries));
                await repository.UnitOfWork.CommitAsync();
                await _eventPublisher.Publish(new InventoryChangedEvent(changedEntries));

                //Reset cached catalogs and catalogs
                InventoryCacheRegion.ExpireRegion();
            }
        }


        public async Task<InventoryInfo> UpsertInventoryAsync(InventoryInfo inventoryInfo)
        {
            if (inventoryInfo == null)
            {
                throw new ArgumentNullException(nameof(inventoryInfo));
            }
            await UpsertInventoriesAsync(new[] { inventoryInfo });
            return inventoryInfo;
        }

        #endregion

    }
}
