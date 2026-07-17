using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Domain;
using KnowledgeWeakness.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeWeakness.Infrastructure.Repositories;

public class SettingsRepository(IDbContextFactory<AppDbContext> dbFactory) : ISettingsRepository
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null) return null;
        if (!row.IsEncrypted) return row.Value;

        try
        {
            return Unprotect(row.Value);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        var value = await GetAsync(key, ct);
        return value;
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await UpsertAsync(key, value, encrypted: false, ct);
    }

    public async Task SetSecretAsync(string key, string value, CancellationToken ct = default)
    {
        var stored = Protect(value);
        await UpsertAsync(key, stored, encrypted: true, ct);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null) return;
        db.AppSettings.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    private async Task UpsertAsync(string key, string value, bool encrypted, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = value, IsEncrypted = encrypted });
        }
        else
        {
            row.Value = value;
            row.IsEncrypted = encrypted;
        }
        await db.SaveChangesAsync(ct);
    }

    private static string Protect(string plain)
    {
        var bytes = Encoding.UTF8.GetBytes(plain);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var enc = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return "dpapi:" + Convert.ToBase64String(enc);
        }
        return "plain:" + Convert.ToBase64String(bytes);
    }

    private static string Unprotect(string stored)
    {
        if (stored.StartsWith("dpapi:", StringComparison.Ordinal) && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var payload = Convert.FromBase64String(stored["dpapi:".Length..]);
            var dec = ProtectedData.Unprotect(payload, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(dec);
        }
        if (stored.StartsWith("dpapi:", StringComparison.Ordinal))
        {
            throw new CryptographicException("DPAPI-protected setting cannot be read on this platform.");
        }
        if (stored.StartsWith("plain:", StringComparison.Ordinal))
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(stored["plain:".Length..]));
        }
        return stored;
    }
}
