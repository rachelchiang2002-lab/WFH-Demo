using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// === Render 埠號設定（本機預設 5030） ===
var portEnv = Environment.GetEnvironmentVariable("PORT");
var port = string.IsNullOrWhiteSpace(portEnv) ? "5030" : portEnv;
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// === CORS：只允許 GitHub Pages 與本機 ===
var allowedOrigins = new[]
{
    "https://rachelchiang2002-lab.github.io", // 你的 GitHub Pages 網域
    "http://localhost:5030",
    "http://127.0.0.1:5030"
};
builder.Services.AddCors(options =>
{
    options.AddPolicy("AppCors", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// === SQLite 路徑：Render 用 /tmp，本機用專案資料夾 ===
string dbPath = string.IsNullOrWhiteSpace(portEnv)
    ? Path.Combine(Directory.GetCurrentDirectory(), "wfhdemo.db")
    : "/tmp/wfhdemo.db";

builder.Services.AddDbContext<AppDb>(opt => opt.UseSqlite($"Data Source={dbPath}"));

var app = builder.Build();
app.UseCors("AppCors");

// 啟動時若資料庫不存在就建立
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.EnsureCreated();
}

// ---- Health & Root ----
app.MapGet("/", () => "WFH Api is running");
app.MapGet("/api/health", () => new { ok = true, time = DateTime.UtcNow });

// ---- Applications ----
app.MapPost("/api/applications", async (AppDb db, CreateAppDto dto) =>
{
    var row = new ApplicationRow
    {
        ApplicantEmail = "demo@local",
        ApplicantName  = "Demo User",
        Department     = "DemoDept",
        DatesJson      = System.Text.Json.JsonSerializer.Serialize(dto.Dates ?? Array.Empty<string>()),
        Type           = string.IsNullOrWhiteSpace(dto.Type) ? "regular" : dto.Type,
        Reason         = dto.Reason,
        Status         = "pending_section",
        SubmitTime     = DateTime.UtcNow,
        LastUpdateTime = DateTime.UtcNow
    };
    db.Applications.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(new { appId = row.AppId });
});

app.MapGet("/api/applications", async (AppDb db) =>
{
    var list = await db.Applications
        .OrderByDescending(x => x.SubmitTime)
        .Take(200)
        .ToListAsync();
    return Results.Ok(list);
});

// ---- Approve / Reject ----
app.MapPost("/api/approve", async (AppDb db, ActionDto dto) =>
{
    var appRow = await db.Applications.FindAsync(dto.AppId);
    if (appRow is null) return Results.NotFound(new { message = "找不到申請單" });

    if (appRow.Status == "pending_section")        appRow.Status = "pending_department";
    else if (appRow.Status == "pending_department") appRow.Status = "approved";
    else return Results.BadRequest(new { message = $"狀態 {appRow.Status} 不能核准" });

    appRow.LastUpdateTime = DateTime.UtcNow;

    var seq = await db.Approvals.CountAsync(x => x.AppId == appRow.AppId) + 1;
    db.Approvals.Add(new ApprovalRow {
        AppId = appRow.AppId,
        ActionSeq = seq,
        ActorEmail = "approver@local",
        ActorName  = "Approver",
        ActorRole  = "section_head",
        Action     = "approved",
        Comment    = dto.Comment,
        ActorIp    = null,
        ActionTime = DateTime.UtcNow
    });

    await db.SaveChangesAsync();
    return Results.Ok(new { status = appRow.Status });
});

app.MapPost("/api/reject", async (AppDb db, ActionDto dto) =>
{
    var appRow = await db.Applications.FindAsync(dto.AppId);
    if (appRow is null) return Results.NotFound(new { message = "找不到申請單" });

    if (appRow.Status != "pending_section" && appRow.Status != "pending_department")
        return Results.BadRequest(new { message = $"狀態 {appRow.Status} 不能駁回" });

    appRow.Status = "rejected";
    appRow.LastUpdateTime = DateTime.UtcNow;

    var seq = await db.Approvals.CountAsync(x => x.AppId == appRow.AppId) + 1;
    db.Approvals.Add(new ApprovalRow {
        AppId = appRow.AppId,
        ActionSeq = seq,
        ActorEmail = "approver@local",
        ActorName  = "Approver",
        ActorRole  = "section_head",
        Action     = "rejected",
        Comment    = dto.Comment,
        ActorIp    = null,
        ActionTime = DateTime.UtcNow
    });

    await db.SaveChangesAsync();
    return Results.Ok(new { status = appRow.Status });
});

// ---- 查稽核軌跡 ----
app.MapGet("/api/approvals", async (AppDb db, long appId) =>
{
    var list = await db.Approvals
        .Where(x => x.AppId == appId)
        .OrderBy(x => x.ActionSeq)
        .ToListAsync();
    return Results.Ok(list);
});

app.Run();

// ---- DTO ----
public record CreateAppDto(string[]? Dates, string? Type, string? Reason);
public record ActionDto(long AppId, string? Comment);
