using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// FAZA 1 Backend Capability System — dodaje SEDAM autorsko-vrijeme metapodatkovnih tablica (CapabilityDefinition,
/// CapabilityDefinitionGrant, DefaultRoleTemplate, DefaultRoleTemplateCapability, DefaultRoleTemplateGrant,
/// GrantGroupCapabilitySnapshot, GrantGroupTemplateGrant) i seeda v1 podatke. Runtime autorizacija OSTAJE
/// isključivo na grant_group_grants (vidi Migration_2026_08_GrantSystem) — ova migracija NIKAD ne piše/mijenja
/// grant_group_grants izravno (backfill dolje samo DODAJE provenance metapodatke za grupe koje već TOČNO
/// odgovaraju v1 predlošku, nikad ne mijenja njihov raw grant skup).
/// </summary>
[DeveloperMigration(2026, 09, 26, Developer.SilvioHabazin, 0)]
public class CreateCapabilityDefinitionsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.CapabilityDefinitions)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_capability_definitions")
            .WithColumn("key").AsString(150).NotNullable()
            .WithColumn("version").AsInt32().NotNullable()
            .WithColumn("category_key").AsString(50).NotNullable()
            .WithColumn("scope_model").AsString(40).NotNullable()
            .WithColumn("sensitivity").AsString(40).NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("deprecated_at").AsDateTimeOffset().Nullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable();

        Create.Index("ux_capability_definitions_key_version")
            .OnTable(Tables.CapabilityDefinitions).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("key").Ascending()
            .OnColumn("version").Ascending()
            .WithOptions().Unique();

        Execute.Sql(@"ALTER TABLE dunelight.capability_definitions ADD CONSTRAINT ck_capability_definitions_version_positive CHECK (version > 0);");
    }
}

[DeveloperMigration(2026, 09, 26, Developer.SilvioHabazin, 1)]
public class CreateCapabilityDefinitionGrantsTable : DuneLightMigration
{
    public override void Up()
    {
        // grant_key nema FK — katalog grantova živi u kodu (Grants.cs), ne u bazi (isti obrazac kao grant_group_grants).
        Create.Table(Tables.CapabilityDefinitionGrants)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_capability_definition_grants")
            .WithColumn("capability_definition_id").AsGuid().NotNullable()
            .WithColumn("grant_key").AsString(100).NotNullable()
            .WithColumn("role").AsString(40).NotNullable();

        // Cascade — vidi DatabaseContext.ConfigureCapabilities napomenu (samo neiskorištene verzije se brišu,
        // to je servisni guard, ne DB-level restrikcija na OVOJ vezi).
        Create.ForeignKey("fk_capability_definition_grants_capability_definition_id")
            .FromTable(Tables.CapabilityDefinitionGrants).InSchema(Tables.Schemas.DuneLight).ForeignColumn("capability_definition_id")
            .ToTable(Tables.CapabilityDefinitions).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("ux_capability_definition_grants_capability_grant")
            .OnTable(Tables.CapabilityDefinitionGrants).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("capability_definition_id").Ascending()
            .OnColumn("grant_key").Ascending()
            .WithOptions().Unique();
    }
}

[DeveloperMigration(2026, 09, 26, Developer.SilvioHabazin, 2)]
public class CreateDefaultRoleTemplatesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.DefaultRoleTemplates)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_default_role_templates")
            .WithColumn("key").AsString(150).NotNullable()
            .WithColumn("version").AsInt32().NotNullable()
            .WithColumn("display_name_hr").AsString(255).NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable();

        Create.Index("ux_default_role_templates_key_version")
            .OnTable(Tables.DefaultRoleTemplates).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("key").Ascending()
            .OnColumn("version").Ascending()
            .WithOptions().Unique();

        Execute.Sql(@"ALTER TABLE dunelight.default_role_templates ADD CONSTRAINT ck_default_role_templates_version_positive CHECK (version > 0);");
    }
}

