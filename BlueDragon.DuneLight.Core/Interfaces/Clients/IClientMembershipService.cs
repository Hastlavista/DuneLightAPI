using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Clients;

namespace BlueDragon.DuneLight.Core.Interfaces.Clients;

/// <summary>
/// P2 (faza 2B, docs/p2) — članstva klijenata i njihov lifecycle: prodaja, otkaz i povlačenje, pauza, promjena plana od
/// sljedećeg ciklusa, raniji izlazak, poništavanje prodaje. Svaka naredba je zasebna radnja sa zasebnim grantom.
/// </summary>
public interface IClientMembershipService
{
    Task<List<ClientMembershipDto>> GetByClient(Guid organizationId, Guid clientId);
    Task<ClientMembershipDto> GetById(Guid organizationId, Guid id);

    /// <summary>Pregled 2B — popis članstava kojima izmjena plana nije primijenjena (trajna oznaka), opcionalno za jedan plan.</summary>
    Task<List<ClientMembershipDto>> GetPlanUpdateNotApplied(Guid organizationId, Guid? membershipPlanId);

    /// <summary>K1-8 — članstva koja stoje zbog zatvorenih poslovnica (StandingStillSince), za ručni otkaz ako je zatvaranje trajno.</summary>
    Task<List<ClientMembershipDto>> GetStandingStill(Guid organizationId);

    /// <summary>Datum od kad bi otkaz zatražen danas djelovao (bez promjene).</summary>
    Task<MembershipCancellationPreviewDto> PreviewCancellation(Guid organizationId, Guid id);

    Task<ClientMembershipDto> Sell(Guid organizationId, Guid userId, Guid clientId, ClientMembershipSellRequest request);
    Task<ClientMembershipDto> RequestCancellation(Guid organizationId, Guid userId, Guid id, ClientMembershipCancelRequest request);
    Task<ClientMembershipDto> WithdrawCancellation(Guid organizationId, Guid userId, Guid id);
    Task<ClientMembershipDto> Pause(Guid organizationId, Guid userId, Guid id, ClientMembershipPauseRequest request);
    Task<ClientMembershipPauseEndEarlyResultDto> EndPauseEarly(Guid organizationId, Guid userId, Guid id, Guid pauseId, ClientMembershipPauseEndEarlyRequest request);
    Task<ClientMembershipDto> CancelPause(Guid organizationId, Guid userId, Guid id, Guid pauseId);
    Task<ClientMembershipDto> ChangePlan(Guid organizationId, Guid userId, Guid id, ClientMembershipPlanChangeRequest request);
    Task<ClientMembershipDto> WithdrawPlanChange(Guid organizationId, Guid userId, Guid id);
    Task<ClientMembershipDto> EndEarly(Guid organizationId, Guid userId, Guid id, ClientMembershipEndRequest request);
    Task<ClientMembershipDto> VoidSale(Guid organizationId, Guid userId, Guid id, ClientMembershipVoidRequest request);

    /// <summary>2F (§16.3) — korisnik provizije na prvu prodaju, dok provizija nije nastala.</summary>
    Task<ClientMembershipDto> SetSaleCommissionEmployee(Guid organizationId, Guid userId, Guid id, DTOs.Commissions.SaleCommissionEmployeeRequest request);

    /// <summary>2C — otvoreni periodi članstva (najnoviji prvi).</summary>
    Task<List<MembershipPeriodDto>> GetPeriods(Guid organizationId, Guid id);

    /// <summary>2C — zaduženja članstva (najnovija prva) s izvedenom plaćenošću.</summary>
    Task<List<MembershipChargeDto>> GetCharges(Guid organizationId, Guid id);

    /// <summary>2C (Q16) — otpis preostalog duga zaduženja (uz razlog); otpisano zaduženje je konačno.</summary>
    Task<MembershipChargeDto> WriteOffCharge(Guid organizationId, Guid userId, Guid chargeId, MembershipChargeWriteOffRequest request);

    /// <summary>2C — članstva neaktivnog plana koja će završiti na sljedećoj obnovi ako plan tada još nije aktivan.</summary>
    Task<List<ClientMembershipDto>> GetEndingDueToPlanDeactivation(Guid organizationId, Guid membershipPlanId);
}
