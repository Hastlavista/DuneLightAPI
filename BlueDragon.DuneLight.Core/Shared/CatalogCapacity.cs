namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Pravilo kapaciteta kataloških entiteta (Room.Capacity = broj osoba, Resource.Capacity = broj jedinica): cijeli broj
/// ≥ 1. Isto pravilo je i CHECK ograničenje u bazi (ck_rooms_capacity_positive, ck_resources_capacity_positive).
/// </summary>
public static class CatalogCapacity
{
    public const int Min = 1;

    public static bool IsValid(int capacity) => capacity >= Min;
}