[DeveloperMigration(2026, 09, 26, Developer.SilvioHabazin, 3)]
public class CreateDefaultRoleTemplateCapabilitiesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.DefaultRoleTemplateCapabilities)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_default_role_template_capabilities")
            .WithColumn("default_role_template_id").AsGuid().NotNullable()
            .WithColumn("capability_definition_id").AsGuid().NotNullable()
            .WithColumn("selected_scope").AsString(40).NotNullable();

        Create.ForeignKey("fk_default_role_template_capabilities_template_id")
            .FromTable(Tables.DefaultRoleTemplateCapabilities).InSchema(Tables.Schemas.DuneLight).ForeignColumn("default_role_template_id")
            .ToTable(Tables.DefaultRoleTemplates).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        // Restrict — referencirana capability VERZIJA se nikad ne smije hard-obrisati dok je predložak koristi
        // (vidi FAZA 1 Part D/Q, CapabilityVersionGuard).
        Create.ForeignKey("fk_default_role_template_capabilities_capability_id")
            .FromTable(Tables.DefaultRoleTemplateCapabilities).InSchema(Tables.Schemas.DuneLight).ForeignColumn("capability_definition_id")
            .ToTable(Tables.CapabilityDefinitions).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.None);

        Create.Index("ux_default_role_template_capabilities_template_capability")
            .OnTable(Tables.DefaultRoleTemplateCapabilities).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("default_role_template_id").Ascending()
            .OnColumn("capability_definition_id").Ascending()
            .WithOptions().Unique();
    }
}

[DeveloperMigration(2026, 09, 26, Developer.SilvioHabazin, 4)]
public class CreateDefaultRoleTemplateGrantsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.DefaultRoleTemplateGrants)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_default_role_template_grants")
            .WithColumn("default_role_template_id").AsGuid().NotNullable()
            .WithColumn("grant_key").AsString(100).NotNullable()
            .WithColumn("reason").AsString(40).NotNullable();

        Create.ForeignKey("fk_default_role_template_grants_template_id")
            .FromTable(Tables.DefaultRoleTemplateGrants).InSchema(Tables.Schemas.DuneLight).ForeignColumn("default_role_template_id")
            .ToTable(Tables.DefaultRoleTemplates).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("ux_default_role_template_grants_template_grant")
            .OnTable(Tables.DefaultRoleTemplateGrants).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("default_role_template_id").Ascending()
            .OnColumn("grant_key").Ascending()
            .WithOptions().Unique();
    }
}

[DeveloperMigration(2026, 09, 26, Developer.SilvioHabazin, 5)]
public class CreateGrantGroupCapabilitySnapshotsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.GrantGroupCapabilitySnapshots)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_grant_group_capability_snapshots")
            .WithColumn("grant_group_id").AsGuid().NotNullable()
            .WithColumn("capability_definition_id").AsGuid().NotNullable()
            .WithColumn("selected_scope").AsString(40).NotNullable()
            .WithColumn("source_template_key").AsString(150).Nullable()
            .WithColumn("source_template_version").AsInt32().Nullable()
            .WithColumn("applied_at").AsDateTimeOffset().NotNullable()
            .WithColumn("applied_by").AsGuid().Nullable();

        Create.ForeignKey("fk_grant_group_capability_snapshots_grant_group_id")
            .FromTable(Tables.GrantGroupCapabilitySnapshots).InSchema(Tables.Schemas.DuneLight).ForeignColumn("grant_group_id")
            .ToTable(Tables.GrantGroups).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        // Restrict — vidi CreateDefaultRoleTemplateCapabilitiesTable napomenu, isto obrazloženje.
        Create.ForeignKey("fk_grant_group_capability_snapshots_capability_id")
            .FromTable(Tables.GrantGroupCapabilitySnapshots).InSchema(Tables.Schemas.DuneLight).ForeignColumn("capability_definition_id")
            .ToTable(Tables.CapabilityDefinitions).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.None);

        Create.Index("ux_grant_group_capability_snapshots_group_capability")
            .OnTable(Tables.GrantGroupCapabilitySnapshots).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("grant_group_id").Ascending()
            .OnColumn("capability_definition_id").Ascending()
            .WithOptions().Unique();
    }
}

