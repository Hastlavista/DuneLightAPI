using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.DTOs.Employees;

public class EmployeeCompanyDto
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; }
    public bool IsPrimary { get; set; }
}

public class EmployeeServiceDto
{
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; }
}

/// <summary>Puni prikaz zaposlenika — isključivo za admina.</summary>
public class EmployeeDto
{
    public Guid Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Phone { get; set; }
    public string Email { get; set; }
    public DateTimeOffset? DateOfBirth { get; set; }
    public string Address { get; set; }
    public string Oib { get; set; }
    public string Note { get; set; }

    /// <summary>Slobodni tekst — čisto informativna bilješka o dogovoru oko naknade. Aplikacija je ne parsira niti koristi.</summary>
    public string CompensationNote { get; set; }

    public string ColorHex { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset EmploymentStartDate { get; set; }
    public DateTimeOffset? EmploymentEndDate { get; set; }
    public Guid EngagementTypeId { get; set; }
    public string EngagementTypeName { get; set; }
    public bool IsActive { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; }

    /// <summary>Imena (ne ID-evi) GrantGroup/Role dodjela ovog korisnika — bulk-friendly
    /// za listu (vidi EmployeeService.GetPaged), da frontend ne mora dodatni lookup po retku.</summary>
    public List<string> GrantGroupNames { get; set; } = new();
    public List<string> RoleNames { get; set; } = new();

    public List<EmployeeCompanyDto> Companies { get; set; } = new();
    public List<EmployeeServiceDto> Services { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    /// <summary>Transient upozorenje (npr. ima buduće termine) — nije perzistirano, popunjava se samo u odgovoru na (de)aktivaciju.</summary>
    public WarningDto Warning { get; set; }
}

public class EmployeeCreateRequest
{
    [Required]
    [MaxLength(255)]
    public string FirstName { get; set; }

    [Required]
    [MaxLength(255)]
    public string LastName { get; set; }

    [MaxLength(50)]
    public string Phone { get; set; }

    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; }

    public DateTimeOffset? DateOfBirth { get; set; }

    public string Address { get; set; }

    [RegularExpression(@"^\d{11}$", ErrorMessage = "OIB mora sadržavati točno 11 znamenki.")]
    public string Oib { get; set; }

    public string Note { get; set; }

    public string CompensationNote { get; set; }

    [MaxLength(7)]
    public string ColorHex { get; set; }

    public int SortOrder { get; set; }

    [Required]
    public DateTimeOffset EmploymentStartDate { get; set; }

    public DateTimeOffset? EmploymentEndDate { get; set; }

    [Required]
    public Guid EngagementTypeId { get; set; }

    [Required]
    public Guid UserId { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Zaposlenik mora imati barem jednu tvrtku.")]
    public List<Guid> CompanyIds { get; set; } = new();

    [Required]
    public Guid PrimaryCompanyId { get; set; }

    /// <summary>Prazno = zaposlenik (još) ne smije izvoditi nijednu uslugu — capability je isključivo eksplicitna.</summary>
    public List<Guid> ServiceIds { get; set; } = new();
}

public class EmployeeUpdateRequest
{
    [Required]
    [MaxLength(255)]
    public string FirstName { get; set; }

    [Required]
    [MaxLength(255)]
    public string LastName { get; set; }

    [MaxLength(50)]
    public string Phone { get; set; }

    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; }

    public DateTimeOffset? DateOfBirth { get; set; }

    public string Address { get; set; }

    [RegularExpression(@"^\d{11}$", ErrorMessage = "OIB mora sadržavati točno 11 znamenki.")]
    public string Oib { get; set; }

    public string Note { get; set; }

    public string CompensationNote { get; set; }

    [MaxLength(7)]
    public string ColorHex { get; set; }

    public int SortOrder { get; set; }

    [Required]
    public DateTimeOffset EmploymentStartDate { get; set; }

    public DateTimeOffset? EmploymentEndDate { get; set; }

    [Required]
    public Guid EngagementTypeId { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Zaposlenik mora imati barem jednu tvrtku.")]
    public List<Guid> CompanyIds { get; set; } = new();

    [Required]
    public Guid PrimaryCompanyId { get; set; }

    public List<Guid> ServiceIds { get; set; } = new();
}

public class UpdateEmployeeRoleRequest
{
    [Required]
    public Enums.UserRole Role { get; set; }
}

/// <summary>
/// Kreiranje zaposlenika ZAJEDNO s korisničkim računom (login), u jednoj transakciji.
/// Polja preuzeta iz <see cref="EmployeeCreateRequest"/> (bez <c>UserId</c>, jer se korisnik stvara ovdje)
/// + login polja (<c>Password</c>, <c>Role</c>). <c>Email</c> je obavezan i služi i kao kontakt i kao login e-mail.
/// </summary>
public class EmployeeWithLoginCreateRequest
{
    [Required]
    [MaxLength(255)]
    public string FirstName { get; set; }

    [Required]
    [MaxLength(255)]
    public string LastName { get; set; }

    [MaxLength(50)]
    public string Phone { get; set; }

    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; }

    public DateTimeOffset? DateOfBirth { get; set; }

    public string Address { get; set; }

    [RegularExpression(@"^\d{11}$", ErrorMessage = "OIB mora sadržavati točno 11 znamenki.")]
    public string Oib { get; set; }

    public string Note { get; set; }

    public string CompensationNote { get; set; }

    [MaxLength(7)]
    public string ColorHex { get; set; }

    public int SortOrder { get; set; }

    [Required]
    public DateTimeOffset EmploymentStartDate { get; set; }

    public DateTimeOffset? EmploymentEndDate { get; set; }

    [Required]
    public Guid EngagementTypeId { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Zaposlenik mora imati barem jednu tvrtku.")]
    public List<Guid> CompanyIds { get; set; } = new();

    [Required]
    public Guid PrimaryCompanyId { get; set; }

    /// <summary>Prazno = zaposlenik (još) ne smije izvoditi nijednu uslugu — capability je isključivo eksplicitna.</summary>
    public List<Guid> ServiceIds { get; set; } = new();

    /// <summary>Inicijalna lozinka koju upisuje admin.</summary>
    [Required]
    public string Password { get; set; }

    /// <summary>Ako je true, korisnik mora promijeniti lozinku (i PIN, ako je postavljen) pri prvoj prijavi.</summary>
    public bool MustChangeCredentialsOnFirstLogin { get; set; }

    /// <summary>Opcionalan inicijalni PIN — korisnik ga kasnije može promijeniti kroz `POST /api/public/Auth/ChangePin`.</summary>
    public string Pin { get; set; }

    /// <summary>Barem jedna GrantGroup je obavezna (osim za Ownera, koji se ne kreira ovim endpointom — vidi Register).</summary>
    [Required]
    [MinLength(1, ErrorMessage = "Korisnik mora imati barem jednu grant-grupu.")]
    public List<Guid> GrantGroupIds { get; set; } = new();

    /// <summary>Poslovne oznake (Role) — opcionalno, ne utječu na ovlasti.</summary>
    public List<Guid> RoleIds { get; set; } = new();
}

public class EmployeeWithLoginCreateResponse
{
    public Guid EmployeeId { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; }
    public List<Guid> GrantGroupIds { get; set; } = new();
}

/// <summary>Odgovor za "tko sam ja" (`GET /api/employees/me`) — dovoljno da frontend zna svoj identitet
/// autorizacijskog principala (User), neovisno o tome ima li Employee profil. AUTORIZACIJA PRIPADA USERU, NE
/// EMPLOYEEU: User -> UserGrantGroup -> GrantGroup -> GrantGroupGrant daje efektivne grantove uvijek, čak i kad
/// HasProfile je false (organizacijski osnivač odmah nakon Register — vidi AuthService.Register's Admin starter
/// GrantGroup dodjelu). Employee profil je opcionalan poslovni/radni profil, ne preduvjet za autorizaciju — zato
/// GetMe više NE baca 404 kad Employee ne postoji (vidi EmployeeService.GetMe). Nema Owner/founder bypass-a bilo
/// gdje u sustavu.</summary>
public class EmployeeMeDto
{
    /// <summary>False dok User nema Employee profil - u praksi gotovo uvijek samo organizacijski osnivač,
    /// odmah nakon Register, prije dovršetka vlastitog profila (vidi CompleteEmployeeProfileCtaComponent na
    /// frontendu). Employee-specifična polja ispod (EmployeeId/FirstName/LastName/ColorHex/Companies) su
    /// null/prazna dok je ovo false - ne postoje fiktivne/placeholder vrijednosti.</summary>
    public bool HasProfile { get; set; }

    public Guid? EmployeeId { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Role { get; set; }

    /// <summary>Efektivna, agregirana unija grant-key-eva iz svih GrantGroup dodjela ovog
    /// korisnika — isti izvor kao GrantResolver koristi za autorizaciju (GrantGroupHandler.ResolveEffective),
    /// samo izložen frontendu za UI-level provjere (canPage/can). NIKAD null/prazno samo zato što HasProfile
    /// je false - to je upravo scenarij (organizacijski osnivač) koji ovo polje mora ispravno pokriti, inače
    /// osnivač ne može ni otvoriti stranice za kreiranje prve Company/EngagementType. Nema Owner bypass-a
    /// nigdje u sustavu (Residual IsOwner Removal — User.IsOwner je potpuno uklonjen).</summary>
    public List<string> Grants { get; set; } = new();

    /// <summary>Ima li korisnik trenutno postavljen PIN (za brzo prebacivanje na dijeljenom uređaju) — frontend
    /// ovime bira prikaz "Postavi PIN" ili "Promijeni PIN". Ovo je User-razine podatak (PinHash živi na Useru),
    /// pa je dostupno i kad HasProfile je false.</summary>
    public bool HasPinSet { get; set; }

    public string ColorHex { get; set; }
    public List<EmployeeCompanyDto> Companies { get; set; } = new();
}

/// <summary>Ograničeni pogled za trenere/recepciju — kolege. Fizički ne sadrži osjetljiva polja.</summary>
public class EmployeeDirectoryDto
{
    public Guid Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string ColorHex { get; set; }
    public bool IsActive { get; set; }
    public List<string> Companies { get; set; } = new();
}
