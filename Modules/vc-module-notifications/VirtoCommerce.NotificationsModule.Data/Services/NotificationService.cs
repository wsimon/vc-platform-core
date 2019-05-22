using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using VirtoCommerce.NotificationsModule.Core.Events;
using VirtoCommerce.NotificationsModule.Core.Model;
using VirtoCommerce.NotificationsModule.Core.Services;
using VirtoCommerce.NotificationsModule.Data.Model;
using VirtoCommerce.NotificationsModule.Data.Repositories;
using VirtoCommerce.NotificationsModule.Data.Validation;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Events;

namespace VirtoCommerce.NotificationsModule.Data.Services
{

    public class NotificationService : INotificationService, INotificationRegistrar
    {
        private readonly IEventPublisher _eventPublisher;
        private readonly Func<INotificationRepository> _repositoryFactory;

        public NotificationService(Func<INotificationRepository> repositoryFactory, IEventPublisher eventPublisher)
        {
            _repositoryFactory = repositoryFactory;
            _eventPublisher = eventPublisher;
        }


        public async Task<Notification[]> GetByIdsAsync(string[] ids, string responseGroup = null)
        {
            using (var repository = _repositoryFactory())
            {
                var notifications = await repository.GetByIdsAsync(ids, responseGroup);
                return notifications.Select(n => n.ToModel(AbstractTypeFactory<Notification>.TryCreateInstance(n.Type))).ToArray();
            }
        }

        public async Task SaveChangesAsync(Notification[] notifications)
        {
            if (notifications != null && notifications.Any())
            {
                ValidateNotificationProperties(notifications);

                var pkMap = new PrimaryKeyResolvingMap();
                var changedEntries = new List<GenericChangedEntry<Notification>>();
                using (var repository = _repositoryFactory())
                {
                    var existingNotificationEntities = await repository.GetByIdsAsync(notifications.Select(m => m.Id).ToArray(), NotificationResponseGroup.Full.ToString());
                    foreach (var notification in notifications)
                    {
                        var originalEntity = existingNotificationEntities.FirstOrDefault(n => n.Id.Equals(notification.Id));
                        var modifiedEntity = AbstractTypeFactory<NotificationEntity>.TryCreateInstance($"{notification.Kind}Entity").FromModel(notification, pkMap);

                        if (originalEntity != null)
                        {
                            changedEntries.Add(new GenericChangedEntry<Notification>(notification, originalEntity.ToModel(AbstractTypeFactory<Notification>.TryCreateInstance()), EntryState.Modified));
                            modifiedEntity?.Patch(originalEntity);
                        }
                        else
                        {
                            repository.Add(modifiedEntity);
                            changedEntries.Add(new GenericChangedEntry<Notification>(notification, EntryState.Added));
                        }
                    }

                    //Raise domain events
                    await _eventPublisher.Publish(new NotificationChangingEvent(changedEntries));
                    //Save changes in database
                    await repository.UnitOfWork.CommitAsync();
                    pkMap.ResolvePrimaryKeys();
                    await _eventPublisher.Publish(new NotificationChangedEvent(changedEntries));
                }
            }
        }


        public void RegisterNotification<T>() where T : Notification
        {
            if (AbstractTypeFactory<Notification>.AllTypeInfos.All(t => t.Type != typeof(T)))
            {
                AbstractTypeFactory<Notification>.RegisterType<T>();
            }
        }

        public void RegisterNotification<T, TMap>() where T : Notification
                                                    where TMap : AuditableEntity
        {
            if (AbstractTypeFactory<Notification>.AllTypeInfos.All(t => t.Type != typeof(T)))
            {
                AbstractTypeFactory<Notification>.RegisterType<T>().MapToType<TMap>();
            }
        }

        public void RegisterNotificationMessage<T>() where T : NotificationMessage
        {
            if (AbstractTypeFactory<NotificationMessage>.AllTypeInfos.All(t => t.Type != typeof(T)))
            {
                AbstractTypeFactory<NotificationMessage>.RegisterType<T>();
            }
        }

        public void RegisterNotificationMessage<T, TMap>() where T : NotificationMessage where TMap : AuditableEntity
        {
            if (AbstractTypeFactory<NotificationMessage>.AllTypeInfos.All(t => t.Type != typeof(T)))
            {
                AbstractTypeFactory<NotificationMessage>.RegisterType<T>().MapToType<TMap>();
            }
        }

        public void RegisterNotificationTemplate<T>() where T : NotificationTemplate
        {
            if (AbstractTypeFactory<NotificationTemplate>.AllTypeInfos.All(t => t.Type != typeof(T)))
            {
                AbstractTypeFactory<NotificationTemplate>.RegisterType<T>();
            }
        }

        public void RegisterNotificationTemplate<T, TMap>() where T : NotificationTemplate where TMap : AuditableEntity
        {
            if (AbstractTypeFactory<NotificationTemplate>.AllTypeInfos.All(t => t.Type != typeof(T)))
            {
                AbstractTypeFactory<NotificationTemplate>.RegisterType<T>().MapToType<TMap>();
            }
        }

        private void ValidateNotificationProperties(IEnumerable<Notification> notifications)
        {
            if (notifications == null)
            {
                throw new ArgumentNullException(nameof(notifications));
            }

            var validator = new NotificationValidator();
            foreach (var notification in notifications)
            {
                validator.ValidateAndThrow(notification);
            }
        }
    }
}