[DeveloperMigration(2026, 09, 26, Developer.SilvioHabazin, 6)]
public class CreateGrantGroupTemplateGrantsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.GrantGroupTemplateGrants)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_grant_group_template_grants")
            .WithColumn("grant_group_id").AsGuid().NotNullable()
            .WithColumn("grant_key").AsString(100).NotNullable()
            .WithColumn("source_template_key").AsString(150).NotNullable()
            .WithColumn("source_template_version").AsInt32().NotNullable()
            .WithColumn("applied_at").AsDateTimeOffset().NotNullable()
            .WithColumn("applied_by").AsGuid().Nullable();

        Create.ForeignKey("fk_grant_group_template_grants_grant_group_id")
            .FromTable(Tables.GrantGroupTemplateGrants).InSchema(Tables.Schemas.DuneLight).ForeignColumn("grant_group_id")
            .ToTable(Tables.GrantGroups).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("ux_grant_group_template_grants_group_grant")
            .OnTable(Tables.GrantGroupTemplateGrants).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("grant_group_id").Ascending()
            .OnColumn("grant_key").Ascending()
            .WithOptions().Unique();
    }
}

/// <summary>Seeda 32 CapabilityDefinition v1 retke + njihove CapabilityDefinitionGrant mape — vidi CapabilityV1SeedData.</summary>
[DeveloperMigration(2026, 09, 26, Developer.SilvioHabazin, 7)]
public class SeedCapabilityDefinitionsV1 : DuneLightMigration
{
    public override void Up()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (CapabilityV1SeedData.CapabilitySeed capability in CapabilityV1SeedData.Capabilities)
        {
            Guid capabilityId = CapabilityV1SeedData.CapabilityId(capability.Key);

            Insert.IntoTable(Tables.CapabilityDefinitions).InSchema(Tables.Schemas.DuneLight).Row(new
            {
                id = capabilityId,
                key = capability.Key,
                version = CapabilityV1SeedData.CapabilityVersion,
                category_key = capability.CategoryKey,
                scope_model = capability.ScopeModel,
                sensitivity = capability.Sensitivity,
                is_active = true,
                created_at = now
            });

            foreach (CapabilityV1SeedData.CapabilityGrantSeed grant in capability.Grants)
            {
                Insert.IntoTable(Tables.CapabilityDefinitionGrants).InSchema(Tables.Schemas.DuneLight).Row(new
                {
                    id = Guid.NewGuid(),
                    capability_definition_id = capabilityId,
                    grant_key = grant.GrantKey,
                    role = grant.Role
                });
            }
        }
    }
}

