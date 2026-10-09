using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.DTOs.Clients;

public class ClientTagRefDto
{
    public Guid TagId { get; set; }
    public string Name { get; set; }
    public string? ColorHex { get; set; }
}

/// <summary>Puni prikaz klijenta — admin, trener i recepcija (svi vide sve, uključivo zdravstvenu napomenu).</summary>
public class ClientDto
{
    public Guid Id { get; set; }
    public int MemberNumber { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Occupation { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Note { get; set; }
    public string? HealthNote { get; set; }
    public bool GdprConsentGiven { get; set; }
    public DateOnly? GdprConsentDate { get; set; }
    public Guid? HomeCompanyId { get; set; }
    public string? HomeCompanyName { get; set; }
    public Guid? HomeTrainerId { get; set; }
    public string? HomeTrainerName { get; set; }
    public bool IsActive { get; set; }
    public bool IsAnonymized { get; set; }
    public DateTimeOffset? AnonymizedAt { get; set; }
    public Guid? AnonymizedBy { get; set; }
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
    /// <summary>K1-3 — null = sustav dodjeljuje sljedeći broj (najveći postojeći + 1). Ručni broj (prijenos iz Excela) mora
    /// biti slobodan i ≥ 1; broj veći od dosadašnjeg najvećeg za više od 1000 traži ConfirmMemberNumberJump.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Broj člana mora biti pozitivan.")]
    public int? MemberNumber { get; set; }

    /// <summary>K1-3 — svjesna potvrda ručnog broja koji pomiče automatsko brojanje (vidi MEMBER_NUMBER_JUMP_NOT_CONFIRMED).</summary>
    public bool ConfirmMemberNumberJump { get; set; }

    [Required]
    [MaxLength(255)]
    public string FirstName { get; set; }

    [Required]
    [MaxLength(255)]
    public string LastName { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    [MaxLength(255)]
    public string? Occupation { get; set; }

    [MaxLength(255)]
    public string? Phone { get; set; }

    [EmailAddress]
    [MaxLength(255)]
    public string? Email { get; set; }

    public string? Note { get; set; }

    public string? HealthNote { get; set; }

    public bool GdprConsentGiven { get; set; }

    public DateOnly? GdprConsentDate { get; set; }

    public Guid? HomeCompanyId { get; set; }

    public Guid? HomeTrainerId { get; set; }

    public List<Guid> TagIds { get; set; } = new();
}

public class ClientUpdateRequest
{
    /// <summary>K1-3 — null = broj člana se ne mijenja. Promjena: slobodan broj ≥ 1, uz isto pravilo potvrde kao kod kreiranja.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Broj člana mora biti pozitivan.")]
    public int? MemberNumber { get; set; }

    /// <summary>K1-3 — vidi ClientCreateRequest.ConfirmMemberNumberJump.</summary>
    public bool ConfirmMemberNumberJump { get; set; }

    [Required]
    [MaxLength(255)]
    public string FirstName { get; set; }

    [Required]
    [MaxLength(255)]
    public string LastName { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    [MaxLength(255)]
    public string? Occupation { get; set; }

    [MaxLength(255)]
    public string? Phone { get; set; }

    [EmailAddress]
    [MaxLength(255)]
    public string? Email { get; set; }

    public string? Note { get; set; }

    public string? HealthNote { get; set; }

    public bool GdprConsentGiven { get; set; }

    public DateOnly? GdprConsentDate { get; set; }

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
    public string? Phone { get; set; }
    public DateOnly DateOfBirth { get; set; }

    /// <summary>Datum sljedeće proslave rođendana unutar traženog razdoblja (godina je izračunata, ne stvarna godina rođenja).</summary>
    public DateOnly NextOccurrence { get; set; }
}
