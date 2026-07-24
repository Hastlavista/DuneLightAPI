using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Employees;

public class EmployeeLocationDto
{
    public Guid LocationId { get; set; }
    public string LocationName { get; set; }
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
    public List<EmployeeLocationDto> Locations { get; set; } = new();
    public List<EmployeeServiceDto> Services { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    /// <summary>Transient poruka upozorenja (npr. "ima buduće termine") — nije perzistirana, popunjava se samo u odgovoru na (de)aktivaciju.</summary>
    public string Warning { get; set; }
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
    [MinLength(1, ErrorMessage = "Zaposlenik mora imati barem jednu lokaciju.")]
    public List<Guid> LocationIds { get; set; } = new();

    [Required]
    public Guid PrimaryLocationId { get; set; }

    /// <summary>Prazno = smije izvoditi sve usluge.</summary>
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
    [MinLength(1, ErrorMessage = "Zaposlenik mora imati barem jednu lokaciju.")]
    public List<Guid> LocationIds { get; set; } = new();

    [Required]
    public Guid PrimaryLocationId { get; set; }

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
    [MinLength(1, ErrorMessage = "Zaposlenik mora imati barem jednu lokaciju.")]
    public List<Guid> LocationIds { get; set; } = new();

    [Required]
    public Guid PrimaryLocationId { get; set; }

    /// <summary>Prazno = smije izvoditi sve usluge.</summary>
    public List<Guid> ServiceIds { get; set; } = new();

    /// <summary>Inicijalna lozinka koju upisuje admin — ne generira se privremena, ne prisiljava se promjena pri prvoj prijavi.</summary>
    [Required]
    public string Password { get; set; }

    [Required]
    public Enums.UserRole Role { get; set; }
}

public class EmployeeWithLoginCreateResponse
{
    public Guid EmployeeId { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; }
    public string Role { get; set; }
}

/// <summary>Odgovor za "tko sam ja" (`GET /api/employees/me`) — dovoljno da frontend zna svoj identitet zaposlenika.</summary>
public class EmployeeMeDto
{
    public Guid EmployeeId { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Role { get; set; }
    public string ColorHex { get; set; }
    public List<EmployeeLocationDto> Locations { get; set; } = new();
}

/// <summary>Ograničeni pogled za trenere/recepciju — kolege. Fizički ne sadrži osjetljiva polja.</summary>
public class EmployeeDirectoryDto
{
    public Guid Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string ColorHex { get; set; }
    public bool IsActive { get; set; }
    public List<string> Locations { get; set; } = new();
}
