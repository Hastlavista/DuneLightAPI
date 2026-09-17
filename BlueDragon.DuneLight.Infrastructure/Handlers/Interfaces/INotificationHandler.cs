using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Notifications;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Notifications;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface INotificationHandler
{
    /// <summary>Idempotency provjera prije stvaranja (vidi spec section 24/28) — ciljano isto uniqueness kao
    /// ux_notifications_org_type_source_version, unutar ISTOG uow kao samo umetanje. sourceVersion identificira
    /// KONKRETNU pojavu (vidi Notification.cs) — za Booking izvore je to Booking.StatusVersion, za izvore bez
    /// ciklirajućeg životnog vijeka (WaitlistEntry) konstantno 0.</summary>
    Task<bool> ExistsForSource(
        IUnitOfWork uow, Guid organizationId, NotificationType type, NotificationSourceType sourceType, Guid sourceId, int sourceVersion);

    Task Add(IUnitOfWork uow, Notification notification);

    /// <summary>Uska administrativna korekcija (vidi spec section 37) — ako je odgovarajući Notification za TU
    /// KONKRETNU pojavu (sourceVersion) VEĆ stvoren (Pending) prije nego je poslovni događaj koji ga je izazvao
    /// naknadno ispravljen (npr. NoShow -&gt; Confirmed), markira ga Cancelled unutar ISTE uow transakcije kao
    /// korekcija. No-op ako Notification još ne postoji (Outbox ga još nije obradio — tu race pokriva re-provjera
    /// u samom OutboxMessageHandleru) ili više nije Pending. NIKAD ne dira Notification koji nije u ovom uskom
    /// Pending stanju (npr. eksterno dostavljen — vidi spec section 37), ni Notification neke DRUGE (kasnije)
    /// pojave istog Bookinga (vidi spec section 11-12).</summary>
    Task CancelIfPending(
        IUnitOfWork uow, Guid organizationId, NotificationType type, NotificationSourceType sourceType, Guid sourceId, int sourceVersion);

    Task<(List<Notification> Items, int TotalCount)> GetPaged(Guid organizationId, NotificationQuery query);
}
