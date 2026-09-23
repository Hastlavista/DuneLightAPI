using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

/// <summary>Samostalan (ne-transakcijski) upis — potencijalna buduća dijagnostika/read-only pristup. Sam
/// upgrade-apply put OVO NE koristi (vidi GrantGroupTemplateUpgradeAuditLog klasnu napomenu) — piše izravno preko
/// uow.Context.GrantGroupTemplateUpgradeAuditLogs da upis bude atomski s ostatkom Apply transakcije.</summary>
public interface IGrantGroupTemplateUpgradeAuditLogHandler
{
    Task Add(GrantGroupTemplateUpgradeAuditLog entry);
}