/// <summary>Seeda Admin/Trener/Recepcija v1 predloške + njihove capability odabire + Admin compatibility extras —
/// vidi CapabilityV1SeedData. v1 REPRODUCIRA postojeće ponašanje TOČNO (vidi Backend Capability Phase 1 report za
/// matematičku provjeru), namjerno bez ikakvih v2 poboljšanja (vidi FAZA 1 Part N).</summary>
[DeveloperMigration(2026, 09, 26, Developer.SilvioHabazin, 8)]
public class SeedDefaultRoleTemplatesV1 : DuneLightMigration
{
    public override void Up()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (CapabilityV1SeedData.TemplateSeed template in CapabilityV1SeedData.Templates)
        {
            Guid templateId = CapabilityV1SeedData.TemplateId(template.Key);

            Insert.IntoTable(Tables.DefaultRoleTemplates).InSchema(Tables.Schemas.DuneLight).Row(new
            {
                id = templateId,
                key = template.Key,
                version = CapabilityV1SeedData.TemplateVersion,
                display_name_hr = template.DisplayNameHr,
                is_active = true,
                created_at = now
            });

            foreach (CapabilityV1SeedData.TemplateCapabilitySeed selection in template.Selections)
            {
                Insert.IntoTable(Tables.DefaultRoleTemplateCapabilities).InSchema(Tables.Schemas.DuneLight).Row(new
                {
                    id = Guid.NewGuid(),
                    default_role_template_id = templateId,
                    capability_definition_id = CapabilityV1SeedData.CapabilityId(selection.CapabilityKey),
                    selected_scope = selection.SelectedScope
                });
            }

            foreach (string grantKey in template.CompatibilityExtraGrants)
            {
                Insert.IntoTable(Tables.DefaultRoleTemplateGrants).InSchema(Tables.Schemas.DuneLight).Row(new
                {
                    id = Guid.NewGuid(),
                    default_role_template_id = templateId,
                    grant_key = grantKey,
                    reason = "CompatibilityExtra"
                });
            }
        }
    }
}

/// <summary>
/// FAZA 1 Part O — za postojeće GrantGroup retke nazvane TOČNO "Admin"/"Trener"/"Recepcija" (bilo koje
/// organizacije), provjerava odgovara li njihov TRENUTNI raw grant skup TOČNO onome što v1 predložak materijalizira
/// (capability selections UNION compatibility extras). Ako DA — dodaje snapshot/provenance metapodatke (BEZ
/// diranja grant_group_grants). Ako NE — ništa ne radi (grupa ostaje "custom"/drifted, njeni permission-i se NIKAD
/// ne prepisuju ovom migracijom). Materijalizacijska logika je namjerno duplicirana (ne poziva Infrastructure
/// projekt) — isti princip kao CapabilityV1SeedData, migracija je samostalan snapshot.
/// </summary>
[DeveloperMigration(2026, 09, 26, Developer.SilvioHabazin, 9)]
public class BackfillExistingDefaultGrantGroupSnapshots : DuneLightMigration
{
    public override void Up()
    {
        Dictionary<string, CapabilityV1SeedData.CapabilitySeed> capabilitiesByKey = CapabilityV1SeedData.Capabilities.ToDictionary(c => c.Key);

        Execute.WithConnection((connection, transaction) =>
        {
            foreach (CapabilityV1SeedData.TemplateSeed template in CapabilityV1SeedData.Templates)
            {
                HashSet<string> expectedRawGrants = ComputeExpectedRawGrants(template, capabilitiesByKey);

                List<(Guid GrantGroupId, Guid OrganizationId)> groups = new();
                using (IDbCommand selectGroups = connection.CreateCommand())
                {
                    selectGroups.Transaction = transaction;
                    selectGroups.CommandText = "SELECT id, organization_id FROM dunelight.grant_groups WHERE name = @name";
                    AddParameter(selectGroups, "@name", template.DisplayNameHr);
                    using IDataReader reader = selectGroups.ExecuteReader();
                    while (reader.Read())
                        groups.Add((reader.GetGuid(0), reader.GetGuid(1)));
                }

                foreach ((Guid grantGroupId, Guid _) in groups)
                {
                    HashSet<string> actualRawGrants = new();
                    using (IDbCommand selectGrants = connection.CreateCommand())
                    {
                        selectGrants.Transaction = transaction;
                        selectGrants.CommandText = "SELECT grant_key FROM dunelight.grant_group_grants WHERE grant_group_id = @gid";
                        AddParameter(selectGrants, "@gid", grantGroupId);
                        using IDataReader reader = selectGrants.ExecuteReader();
                        while (reader.Read())
                            actualRawGrants.Add(reader.GetString(0));
                    }

                    if (!actualRawGrants.SetEquals(expectedRawGrants))
                        continue;

                    DateTimeOffset now = DateTimeOffset.UtcNow;

                    foreach (CapabilityV1SeedData.TemplateCapabilitySeed selection in template.Selections)
                    {
                        using IDbCommand insertSnapshot = connection.CreateCommand();
                        insertSnapshot.Transaction = transaction;
                        insertSnapshot.CommandText = @"
                            INSERT INTO dunelight.grant_group_capability_snapshots
                                (id, grant_group_id, capability_definition_id, selected_scope, source_template_key, source_template_version, applied_at, applied_by)
                            VALUES (@id, @gid, @cid, @scope, @tkey, @tver, @appliedAt, NULL)";
                        AddParameter(insertSnapshot, "@id", Guid.NewGuid());
                        AddParameter(insertSnapshot, "@gid", grantGroupId);
                        AddParameter(insertSnapshot, "@cid", CapabilityV1SeedData.CapabilityId(selection.CapabilityKey));
                        AddParameter(insertSnapshot, "@scope", selection.SelectedScope);
                        AddParameter(insertSnapshot, "@tkey", template.Key);
                        AddParameter(insertSnapshot, "@tver", CapabilityV1SeedData.TemplateVersion);
                        AddParameter(insertSnapshot, "@appliedAt", now);
                        insertSnapshot.ExecuteNonQuery();
                    }

                    foreach (string grantKey in template.CompatibilityExtraGrants)
                    {
                        using IDbCommand insertProvenance = connection.CreateCommand();
                        insertProvenance.Transaction = transaction;
                        insertProvenance.CommandText = @"
                            INSERT INTO dunelight.grant_group_template_grants
                                (id, grant_group_id, grant_key, source_template_key, source_template_version, applied_at, applied_by)
                            VALUES (@id, @gid, @gkey, @tkey, @tver, @appliedAt, NULL)";
                        AddParameter(insertProvenance, "@id", Guid.NewGuid());
                        AddParameter(insertProvenance, "@gid", grantGroupId);
                        AddParameter(insertProvenance, "@gkey", grantKey);
                        AddParameter(insertProvenance, "@tkey", template.Key);
                        AddParameter(insertProvenance, "@tver", CapabilityV1SeedData.TemplateVersion);
                        AddParameter(insertProvenance, "@appliedAt", now);
                        insertProvenance.ExecuteNonQuery();
                    }
                }
            }
        });
    }

