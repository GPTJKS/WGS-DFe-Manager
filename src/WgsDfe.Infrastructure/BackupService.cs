using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Database;
namespace WgsDfe.Infrastructure;

/// <summary>SQLite online snapshot; restore is deliberately restricted to a stopped runtime.</summary>
public static class BackupService
{
 private sealed record Manifest(int Version,Guid OrganizationId,string DatabaseHash,DateTime CreatedAtUtc);
 private static SqliteConnection Connection(string path,bool readOnly=false)=>new(new SqliteConnectionStringBuilder{DataSource=path,Mode=readOnly?SqliteOpenMode.ReadOnly:SqliteOpenMode.ReadWriteCreate,Pooling=false}.ToString());
 public static async Task CreateAsync(string dataDirectory,Guid organizationId,string destination,CancellationToken ct=default)
 {
  if(IsInside(destination,dataDirectory))throw new InvalidOperationException("Salve o backup fora da pasta de dados.");
  string temp=destination+"."+Guid.NewGuid().ToString("N")+".partial",snapshot=Path.Combine(dataDirectory,"backup-"+Guid.NewGuid().ToString("N")+".tmp");
  try
  {
   await using(var source=Connection(Path.Combine(dataDirectory,"wgs-dfe.db"),true))
   await using(var target=Connection(snapshot))
   {await source.OpenAsync(ct);await target.OpenAsync(ct);source.BackupDatabase(target);await ValidateAsync(target,organizationId,ct);}
   ct.ThrowIfCancellationRequested();
   using(var zip=ZipFile.Open(temp,ZipArchiveMode.Create))
   {
    zip.CreateEntryFromFile(snapshot,"database.sqlite");
    using var stream=File.OpenRead(snapshot);var hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,ct));
    using(var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open()))await writer.WriteAsync(JsonSerializer.Serialize(new Manifest(1,organizationId,hash,DateTime.UtcNow)));
    foreach(var name in new[]{"preferences.json","appsettings.snapshot.json"})
    {var file=Path.Combine(dataDirectory,name);if(File.Exists(file))zip.CreateEntryFromFile(file,name);}
    var receipts=Path.Combine(dataDirectory,"ExportReceipts");
    if(Directory.Exists(receipts))foreach(var file in Directory.EnumerateFiles(receipts,"*.json")){ct.ThrowIfCancellationRequested();zip.CreateEntryFromFile(file,"ExportReceipts/"+Path.GetFileName(file));}
   }
   ct.ThrowIfCancellationRequested();File.Move(temp,destination,false);
  }
  finally{if(File.Exists(temp))File.Delete(temp);if(File.Exists(snapshot))File.Delete(snapshot);}
 }
 public static async Task RestoreOfflineAsync(string dataDirectory,Guid organizationId,string backup,CancellationToken ct=default)
 {
  Directory.CreateDirectory(dataDirectory);
  // Same lock used by FoundationRuntime. Never replace a database open in this or another process.
  await using var instance=new FileStream(Path.Combine(dataDirectory,"instance.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  string temp=Path.Combine(dataDirectory,"restore-"+Guid.NewGuid().ToString("N")+".tmp");
  try
  {
   using var zip=ZipFile.OpenRead(backup);
   var manifestEntry=zip.GetEntry("manifest.json")??throw new InvalidDataException("Backup sem manifesto.");
   if(manifestEntry.Length>4096)throw new InvalidDataException("Manifesto inválido.");
   using var manifestStream=manifestEntry.Open();var manifest=await JsonSerializer.DeserializeAsync<Manifest>(manifestStream,cancellationToken:ct);
   if(manifest is null||manifest.Version!=1||manifest.OrganizationId!=organizationId)throw new InvalidDataException("Backup de outra organização ou versão incompatível.");
   var database=zip.GetEntry("database.sqlite")??throw new InvalidDataException("Banco ausente.");
   if(database.Length is <1 or >2L*1024*1024*1024)throw new InvalidDataException("Tamanho de backup inválido.");
   await using(var input=database.Open())await using(var output=File.Create(temp))await input.CopyToAsync(output,ct);
   await using(var input=File.OpenRead(temp))if(Convert.ToHexString(await SHA256.HashDataAsync(input,ct))!=manifest.DatabaseHash)throw new InvalidDataException("O backup falhou na verificação de integridade.");
   await using(var restored=Connection(temp,true)){await restored.OpenAsync(ct);await ValidateAsync(restored,organizationId,ct);}
   await using(var model=new WgsDbContext(new DbContextOptionsBuilder<WgsDbContext>().UseSqlite(new SqliteConnectionStringBuilder{DataSource=temp,Pooling=false,Mode=SqliteOpenMode.ReadOnly}.ToString()).Options,organizationId))
   {
    var known=model.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
    if((await model.Database.GetAppliedMigrationsAsync(ct)).Any(id=>!known.Contains(id)))throw new InvalidDataException("O backup foi criado por uma versão mais recente. Atualize antes de restaurar.");
   }
   UserPreferences? restoredPreferences=null;
   var preferences=zip.GetEntry("preferences.json");
   if(preferences is not null)
   {
    if(preferences.Length>65536)throw new InvalidDataException("Preferências inválidas.");
    using var prefStream=preferences.Open();restoredPreferences=await JsonSerializer.DeserializeAsync<UserPreferences>(prefStream,cancellationToken:ct);
   }
   var receiptEntries=zip.Entries.Where(e=>e.FullName.StartsWith("ExportReceipts/",StringComparison.Ordinal)).ToArray();
   if(receiptEntries.Length>100000||receiptEntries.Any(e=>e.Length>16384||Path.GetFileName(e.FullName).Length!=69||!Path.GetFileName(e.FullName)[..64].All(char.IsAsciiHexDigit)||e.FullName!="ExportReceipts/"+Path.GetFileName(e.FullName)))throw new InvalidDataException("Comprovantes inválidos.");
   var dbPath=Path.Combine(dataDirectory,"wgs-dfe.db");
   if(File.Exists(dbPath))
   {
    // First open and close the old DB so SQLite recovers any WAL; create a consistent recovery copy.
    await using var old=Connection(dbPath);await old.OpenAsync(ct);
    await using var safety=Connection(dbPath+".before-restore-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N"));await safety.OpenAsync(ct);old.BackupDatabase(safety);
    using var checkpoint=old.CreateCommand();checkpoint.CommandText="PRAGMA wal_checkpoint(TRUNCATE);";await checkpoint.ExecuteNonQueryAsync(ct);
   }
   ct.ThrowIfCancellationRequested();
   if(File.Exists(dbPath))File.Replace(temp,dbPath,null);else File.Move(temp,dbPath);
   // Copy only allowlisted non-secret files. No path from a ZIP is extracted directly.
   if(restoredPreferences is not null)(restoredPreferences with{RetentionDays=0}).Save(dataDirectory);
   foreach(var entry in receiptEntries)
   {var directory=Path.Combine(dataDirectory,"ExportReceipts");Directory.CreateDirectory(directory);entry.ExtractToFile(Path.Combine(directory,Path.GetFileName(entry.FullName)),true);}
   // External files are checked again before they are read or cleaned. Retention restarts disabled.
  }
  finally{if(File.Exists(temp))File.Delete(temp);}
 }
 private static async Task ValidateAsync(SqliteConnection connection,Guid organizationId,CancellationToken ct)
 {
  using var integrity=connection.CreateCommand();integrity.CommandText="PRAGMA integrity_check;";
  if(!string.Equals((await integrity.ExecuteScalarAsync(ct))?.ToString(),"ok",StringComparison.Ordinal))throw new InvalidDataException("SQLite inconsistente.");
  using var foreign=connection.CreateCommand();foreign.CommandText="PRAGMA foreign_key_check;";
  await using(var reader=await foreign.ExecuteReaderAsync(ct))if(await reader.ReadAsync(ct))throw new InvalidDataException("Vínculos inconsistentes no backup.");
  using var tenant=connection.CreateCommand();tenant.CommandText="SELECT Id FROM Organizations LIMIT 2;";
  await using var rows=await tenant.ExecuteReaderAsync(ct);
  if(!await rows.ReadAsync(ct)||!Guid.TryParse(rows.GetString(0),out var id)||id!=organizationId||await rows.ReadAsync(ct))throw new InvalidDataException("Organização do backup incompatível.");
 }
 private static bool IsInside(string path,string root)=>Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
}
