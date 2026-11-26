using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Verx.Observability;

public sealed record LogEntry(DateTimeOffset Timestamp, [property: System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))] LogLevel Nivel, string Categoria, string Mensagem, string? Excecao, string? CorrelationId = null);

/// <summary>Guarda os últimos logs em memória (buffer circular) para consulta via API. Não substitui um log centralizado.</summary>
public sealed class LogStore(int capacidade = 1000)
{
    private readonly ConcurrentQueue<LogEntry> _itens = new();

    public void Adicionar(LogEntry entry)
    {
        _itens.Enqueue(entry);
        while (_itens.Count > capacidade && _itens.TryDequeue(out _)) { }
    }

    /// <summary>Mais recentes primeiro.</summary>
    public IReadOnlyList<LogEntry> Consultar(LogLevel nivelMinimo, string? busca, int limite) =>
        _itens.Reverse()
            .Where(e => e.Nivel >= nivelMinimo)
            .Where(e => string.IsNullOrWhiteSpace(busca)
                        || e.Mensagem.Contains(busca, StringComparison.OrdinalIgnoreCase)
                        || e.Categoria.Contains(busca, StringComparison.OrdinalIgnoreCase)
                        || (e.CorrelationId?.Contains(busca, StringComparison.OrdinalIgnoreCase) ?? false))
            .Take(limite)
            .ToList();
}

public sealed class LogStoreProvider(LogStore store) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new StoreLogger(categoryName, store);

    public void Dispose() { }

    private sealed class StoreLogger(string categoria, LogStore store) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var traceId = System.Diagnostics.Activity.Current?.TraceId;
            var correlationId = traceId is { } id && id != default ? id.ToString() : null;
            store.Adicionar(new LogEntry(DateTimeOffset.UtcNow, logLevel, categoria, formatter(state, exception), exception?.ToString(), correlationId));
        }
    }
}
