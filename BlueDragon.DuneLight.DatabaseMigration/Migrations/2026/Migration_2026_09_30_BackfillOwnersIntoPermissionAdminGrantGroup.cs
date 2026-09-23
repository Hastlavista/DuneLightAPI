using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Grant-only Tenant Authorization Refactor, Part I/P — postojeće organizacije se NE smiju zaključati kad
/// GrantContext izgubi User.IsOwner bypass (vidi GrantGroupHandler.ResolveEffective/GrantContext). Za SVAKOG
/// aktivnog korisnika s IsOwner=true:
/// 1. Ako organizacija ima GrantGroup TOČNO nazvanu "Admin" (poznata provenance — to je jedino ime koje je stari
///    EnsureDefaultGrantGroups ikad automatski kreirao za "admin" predložak, vidi CapabilityV1SeedData/
///    CapabilityV2SeedData.Templates "admin".DisplayNameHr), dodaje joj permissions.view/manage/assignments.manage
///    (ako ih već nema) i dodjeljuje Ownera na nju (ako već nije).
/// 2. Inače (grupa preimenovana/obrisana — rijedak edge-case) kreira NOVU, migracijski-markiranu GrantGroup
///    ("Administratori (migracija)") s punim Admin v3 grant skupom (materijaliziran isto kao
///    SeedAdminTemplateV3/BackfillExistingDefaultGrantGroupSnapshots) i dodjeljuje Ownera na nju.
/// Ne dira Trener/Recepcija niti bilo koju drugu postojeću GrantGroup. Idempotentno — svaki insert je uvjetovan
/// WHERE NOT EXISTS, siguran za ponovno pokretanje.
/// </summary>
[DeveloperMigration(2026, 09, 30, Developer.SilvioHabazin, 0)]
public class BackfillOwnersIntoPermissionAdminGrantGroup : DuneLightMigration
{
    private const string FallbackGroupName = "Administratori (migracija)";

    public override void Up()
    {
        HashSet<string> fullAdminGrantSet = ComputeAdminV3GrantSet();

        Execute.WithConnection((connection, transaction) =>
        {
            List<(Guid UserId, Guid OrganizationId)> owners = new();
            using (IDbCommand selectOwners = connection.CreateCommand())
            {
                selectOwners.Transaction = transaction;
                selectOwners.CommandText = "SELECT id, organization_id FROM dunelight.users WHERE is_owner = true AND is_active = true";
                using IDataReader reader = selectOwners.ExecuteReader();
                while (reader.Read())
                    owners.Add((reader.GetGuid(0), reader.GetGuid(1)));
            }

            foreach ((Guid userId, Guid organizationId) in owners)
            {
                Guid? adminGroupId = FindGroupByName(connection, transaction, organizationId, "Admin");

                Guid targetGroupId;
                if (adminGroupId.HasValue)
                {
                    targetGroupId = adminGroupId.Value;
                    EnsureGrant(connection, transaction, targetGroupId, "permissions.view");
                    EnsureGrant(connection, transaction, targetGroupId, "permissions.manage");
                    EnsureGrant(connection, transaction, targetGroupId, "permissions.assignments.manage");
                }
                else
                {
                    Guid? fallbackGroupId = FindGroupByName(connection, transaction, organizationId, FallbackGroupName);
                    if (fallbackGroupId.HasValue)
                    {
                        targetGroupId = fallbackGroupId.Value;
                    }
                    else
                    {
                        targetGroupId = Guid.NewGuid();
                        using IDbCommand insertGroup = connection.CreateCommand();
                        insertGroup.Transaction = transaction;
                        insertGroup.CommandText = @"
                            INSERT INTO dunelight.grant_groups (id, organization_id, name, created_at)
                            VALUES (@id, @orgId, @name, @createdAt)";
                        AddParameter(insertGroup, "@id", targetGroupId);
                        AddParameter(insertGroup, "@orgId", organizationId);
                        AddParameter(insertGroup, "@name", FallbackGroupName);
                        AddParameter(insertGroup, "@createdAt", DateTimeOffset.UtcNow);
                        insertGroup.ExecuteNonQuery();

                        foreach (string grantKey in fullAdminGrantSet)
                            EnsureGrant(connection, transaction, targetGroupId, grantKey);
                    }
                }

                using IDbCommand insertAssignment = connection.CreateCommand();
                insertAssignment.Transaction = transaction;
                insertAssignment.CommandText = @"
                    INSERT INTO dunelight.user_grant_groups (id, user_id, grant_group_id)
                    SELECT @id, @userId, @groupId
                    WHERE NOT EXISTS (
                        SELECT 1 FROM dunelight.user_grant_groups WHERE user_id = @userId AND grant_group_id = @groupId)";
                AddParameter(insertAssignment, "@id", Guid.NewGuid());
                AddParameter(insertAssignment, "@userId", userId);
                AddParameter(insertAssignment, "@groupId", targetGroupId);
                insertAssignment.ExecuteNonQuery();
            }
        });
    }

