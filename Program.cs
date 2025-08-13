using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Json;
using System.Text.Json.Serialization;

// ===================== 程式主體（Top-level） =====================

var builder = WebApplication.CreateBuilder(args);

// JSON 行為
builder.Services.Configure<JsonOptions>(o =>
{
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// CORS：全開（測試/PoC 用；之後可收斂成公司網域）
builder.Services.AddCors(opt =>
{
    opt.AddPolicy("AllowAll", p => p
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// SQLite 位置：雲端 /tmp、本機檔案
var isCloud = Environment.GetEnvironmentVariable("RENDER") == "true";
var dbPath  = isCloud ? "/tmp/wfhdemo.db" : "wfhdemo.db";
builder.Services.AddDbContext<AppDb>(o => o.UseSqlite($"Data Source={dbPath}"));

var app = builder.Build();

// 掛 CORS（務必在 Map 端點前）
app.UseCors("AllowAll");

// 回應所有預檢（OPTIONS），避免 415/CORS header 缺失
app.MapMethods("/{*any}", new[] { "OPTIONS" }, (HttpRequest _) => Results.Ok());

// 健康檢查/首頁
app.MapGet("/", () => Results.Text("WFH Api is running"));
app.MapGet("/api/health", () => Results.Json(new { ok = true, time = DateTime.UtcNow }));

// 啟動時建 DB
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.EnsureCreated();
}

// ===== 測試帳號（簡易版） =====
var Users = new Dictionary<string,(string Name,string Role,string Password)>(StringComparer.OrdinalIgnoreCase) {
    ["Rachel"]  = ("Rachel","L1","1234"),
    ["Joan"]    = ("Joan","user","1234"),
    ["Amanda"]  = ("Amanda","user","1234"),
    ["Albee"]   = ("Albee","user","1234"),
    ["Emily"]   = ("Emily","user","1234"),
    ["CE"]      = ("CE","user","1234"),
    ["Jason"]   = ("Jason","L2","1234"),
};

// 登入（接 JSON）
app.MapPost("/api/login", (LoginDto dto) =>
{
    if (!Users.TryGetValue(dto.Username, out var u) || u.Password != dto.Password)
        return Results.Unauthorized();

    var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()); // Demo token
    return Results.Json(new { token, name = u.Name, role = u.Role });
})
.Accepts<LoginDto>("application/json");

// 建立申請
app.MapPost("/api/applications", async (AppDb db, CreateAppDto dto) =>
{
    var row = new ApplicationRow {
        ApplicantEmail = "demo@local",
        ApplicantName  = "Demo User",
        Department     = "DemoDept",
        DatesJson      = System.Text.Json.JsonSerializer.Serialize(dto.Dates ?? Array.Empty<string>()),
        Type           = string.IsNullOrWhiteSpace(dto.Type) ? "regular" : dto.Type!,
        Reason         = dto.Reason,
        Status         = "pending_section",
        SubmitTime     = DateTime.UtcNow,
        LastUpdateTime = DateTime.UtcNow
    };
    db.Applications.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(new { appId = row.AppId });
})
.Accepts<CreateAppDto>("application/json");

// 查詢申請
app.MapGet("/api/applications", async (AppDb db) =>
{
    var list = await db.Applications
        .OrderByDescending(x => x.SubmitTime)
        .Take(200)
        .ToListAsync();
    return Results.Ok(list);
});

// 核准
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
        AppId = appRow.AppId, ActionSeq = seq, ActorEmail = "approver@local",
        ActorName = "Approver", ActorRole = "section_head", Action = "approved",
        Comment = dto.Comment, ActorIp = null, ActionTime = DateTime.UtcNow
    });

    await db.SaveChangesAsync();
    return Results.Ok(new { status = appRow.Status });
})
.Accepts<ActionDto>("application/json");

// 駁回
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
        AppId = appRow.AppId, ActionSeq = seq, ActorEmail = "approver@local",
        ActorName = "Approver", ActorRole = "section_head", Action = "rejected",
        Comment = dto.Comment, ActorIp = null, ActionTime = DateTime.UtcNow
    });

    await db.SaveChangesAsync();
    return Results.Ok(new { status = appRow.Status });
})
.Accepts<ActionDto>("application/json");

// 稽核軌跡
app.MapGet("/api/approvals", async (AppDb db, long appId) =>
{
    var list = await db.Approvals
        .Where(x => x.AppId == appId)
        .OrderBy(x => x.ActionSeq)
        .ToListAsync();
    return Results.Ok(list);
});

app.Run();

// ===================== DTO（留在這裡即可） =====================
public record CreateAppDto(string[]? Dates, string? Type, string? Reason);
public record ActionDto(long AppId, string? Comment);
public record LoginDto(string Username, string Password);
