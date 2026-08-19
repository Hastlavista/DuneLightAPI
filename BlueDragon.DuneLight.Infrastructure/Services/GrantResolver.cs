using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Grantovi se NE bakaju u JWT — dohvaćaju se iz baze po zahtjevu, da promjena dozvola vrijedi odmah.
/// Kratkotrajni in-memory cache (~30s po korisniku) ublažava dodatni DB round-trip bez značajnog kašnjenja
/// promjena; nema eksplicitne invalidacije na pisanje (prihvaćen trade-off, vidi FAZA 1 razgovor).
/// </summary>
public class GrantResolver : IGrantResolver
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly IGrantGroupHandler _grantGroupHandler;
    private readonly IMemoryCache _cache;

    public GrantResolver(IGrantGroupHandler grantGroupHandler, IMemoryCache cache)
    {
        _grantGroupHandler = grantGroupHandler;
        _cache = cache;
    }

    public async Task<GrantContext> Resolve(Guid organizationId, Guid userId)
    {
        string cacheKey = $"grant-context:{organizationId}:{userId}";
        if (_cache.TryGetValue(cacheKey, out GrantContext cached))
            return cached;

        (bool isOwner, HashSet<string> grants) = await _grantGroupHandler.ResolveEffective(organizationId, userId);
        GrantContext context = new GrantContext(isOwner, grants);
        _cache.Set(cacheKey, context, CacheDuration);
        return context;
    }
}