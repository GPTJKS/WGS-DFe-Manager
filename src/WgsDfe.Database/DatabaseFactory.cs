using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace WgsDfe.Database;
public static class DatabaseFactory
{
 public static WgsDbContext Create(string path, Guid organizationId)
 {
  var connection=new SqliteConnectionStringBuilder {DataSource=Path.GetFullPath(path),ForeignKeys=true,DefaultTimeout=30}.ToString();
  return new WgsDbContext(new DbContextOptionsBuilder<WgsDbContext>().UseSqlite(connection).Options,organizationId);
 }
}
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<WgsDbContext>
{
 // EF tooling cria apenas contexto; não migra ou abre banco real do usuário.
 public WgsDbContext CreateDbContext(string[] args) => DatabaseFactory.Create(
  Path.Combine(Path.GetTempPath(),"wgs-dfe-design.db"),Guid.Parse("0f5cde11-69d5-4b33-b6c3-c603fd02bb61"));
}
