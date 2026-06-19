using Microsoft.EntityFrameworkCore;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure.Data;

namespace KnxMonitor.Infrastructure.Repositories;

public class ProjectRepository : Repository<Project>, IProjectRepository
{
    public ProjectRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<Project?> GetWithDetailsAsync(int id)
    {
        return await _dbSet
            .Include(p => p.GroupAddresses)
            .Include(p => p.Devices)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<List<Project>> GetAllWithCountsAsync()
    {
        return await _dbSet
            .Include(p => p.GroupAddresses)
            .Include(p => p.Devices)
            .ToListAsync();
    }

    public async Task<Project?> GetActiveProjectAsync()
    {
        return await _dbSet
            .Include(p => p.GroupAddresses)
            .Include(p => p.Devices)
            .FirstOrDefaultAsync(p => p.IsActive);
    }

    public async Task<int> CountTelegramsForProjectAsync(int id)
    {
        return await _context.KnxTelegrams
            .CountAsync(t => t.GroupAddress != null && t.GroupAddress.ProjectId == id);
    }

    public async Task<bool> DeleteProjectFastAsync(int id)
    {
        var exists = await _dbSet.AnyAsync(p => p.Id == id);
        if (!exists)
            return false;

        // Atomic: each ExecuteUpdate/ExecuteDelete is its own statement, so wrap the whole
        // sequence in a transaction — a mid-sequence failure must not leave orphaned children
        // pointing at a still-present (or half-deleted) project.
        await using var tx = await _context.Database.BeginTransactionAsync();

        // 1. Preserve telegrams: null their GA reference in one set-based, index-backed UPDATE
        //    (the FK index on KnxTelegrams.GroupAddressId keeps this off the full-scan path).
        await _context.KnxTelegrams
            .Where(t => t.GroupAddress != null && t.GroupAddress.ProjectId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.GroupAddressId, (int?)null));

        // 2. Bulk-delete the project's dependents and the project itself (no EF tracking).
        await _context.Devices.Where(d => d.ProjectId == id).ExecuteDeleteAsync();
        await _context.Locations.Where(l => l.ProjectId == id).ExecuteDeleteAsync();
        await _context.CommunicationObjects.Where(c => c.ProjectId == id).ExecuteDeleteAsync();
        await _context.GroupRanges.Where(r => r.ProjectId == id).ExecuteDeleteAsync();
        await _context.ProjectKeyringKeys.Where(k => k.ProjectId == id).ExecuteDeleteAsync();
        await _context.ProjectKeyringBlobs.Where(b => b.ProjectId == id).ExecuteDeleteAsync();
        await _context.GroupAddresses.Where(g => g.ProjectId == id).ExecuteDeleteAsync();
        await _context.Projects.Where(p => p.Id == id).ExecuteDeleteAsync();

        await tx.CommitAsync();

        return true;
    }

    public async Task SetActiveExclusiveAsync(int id)
    {
        await using var tx = await _context.Database.BeginTransactionAsync();
        // Clear every other active project, then activate the target — two set-based UPDATEs
        // instead of loading + saving each project row individually.
        await _dbSet.Where(p => p.IsActive && p.Id != id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false));
        await _dbSet.Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, true));
        await tx.CommitAsync();
    }

    public async Task<List<Location>> GetLocationsAsync(int id)
    {
        return await _context.Locations
            .Where(l => l.ProjectId == id)
            .OrderBy(l => l.Id)
            .ToListAsync();
    }

    public async Task<List<CommunicationObject>> GetCommObjectsAsync(int id, string? groupAddress)
    {
        var query = _context.CommunicationObjects.Where(c => c.ProjectId == id);

        if (!string.IsNullOrWhiteSpace(groupAddress))
            query = query.Where(c => c.GroupAddressLink == groupAddress);

        return await query
            .OrderBy(c => c.DeviceAddress)
            .ThenBy(c => c.Number)
            .ToListAsync();
    }

    public async Task<List<GroupRange>> GetGroupRangesAsync(int id)
    {
        return await _context.GroupRanges
            .Where(r => r.ProjectId == id)
            .OrderBy(r => r.RangeStart)
            .ToListAsync();
    }

    public async Task<List<Device>> GetDevicesAsync(int id)
    {
        return await _context.Devices
            .Where(d => d.ProjectId == id)
            .ToListAsync();
    }

    public async Task<Device?> GetDeviceByAddressAsync(int projectId, string physicalAddress)
    {
        return await _context.Devices
            .FirstOrDefaultAsync(d => d.ProjectId == projectId && d.PhysicalAddress == physicalAddress);
    }

    public async Task ReplaceKeyringKeysAsync(int id, IEnumerable<ProjectKeyringKey> keys)
    {
        // Replace semantics: drop the project's existing keyring rows, then bulk-insert the new set.
        await _context.ProjectKeyringKeys.Where(k => k.ProjectId == id).ExecuteDeleteAsync();

        var list = keys.ToList();
        if (list.Count > 0)
        {
            await _context.ProjectKeyringKeys.AddRangeAsync(list);
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpsertKeyringBlobAsync(int projectId, byte[] keyringFile, string keyringPassword)
    {
        // SECURITY: persists the keyring password at rest in the local SQLite DB. Same trust
        // level as the already-stored decrypted keyring keys — required so Data Secure can
        // Load() the raw keyring at connect time without re-prompting the user.
        // Replace-semantics: drop any existing blob for this project, then insert the new one.
        await _context.ProjectKeyringBlobs.Where(b => b.ProjectId == projectId).ExecuteDeleteAsync();

        await _context.ProjectKeyringBlobs.AddAsync(new ProjectKeyringBlob
        {
            ProjectId = projectId,
            KeyringFile = keyringFile,
            KeyringPassword = keyringPassword
        });
        await _context.SaveChangesAsync();
    }

    public async Task<ProjectKeyringBlob?> GetKeyringBlobAsync(int projectId)
    {
        return await _context.ProjectKeyringBlobs
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.ProjectId == projectId);
    }
}
