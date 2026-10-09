using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// K1-9 — neblokirajuće upozorenje recepciji pri dolasku i odradi kad sesija ima neplaćen dug (nije pokrivena ni paketom ni
/// članarinom). Čita stanje NAKON naredbe (DTO sudjelovanja): dug ≤ 0 (plaćeno, cijena 0, pokriveno paketom ili članarinom,
/// pokriće na čekanju) → bez upozorenja; klijent ima prihvatljiv neodabran paket → PARTICIPATION_PACKAGE_AVAILABLE; inače
/// PARTICIPATION_NOT_COVERED, uz odluku pokrića kad članarina postoji, ali ne pokriva.
/// </summary>
public static class ParticipationCoverageWarnings
{
    public static async Task<List<WarningDto>> For(
        IClientPackageService clientPackageService, Guid organizationId, Appointment appointment, Guid clientId, BookingParticipationDto participation)
    {
        List<WarningDto> warnings = new();
        if (participation.OutstandingAmount <= 0m
            || participation.Status is BookingStatus.Cancelled or BookingStatus.NoShow)
            return warnings;

        AppointmentSegment segment = appointment.Segments.Single(s => s.Id == participation.AppointmentSegmentId);
        List<ClientPackageDto> eligible = participation.PackageCovered
            ? new List<ClientPackageDto>()
            : await clientPackageService.GetEligibleForService(
                organizationId, clientId, segment.ServiceId, segment.PlannedStart, appointment.CompanyId);

        WarningParticipationCoverageDetails details = new()
        {
            ParticipationId = participation.Id,
            ClientId = clientId,
            OutstandingAmount = participation.OutstandingAmount,
            MembershipCoverageStatus = participation.MembershipCoverage?.Status,
            MembershipCoverageReason = participation.MembershipCoverage?.Reason
        };

        if (eligible.Count > 0)
        {
            details.EligiblePackages = eligible
                .Select(p => new WarningEligiblePackage { ClientPackageId = p.Id, PackageName = p.PackageName })
                .ToList();
            warnings.Add(new WarningDto(WarningCodes.ParticipationPackageAvailable, details));
        }
        else
        {
            warnings.Add(new WarningDto(WarningCodes.ParticipationNotCovered, details));
        }

        return warnings;
    }
}
