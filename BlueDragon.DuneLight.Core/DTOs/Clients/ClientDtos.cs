using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Clients;

public class ClientTagRefDto
{
    public Guid TagId { get; set; }
    public string Name { get; set; }
    public string ColorHex { get; set; }
}

/// <summary>Puni prikaz klijenta — admin, trener i recepcija (svi vide sve, uključivo zdravstvenu napomenu).</summary>
public class ClientDto
{
    public Guid Id { get; set; }
    public int MemberNumber { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public DateTimeOffset? DateOfBirth { get; set; }
    public string Occupation { get; set; }
    public string Phone { get; set; }
    public string Email { get; set; }
    public string Note { get; set; }
    public string HealthNote { get; set; }
    public bool GdprConsentGiven { get; set; }
    public DateTimeOffset? GdprConsentDate { get; set; }
    public Guid? HomeCompanyId { get; set; }
    public string HomeCompanyName { get; set; }
    public Guid? HomeTrainerId { get; set; }
    public string HomeTrainerName { get; set; }
    public bool IsActive { get; set; }
    public bool IsAnonymized { get; set; }
    public DateTimeOffset? AnonymizedAt { get; set; }
    public List<ClientTagRefDto> Tags { get; set; } = new();

    /// <summary>Ukupan broj termina statusa NoShow za ovog klijenta (bez vremenskog ograničenja).</summary>
    public int NoShowCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class ClientCreateRequest
{
    [Required]
    public int MemberNumber { get; set; }

    [Required]
    [MaxLength(255)]
    public string FirstName { get; set; }

    [Required]
    [MaxLength(255)]
    public string LastName { get; set; }

    public DateTimeOffset? DateOfBirth { get; set; }

    [MaxLength(255)]
    public string Occupation { get; set; }

    [MaxLength(255)]
    public string Phone { get; set; }

    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; }

    public string Note { get; set; }

    public string HealthNote { get; set; }

    public bool GdprConsentGiven { get; set; }

    public DateTimeOffset? GdprConsentDate { get; set; }

    public Guid? HomeCompanyId { get; set; }

    public Guid? HomeTrainerId { get; set; }

    public List<Guid> TagIds { get; set; } = new();
}

public class ClientUpdateRequest
{
    [Required]
    public int MemberNumber { get; set; }

    [Required]
    [MaxLength(255)]
    public string FirstName { get; set; }

    [Required]
    [MaxLength(255)]
    public string LastName { get; set; }

    public DateTimeOffset? DateOfBirth { get; set; }

    [MaxLength(255)]
    public string Occupation { get; set; }

    [MaxLength(255)]
    public string Phone { get; set; }

    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; }

    public string Note { get; set; }

    public string HealthNote { get; set; }

    public bool GdprConsentGiven { get; set; }

    public DateTimeOffset? GdprConsentDate { get; set; }

    public Guid? HomeCompanyId { get; set; }

    public Guid? HomeTrainerId { get; set; }

    public List<Guid> TagIds { get; set; } = new();
}

/// <summary>Redak u popisu rođendana — izvedeni podatak iz DateOfBirth, bez obzira na godinu.</summary>
public class ClientBirthdayDto
{
    public Guid Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Phone { get; set; }
    public DateTimeOffset DateOfBirth { get; set; }

    /// <summary>Datum sljedeće proslave rođendana unutar traženog razdoblja (godina je izračunata, ne stvarna godina rođenja).</summary>
    public DateTimeOffset NextOccurrence { get; set; }
}
