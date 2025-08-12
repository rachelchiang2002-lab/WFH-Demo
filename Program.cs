using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 讀取 Render 提供的動態埠；本機預設 5030
var portEnv = Environment.GetEnvironmentVariable("PORT");
var port = string.IsNullOrWhiteSpace(portEnv) ? "5030" : portEnv;
// 讓 Kestrel 綁定到 0.0.0.0:PORT（Render 必須這樣）
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// 先開放 CORS（之後可收斂來源）
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// 決定 SQLite 實體檔案路徑：Render 用 /tmp，可寫；本機用專案目錄
string dbPath;
if (string.IsNullOrWhiteSpace(portEnv))
{
    // 本機
    dbPath = Path.Combine(Directory.GetCurrentDirectory(), "wfhdemo.db");
}
else
{
    // 雲端（Render）
    dbPath = "/tmp/wfhdemo.db";
}

// 設定 SQLite
builder.Services.AddDbContext<AppDb>(opt =>
    opt.UseSqlite($"Data Source={dbPath}"));

var app = builder.Build();
app.UseCors();

// 啟動時若資料庫不存在就建立
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.EnsureCreated();
}

// 簡單首頁 & 健康檢查
app.MapGet("/", () => "WFH Api is running");
app.MapGet("/api/health", () => new { ok = true, time = DateTime.UtcNow });

// 建立申請單（最小可行版）
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

// 讀取所有申請單
app.MapGet("/api/applications", async (AppDb db) =>
{
    var list = await db.Applications
        .OrderByDescending(x => x.SubmitTime)
        .Take(200)
        .ToListAsync();
    return Results.Ok(list);
});

// 核准：pending_section -> pending_department -> approved
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

// 駁回：僅允許在兩個待審狀態
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

// 查詢稽核軌跡
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
