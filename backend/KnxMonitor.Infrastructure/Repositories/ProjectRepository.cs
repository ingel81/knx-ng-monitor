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

    public async Task<Project?> GetByEtsProjectIdAsync(string etsProjectId)
    {
        return await _dbSet
            .Include(p => p.GroupAddresses)
            .FirstOrDefaultAsync(p => p.EtsProjectId == etsProjectId);
    }

    public async Task MergeReimportAsync(
        int projectId,
        string fileName,
        IEnumerable<GroupAddress> groupAddresses,
        IEnumerable<Device> devices,
        IEnumerable<Location> locations,
        IEnumerable<CommunicationObject> commObjects,
        IEnumerable<GroupRange> groupRanges)
    {
        var newGas = groupAddresses.ToList();

        await using var tx = await _context.Database.BeginTransactionAsync();

        var project = await _dbSet
            .Include(p => p.GroupAddresses)
            .FirstAsync(p => p.Id == projectId);

        // Metadata
        project.Name = Path.GetFileNameWithoutExtension(fileName);
        project.FileName = fileName;
        project.ImportDate = DateTime.UtcNow;

        // --- Group addresses: merge by Address (preserve Id of matched rows) ---
        var existingByAddr = project.GroupAddresses
            .GroupBy(g => g.Address)
            .ToDictionary(g => g.Key, g => g.First());
        var newAddrs = new HashSet<string>(newGas.Select(g => g.Address));

        foreach (var ng in newGas)
        {
            if (existingByAddr.TryGetValue(ng.Address, out var existing))
            {
                // Update in place — keeps existing.Id so telegrams stay linked.
                existing.Name = ng.Name;
                existing.Description = ng.Description;
                existing.DatapointType = ng.DatapointType;
            }
            else
            {
                ng.Id = 0;
                ng.ProjectId = projectId;
                _context.GroupAddresses.Add(ng);
            }
        }

        // Group addresses that vanished from the project — remove (their telegrams keep the row
        // until FK SetNull on save; raw address stays queryable).
        var removed = project.GroupAddresses.Where(g => !newAddrs.Contains(g.Address)).ToList();
        if (removed.Count > 0)
        {
            _context.GroupAddresses.RemoveRange(removed);
        }

        // --- Replace the rest wholesale (no telegram dependency) ---
        await _context.Devices.Where(d => d.ProjectId == projectId).ExecuteDeleteAsync();
        await _context.Locations.Where(l => l.ProjectId == projectId).ExecuteDeleteAsync();
        await _context.CommunicationObjects.Where(c => c.ProjectId == projectId).ExecuteDeleteAsync();
        await _context.GroupRanges.Where(r => r.ProjectId == projectId).ExecuteDeleteAsync();

        var deviceList = devices.ToList();
        foreach (var d in deviceList) { d.Id = 0; d.ProjectId = projectId; }
        await _context.Devices.AddRangeAsync(deviceList);

        var locationList = locations.ToList();
        foreach (var l in locationList) { l.Id = 0; l.ProjectId = projectId; }
        await _context.Locations.AddRangeAsync(locationList);

        var comList = commObjects.ToList();
        foreach (var c in comList) { c.Id = 0; c.ProjectId = projectId; }
        await _context.CommunicationObjects.AddRangeAsync(comList);

        var rangeList = groupRanges.ToList();
        foreach (var r in rangeList) { r.Id = 0; r.ProjectId = projectId; }
        await _context.GroupRanges.AddRangeAsync(rangeList);

        await _context.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