    private static HashSet<string> ComputeExpectedRawGrants(CapabilityV1SeedData.TemplateSeed template, Dictionary<string, CapabilityV1SeedData.CapabilitySeed> capabilitiesByKey)
    {
        HashSet<string> result = new();

        foreach (CapabilityV1SeedData.TemplateCapabilitySeed selection in template.Selections)
        {
            CapabilityV1SeedData.CapabilitySeed capability = capabilitiesByKey[selection.CapabilityKey];
            result.UnionWith(Materialize(capability.ScopeModel, selection.SelectedScope, capability.Grants));
        }

        result.UnionWith(template.CompatibilityExtraGrants);
        return result;
    }

    /// <summary>Duplikat CapabilityMaterializationService algoritma — vidi klasnu napomenu zašto migracija ne
    /// smije referencirati Infrastructure projekt.</summary>
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
                activeRoles.Add(selectedScope == "Own" ? "PrimaryOwn" : "PrimaryAll");
                break;
            case "ViewOwnAll":
                activeRoles.Add("PrimaryViewOnly");
                if (selectedScope == "Own")
                    activeRoles.Add("PrimaryOwn");
                else if (selectedScope == "All")
                    activeRoles.Add("PrimaryAll");
                break;
            default:
                throw new InvalidOperationException($"Nepoznat ScopeModel '{scopeModel}'.");
        }

        activeRoles.Add("MandatorySupporting");

        return grants.Where(g => activeRoles.Contains(g.Role)).Select(g => g.GrantKey);
    }

    private static void AddParameter(IDbCommand command, string name, object value)
    {
        IDbDataParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
