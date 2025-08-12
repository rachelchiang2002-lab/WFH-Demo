using Microsoft.EntityFrameworkCore;

public class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> options) : base(options) { }

    public DbSet<ApplicationRow> Applications => Set<ApplicationRow>();
    public DbSet<ApprovalRow>    Approvals    => Set<ApprovalRow>();
}
