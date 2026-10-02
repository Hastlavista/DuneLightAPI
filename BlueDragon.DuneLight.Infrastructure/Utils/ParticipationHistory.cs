using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase D3B1 — JEDINO mjesto koje definira kada se Booking + njegovo sudjelovanje smiju fizički obrisati (zaključano
/// pravilo): samo dok sudjelovanje NEMA izvršnu povijest, tj. je "netaknuto" — Status == Confirmed, StatusVersion == 0,
/// bez dolaska (ArrivedAt/ArrivedBy), bez razloga otkazivanja i bez klasifikacije kasnog otkazivanja. Sam trenutni status
/// nije dovoljan: Confirmed → Completed → ispravak na Confirmed ima StatusVersion &gt; 0 i NIJE netaknuto.
/// Phase D3B3A: ni sudjelovanje s BILO KAKVOM poviješću potrošnje paketa (aktivnom ili poništenom PackageConsumption)
/// nije netaknuto — taj ledger se ne smije izgubiti brisanjem. Phase D3B3B (F-09): ni sudjelovanje s BILO KOJOM
/// stavkom checkouta (pa time i plaćanjem/alokacijom — svaka alokacija visi na stavci) — povijest namirenja se ne
/// briše radi uređivanja termina, a pokušaj vraća REFERENCED_CANNOT_DELETE umjesto sirove FK greške baze. Cijena (D3B2)
/// NIJE izvršna povijest.
///
/// Koriste ga sva tri postojeća toka fizičkog brisanja: izostavljeni Confirmed klijent kod Update i CompleteExisting
/// (AppointmentHandler.UpdateWithBookingsCore) te brisanje termina istog dana (AppointmentHandler.Delete). Brisanje je
/// UVIJEK eksplicitno (sudjelovanje pa Booking), nikad kaskadom; sudjelovanje s poviješću → REFERENCED_CANNOT_DELETE.
/// </summary>
public static class ParticipationHistory
{
    public static bool IsUntouched(BookingSegmentParticipation participation)
    {
        ArgumentNullException.ThrowIfNull(participation);
        return participation.Status == ParticipationStatus.Confirmed
               && participation.StatusVersion == 0
               && participation.ArrivedAt == null
               && participation.ArrivedBy == null
               && participation.CancellationReason == null
               && participation.IsLateCancellation == null
               && participation.PackageConsumptions.Count == 0
               && participation.CheckoutItems.Count == 0;
    }

    /// <summary>Phase M1E — uklanja SAMO zadana sudjelovanja (npr. jednog segmenta) ako su sva netaknuta, a Booking-spremnik
    /// samo kad mu nakon toga ne ostane nijedno sudjelovanje; ili baca REFERENCED_CANNOT_DELETE (ništa ne označava). Booking
    /// nema vlastitu povijest izvan sudjelovanja (nema statusa ni iznosa).</summary>
    public static async Task RemoveUntouchedParticipations(
        DatabaseContext context, IReadOnlyCollection<Booking> bookings, IReadOnlyCollection<BookingSegmentParticipation> participations, string message)
    {
        foreach (BookingSegmentParticipation participation in participations)
        {
            var checkoutItems = context.Entry(participation).Collection(p => p.CheckoutItems);
            if (!checkoutItems.IsLoaded)
                await checkoutItems.LoadAsync();
            var consumptions = context.Entry(participation).Collection(p => p.PackageConsumptions);
            if (!consumptions.IsLoaded)
                await consumptions.LoadAsync();
        }

        if (participations.Any(p => !IsUntouched(p)))
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, message);

        HashSet<Guid> removed = participations.Select(p => p.Id.GetValueOrDefault()).ToHashSet();
        context.BookingSegmentParticipations.RemoveRange(participations);
        context.Bookings.RemoveRange(bookings.Where(b =>
            b.Participations.Count > 0 && b.Participations.All(p => removed.Contains(p.Id.GetValueOrDefault()))));
    }

    /// <summary>Označava za brisanje (unutar pozivateljevog SaveChanges) svako sudjelovanje pa Booking — ili baca
    /// REFERENCED_CANNOT_DELETE (i ne označava ništa) ako bilo koje sudjelovanje ima povijest.</summary>
    public static async Task RemoveUntouched(DatabaseContext context, IReadOnlyCollection<Booking> bookings, string message)
    {
        List<BookingSegmentParticipation> participations = bookings.SelectMany(b => b.Participations).ToList();
        // Povijest namirenja (CheckoutItems) se ne učitava automatski — učitaj je eksplicitno da pravilo ne bi tiho
        // vidjelo "praznu" kolekciju (sudjelovanja su praćena u ovom kontekstu).
        foreach (BookingSegmentParticipation participation in participations)
        {
            var checkoutItems = context.Entry(participation).Collection(p => p.CheckoutItems);
            if (!checkoutItems.IsLoaded)
                await checkoutItems.LoadAsync();
        }

        if (participations.Any(p => !IsUntouched(p)))
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, message);

        context.BookingSegmentParticipations.RemoveRange(participations);
        context.Bookings.RemoveRange(bookings);
    }
}
