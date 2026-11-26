using Microsoft.Extensions.Caching.Memory;
using Verx.Consolidado.Api.Application;

namespace Verx.Consolidado.Api.Infrastructure.Caching;

public sealed class MemorySaldoCache(IMemoryCache cache, ConsolidadoOptions options) : ISaldoCache
{
    public static string Chave(DateOnly data) => $"saldo:{data:yyyy-MM-dd}";

    public async Task<SaldoDiarioResponse> ObterOuCriarAsync(DateOnly data, Func<Task<SaldoDiarioResponse>> fabrica) =>
        (await cache.GetOrCreateAsync(Chave(data), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.TtlSeconds);
            return fabrica();
        }))!;

    public void Invalidar(DateOnly data) => cache.Remove(Chave(data));
}