    private static Guid? FindGroupByName(IDbConnection connection, IDbTransaction transaction, Guid organizationId, string name)
    {
        using IDbCommand select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "SELECT id FROM dunelight.grant_groups WHERE organization_id = @orgId AND name = @name LIMIT 1";
        AddParameter(select, "@orgId", organizationId);
        AddParameter(select, "@name", name);
        object result = select.ExecuteScalar();
        return result == null || result == DBNull.Value ? null : (Guid?)result;
    }

    private static void EnsureGrant(IDbConnection connection, IDbTransaction transaction, Guid grantGroupId, string grantKey)
    {
        using IDbCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = @"
            INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
            SELECT @id, @groupId, @grantKey
            WHERE NOT EXISTS (
                SELECT 1 FROM dunelight.grant_group_grants WHERE grant_group_id = @groupId AND grant_key = @grantKey)";
        AddParameter(insert, "@id", Guid.NewGuid());
        AddParameter(insert, "@groupId", grantGroupId);
        AddParameter(insert, "@grantKey", grantKey);
        insert.ExecuteNonQuery();
    }

    private static void AddParameter(IDbCommand command, string name, object value)
    {
        IDbDataParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Duplikat CapabilityMaterializationService algoritma (isti princip kao
    /// BackfillExistingDefaultGrantGroupSnapshots — migracija ne smije ovisiti o Infrastructure projektu). Računa
    /// TOČNO ono što SeedAdminTemplateV3 materijalizira: Admin v2 (32 selekcije) + nova organization.permissions.manage
    /// (Manage) + 6 compatibility extras.</summary>
    private static HashSet<string> ComputeAdminV3GrantSet()
    {
        Dictionary<string, CapabilityV1SeedData.CapabilitySeed> capsByKey = CapabilityV1SeedData.Capabilities.ToDictionary(c => c.Key);
        CapabilityV1SeedData.TemplateSeed adminV2 = CapabilityV2SeedData.Templates.Single(t => t.Key == "admin");

        HashSet<string> result = new();
        foreach (CapabilityV1SeedData.TemplateCapabilitySeed selection in adminV2.Selections)
        {
            CapabilityV1SeedData.CapabilitySeed capability = capsByKey[selection.CapabilityKey];
            result.UnionWith(Materialize(capability.ScopeModel, selection.SelectedScope, capability.Grants));
        }

        result.UnionWith(adminV2.CompatibilityExtraGrants);

        foreach ((string grantKey, string _) in PermissionAdministrationCapabilitySeedData.Grants)
            result.Add(grantKey);

        return result;
    }

    /// <summary>Duplikat CapabilityMaterializationService.ResolveActiveRoles+filter — vidi
    /// BackfillExistingDefaultGrantGroupSnapshots.Materialize za isti obrazac/obrazloženje.</summary>
    private static IEnumerable<string> Materialize(string scopeModel, string selectedScope, CapabilityV1SeedData.CapabilityGrantSeed[] grants)
    {
        HashSet<string> activeRoles = new();

        switch (scopeModel)
        {
            case "None":
                activeRoles.Add("PrimaryNoScope");
                break;
            case "ViewManage":
                activeRoles.Add("PrimaryViewOnly");
                if (selectedScope == "Manage")
                    activeRoles.Add("PrimaryManage");
                break;
            case "OwnAll":
                if (selectedScope == "Own")
                    activeRoles.Add("PrimaryOwn");
                if (selectedScope == "All")
                    activeRoles.Add("PrimaryAll");
                break;
            case "ViewOwnAll":
                activeRoles.Add("PrimaryViewOnly");
                if (selectedScope == "Own")
                    activeRoles.Add("PrimaryOwn");
                if (selectedScope == "All")
                    activeRoles.Add("PrimaryAll");
                break;
        }

        activeRoles.Add("MandatorySupporting");

        return grants.Where(g => activeRoles.Contains(g.Role)).Select(g => g.GrantKey);
    }
}
