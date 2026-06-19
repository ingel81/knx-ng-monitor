namespace KnxMonitor.Core.Entities;

/// <summary>
/// Stores the ORIGINAL (still-encrypted) .knxkeys keyring bytes plus the keyring password
/// for a project. This is required for KNX Data Secure runtime decryption:
/// <c>Knx.Falcon.DataSecurity.GroupCommunicationSecurity.Load(stream, password)</c> needs the
/// raw keyring stream + password at connect time — the decrypted per-GA keys in
/// <see cref="ProjectKeyringKey"/> are NOT sufficient for that Load() call.
///
/// One row per project (unique <see cref="ProjectId"/>, replace-semantics on re-upload).
///
/// SECURITY: the keyring password is stored at rest in the local SQLite DB. This is the same
/// trust level as the already-stored decrypted keyring keys (<see cref="ProjectKeyringKey"/>),
/// which are themselves derived secrets. The DB file is intended to live on a trusted host
/// (the monitoring box). Anyone with read access to the DB can already read the decrypted keys;
/// storing the password here does not lower the existing trust boundary. It enables runtime
/// secure decryption without re-prompting the user on every reconnect.
/// </summary>
public class ProjectKeyringBlob
{
    public int Id { get; set; }

    /// <summary>FK to the owning project. Unique — one blob per project. Cascade-deleted with the project.</summary>
    public int ProjectId { get; set; }

    /// <summary>The original, still-encrypted .knxkeys file bytes (as uploaded / imported).</summary>
    public byte[] KeyringFile { get; set; } = Array.Empty<byte>();

    /// <summary>The keyring password needed to decrypt <see cref="KeyringFile"/> at runtime. SECURITY: see class remarks.</summary>
    public string KeyringPassword { get; set; } = string.Empty;

    // Navigation property
    public Project Project { get; set; } = null!;
}
