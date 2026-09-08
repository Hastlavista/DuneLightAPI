using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Groups;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class ClientHistoryService : IClientHistoryService
{
    private readonly IClientHandler _clientHandler;
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly IClientPackageService _clientPackageService;
    private readonly IGroupService _groupService;

    public ClientHistoryService(
        IClientHandler clientHandler,
        IAppointmentHandler appointmentHandler,
        IClientPackageService clientPackageService,
        IGroupService groupService)
    {
        _clientHandler = clientHandler;
        _appointmentHandler = appointmentHandler;
        _clientPackageService = clientPackageService;
        _groupService = groupService;
    }

    public async Task<ClientHistorySummaryDto> GetSummary(Guid organizationId, Guid clientId)
    {
        Client client = await _clientHandler.GetByIdLight(organizationId, clientId);
        if (client == null)
            throw new NotFoundAppException("Client", clientId);

        List<ClientGroupMembershipDto> memberships = await _groupService.GetMembershipsByClient(organizationId, clientId);
        List<Guid> activeGroupIds = memberships.Where(m => m.IsActive).Select(m => m.GroupId).ToList();

        ClientAppointmentStatsDto stats = await _appointmentHandler.GetStatsForClient(organizationId, clientId, activeGroupIds);

        List<ClientPackageDto> packages = await _clientPackageService.GetByClient(organizationId, clientId);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int activePackagesCount = packages.Count(p => p.Status == ClientPackageStatus.Active && p.ExpiryDate >= now);

        return new ClientHistorySummaryDto
        {
            ClientSince = client.CreatedAt,
            CompletedVisitsCount = stats.CompletedVisitsCount,
            NoShowCount = stats.NoShowCount,
            CancelledCount = stats.CancelledCount,
            LastVisitAt = stats.LastVisitAt,
            NextVisitAt = stats.NextVisitAt,
            ActivePackagesCount = activePackagesCount,
            ActiveGroupMembershipsCount = activeGroupIds.Count
        };
    }
}
